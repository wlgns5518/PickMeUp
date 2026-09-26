using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// 브리지의 양 끝. 시뮬레이션 앞뒤에 하나씩 붙는다.
//
// 이 두 시스템만 관리 코드(UnitRegistry, UnitController)를 만진다. 나머지 적 시스템은
// 전부 Burst 잡이고 관리 참조를 하나도 모른다 — 그 경계를 여기 두 파일에 가둬 두는 것이
// 이 구조의 값어치다.

// 시뮬레이션 앞. 아군 상태를 잡이 읽을 수 있는 배열로 옮겨 적는다.
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class EnemyBridgeInputSystem : SystemBase
{
    protected override void OnCreate()
    {
        EnemyWorldBridge.Initialize();

        // 잡에서 볼 손잡이를 싱글턴으로 올려 둔다(EnemyWorldBridge.BridgeData 주석 참조).
        EntityManager.CreateSingleton(EnemyWorldBridge.AsComponent());
    }

    protected override void OnDestroy()
    {
        EnemyWorldBridge.Dispose();
    }

    protected override void OnUpdate()
    {
        // 지난 프레임의 잡이 아직 이 배열을 읽고 있을 수 있다. 덮어쓰기 전에 반드시 맞춘다.
        Dependency.Complete();

        EnemyWorldBridge.PublishAllies(UnitRegistry.Allies);
    }
}

// 시뮬레이션 뒤. 적 상태를 아군이 읽을 배열로 내보내고, 밀린 피해를 아군에게 흘린다.
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
public partial class EnemyBridgeOutputSystem : SystemBase
{
    private EntityQuery enemyQuery;

    protected override void OnCreate()
    {
        enemyQuery = SystemAPI.QueryBuilder()
            .WithAll<EnemyTag, LocalTransform, EnemyHealth, EnemyAction, EnemyTarget, EnemyStats>()
            .Build();
    }

    protected override void OnUpdate()
    {
        // 적 시뮬레이션 잡이 끝나야 위치와 상태가 확정된다.
        Dependency.Complete();

        if (!EnemyWorldBridge.IsReady) return;

        // 한 마리씩 옮겨 적는 일은 Burst로 한다. 예전에는 이 시스템(관리 코드) 안의 foreach였는데,
        // 1000마리에서 그 복사만으로 메인 스레드 0.27ms가 나가 적 시뮬레이션 전체(0.19ms)보다 비쌌다.
        // 자리는 질의 순서(EntityIndexInQuery)로 정하므로 예전 foreach와 같은 순서로 채워진다.
        var enemies = EnemyWorldBridge.EnemyStates;
        enemies.ResizeUninitialized(enemyQuery.CalculateEntityCount());
        new SnapshotJob { enemies = enemies.AsArray() }.Run(enemyQuery);

        // 손잡이로 찾을 수 있게 표를 다시 세운다. 아군이 표적으로 들고 있는 Entity를
        // 이번 프레임의 값으로 푸는 자리다(TargetRef).
        EnemyWorldBridge.RebuildEnemyIndex();

        // 적이 누구를 겨누는지 아군 쪽 머릿수로 옮겨 둔다. 다음 프레임의 표적 점수가 이걸 읽는다.
        EnemyWorldBridge.CountEntityAttackers();

        // 적이 아군을 때린 것을 실제 UnitController로 흘려보낸다.
        EnemyWorldBridge.DrainHitsOnAllies();

        // 쓰러진 적을 때린 아군의 처치로 얹고, 전투 매니저에게 알린다.
        EnemyWorldBridge.DrainKills();

        // 살에 닿은 자리에 피를 뿌린다. 파티클은 관리 객체라 잡 안에서 만들 수 없어
        // 자리만 큐로 넘어온다(EnemyHorde.DrainBlood).
        EnemyHorde.DrainBlood();
    }

    [BurstCompile]
    private partial struct SnapshotJob : IJobEntity
    {
        public NativeArray<EnemyWorldBridge.EnemyState> enemies;

