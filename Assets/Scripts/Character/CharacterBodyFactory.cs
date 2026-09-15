using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

// 저장해 둔 GLB를 읽어 "싸울 수 있는 몸"으로 세우는 곳. 캐릭터의 몸은 전부 이 길로 선다.
//
// 소환으로 구운 몸이든 에디터 메뉴로 구운 몸이든 결과물은 같은 GLB 한 장이고(CharacterModelStore),
// 에디터와 빌드가 같은 코드로 세운다. 예전에는 에디터에서 FBX로 프리팹을 따로 만들었는데,
// 같은 캐릭터가 두 모습을 갖고 한쪽만 이상하게 보이는 일이 생겨 이 길 하나로 합쳤다.
//
// 세우는 순서가 중요하다.
//   1. 전투 부품만 든 템플릿을 꺼내되, Awake가 돌지 않게 꺼진 부모 아래에 만든다.
//   2. 그 아래에 GLB를 붙인다.
//   3. 뼈를 읽어 아바타를 세우고 Animator에 물린다.
//
// 1번에서 그냥 Instantiate하면 안 된다 — Awake는 Instantiate가 돌아오기 전에 이미 실행되므로,
// 받아서 SetActive(false)를 하는 것으로는 늦는다. 아직 사람도 아닌 Animator에서 손뼈를 찾다가
// 무기 소켓을 못 잡은 채로 굳고, UnitRegistry에는 싸우지도 않을 유령이 하나 등록된다.
// 꺼진 부모 아래에서 만들면 Awake 자체가 미뤄진다.
//
// 그래서 세워 둔 몸(프로토타입)은 계속 꺼진 부모 아래에 산다. 전투에 나갈 때는 그것을
// Instantiate해서 씬 루트에 놓는데, 그 순간 처음으로 Awake가 돈다 — 이미 사람인 몸으로.
public static class CharacterBodyFactory
{
    public const string TemplateResourceName = "CharacterBody";

    // 캐릭터 id → 꺼진 채로 대기 중인 몸. 씬이 바뀌어도 살아남는다(소환은 마을, 전투는 던전).
    private static readonly Dictionary<string, GameObject> Prototypes = new Dictionary<string, GameObject>();

    // GltfImport를 놓아 버리면 그것이 만든 메시와 텍스처가 함께 사라진다. 몸이 살아 있는 동안 같이 붙잡아 둔다.
    private static readonly Dictionary<string, GltfImport> Imports = new Dictionary<string, GltfImport>();

    // 세워 둔 몸이 사는 곳. 꺼져 있어서 그 아래 있는 동안에는 Awake가 돌지 않는다.
    private static Transform nursery;

    private static Transform Nursery()
    {
        if (nursery != null) return nursery;

        var go = new GameObject("[CharacterBodies]");
        go.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(go);
        nursery = go.transform;
        return nursery;
    }

    /// 이미 세워 둔 몸. 아직 안 세웠으면 null.
    public static GameObject Ready(CharacterSO character) =>
        character != null && Prototypes.TryGetValue(character.Id, out GameObject body) ? body : null;

