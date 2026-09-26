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
            deltaTime = SystemAPI.Time.DeltaTime,
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    // 클립이 바뀔 때 앞 클립을 섞어 내보내는 시간(초).
    //
    // 스윙·피격·도약처럼 게임플레이가 시작 프레임을 정해 둔 동작은 짧게 — 아군 Animator와 같은
    // 0.08초보다 조금 길다. 그보다 길면 타격 순간(attackWindup)과 보이는 칼끝이 어긋난다.
    // 제자리걸음 ↔ 걷기 ↔ 달리기는 속도 문턱을 오가며 가장 자주 바뀌고(위 230번 중 208번),
    // 발을 옮기는 박자가 서로 달라 짧게 섞으면 다리가 튄다. 그래서 길게 잡는다.
    private const float ActionFade = 0.1f;
    private const float LocomotionFade = 0.2f;

    private static bool IsLocomotion(EnemyClip clip)
    {
        return clip == EnemyClip.Idle || clip == EnemyClip.GuardIdle ||
               clip == EnemyClip.Walk || clip == EnemyClip.Run;
    }

    // 구간표 배열은 Publish가 Persistent로 만들지만, 그쪽은 다시 Publish될 때만 옛 배열을 치운다.
    // 플레이를 끝내 월드가 통째로 사라질 때는 아무도 치우지 않아 다음 도메인 리로드에 누수로 보고됐다
    // ("Leak Detected : Persistent allocates 1 individual allocations"). 월드가 내려갈 때 여기서 함께 치운다.
    public void OnDestroy(ref SystemState state)
    {
        // 지난 프레임에 걸어 둔 잡이 아직 이 배열을 읽고 있을 수 있다.
        state.CompleteDependency();

        if (!SystemAPI.TryGetSingleton(out EnemyAnimationLookup lookup)) return;
        if (lookup.clipRanges.IsCreated) lookup.clipRanges.Dispose();
    }

    [BurstCompile]
    private partial struct WriteAnimationJob : IJobEntity
    {
        [ReadOnly] public NativeArray<float4> clipRanges;
        public float textureHeight;
        public float deltaTime;

        private void Execute(ref EnemyAnimationMaterial material, ref EnemyAnimationFadeMaterial fadeMaterial,
            ref EnemyAnimationFade fade, in EnemyAnimation animation)
        {
            int index = (int)animation.clip;
            if (index < 0 || index >= clipRanges.Length) return;

            float4 range = clipRanges[index];
            float time = math.saturate(animation.normalizedTime);

            // x = 시작 줄, y = 프레임 수, z = 진행도, w = 텍스처 세로 크기.
            // 셰이더는 이 넷으로 읽을 줄 번호(와 그 다음 줄로 넘어가는 비율)를 계산한다.
            material.Value = new float4(range.x, range.y, time, textureHeight);

            TrackFade(ref fade, animation.clip, time);
            fadeMaterial.Value = FadeData(fade);
        }

        // 지난 프레임과 비교해 클립이 바뀌었는지 보고, 바뀌었으면 앞 클립을 빠져나가는 쪽으로 넘긴다.
        private void TrackFade(ref EnemyAnimationFade fade, EnemyClip clip, float time)
        {
            if (!fade.initialized)
            {
                fade.initialized = true;
                fade.lastClip = clip;
                fade.lastTime = time;
                fade.lastRate = 0f;
                fade.fadeRemaining = 0f;
                return;
            }

            // 같은 클립을 처음부터 다시 트는 것도 바뀐 것이다(같은 방향에서 연달아 맞는 피격 등).
            // 도는 클립은 끝에서 처음으로 넘어가는 것이 정상이라 여기서 빼야 한다.
            bool restarted = clip == fade.lastClip && !IsLocomotion(clip) && time < fade.lastTime - 0.1f;

            if (clip != fade.lastClip || restarted)
            {
                // 빠져나가는 클립은 지금까지 가던 속도로 조금 더 간다. 멈춘 자세에서 섞으면
                // 달리던 다리가 공중에서 굳었다가 풀리는 것처럼 보인다.
                fade.fromClip = fade.lastClip;
                fade.fromTime = fade.lastTime;
                fade.fromRate = fade.lastRate;
                fade.fadeDuration = IsLocomotion(clip) && IsLocomotion(fade.lastClip) ? LocomotionFade : ActionFade;
                fade.fadeRemaining = fade.fadeDuration;
                fade.lastRate = 0f;
            }
            else if (deltaTime > 0f)
            {
                float step = time - fade.lastTime;
                // 도는 클립이 한 바퀴를 넘긴 프레임. 1에서 0으로 떨어진 만큼을 되돌려 잰다.
                if (IsLocomotion(clip) && step < -0.5f) step += 1f;
                fade.lastRate = step / deltaTime;
            }

            fade.lastClip = clip;
            fade.lastTime = time;

            if (fade.fadeRemaining <= 0f) return;

            fade.fadeRemaining = math.max(0f, fade.fadeRemaining - deltaTime);
            fade.fromTime += fade.fromRate * deltaTime;
            fade.fromTime = IsLocomotion(fade.fromClip) ? math.frac(fade.fromTime) : math.saturate(fade.fromTime);
        }

        // x = 앞 클립 시작 줄, y = 프레임 수, z = 진행도, w = 앞 클립이 섞이는 비율.
        private float4 FadeData(in EnemyAnimationFade fade)
        {
            int index = (int)fade.fromClip;
            if (fade.fadeRemaining <= 0f || fade.fadeDuration <= 0f || index < 0 || index >= clipRanges.Length)
                return float4.zero;

            float4 range = clipRanges[index];
            // 선형으로 빼면 섞이기 시작하는 순간과 끝나는 순간에 꺾임이 보인다. 양 끝을 눕힌다.
            float weight = math.smoothstep(0f, 1f, fade.fadeRemaining / fade.fadeDuration);
            return new float4(range.x, range.y, fade.fromTime, weight);
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
