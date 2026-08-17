using System.Collections.Generic;
using UnityEngine;

public class BloodEffectPool : MonoBehaviour
{
    // 볼류메트릭 블러드는 레이마칭 셰이더라 GPU 비용이 큼 - 프리팹당 풀 크기와
    // 동시 활성 개수를 모두 제한해 전투가 몰려도 GPU 부하가 무한정 쌓이지 않도록 한다.
    [SerializeField] private int poolSizePerPrefab = 4;
    [SerializeField] private int maxConcurrentActive = 8;
    [SerializeField] private float defaultLifetime = 3f;
    [SerializeField] private float maxLifetime = 10f;

    private static BloodEffectPool instance;

    private readonly Dictionary<GameObject, List<GameObject>> pools = new Dictionary<GameObject, List<GameObject>>();
    // 인스턴스별 셰이더 설정 캐시 — 타격마다 GetComponentInChildren으로 계층을 다시 훑지 않도록 한다.
    private readonly Dictionary<GameObject, BFX_BloodSettings> settingsByInstance = new Dictionary<GameObject, BFX_BloodSettings>();

    // 활성 인스턴스와 만료 시각을 같은 인덱스로 나란히 관리. 예전에는 스폰마다 코루틴 +
    // WaitForSeconds를 새로 만들었는데, 타격 한 번마다 힙 할당이 두 번씩 생기는 구조였다.
    private readonly List<GameObject> activeInstances = new List<GameObject>();
    private readonly List<float> activeExpireTimes = new List<float>();

    public static BloodEffectPool Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject holder = new GameObject(nameof(BloodEffectPool));
                instance = holder.AddComponent<BloodEffectPool>();
            }

            return instance;
        }
    }

    // 씬에 미리 배치해둔 풀이 있으면 그것을 쓰도록 등록한다.
    // (등록하지 않으면 Instance 게터가 중복 홀더를 새로 만들어버린다.)
    private void Awake()
    {
        if (instance == null) instance = this;
    }

    private void Update()
    {
        float now = Time.time;
        for (int i = activeInstances.Count - 1; i >= 0; i--)
        {
            if (now < activeExpireTimes[i]) continue;
            DeactivateAt(i);
        }
    }

    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null;

        if (activeInstances.Count >= maxConcurrentActive)
        {
            DeactivateAt(0); // 가장 오래된 것부터 회수 (추가 순서 = 오래된 순서)
        }

        List<GameObject> pool = GetOrCreatePool(prefab);
        GameObject instanceObject = FindInactiveInstance(pool);
        if (instanceObject == null)
        {
            instanceObject = Instantiate(prefab, transform);
            instanceObject.SetActive(false);
            pool.Add(instanceObject);
        }

        int activeIndex = activeInstances.IndexOf(instanceObject);
        if (activeIndex >= 0) RemoveActiveAt(activeIndex);

        instanceObject.transform.SetPositionAndRotation(position, rotation);
        instanceObject.SetActive(true);

        activeInstances.Add(instanceObject);
        activeExpireTimes.Add(Time.time + GetLifetime(instanceObject));

        return instanceObject;
    }

    private void DeactivateAt(int index)
    {
        GameObject instanceObject = activeInstances[index];
        RemoveActiveAt(index);
        if (instanceObject != null) instanceObject.SetActive(false);
    }

    private void RemoveActiveAt(int index)
    {
        activeInstances.RemoveAt(index);
        activeExpireTimes.RemoveAt(index);
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

    private static GameObject FindInactiveInstance(List<GameObject> pool)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (!pool[i].activeSelf) return pool[i];
        }

        return null;
    }

    private List<GameObject> GetOrCreatePool(GameObject prefab)
    {
        if (pools.TryGetValue(prefab, out List<GameObject> existingPool)) return existingPool;

        List<GameObject> pool = new List<GameObject>(poolSizePerPrefab);
        for (int i = 0; i < poolSizePerPrefab; i++)
        {
            GameObject pooledInstance = Instantiate(prefab, transform);
            pooledInstance.SetActive(false);
            pool.Add(pooledInstance);
        }

        pools[prefab] = pool;
        return pool;
    }
}