        private void Execute([EntityIndexInQuery] int index, Entity entity, in LocalTransform transform,
            in EnemyHealth health, in EnemyAction action, in EnemyTarget target, in EnemyStats stats)
        {
            enemies[index] = new EnemyWorldBridge.EnemyState
            {
                entity = entity,
                position = transform.Position,
                forward = transform.Forward(),
                radius = stats.radius,
                hp = health.current,
                maxHp = stats.maxHp,
                poise = health.poise,
                threatWeight = stats.threatWeight,
                targetAllyIndex = target.allyIndex,
                action = action.kind,
                lungeOrBiteIncoming = (byte)((action.kind == EnemyActionKind.Leap || action.kind == EnemyActionKind.Bite) &&
                                        !action.struckThisSwing ? 1 : 0),
            };
        }
    }
}

// 아군이 적에게 준 피해를 적용한다. 강인도·경직·사망이 전부 여기서 갈린다.
//
// 큐로 받는 이유는 아군이 메인 스레드에서(애니메이션 이벤트로) 때리기 때문이다. 그 자리에서
// 엔티티를 건드리면 시뮬레이션 도중에 구조가 바뀌고, 그러면 돌고 있던 잡이 전부 무효가 된다.
[UpdateInGroup(typeof(EnemySimulationGroup), OrderFirst = true)]
public partial struct EnemyDamageSystem : ISystem
{
    // 쓰러진 뒤 엔티티가 남아 있는 시간. 쓰러지는 모션이 이 시간에 딱 맞춰 끝나야 하므로
    // 지우는 쪽(EnemyCleanupSystem)과 같은 값을 써야 한다.
    internal const float CorpseLinger = 1.2f;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out EnemyWorldBridge.BridgeData bridge)) return;
        if (!bridge.hitsOnEnemies.IsCreated || bridge.hitsOnEnemies.Count == 0) return;

        // 아군 쪽이 메인 스레드에서 쌓아 둔 것을 여기서 한 번에 꺼낸다.
        state.Dependency.Complete();

        double now = SystemAPI.Time.ElapsedTime;
        var healthLookup = SystemAPI.GetComponentLookup<EnemyHealth>();
        var actionLookup = SystemAPI.GetComponentLookup<EnemyAction>();
        var animationLookup = SystemAPI.GetComponentLookup<EnemyAnimation>();
        var statsLookup = SystemAPI.GetComponentLookup<EnemyStats>(true);
        var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>();
        var targetLookup = SystemAPI.GetComponentLookup<EnemyTarget>();
        var tacticsLookup = SystemAPI.GetComponentLookup<EnemyTactics>();
        var impactLookup = SystemAPI.GetComponentLookup<EnemyImpact>();
        var emotionLookup = SystemAPI.GetComponentLookup<EnemyEmotion>();
        var profileLookup = SystemAPI.GetComponentLookup<EnemyEmotionProfile>(true);

        // 쓰러진 자리를 남길 곳. 감정 시스템이 없는 월드(테스트)에는 없다.
        bool hasDeathLog = SystemAPI.TryGetSingleton(out EnemyDeathLog deathLog) && deathLog.positions.IsCreated;

        while (bridge.hitsOnEnemies.TryDequeue(out EnemyWorldBridge.HitOnEnemy hit))
        {
            if (!healthLookup.HasComponent(hit.enemy)) continue;

            EnemyHealth health = healthLookup[hit.enemy];
            EnemyAction action = actionLookup[hit.enemy];
            EnemyStats stats = statsLookup[hit.enemy];
            EnemyAnimation animation = animationLookup[hit.enemy];

            if (action.kind == EnemyActionKind.Dead) continue;

            bool hasImpact = impactLookup.HasComponent(hit.enemy);
            EnemyImpact impact = hasImpact ? impactLookup[hit.enemy] : default;
            bool hasTactics = tacticsLookup.HasComponent(hit.enemy);
            EnemyTactics tactics = hasTactics ? tacticsLookup[hit.enemy] : default;
            bool hasEmotion = emotionLookup.HasComponent(hit.enemy) && profileLookup.HasComponent(hit.enemy) &&
                              profileLookup[hit.enemy].enabled;
            EnemyEmotion emotion = hasEmotion ? emotionLookup[hit.enemy] : default;
            EnemyEmotionProfile profile = hasEmotion ? profileLookup[hit.enemy] : default;

            // 발을 묶는 것은 피해와 따로 온다(마법은 둘을 따로 건다). 더 센 쪽이 이기고,
            // 같은 세기면 더 오래 가는 쪽으로 늘린다 — 아군 쪽 ApplySlow와 같은 규칙이다.
            if (hit.slowDuration > 0f && hit.slowMultiplier < 1f)
            {
                var motionLookup = SystemAPI.GetComponentLookup<EnemyMotion>();
                if (motionLookup.HasComponent(hit.enemy))
                {
                    EnemyMotion motion = motionLookup[hit.enemy];
                    double until = now + hit.slowDuration;
                    bool stronger = hit.slowMultiplier < motion.slowMultiplier || motion.slowUntil <= now;
                    if (stronger)
                    {
                        motion.slowMultiplier = hit.slowMultiplier;
                        motion.slowUntil = until;
                    }
                    else if (until > motion.slowUntil)
                    {
                        motion.slowUntil = until;
                    }

                    motionLookup[hit.enemy] = motion;
                }
            }

            // 발만 묶고 몸에는 닿지 않은 것(빙결·억제가 피해와 따로 온 경우)은 여기서 끝난다.
            //
            // 예전에는 이것도 아래로 흘러가 움찔(HitReact)을 틀었다 — 피해 0짜리 둔화 한 줄이
            // 휘두르려던 고블린을 매번 끊고 모션을 처음부터 다시 틀었다.
            bool struck = hit.damage > 0 || hit.poiseDamage > 0f || hit.forceStagger;
            if (!struck) continue;

            LocalTransform transform = transformLookup[hit.enemy];

            // 뒤를 잡혔으면 더 아프다. 아군 쪽 backstabDamageMultiplier와 같은 규칙인데,
            // 여기서는 적이 맞는 쪽이라 아군의 배후 공격에 값이 붙는다.
            // 출혈은 흐르던 피라 어디서 맞았는지가 없다.
            float3 toAttacker = hit.fromPosition - transform.Position;
            toAttacker.y = 0f;
            bool fromBehind = !hit.bleed && math.lengthsq(toAttacker) > 0.0001f &&
                              math.dot(math.normalizesafe(transform.Forward()), math.normalize(toAttacker)) < 0f;

            int damage = hit.damage;
            float poiseDamage = hit.poiseDamage;
            if (!hit.bleed)
            {
                float damageMultiplier = 1f;
                if (fromBehind)
                {
                    damageMultiplier *= 1.6f;
                    poiseDamage *= 2f;
                }

                // 무방비일 때 받는 배율. 게임오브젝트 고블린은 맞는 쪽 TakeDamage가 이 둘을 곱했는데
                // 엔티티로 옮기면서 빠져, 칼을 거두는 틈에 받아쳐도 무너진 놈을 두들겨도 똑같이 아팠다.
                //  · 칼을 내지르고 거두는 중(평타 회수, 도약 착지 뒤) — recoveryVulnerabilityMultiplier
                //  · 자세가 무너져 있는 중 — staggerDamageMultiplier
                if (IsRecovering(action)) damageMultiplier *= OrOne(stats.recoveryVulnerabilityMultiplier);
                if (action.kind == EnemyActionKind.Stagger) damageMultiplier *= OrOne(stats.staggerDamageMultiplier);

                damage = (int)math.round(damage * damageMultiplier);
            }

            health.current -= damage;

            // 잃은 만큼 겁을 먹고, 때린 쪽 직군이 남긴 출혈이 걸린다(UnitEmotion.NotifyDamaged와
            // UnitController.ApplyOnHitDebuffs). 급소 타격은 뒤에서 그었을 때 두 배로 잘 걸린다.
            if (hasEmotion && !hit.bleed && damage > 0)
            {
                emotion.NotifyDamaged(profile, damage, stats.maxHp);

                if (hasTactics && tactics.random.state != 0)
                {
                    if (hit.fromSkill && profile.bleedChanceOnSkillHit > 0f &&
                        tactics.random.NextFloat() < profile.bleedChanceOnSkillHit)
                    {
                        emotion.ApplyBleeding(profile);
                    }

                    float chance = hit.bleedChance * (fromBehind ? 2f : 1f);
                    if (chance > 0f && tactics.random.NextFloat() < chance) emotion.ApplyBleeding(profile);
                }
            }

            // 살에 닿았으면 피가 튄다. 흘려낸 타격(피해 0)에는 뿌리지 않는다 —
            // 아군 쪽도 막아낸 공격에는 피를 뿌리지 않는다. 출혈 틱도 새로 벤 상처가 아니다.
            if (damage > 0 && !hit.bleed && bridge.bloodOnEnemies.IsCreated)
            {
                bridge.bloodOnEnemies.Enqueue(new EnemyWorldBridge.BloodOnEnemy
                {
                    position = transform.Position,
                    fromPosition = hit.fromPosition,
                });
            }

            // 마지막으로 때린 쪽을 남긴다. 이 적이 쓰러지면 그 아군의 처치가 된다.
            // 흘려내기(피해 0)로는 갱신하지 않는다 — 쳐낸 것이 처치의 공은 아니다. 출혈은 걸어 둔 쪽이
            // 이미 남아 있으므로 덮지 않는다.
            if (damage > 0 && !hit.bleed) health.lastAttackerAllyIndex = hit.attackerAllyIndex;

            if (damage > 0 && !hit.bleed) Retaliate(ref targetLookup, hit, bridge);

            if (health.current <= 0)
            {
                health.current = 0;
                action.kind = EnemyActionKind.Dead;

                // 곁에 있던 동료가 이걸 보고 겁을 먹는다(EnemyEmotionSystem).
                if (hasDeathLog) deathLog.positions.Add(transform.Position);

                // 처치를 알린다. 실제 귀속은 메인 스레드가 큐를 비우며 한다(DrainKills).
                bridge.kills.Enqueue(new EnemyWorldBridge.EnemyKill
                {
                    allyIndex = health.lastAttackerAllyIndex,
                });
                // 쓰러지는 모션이 끝나면 엔티티를 지운다(EnemyCleanupSystem).
                action.timer = CorpseLinger;
                action.animationLength = CorpseLinger;
                animation.clip = EnemyClip.Death;
                animation.normalizedTime = 0f;

                // 쥐고 있던 칼 들 자리는 여기서 돌려준다. 다음 프레임에 세는 쪽도 시체를 걸러 내지만,
                // 그 한 프레임 동안 자리가 차 있으면 기다리던 놈의 판단 박자가 그대로 헛돈다.
                if (hasTactics) tactics.ReleaseSlot(now, 0f);

                // 마무리 일격은 조금 더 멀리 밀려 쓰러진다. 피를 흘리다 쓰러진 놈은 제자리에 무너진다.
                if (hasImpact && !hit.bleed) ApplyImpact(ref impact, hit, stats, transform, now, 1.2f, false);

                healthLookup[hit.enemy] = health;
                actionLookup[hit.enemy] = action;
                animationLookup[hit.enemy] = animation;
                if (hasTactics) tacticsLookup[hit.enemy] = tactics;
                if (hasImpact) impactLookup[hit.enemy] = impact;
                if (hasEmotion) emotionLookup[hit.enemy] = emotion;
                continue;
            }

            // 출혈 틱은 HP만 깎는다. 움찔·넉백·멈칫이 없다 — 아군 쪽 TakeBleedDamage와 같다.
            if (hit.bleed)
            {
                healthLookup[hit.enemy] = health;
                continue;
            }

            // 흘려내기(퍼펙트 가드)에 걸렸으면 강인도와 무관하게 그 자리에서 무너진다.
            bool broken = hit.forceStagger;
            float staggerDuration = hit.forceStaggerDuration;

            if (!broken && poiseDamage > 0f && now >= health.poiseImmuneUntil)
            {
                health.poise -= poiseDamage;
                if (health.poise <= 0f)
                {
                    broken = true;
                    staggerDuration = stats.staggerDuration;
                }
            }

            // 넉백 거리는 반응의 무게를 따른다. 무너진 놈이 가장 멀리, 움찔한 놈이 그 절반,
            // 스윙 도중이라 버틴 놈도 반 뼘은 밀린다 — 버틴 한 대가 아무 흔적도 없으면 칼이 몸을
            // 통과한 것처럼 보인다.
            float knockbackScale = 0.15f;

            if (broken)
            {
                // 무너진 뒤에는 잠깐 면역이다. 이게 없으면 여럿에게 둘러싸인 순간
                // 다시 일어나지 못한다 — 아군 쪽 poiseBreakImmunity와 같은 이유다.
                health.poise = stats.maxPoise;
                health.poiseImmuneUntil = now + stats.poiseBreakImmunity;

                action.kind = EnemyActionKind.Stagger;
                action.timer = math.max(0.3f, staggerDuration);
                action.animationLength = action.timer;
                action.struckThisSwing = true;
                animation.clip = EnemyClip.Stagger;
                animation.normalizedTime = 0f;

                knockbackScale = 1f;
                // 무너졌으면 칼 들 자리를 내놓는다. 무너진 몇 초 동안 자리를 쥐고 있으면
                // 곁에서 기다리던 놈이 그 빈틈을 채우지 못한다.
                if (hasTactics) tactics.ReleaseSlot(now, stats.slotYieldDelay);
            }
            else if (action.kind != EnemyActionKind.Stagger)
            {
                // 강인도가 안 깨졌으면 짧게 움찔하고 만다. 이미 나간 스윙은 끊지 않는다 —
                // 아군 쪽 "슈퍼아머는 아니지만 콤보 마무리만 진짜 경직을 준다"와 같은 무게다.
                if (action.kind != EnemyActionKind.Windup && action.kind != EnemyActionKind.Recover)
                {
                    action.kind = EnemyActionKind.HitReact;
                    action.timer = stats.hitReactionDuration;
                    action.animationLength = stats.hitReactionDuration;
                    animation.clip = DirectionalHitClip(transform, hit.fromPosition);
                    animation.normalizedTime = 0f;

                    knockbackScale = 0.5f;
                    // 도약·물기가 끊긴 경우다. 쥐고 있던 자리는 취소로 돌려준다.
                    if (hasTactics) tactics.ReleaseSlot(now, stats.slotYieldDelay);
                }
            }

            if (hasImpact) ApplyImpact(ref impact, hit, stats, transform, now, knockbackScale, !broken && damage > 0);

            healthLookup[hit.enemy] = health;
            actionLookup[hit.enemy] = action;
            animationLookup[hit.enemy] = animation;
            if (hasTactics) tacticsLookup[hit.enemy] = tactics;
            if (hasImpact) impactLookup[hit.enemy] = impact;
            if (hasEmotion) emotionLookup[hit.enemy] = emotion;
        }
    }

    // 칼을 내지르고 거두는 중인가. 평타의 회수 구간과, 도약이 내려앉아 칼을 거두는 뒷부분이다.
    private static bool IsRecovering(in EnemyAction action)
    {
        return action.kind == EnemyActionKind.Recover ||
               (action.kind == EnemyActionKind.Leap && action.struckThisSwing);
    }

    // 굽기 전 원본·테스트처럼 배율을 비워 둔 개체는 1로 본다.
    private static float OrOne(float multiplier) => multiplier > 0f ? multiplier : 1f;

    // 때린 쪽을 돌아본다.
    //
    // 아무도 겨누지 않고 있었으면 곧바로 그쪽이다. 이게 없으면 등지고 서 있던 고블린이 영영 깨어나지
    // 않는다 — 표적이 없으면 움직이지도 돌지도 않는데(EnemyMovementSystem의 facing은 표적이 있어야
    // 잡힌다), 표적을 고르는 쪽은 시야각 안만 보기 때문이다.
    //
    // 이미 겨누는 상대가 있으면 때린 쪽의 어그로가 그 상대보다 크거나 같을 때만 돌아선다
    // (게임오브젝트 고블린의 ShouldSwitchAggroTo). 탱커(3.2)가 사제(0.3)를 물고 있는 놈을 치면 끌려오고,
    // 사제가 탱커와 싸우는 놈을 쳐도 끌려오지 않는다. 옮기는 것은 손이 빌 때다(EnemyTarget.retaliate 주석).
    private static void Retaliate(ref ComponentLookup<EnemyTarget> targetLookup, in EnemyWorldBridge.HitOnEnemy hit,
        in EnemyWorldBridge.BridgeData bridge)
    {
        int attacker = hit.attackerAllyIndex;
        if (attacker < 0 || !bridge.allies.IsCreated || attacker >= bridge.allies.Length) return;
        if (bridge.allies[attacker].alive == 0) return;
        if (!targetLookup.HasComponent(hit.enemy)) return;

        EnemyTarget target = targetLookup[hit.enemy];
        int current = target.allyIndex;
        bool hasTarget = current >= 0 && current < bridge.allies.Length && bridge.allies[current].alive != 0;

        if (!hasTarget)
        {
            target.allyIndex = attacker;
            target.retaliate = false;
        }
        else if (current != attacker &&
                 bridge.allies[attacker].threatWeight >= bridge.allies[current].threatWeight)
        {
            target.retaliate = true;
            target.retaliateAllyIndex = attacker;
        }
        else
        {
            return;
        }

        targetLookup[hit.enemy] = target;
    }

    // 한 대의 무게를 몸에 남긴다 — 멈칫(히트스톱), 밀려남(넉백), 무거워진 발(피격 둔화).
    //
    // 멈칫하는 시간은 때린 쪽이 정해 보낸다(무거운 무기일수록 길다). 밀려나는 거리는 맞은 쪽의
    // 체급(stats.knockbackDistance)에 이 한 방의 무게와 반응의 무게를 곱한다.
    private static void ApplyImpact(ref EnemyImpact impact, in EnemyWorldBridge.HitOnEnemy hit, in EnemyStats stats,
        in LocalTransform transform, double now, float knockbackScale, bool flinch)
    {
        float weight = hit.impactWeight > 0f ? hit.impactWeight : 1f;

        impact.ApplyHitStop(now, hit.hitStopDuration, hit.hitStopScale);

        float3 away = transform.Position - hit.fromPosition;
        away.y = 0f;
        // 때린 쪽과 같은 자리에 겹쳐 있으면 방향이 없다. 그때는 뒤로 민다.
        if (math.lengthsq(away) <= 0.0001f) away = -transform.Forward();
        impact.ApplyKnockback(away, stats.knockbackDistance * knockbackScale * weight);

        if (flinch && stats.hitFlinchDuration > 0f)
        {
            impact.flinchUntil = math.max(impact.flinchUntil, now + stats.hitFlinchDuration);
            impact.flinchMoveMultiplier = stats.hitFlinchMoveMultiplier;
        }
    }

    // 어디서 맞았는지에 맞는 움찔 모션을 고른다. 아군 쪽 ResolveDirectionalHitHash와 같은 규칙이다.
    //
    // 이게 없으면 사방에서 두들겨 맞아도 전부 같은 방향으로 움찔해서, 난전이 "각자 같은
    // 동작을 반복하는 인형들"로 보인다. 굽지 않은 리그는 HitFront 자리가 비어 있을 수 있는데,
    // 그때는 렌더러가 구간표에서 걸러 내므로 여기서 따로 확인하지 않는다.
    private static EnemyClip DirectionalHitClip(in LocalTransform transform, float3 fromPosition)
    {
        float3 toAttacker = fromPosition - transform.Position;
        toAttacker.y = 0f;
        if (math.lengthsq(toAttacker) <= 0.0001f) return EnemyClip.Hit;

        toAttacker = math.normalize(toAttacker);
        float3 forward = math.normalizesafe(transform.Forward(), new float3(0f, 0f, 1f));

        float front = math.dot(forward, toAttacker);
        if (front > 0.5f) return EnemyClip.HitFront;
        if (front < -0.5f) return EnemyClip.HitBack;

        // 좌우는 외적의 y 부호로 가른다.
        float side = math.cross(forward, toAttacker).y;
        return side > 0f ? EnemyClip.HitRight : EnemyClip.HitLeft;
    }
}

