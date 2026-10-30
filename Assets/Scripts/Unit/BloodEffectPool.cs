using System.Collections.Generic;
using UnityEngine;

public class BloodEffectPool : MonoBehaviour, IBloodEffects
{
    // 볼류메트릭 블러드는 레이마칭 셰이더라 GPU 비용이 큼 - 프리팹당 풀 크기와
    // 동시 활성 개수를 모두 제한해 전투가 몰려도 GPU 부하가 무한정 쌓이지 않도록 한다.
    [SerializeField] private int poolSizePerPrefab = 4;
    [SerializeField] private int maxConcurrentActive = 8;
    [SerializeField] private float defaultLifetime = 3f;
    [SerializeField] private float maxLifetime = 10f;


    private static readonly int BloodColorId = Shader.PropertyToID("_Color");      // BFX_Blood - 튀는 피 메시
    private static readonly int DecalTintColorId = Shader.PropertyToID("_TintColor"); // BFX_Decal - 바닥 자국

    // 프리팹별로 쉬고 있는 인스턴스만 쌓는다. 방금 돌려받은 것을 먼저 꺼내므로(LIFO)
    // 꺼낼 때 리스트를 훑지 않고, 캐시에 남아 있을 확률이 높은 인스턴스를 다시 쓴다.
    private readonly Dictionary<GameObject, Stack<GameObject>> freeStacks = new Dictionary<GameObject, Stack<GameObject>>();
    // 인스턴스별 셰이더 설정 캐시 — 타격마다 GetComponentInChildren으로 계층을 다시 훑지 않도록 한다.
    private readonly Dictionary<GameObject, BFX_BloodSettings> settingsByInstance = new Dictionary<GameObject, BFX_BloodSettings>();
    // 색을 갈아끼울 머티리얼 캐시. 프리팹은 종족 구분 없이 공유하고 풀도 프리팹 단위라,
    // 같은 인스턴스가 고블린에게 쓰였다가 아군에게 다시 쓰인다. 그래서 스폰할 때마다 칠한다.
    private readonly Dictionary<GameObject, BloodTintTarget[]> tintTargetsByInstance = new Dictionary<GameObject, BloodTintTarget[]>();
    private readonly List<Renderer> rendererBuffer = new List<Renderer>();
    private MaterialPropertyBlock tintBlock;

    private struct BloodTintTarget
    {
        public Renderer Renderer;
        public int PropertyId;
        public Color OriginalColor;
    }

    // 활성 인스턴스와 만료 시각을 같은 인덱스로 나란히 관리. 예전에는 스폰마다 코루틴 +
    // WaitForSeconds를 새로 만들었는데, 타격 한 번마다 힙 할당이 두 번씩 생기는 구조였다.
    private readonly List<GameObject> activeInstances = new List<GameObject>();
    private readonly List<float> activeExpireTimes = new List<float>();
    // 꺼진 인스턴스를 돌려놓을 스택. 위 두 리스트와 같은 인덱스를 쓴다.
    private readonly List<Stack<GameObject>> activeFreeStacks = new List<Stack<GameObject>>();

    // 씬에 놓인 풀이 없을 때 GameServices가 부른다. 만든 풀은 Awake에서 스스로 자리에 앉는다.
    public static IBloodEffects CreateHost()
    {
        var holder = new GameObject(nameof(BloodEffectPool));
        return holder.AddComponent<BloodEffectPool>();
    }

    // 씬에 미리 배치해둔 풀이 있으면 그것을 쓰도록 자리에 앉힌다.
    // (앉히지 않으면 처음 쓰일 때 GameServices가 중복 홀더를 새로 만들어버린다.)
    private void Awake()
    {
        GameServices.BloodEffects.TryRegister(this);
    }

    private void OnDestroy()
    {
        GameServices.BloodEffects.Unregister(this);
    }

