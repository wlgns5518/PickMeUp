using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// 적의 감정 — 공포·패닉·빈사·붕괴·출혈.
//
// 게임오브젝트 고블린은 아군과 같은 UnitEmotion을 달고 있었다. 맞아서 HP를 잃을수록, 곁에서 동료가
// 쓰러질수록, HP가 바닥일수록 공포가 차오르고, 차면 능력치가 30% 떨어지고(Fear), 넘치면 2.5초 동안
// 아무것도 못 했다(Panic). 암살자의 급소 타격과 스킬에 맞으면 피를 흘렸다(Bleeding).
// DOTS로 옮기면서 이 컴포넌트가 통째로 빠져, 고블린은 반쯤 죽어도 동료가 몰살돼도 겁먹지 않고
// 출혈에도 걸리지 않았다. 여기서 같은 규칙을 그대로 옮긴다 — 수치도 같은 EmotionProfile에서 온다.
//
// 튜닝값(EnemyEmotionProfile)과 매 프레임 바뀌는 값(EnemyEmotion)을 나눈 이유는 EnemyStats와
// EnemyHealth를 나눈 것과 같다.

// 감정 튜닝값. 스폰할 때 EmotionProfile에서 한 번 구워 온다(From).
public struct EnemyEmotionProfile : IComponentData
{
    // 감정을 굴리는가. 기본값(전부 0)으로는 굴리면 안 된다 — 패닉 문턱이 0이라 태어나자마자 굳는다.
    // From으로 구운 것만 켜진다.
    public bool enabled;

    // 멘탈 저항을 미리 곱해 둔 값. 공포가 오를 때와 식을 때 곱한다(UnitEmotion.FearScale/RecoveryScale).
    public float fearScale;
    public float recoveryScale;

    public float fearThreshold;
    public float fearClearThreshold;
    public float panicThreshold;
    public float fearStatMultiplier;
    public float fearRecoveryPerSecond;

    public float fearPerHpPercentLost;
    public float fearOnAllyDeath;
    public float allyDeathWitnessRange;
    public float lowHpFearPerSecond;
    public float lowHpRatio;

    public float panicDuration;
    public float panicExitFear;

    public float bleedTickInterval;
    public float bleedDamageRatio;
    public float bleedDuration;
    public float bleedChanceOnSkillHit;

    public float dyingHpRatio;

    // 붕괴. 스트레스 누적을 켜 둔 판에서만 쌓인다(CharacterStress.AccumulationEnabled — 아군과 같은 스위치).
    public bool stressEnabled;
    public float stressLimit;
    public float brokenDuration;
    public float brokenExitStress;
    public float stressPerPanic;
    public float stressPerAllyDeath;
    public float initialStress;

    public static EnemyEmotionProfile From(EmotionProfile profile, bool stressEnabled)
    {
        float resist = profile.mental * profile.mentalResistPerPoint;

        return new EnemyEmotionProfile
        {
            enabled = true,
            fearScale = math.max(profile.minFearScale, 1f - resist),
            recoveryScale = 1f + resist,

            fearThreshold = profile.fearThreshold,
            fearClearThreshold = profile.fearClearThreshold,
            panicThreshold = profile.panicThreshold,
            fearStatMultiplier = profile.fearStatMultiplier,
            fearRecoveryPerSecond = profile.fearRecoveryPerSecond,

            fearPerHpPercentLost = profile.fearPerHpPercentLost,
            fearOnAllyDeath = profile.fearOnAllyDeath,
            allyDeathWitnessRange = profile.allyDeathWitnessRange,
            lowHpFearPerSecond = profile.lowHpFearPerSecond,
            lowHpRatio = profile.lowHpRatio,

            panicDuration = profile.panicDuration,
            panicExitFear = profile.panicExitFear,

            bleedTickInterval = profile.bleedTickInterval,
            bleedDamageRatio = profile.bleedDamageRatio,
            bleedDuration = profile.bleedDuration,
            bleedChanceOnSkillHit = profile.bleedChanceOnSkillHit,

            dyingHpRatio = profile.dyingHpRatio,

            stressEnabled = stressEnabled,
            stressLimit = profile.stressLimit,
            brokenDuration = profile.brokenDuration,
            brokenExitStress = profile.brokenExitStress,
            stressPerPanic = profile.stressPerPanic,
            stressPerAllyDeath = profile.stressPerAllyDeath,
            initialStress = profile.stress,
        };
    }
}

// 매 프레임 바뀌는 감정. 규칙은 UnitEmotion과 한 줄씩 대응한다.
public struct EnemyEmotion : IComponentData
{
    public float fear;
    public float panicTimer;
    public float stress;
    public float brokenTimer;
    public float bleedRemaining;
    public float bleedTickTimer;

