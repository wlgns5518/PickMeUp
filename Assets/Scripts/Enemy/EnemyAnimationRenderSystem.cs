using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// 엔티티가 들고 있는 애니메이션 상태를 셰이더가 읽을 값으로 옮긴다.
//
// 이 시스템이 하는 일은 그것뿐이다. 뼈를 돌리는 것은 정점 셰이더가 하고, 어느 클립의 몇
// 퍼센트인지는 전투 시스템이 이미 정해 놨다(EnemyAnimation). 그 둘을 잇는 한 줄이 여기다.
//
// 클립 구간표(어느 클립이 텍스처의 몇 번째 줄부터인가)는 매 프레임 만들지 않고 싱글턴에
// 담아 둔다 — 1000마리가 각자 찾으면 그 조회만으로 비용이 붙는다.
public struct EnemyAnimationLookup : IComponentData
{
    public NativeArray<float4> clipRanges;
    public float textureHeight;
}

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct EnemyAnimationRenderSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out EnemyAnimationLookup lookup)) return;
        if (!lookup.clipRanges.IsCreated) return;

        var job = new WriteAnimationJob
        {
            clipRanges = lookup.clipRanges,
            textureHeight = lookup.textureHeight,
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct WriteAnimationJob : IJobEntity
    {
        [ReadOnly] public NativeArray<float4> clipRanges;
        public float textureHeight;

        private void Execute(ref EnemyAnimationMaterial material, in EnemyAnimation animation)
        {
            int index = (int)animation.clip;
            if (index < 0 || index >= clipRanges.Length) return;

            float4 range = clipRanges[index];

            // x = 시작 줄, y = 프레임 수, z = 진행도, w = 텍스처 세로 크기.
            // 셰이더는 이 넷으로 읽을 줄 번호 하나를 계산한다.
            material.Value = new float4(range.x, range.y, math.saturate(animation.normalizedTime), textureHeight);
        }
    }
}

// 구간표를 싱글턴에 올려 두는 자리. EnemyHorde가 처음 그릴 준비를 할 때 채운다.
public static class EnemyAnimationLookupSetup
{
    public static void Publish(EnemyAnimationLibrary library)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated || library == null || !library.IsBaked) return;

        EntityManager manager = world.EntityManager;
        using var query = manager.CreateEntityQuery(typeof(EnemyAnimationLookup));

        if (query.CalculateEntityCount() > 0)
        {
            EnemyAnimationLookup existing = query.GetSingleton<EnemyAnimationLookup>();
            if (existing.clipRanges.IsCreated) existing.clipRanges.Dispose();
            manager.DestroyEntity(query);
        }

        manager.CreateSingleton(new EnemyAnimationLookup
        {
            clipRanges = library.BuildLookup(Allocator.Persistent),
            textureHeight = library.boneTexture.height,
        });
    }
}