    private void Update()
    {
        float now = Time.time;
        for (int i = activeInstances.Count - 1; i >= 0; i--)
        {
            if (now < activeExpireTimes[i]) continue;
            DeactivateAt(i);
        }

        // 회수할 것이 없으면 다음 Spawn까지 쉰다. 피는 교전 순간에만 튀므로 대부분의 프레임이 여기에 걸린다.
        if (activeInstances.Count == 0) enabled = false;
    }

    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Color bloodColor)
    {
        if (prefab == null) return null;

        if (activeInstances.Count >= maxConcurrentActive)
        {
            DeactivateAt(0); // 가장 오래된 것부터 회수 (추가 순서 = 오래된 순서)
        }

        Stack<GameObject> freeStack = GetOrCreateFreeStack(prefab);
        GameObject instanceObject = PopFree(freeStack);
        if (instanceObject == null)
        {
            instanceObject = Instantiate(prefab, transform);
            instanceObject.SetActive(false);
        }

        ApplyBloodColor(instanceObject, bloodColor);
        instanceObject.transform.SetPositionAndRotation(position, rotation);
        instanceObject.SetActive(true);

        activeInstances.Add(instanceObject);
        activeExpireTimes.Add(Time.time + GetLifetime(instanceObject));
        activeFreeStacks.Add(freeStack);
        enabled = true;

        return instanceObject;
    }

    private void DeactivateAt(int index)
    {
        GameObject instanceObject = activeInstances[index];
        Stack<GameObject> freeStack = activeFreeStacks[index];
        activeInstances.RemoveAt(index);
        activeExpireTimes.RemoveAt(index);
        activeFreeStacks.RemoveAt(index);

        if (instanceObject == null) return;
        instanceObject.SetActive(false);
        freeStack.Push(instanceObject);
    }

    // maxLifetime으로 강제 클램프하고, 셰이더 자체 페이드 타이머(DecalLifeTimeSeconds)도
    // 같이 줄여서 갑자기 팝오프되지 않고 줄어든 시간 안에 자연스럽게 페이드되도록 한다.
    private float GetLifetime(GameObject instanceObject)
    {
        if (!settingsByInstance.TryGetValue(instanceObject, out BFX_BloodSettings settings))
        {
            settings = instanceObject.GetComponentInChildren<BFX_BloodSettings>(true);
            settingsByInstance[instanceObject] = settings;
        }

        if (settings == null) return defaultLifetime;

        if (settings.DecalLifeTimeSeconds > maxLifetime) settings.DecalLifeTimeSeconds = maxLifetime;
        return settings.DecalLifeTimeSeconds;
    }

    private void ApplyBloodColor(GameObject instanceObject, Color bloodColor)
    {
        BloodTintTarget[] targets = GetTintTargets(instanceObject);
        if (targets.Length == 0) return;

        if (tintBlock == null) tintBlock = new MaterialPropertyBlock();

        for (int i = 0; i < targets.Length; i++)
        {
            Renderer renderer = targets[i].Renderer;
            // 기존 블록을 읽어와서 색만 얹는다. BFX 스크립트들이 같은 블록에 애니메이션
            // 프레임(_TimeInFrames)을 넣으므로 통째로 덮어쓰면 재생이 멈춘다.
            renderer.GetPropertyBlock(tintBlock);
            tintBlock.SetColor(targets[i].PropertyId, Recolor(targets[i].OriginalColor, bloodColor));
            renderer.SetPropertyBlock(tintBlock);
        }
    }

    // 프리팹 원본의 진하기와 알파는 그대로 두고 색조만 바꾼다. 프리팹마다 원본 농도가
    // 조금씩 다르게(0.59~0.75) 잡혀 있는데, 절대색으로 덮어쓰면 그 편차가 사라진다.
    private static Color Recolor(Color original, Color bloodColor)
    {
        float tintPeak = Mathf.Max(bloodColor.r, Mathf.Max(bloodColor.g, bloodColor.b));
        if (tintPeak <= 0.0001f) return original;

        float originalPeak = Mathf.Max(original.r, Mathf.Max(original.g, original.b));
        Color result = bloodColor * (originalPeak / tintPeak);
        result.a = original.a;
        return result;
    }

    // 렌더러와 함께 프리팹 원본 색을 캐시한다. 머티리얼은 건드리지 않고 프로퍼티 블록으로만
    // 덮어쓰기 때문에, 원본은 계속 머티리얼에 남아 Recolor의 기준으로 쓸 수 있다.
    private BloodTintTarget[] GetTintTargets(GameObject instanceObject)
    {
        if (tintTargetsByInstance.TryGetValue(instanceObject, out BloodTintTarget[] cached)) return cached;

        List<BloodTintTarget> targets = new List<BloodTintTarget>();
        instanceObject.GetComponentsInChildren(true, rendererBuffer);
        for (int i = 0; i < rendererBuffer.Count; i++)
        {
            Renderer renderer = rendererBuffer[i];
            Material shared = renderer.sharedMaterial;
            if (shared == null) continue;

            int propertyId = shared.HasProperty(DecalTintColorId) ? DecalTintColorId
                : shared.HasProperty(BloodColorId) ? BloodColorId
                : 0;
            if (propertyId == 0) continue;

            targets.Add(new BloodTintTarget
            {
                Renderer = renderer,
                PropertyId = propertyId,
                OriginalColor = shared.GetColor(propertyId)
            });
        }

        BloodTintTarget[] result = targets.ToArray();
        tintTargetsByInstance[instanceObject] = result;
        return result;
    }

    // 바깥에서 파괴된 인스턴스는 건너뛴다.
    private static GameObject PopFree(Stack<GameObject> freeStack)
    {
        while (freeStack.Count > 0)
        {
            GameObject instanceObject = freeStack.Pop();
            if (instanceObject != null) return instanceObject;
        }

        return null;
    }

    private Stack<GameObject> GetOrCreateFreeStack(GameObject prefab)
    {
        if (freeStacks.TryGetValue(prefab, out Stack<GameObject> existingStack)) return existingStack;

        Stack<GameObject> freeStack = new Stack<GameObject>(poolSizePerPrefab);
        for (int i = 0; i < poolSizePerPrefab; i++)
        {
            GameObject pooledInstance = Instantiate(prefab, transform);
            pooledInstance.SetActive(false);
            freeStack.Push(pooledInstance);
        }

        freeStacks[prefab] = freeStack;
        return freeStack;
    }
}