    // 공포에 빠져 있는가(히스테리시스로 켜고 끈다 — UnitEmotion.RecomputeState).
    public bool fearful;

    // HP가 빈사 선 아래인가.
    public bool dying;

    // 패닉·빈사·붕괴는 스스로 아무것도 못 한다(EmotionState.ActionBlocking).
    public bool IsActionBlocked => panicTimer > 0f || dying || brokenTimer > 0f;

    public bool IsBleeding => bleedRemaining > 0f;

    // 공포에 빠지면 공격력과 이동속도에 곱해진다(원작 설정: 모든 능력치 30% 감소).
    public float StatMultiplier(in EnemyEmotionProfile profile) => fearful ? profile.fearStatMultiplier : 1f;

    public void AddFear(in EnemyEmotionProfile profile, float amount)
    {
        if (amount <= 0f) return;
        fear = math.clamp(fear + amount * profile.fearScale, 0f, profile.panicThreshold);
    }

    public void AddStress(in EnemyEmotionProfile profile, float amount)
    {
        if (!profile.stressEnabled || amount <= 0f) return;
        stress = math.min(stress + amount, profile.stressLimit);
    }

    public void ApplyBleeding(in EnemyEmotionProfile profile)
    {
        bleedRemaining = math.max(bleedRemaining, profile.bleedDuration);
        if (bleedTickTimer <= 0f) bleedTickTimer = profile.bleedTickInterval;
    }

    // 맞아서 HP를 잃었다(UnitEmotion.NotifyDamaged). 잃은 비율만큼 공포가 오른다.
    public void NotifyDamaged(in EnemyEmotionProfile profile, int damage, int maxHp)
    {
        if (damage <= 0) return;
        float lostPercent = damage / (float)math.max(1, maxHp) * 100f;
        AddFear(profile, lostPercent * profile.fearPerHpPercentLost);
    }

    // 감정이 어느 쪽이든 적에게 거는 배율. 감정이 없는 개체(테스트, 굽기 전의 원본)는 1이다.
    public static float StatMultiplier(Entity entity, in ComponentLookup<EnemyEmotion> emotions,
        in ComponentLookup<EnemyEmotionProfile> profiles)
    {
        if (!emotions.HasComponent(entity) || !profiles.HasComponent(entity)) return 1f;

        EnemyEmotionProfile profile = profiles[entity];
        return profile.enabled ? emotions[entity].StatMultiplier(profile) : 1f;
    }

    public static bool IsBlocked(Entity entity, in ComponentLookup<EnemyEmotion> emotions)
    {
        return emotions.HasComponent(entity) && emotions[entity].IsActionBlocked;
    }
}

// 이번 프레임에 쓰러진 적의 자리. 곁에 있던 동료들이 그걸 보고 겁을 먹는다(UnitEmotion.BroadcastAllyDeath).
//
// 쓰러뜨리는 쪽(EnemyDamageSystem)이 적고 감정 시스템이 읽고 비운다. 둘 다 메인 스레드에서 돈다.
public struct EnemyDeathLog : IComponentData
{
    public NativeList<float3> positions;
}