    /// 디스크의 GLB에서 몸을 세운다. 세우지 못하면 onDone(null).
    /// 이미 세워 둔 몸이 있으면 그것을 그대로 돌려주고 끝난다.
    public static IEnumerator Build(CharacterSO character, Action<GameObject> onDone)
    {
        if (character == null) { onDone?.Invoke(null); yield break; }

        string id = character.Id;
        if (Prototypes.TryGetValue(id, out GameObject existing) && existing != null)
        {
            onDone?.Invoke(existing);
            yield break;
        }

        if (!CharacterModelStore.Exists(id))
        {
            onDone?.Invoke(null);
            yield break;
        }

        var template = Resources.Load<GameObject>(TemplateResourceName);
        if (template == null)
        {
            Debug.LogError($"[CharacterBodyFactory] 전투 부품 템플릿을 찾지 못했다: " +
                           $"Resources/{TemplateResourceName}. 이게 없으면 몸을 세워도 싸우지 못한다.");
            onDone?.Invoke(null);
            yield break;
        }

        // 코루틴이 한 번 쉬고 나서 시작한다. 부른 쪽의 호출 스택 위에서 곧바로 glTFast를 건드리면
        // await가 붙잡는 동기화 문맥이 Unity의 것이 아닐 수 있다.
        yield return null;


        // 프레임을 쪼개지 않는 에이전트를 쓴다.
        //
        // glTFast의 기본값(TimeBudgetPerFrameDeferAgent)은 예산을 넘기면 await Task.Yield()로
        // 다음 프레임을 기다리는데, 그 재개가 돌아오지 않으면 메인 스레드가 그대로 잠긴다 —
        // 에디터가 통째로 멈추고 CPU는 0인 채로 예외도 남지 않는다(실제로 두 번 겪었다).
        // 어차피 몸 굽기는 뒤에서 도는 일이라 프레임 몇 개를 한 번에 먹어도 상관없다.
        var import = new GltfImport(deferAgent: new UninterruptedDeferAgent());

        // 밉맵을 반드시 만든다. glTFast는 기본값이 꺼져 있어서, 2048 텍스처가 밉맵 한 장으로만
        // 올라왔다. 전투 카메라(10m 거리)에서는 그게 화면 몇십 픽셀로 줄어드는데 밉맵이 없으면
        // 픽셀을 건너뛰며 찍어 옷 무늬가 자글자글 깨지고 반짝거린다. 에디터판은 12단계가 있다.
        // GLB 자체도 샘플러에 밉맵 필터(LINEAR_MIPMAP_LINEAR)를 적어 두었다 — 원래 있어야 했던 것이다.
        var settings = new ImportSettings { GenerateMipMaps = true, AnisotropicFilterLevel = 1 };
        Task<bool> loading = import.LoadFile(CharacterModelStore.PathFor(id), importSettings: settings);
        while (!loading.IsCompleted) yield return null;


        if (loading.IsFaulted || !loading.Result)
        {
            Debug.LogError($"[CharacterBodyFactory] {character.characterName}의 GLB를 읽지 못했다: " +
                           (loading.Exception != null ? loading.Exception.Message : "형식이 맞지 않는다"));
            onDone?.Invoke(null);
            yield break;
        }

        // 꺼진 부모 아래에서 만든다 — 아바타를 물리기 전에 Awake가 돌면 안 된다.
        GameObject body = UnityEngine.Object.Instantiate(template, Nursery());
        body.name = string.IsNullOrEmpty(character.characterName) ? id : character.characterName;

        // 읽기와 같이 Task를 코루틴에서 기다린다. 동기판 InstantiateMainScene은 이 비동기판의
        // .Result를 그대로 붙잡고 있을 뿐이라(glTFast 6), 메인 스레드를 막는 것 말고는 다른 점이 없다.
        Task<bool> instantiating = import.InstantiateMainSceneAsync(body.transform);
        while (!instantiating.IsCompleted) yield return null;

        if (instantiating.IsFaulted || !instantiating.Result)
        {
            Debug.LogError($"[CharacterBodyFactory] {character.characterName}의 GLB에서 장면을 꺼내지 못했다" +
                           (instantiating.Exception != null ? ": " + instantiating.Exception.Message : "."));
            UnityEngine.Object.Destroy(body);
            onDone?.Invoke(null);
            yield break;
        }

        // 아바타를 세우기 전에 해야 한다. 아바타는 이 순간의 뼈 값을 기준으로 삼는다.
        BakeOutScale(body);
        ApplyBodyMaterial(body);

        // 한 프레임 쉬어 준다. 여기까지가 무거워서, 아바타 세우기와 같은 프레임에 몰면
        // 에디터가 오래 멈춘 것처럼 보인다.
        yield return null;

        if (!CharacterModelRig.TryBuildAvatar(body, out Avatar avatar, out string problem))
        {
            Debug.LogError($"[CharacterBodyFactory] {character.characterName}: 사람으로 세우지 못했다 — {problem}");
            UnityEngine.Object.Destroy(body);
            onDone?.Invoke(null);
            yield break;
        }

        var animator = body.GetComponent<Animator>();
        animator.avatar = avatar;

        // 무기 소켓을 지금 박아 둔다. Meshy 리그는 손가락이 없어서 소켓을 몸을 기준 자세에 잠깐 세워 계산한다(HandSocket).
        // 전투에 나간 복사본마다 그걸 하면 애니메이션이 도는 몸을 한 번씩 흔드는 셈이고, 여기서는 몸이 아직
        // 바인드 포즈 그대로다. 복사본은 소켓을 그대로 물려받는다(WeaponEquipper가 손뼈 아래에서 찾는다).
        var equipper = body.GetComponent<WeaponEquipper>();
        float palmGripRatio = equipper != null ? equipper.PalmGripRatio : HandSocket.DefaultPalmGripRatio;
        if (HandSocket.Resolve(animator, EquipHand.Right, palmGripRatio) == null ||
            HandSocket.Resolve(animator, EquipHand.Left, palmGripRatio) == null)
            Debug.LogWarning($"[CharacterBodyFactory] {character.characterName}: 손 소켓을 놓지 못했다. 전투에 나갈 때 다시 시도한다.");

        // 씬이 갈려도 남는다(소환은 마을, 전투는 던전). 부모가 DontDestroyOnLoad라 따라간다.
        Prototypes[id] = body;
        Imports[id] = import;

        onDone?.Invoke(body);
    }