// 쓰러진 뒤 모션이 끝나면 지운다.
//
// 아군은 시체를 남기고 Animator만 끄는데(FinalizeDeath), 적은 그럴 수 없다. 1000마리가
// 쌓이면 시체만으로 청크가 가득 차고, 매 프레임 도는 질의가 전부 그 위를 지나가게 된다.
[UpdateInGroup(typeof(EnemySimulationGroup))]
[UpdateAfter(typeof(EnemyMovementSystem))]
public partial struct EnemyCleanupSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        float deltaTime = SystemAPI.Time.DeltaTime;
        double now = SystemAPI.Time.ElapsedTime;

        foreach (var (action, animation, impact, entity) in
                 SystemAPI.Query<RefRW<EnemyAction>, RefRW<EnemyAnimation>, RefRO<EnemyImpact>>()
                     .WithAll<EnemyTag>().WithEntityAccess())
        {
            if (action.ValueRO.kind != EnemyActionKind.Dead) continue;

            // 마무리 일격의 멈칫은 쓰러지는 모션에도 걸린다. 그 한 박자가 "베어 넘겼다"를 만든다.
            float dt = deltaTime * impact.ValueRO.TimeScale(now);

            action.ValueRW.timer -= dt;
            float length = math.max(0.01f, action.ValueRO.animationLength);
            animation.ValueRW.normalizedTime = math.saturate(animation.ValueRO.normalizedTime + dt / length);

            if (action.ValueRO.timer <= 0f) ecb.DestroyEntity(entity);
        }
    }
}