// 공포를 굴리고, 출혈을 흘리고, 패닉·붕괴 시계를 돌린다. 행동을 막는 것은 전투 시스템이 이 값을 보고 한다.
//
// 피해 다음, 표적 고르기 전에 돈다 — 방금 맞은 피해와 방금 쓰러진 동료가 이번 프레임의 판단에 먹어야 한다.
// 출혈 피해는 여기서 HP를 직접 깎지 않고 피해 큐로 보낸다. 쓰러뜨리는 규칙(처치 귀속, 칼 들 자리 반납,
// 쓰러지는 모션)이 피해 시스템 한 곳에만 있어야 둘이 어긋나지 않는다.
[UpdateInGroup(typeof(EnemySimulationGroup))]
[UpdateAfter(typeof(EnemyDamageSystem))]
[UpdateBefore(typeof(EnemyTargetingSystem))]
public partial struct EnemyEmotionSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.CreateSingleton(new EnemyDeathLog
        {
            positions = new NativeList<float3>(16, Allocator.Persistent),
        });
    }

    public void OnDestroy(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out EnemyDeathLog log)) return;
        if (log.positions.IsCreated) log.positions.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out EnemyDeathLog log)) return;

        bool hasBridge = SystemAPI.TryGetSingleton(out EnemyWorldBridge.BridgeData bridge) &&
                         bridge.hitsOnEnemies.IsCreated;
        float dt = SystemAPI.Time.DeltaTime;
        NativeArray<float3> deaths = log.positions.AsArray();

        foreach (var (emotionRef, profileRef, health, stats, action, transform, entity) in
                 SystemAPI.Query<RefRW<EnemyEmotion>, RefRO<EnemyEmotionProfile>, RefRO<EnemyHealth>,
                     RefRO<EnemyStats>, RefRO<EnemyAction>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            ref EnemyEmotion emotion = ref emotionRef.ValueRW;
            EnemyEmotionProfile profile = profileRef.ValueRO;
            if (!profile.enabled) continue;

            if (action.ValueRO.kind == EnemyActionKind.Dead)
            {
                emotion = default;
                continue;
            }

            float3 position = transform.ValueRO.Position;

            // 출혈. 틱마다 최대 HP의 일정 비율을 흘린다(UnitEmotion.UpdateBleeding).
            if (emotion.bleedRemaining > 0f)
            {
                emotion.bleedRemaining -= dt;
                emotion.bleedTickTimer -= dt;
                if (emotion.bleedTickTimer <= 0f)
                {
                    emotion.bleedTickTimer = profile.bleedTickInterval;
                    int damage = math.max(1, (int)math.round(stats.ValueRO.maxHp * profile.bleedDamageRatio));

                    if (hasBridge)
                    {
                        bridge.hitsOnEnemies.Enqueue(new EnemyWorldBridge.HitOnEnemy
                        {
                            enemy = entity,
                            damage = damage,
                            fromPosition = position,
                            // 처치는 출혈을 걸어 둔 쪽에게 간다 — 마지막으로 때린 아군이 그대로 남아 있다.
                            attackerAllyIndex = health.ValueRO.lastAttackerAllyIndex,
                            bleed = true,
                        });
                    }
                }
            }

            // 곁에서 동료가 쓰러지는 것을 봤다(UnitEmotion.BroadcastAllyDeath). 거리만 본다 — 시야를 재면
            // 전멸 구간에서 한 프레임에 수십 번 겹친다.
            float witnessSqr = profile.allyDeathWitnessRange * profile.allyDeathWitnessRange;
            for (int i = 0; i < deaths.Length; i++)
            {
                if (math.distancesq(deaths[i], position) > witnessSqr) continue;
                emotion.AddFear(profile, profile.fearOnAllyDeath);
                emotion.AddStress(profile, profile.stressPerAllyDeath);
            }

            float hpRatio = health.ValueRO.current / (float)math.max(1, stats.ValueRO.maxHp);

            // 공포 게이지. HP가 바닥이면 차오르고, 아니면 식는다. 패닉 중에는 건드리지 않는다.
            if (emotion.panicTimer <= 0f)
            {
                if (hpRatio <= profile.lowHpRatio) emotion.AddFear(profile, profile.lowHpFearPerSecond * dt);
                else emotion.fear = math.max(0f, emotion.fear - profile.fearRecoveryPerSecond * profile.recoveryScale * dt);
            }

            // 패닉. 게이지가 넘치면 정해진 시간 동안 굳고, 풀린 직후 게이지를 임계 아래로 내려 곧바로 다시 굳지 않게 한다.
            if (emotion.panicTimer > 0f)
            {
                emotion.panicTimer -= dt;
                if (emotion.panicTimer <= 0f)
                {
                    emotion.panicTimer = 0f;
                    emotion.fear = math.min(profile.panicExitFear, profile.panicThreshold);
                }
            }
            else if (emotion.fear >= profile.panicThreshold)
            {
                emotion.panicTimer = profile.panicDuration;
                emotion.AddStress(profile, profile.stressPerPanic);
            }

            // 붕괴도 시간이 지나면 풀린다(UnitEmotion.UpdateBroken).
            if (emotion.brokenTimer > 0f)
            {
                emotion.brokenTimer -= dt;
                if (emotion.brokenTimer <= 0f)
                {
                    emotion.brokenTimer = 0f;
                    emotion.stress = math.min(profile.brokenExitStress, profile.stressLimit);
                    if (emotion.stress >= profile.stressLimit) emotion.stress = profile.stressLimit * 0.7f;
                }
            }
            else if (profile.stressEnabled && profile.stressLimit > 0f && emotion.stress >= profile.stressLimit)
            {
                emotion.brokenTimer = math.max(0.1f, profile.brokenDuration);
            }

            // 켤 때와 끌 때의 문턱을 달리 둔다. 경계에서 게이지가 흔들릴 때 매 프레임 깜빡이지 않게.
            float threshold = emotion.fearful ? profile.fearClearThreshold : profile.fearThreshold;
            emotion.fearful = emotion.fear >= threshold || emotion.panicTimer > 0f;
            emotion.dying = hpRatio < profile.dyingHpRatio;
        }

        log.positions.Clear();
    }
}
