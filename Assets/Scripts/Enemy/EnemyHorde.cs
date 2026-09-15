using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

// 적을 만들고 지우는 유일한 입구. 관리 쪽(전투 매니저, 스포너)이 부르는 자리다.
//
// 서브신 베이킹을 쓰지 않고 코드로 원본 엔티티를 세운다. 베이커는 서브신 안의 게임오브젝트에만
// 도는데, 적은 층마다 마리 수가 달라지는 순수 데이터라 씬에 미리 놓아 둘 것이 없다.
//
// 한 마리씩 만들지 않고 원본 하나를 세워 두고 복제한다(Instantiate). 렌더러 컴포넌트를
// 마리마다 붙이면 그때마다 청크 구조가 바뀌어 1000마리 스폰이 통째로 한 프레임을 잡아먹는다 —
// 원본에 한 번만 붙여 두면 복제는 메모리 복사에 가깝다.
public static class EnemyHorde
{
    private static World cachedWorld;
    private static Entity prototype;

    private static Mesh visualMesh;
    private static Material visualMaterial;
    private static EnemyAnimationLibrary animationLibrary;

    private static EntityManager Manager
    {
        get
        {
            World world = World.DefaultGameObjectInjectionWorld;
            return world != null ? world.EntityManager : default;
        }
    }

    // 무엇으로 그릴지 알려 준다. 스포너가 전투를 열 때 한 번 부른다.
    //
    // 이걸 부르지 않으면 적은 보이지 않는 채로 시뮬레이션만 돈다. 테스트는 그 상태로 돌리므로
    // (씬도 렌더러도 없다) 여기서 없으면 그냥 넘어가야 한다.
    public static void ConfigureVisual(Mesh mesh, Material material, EnemyAnimationLibrary library)
    {
        if (visualMesh == mesh && visualMaterial == material && animationLibrary == library) return;

        visualMesh = mesh;
        visualMaterial = material;
        animationLibrary = library;

        // 클립 구간표를 잡에서 볼 수 있는 자리에 올려 둔다.
        EnemyAnimationLookupSetup.Publish(library);

        // 원본을 다시 세워야 한다.
        DestroyPrototype();
    }

    private static void DestroyPrototype()
    {
        if (prototype == Entity.Null) return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated && world.EntityManager.Exists(prototype))
        {
            world.EntityManager.DestroyEntity(prototype);
        }