    // 꺼내 놓는 일은 여기서 하지 않는다. 세워 둔 몸은 프리팹 에셋과 똑같이 Instantiate하면 되고
    // (부모 없는 활성 복사본이 나온다), 꺼낸 뒤에 전투 수치를 물리는 일까지는 스포너의 몫이다.

    // 뼈대에 걸린 배율을 없애고 그 크기를 뼈 위치에 녹여 넣는다. 보이는 모습은 한 치도 바뀌지 않는다.
    //
    // Meshy GLB는 센티미터로 만들어져 있어서 Armature 노드에 0.01 배율이 걸려 있고, 그 아래 뼈는
    // 87.8 같은 센티미터 값으로 서 있다. 겉보기 크기는 맞는데, 이 배율이 뼈에 매다는 모든 것에 번진다 —
    //   · 손 소켓에 무기를 배율 1로 붙이면 0.01을 물려받아, 76cm짜리 칼이 7mm가 됐다.
    //     일라리스는 보이지 않는 칼을 휘두르고 있었다(에디터판 라엘은 멀쩡했다).
    //   · 아바타가 사람 크기(humanScale)를 뼈의 로컬 값으로 재기 때문에 0.9312로 잡혀
    //     임포터판(0.9585)보다 몸이 낮게 섰다.
    // FBX 임포터는 단위를 변환하면서 이 배율을 뼈 위치에 녹여 넣는다(라엘의 골반 배율은 1이다).
    // 같은 일을 한다.
    //
    // 뼈만 옮기면 스킨이 터진다. 스킨은 "그 뼈가 원래 어디 있었나(바인드 포즈)"를 행렬로 들고 있어서,
    // 뼈의 배율이 바뀌면 메시가 100배로 부풀거나 쪼그라든다. 그래서 바인드 포즈도 같이 고쳐 적는다 —
    // 뼈의 새 행렬과 새 바인드 포즈를 곱한 값이 예전 둘을 곱한 값과 같게.
    private static void BakeOutScale(GameObject body)
    {
        Transform[] all = body.GetComponentsInChildren<Transform>(true);

        bool scaled = false;
        for (int i = 1; i < all.Length; i++)
            if ((all[i].localScale - Vector3.one).sqrMagnitude > 0.000001f) { scaled = true; break; }
        if (!scaled) return;

        // 뼈마다 지금의 월드 행렬을 적어 둔다. 바인드 포즈를 고칠 때 "예전 값"으로 쓴다.
        SkinnedMeshRenderer[] skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var before = new Matrix4x4[skins.Length][];
        var rootBefore = new Matrix4x4[skins.Length];
        for (int s = 0; s < skins.Length; s++)
        {
            Transform[] bones = skins[s].bones;
            before[s] = new Matrix4x4[bones.Length];
            for (int b = 0; b < bones.Length; b++)
                before[s][b] = bones[b] != null ? bones[b].localToWorldMatrix : Matrix4x4.identity;
            rootBefore[s] = skins[s].rootBone != null ? skins[s].rootBone.localToWorldMatrix : Matrix4x4.identity;
        }

        // 부모부터 배율을 1로 만들고, 월드 위치·회전은 적어 둔 값으로 되돌린다.
        // GetComponentsInChildren는 부모를 먼저 돌려주므로, 자식을 놓을 때는 부모가 이미 정리돼 있다.
        var positions = new Vector3[all.Length];
        var rotations = new Quaternion[all.Length];
        for (int i = 0; i < all.Length; i++) { positions[i] = all[i].position; rotations[i] = all[i].rotation; }
        for (int i = 1; i < all.Length; i++)
        {
            // 스킨이 아닌 메시는 배율이 곧 크기라 건드리지 않는다(Meshy 몸에는 없지만).
            if (all[i].GetComponent<MeshFilter>() != null) continue;
            all[i].localScale = Vector3.one;
            all[i].SetPositionAndRotation(positions[i], rotations[i]);
        }

        for (int s = 0; s < skins.Length; s++)
        {
            SkinnedMeshRenderer skin = skins[s];
            Mesh mesh = skin.sharedMesh;
            if (mesh == null) continue;

            Transform[] bones = skin.bones;
            Matrix4x4[] bindposes = mesh.bindposes;
            for (int b = 0; b < bones.Length && b < bindposes.Length; b++)
            {
                if (bones[b] == null) continue;
                bindposes[b] = bones[b].worldToLocalMatrix * before[s][b] * bindposes[b];
            }
            mesh.bindposes = bindposes;

            // 경계 상자는 루트 뼈 기준으로 적혀 있어서 같이 옮겨 적는다(화면 밖 판정에 쓰인다).
            if (skin.rootBone != null)
            {
                Bounds old = skin.localBounds;
                Matrix4x4 toNew = skin.rootBone.worldToLocalMatrix * rootBefore[s];
                Vector3 center = toNew.MultiplyPoint3x4(old.center);
                Vector3 extents = toNew.MultiplyVector(old.extents);
                skin.localBounds = new Bounds(center, new Vector3(Mathf.Abs(extents.x), Mathf.Abs(extents.y), Mathf.Abs(extents.z)) * 2f);
            }
        }
    }

