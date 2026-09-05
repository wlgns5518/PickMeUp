using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// 적을 만들고 지우는 유일한 입구. 관리 쪽(전투 매니저, 스포너)이 부르는 자리다.
//
// 서브신 베이킹을 쓰지 않고 코드로 아키타입을 세운다. 베이커는 서브신 안의 게임오브젝트에만
// 도는데, 적은 층마다 마리 수가 달라지는 순수 데이터라 씬에 미리 놓아 둘 것이 없다.
// 프리팹에서 구울 것은 결국 메시 하나뿐이고, 그건 렌더링을 붙일 때 따로 들어온다.
public static class EnemyHorde
{
    private static EntityArchetype archetype;
    private static World cachedWorld;

    private static EntityManager Manager
    {
        get
        {
            World world = World.DefaultGameObjectInjectionWorld;
            return world != null ? world.EntityManager : default;
        }
    }

    private static bool TryGetArchetype(out EntityArchetype result)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            result = default;
            return false;
        }

        if (cachedWorld != world)
        {
            cachedWorld = world;
            archetype = world.EntityManager.CreateArchetype(
                typeof(EnemyTag),
                typeof(EnemyStats),
                typeof(EnemyHealth),
                typeof(EnemyMotion),
                typeof(EnemyTarget),
                typeof(EnemyAction),
                typeof(EnemyAnimation),
                typeof(LocalTransform),
                typeof(LocalToWorld));
        }

        result = archetype;
        return true;
    }

    // 한 층 분량을 한 번에 만든다.
    //
    // 마리 수만큼 CreateEntity를 부르지 않고 배열로 한 번에 만드는 것이 중요하다 —
    // 1000번의 개별 생성은 그때마다 청크를 건드려서 스폰 프레임이 통째로 튄다.
    public static int Spawn(in EnemyStats stats, int count, Vector3 center, float spread, uint seed = 1)
    {
        if (count <= 0) return 0;
        if (!TryGetArchetype(out EntityArchetype type)) return 0;

        EntityManager manager = Manager;
        using var created = manager.CreateEntity(type, count, Allocator.Temp);

        var random = Unity.Mathematics.Random.CreateFromIndex(seed);

        for (int i = 0; i < created.Length; i++)
        {
            Entity entity = created[i];

            float2 offset = random.NextFloat2Direction() * random.NextFloat(0f, spread);
            float3 position = new float3(center.x + offset.x, center.y, center.z + offset.y);

            manager.SetComponentData(entity, LocalTransform.FromPositionRotation(
                position, quaternion.RotateY(random.NextFloat(0f, math.PI * 2f))));

            manager.SetComponentData(entity, stats);
            manager.SetComponentData(entity, new EnemyHealth
            {
                current = stats.maxHp,
                poise = stats.maxPoise,
                poiseImmuneUntil = 0d,
            });
            manager.SetComponentData(entity, new EnemyMotion());
            manager.SetComponentData(entity, new EnemyTarget { allyIndex = EnemyTarget.None });
            manager.SetComponentData(entity, new EnemyAction { kind = EnemyActionKind.Idle });
            manager.SetComponentData(entity, new EnemyAnimation { clip = EnemyClip.Idle });
        }

        return created.Length;
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

    public static int AliveCount => EnemyWorldBridge.EnemyCount;
}