        prototype = Entity.Null;
    }

    private static bool TryGetPrototype(out Entity result)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            result = Entity.Null;
            return false;
        }

        if (cachedWorld != world)
        {
            cachedWorld = world;
            prototype = Entity.Null;
        }

        if (prototype != Entity.Null && world.EntityManager.Exists(prototype))
        {
            result = prototype;
            return true;
        }

        EntityManager manager = world.EntityManager;

        prototype = manager.CreateEntity(
            typeof(EnemyTag),
            typeof(EnemyStats),
            typeof(EnemyHealth),
            typeof(EnemyMotion),
            typeof(EnemyTarget),
            typeof(EnemyAction),
            typeof(EnemyAnimation),
            typeof(LocalTransform),
            typeof(LocalToWorld));

        // 원본은 시뮬레이션에 참여하면 안 된다. Prefab 태그가 붙은 엔티티는 모든 질의에서
        // 제외되므로, 표적을 찾지도 움직이지도 않고 스냅샷에도 실리지 않는다.
        manager.AddComponent<Prefab>(prototype);

        if (visualMesh != null && visualMaterial != null)
        {
            var description = new RenderMeshDescription(ShadowCastingMode.On, receiveShadows: true);
            var renderMeshArray = new RenderMeshArray(
                new[] { visualMaterial },
                new[] { visualMesh });

            RenderMeshUtility.AddComponents(
                prototype,
                manager,
                description,
                renderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));

            // 컬링이 쓰는 경계. 스키닝으로 정점이 밀려 나가므로 메시 경계보다 넉넉하게 잡는다 —
            // 딱 맞추면 팔을 뻗는 프레임에 몸이 통째로 사라졌다 나타난다.
            Bounds bounds = visualMesh.bounds;
            manager.AddComponentData(prototype, new RenderBounds
            {
                Value = new AABB
                {
                    Center = bounds.center,
                    Extents = (float3)bounds.extents + new float3(0.5f),
                },
            });

            // 애니메이션을 셰이더에 넘길 자리. 값은 EnemyAnimationRenderSystem이 매 프레임 채운다.
            manager.AddComponentData(prototype, new EnemyAnimationMaterial { Value = float4.zero });
        }

        result = prototype;
        return true;
    }

    // 한 층 분량을 한 번에 만든다.
    public static int Spawn(in EnemyStats stats, int count, Vector3 center, float spread, uint seed = 1)
    {
        if (count <= 0) return 0;
        if (!TryGetPrototype(out Entity source)) return 0;

        EntityManager manager = Manager;
        using var created = manager.Instantiate(source, count, Allocator.Temp);

        var random = Unity.Mathematics.Random.CreateFromIndex(seed);

        // 콤보 단수는 부르는 쪽이 정하지 않는다. 실제로 구워진 클립이 몇 단까지 있는지는
        // 라이브러리만 알고, 없는 단을 가리키면 그 스윙만 서 있는 그림이 되기 때문이다.
        EnemyStats resolved = stats;
        resolved.comboSteps = ResolveComboSteps();

        for (int i = 0; i < created.Length; i++)
        {
            Entity entity = created[i];

            float2 offset = random.NextFloat2Direction() * random.NextFloat(0f, spread);
            float3 position = new float3(center.x + offset.x, center.y, center.z + offset.y);

            manager.SetComponentData(entity, LocalTransform.FromPositionRotation(
                position, quaternion.RotateY(random.NextFloat(0f, math.PI * 2f))));

            manager.SetComponentData(entity, resolved);
            manager.SetComponentData(entity, new EnemyHealth
            {
                current = stats.maxHp,
                poise = stats.maxPoise,
                poiseImmuneUntil = 0d,
                lastAttackerAllyIndex = -1,
            });
            manager.SetComponentData(entity, new EnemyMotion());
            manager.SetComponentData(entity, new EnemyTarget { allyIndex = EnemyTarget.None });
            manager.SetComponentData(entity, new EnemyAction { kind = EnemyActionKind.Idle });

            // 같은 클립이라도 시작 지점을 흩어 놓는다. 그러지 않으면 1000마리가 완전히
            // 같은 박자로 숨 쉬는 그림이 되어, 무리가 아니라 복사본으로 보인다.
            manager.SetComponentData(entity, new EnemyAnimation
            {
                clip = EnemyClip.Idle,
                normalizedTime = random.NextFloat(0f, 1f),
            });
        }

        return created.Length;
    }

    // 굽힌 콤보가 몇 단까지 있는가. Attack(1단)에서 시작해 Attack2부터 끊길 때까지 센다 —
    // 중간이 빈 리그에서 그 구멍을 건너뛰어 재생하면 콤보가 튀므로, 이어진 데까지만 쓴다.
    private static byte ResolveComboSteps()
    {
        if (animationLibrary == null || !animationLibrary.IsBaked) return 1;
        if (!animationLibrary.TryGetClip(EnemyClip.Attack, out _)) return 1;

        int steps = 1;
        for (int i = 0; i < 6; i++)
        {
            EnemyClip next = (EnemyClip)((int)EnemyClip.Attack2 + i);
            if (!animationLibrary.TryGetClip(next, out _)) break;
            steps++;
        }

        return (byte)steps;
    }

    // ---------------------------------------------------------------- 피

    // 무엇으로 피를 뿌릴지. 프리팹과 색은 관리 객체라 엔티티에 실을 수 없어 여기 둔다.
    // 스포너가 그릴 것을 알려 줄 때 함께 넘긴다.
    private static GameObject[] bloodPrefabs;
    private static Color bloodColor = new Color(0.2f, 0.6f, 0.1f, 1f);
    private static Vector3 bloodOffset = new Vector3(0f, 1f, 0f);

    public static void ConfigureBlood(GameObject[] prefabs, Color color, Vector3 offset)
    {
        bloodPrefabs = prefabs;
        bloodColor = color;
        bloodOffset = offset;
    }

    // 큐에 쌓인 자리에 피를 뿌린다. 메인 스레드에서만 부른다(EnemyBridgeOutputSystem).
    //
    // 프리팹이 없으면 큐만 비운다 — 그러지 않으면 전투 내내 쌓이기만 한다.
    public static void DrainBlood()
    {
        if (!EnemyWorldBridge.IsReady || !EnemyWorldBridge.BloodOnEnemies.IsCreated) return;

        bool canSpawn = bloodPrefabs != null && bloodPrefabs.Length > 0 && BloodEffectPool.Instance != null;

        while (EnemyWorldBridge.BloodOnEnemies.TryDequeue(out EnemyWorldBridge.BloodOnEnemy blood))
        {
            if (!canSpawn) continue;

            GameObject prefab = bloodPrefabs[UnityEngine.Random.Range(0, bloodPrefabs.Length)];
            if (prefab == null) continue;

            // 때린 쪽을 향해 튀게 한다. 아군 쪽 SpawnBloodEffect와 같은 규칙이다.
            Vector3 position = (Vector3)blood.position + bloodOffset;
            Vector3 toAttacker = (Vector3)blood.fromPosition - (Vector3)blood.position;
            toAttacker.y = 0f;
            Quaternion rotation = toAttacker.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(toAttacker.normalized)
                : Quaternion.identity;

            BloodEffectPool.Instance.Spawn(prefab, position, rotation, bloodColor);
        }
    }

    // 전투가 끝났을 때 남은 적을 치운다.
    public static void Clear()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        EntityManager manager = world.EntityManager;
        using var query = manager.CreateEntityQuery(typeof(EnemyTag));
        manager.DestroyEntity(query);
    }
}