    // glTFast가 만든 재질을 버리고 게임용 재질로 갈아 끼운다.
    //
    // Meshy GLB의 재질은 게임에 그대로 쓰기 곤란하다. 예전 에디터판(FBX 프리팹) 라엘과 견줘 보니 —
    //   · 베이스컬러 텍스처를 발광(emissive) 슬롯에 세기 1.0으로 한 번 더 걸어 두었다.
    //     조명과 상관없이 텍스처가 제 빛을 내서, 그늘이 지지 않는 납작하고 허옇게 뜬 몸이 된다.
    //   · metallic을 적지 않았다. glTF 규약의 기본값은 1(완전 금속)이라, 옷과 피부가 금속처럼
    //     제 색 대신 주변을 비춘다.
    // 그 에디터판이 정상으로 보였던 재질(금속 0, 매끄러움 0.15짜리 URP Lit)을 에셋으로 떠 두었다
    // (Resources/CharacterBodyMaterial). 그걸 복제해 같은 텍스처를 올린다.
    // Shader.Find 대신 에셋을 복제하는 것은, 에셋이 셰이더와 그 변형을 빌드에 끌고 들어가기 때문이다.
    public const string MaterialResourceName = "CharacterBodyMaterial";

    private static readonly string[] BaseColorProperties = { "baseColorTexture", "_BaseMap", "_MainTex" };

    private static void ApplyBodyMaterial(GameObject body)
    {
        var template = Resources.Load<Material>(MaterialResourceName);
        if (template == null)
        {
            Debug.LogWarning($"[CharacterBodyFactory] 몸 재질 템플릿이 없다: Resources/{MaterialResourceName}. " +
                             "GLB에 딸려 온 재질을 그대로 쓴다 — 발광·금속으로 떠서 에디터판과 달라 보인다.");
            return;
        }

        foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
        {
            Material[] source = renderer.sharedMaterials;
            var replaced = new Material[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var material = new Material(template) { name = body.name + "_Body" };
                Texture baseColor = BaseColorOf(source[i]);
                if (baseColor != null) material.SetTexture("_BaseMap", baseColor);
                replaced[i] = material;
            }
            renderer.sharedMaterials = replaced;
        }
    }

    private static Texture BaseColorOf(Material material)
    {
        if (material == null) return null;
        foreach (string property in BaseColorProperties)
        {
            if (!material.HasProperty(property)) continue;
            Texture texture = material.GetTexture(property);
            if (texture != null) return texture;
        }
        return null;
    }

    /// 캐릭터가 사라졌을 때(영구 사망, 삭제) 붙잡고 있던 것을 놓는다.
    public static void Forget(string characterId)
    {
        if (Prototypes.TryGetValue(characterId, out GameObject body) && body != null)
            UnityEngine.Object.Destroy(body);
        Prototypes.Remove(characterId);

        if (Imports.TryGetValue(characterId, out GltfImport import)) import?.Dispose();
        Imports.Remove(characterId);
    }
}
