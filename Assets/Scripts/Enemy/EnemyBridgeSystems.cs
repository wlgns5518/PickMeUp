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
    protected override void OnUpdate()
    {
        // 적 시뮬레이션 잡이 끝나야 위치와 상태가 확정된다.
        Dependency.Complete();

        if (!EnemyWorldBridge.IsReady) return;

        var enemies = EnemyWorldBridge.EnemyStates;
        enemies.Clear();

        foreach (var (transform, health, action, target, stats, entity) in
                 SystemAPI.Query<RefRO<LocalTransform>, RefRO<EnemyHealth>, RefRO<EnemyAction>,
                     RefRO<EnemyTarget>, RefRO<EnemyStats>>().WithAll<EnemyTag>().WithEntityAccess())
        {
            enemies.Add(new EnemyWorldBridge.EnemyState
            {
                entity = entity,
                position = transform.ValueRO.Position,
                forward = transform.ValueRO.Forward(),
                radius = stats.ValueRO.radius,
                hp = health.ValueRO.current,
                maxHp = stats.ValueRO.maxHp,
                poise = health.ValueRO.poise,
                threatWeight = stats.ValueRO.threatWeight,
                targetAllyIndex = target.ValueRO.allyIndex,
                action = action.ValueRO.kind,
            });
        }

        // 손잡이로 찾을 수 있게 표를 다시 세운다. 아군이 표적으로 들고 있는 Entity를
        // 이번 프레임의 값으로 푸는 자리다(TargetRef).
        EnemyWorldBridge.RebuildEnemyIndex();

        // 적이 아군을 때린 것을 실제 UnitController로 흘려보낸다.
        EnemyWorldBridge.DrainHitsOnAllies();

        // 쓰러진 적을 때린 아군의 처치로 얹고, 전투 매니저에게 알린다.
        EnemyWorldBridge.DrainKills();
    }
}

// 아군이 적에게 준 피해를 적용한다. 강인도·경직·사망이 전부 여기서 갈린다.
//
// 큐로 받는 이유는 아군이 메인 스레드에서(애니메이션 이벤트로) 때리기 때문이다. 그 자리에서
// 엔티티를 건드리면 시뮬레이션 도중에 구조가 바뀌고, 그러면 돌고 있던 잡이 전부 무효가 된다.
[UpdateInGroup(typeof(EnemySimulationGroup), OrderFirst = true)]
public partial struct EnemyDamageSystem : ISystem
{
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

        while (bridge.hitsOnEnemies.TryDequeue(out EnemyWorldBridge.HitOnEnemy hit))
        {
            if (!healthLookup.HasComponent(hit.enemy)) continue;

            EnemyHealth health = healthLookup[hit.enemy];
            EnemyAction action = actionLookup[hit.enemy];
            EnemyStats stats = statsLookup[hit.enemy];
            EnemyAnimation animation = animationLookup[hit.enemy];

            if (action.kind == EnemyActionKind.Dead) continue;

            // 뒤를 잡혔으면 더 아프다. 아군 쪽 backstabDamageMultiplier와 같은 규칙인데,
            // 여기서는 적이 맞는 쪽이라 아군의 배후 공격에 값이 붙는다.
            LocalTransform transform = transformLookup[hit.enemy];
            float3 toAttacker = hit.fromPosition - transform.Position;
            toAttacker.y = 0f;
            bool fromBehind = math.lengthsq(toAttacker) > 0.0001f &&
                              math.dot(math.normalizesafe(transform.Forward()), math.normalize(toAttacker)) < 0f;

            int damage = hit.damage;
            float poiseDamage = hit.poiseDamage;
            if (fromBehind)
            {
                damage = (int)math.round(damage * 1.6f);
                poiseDamage *= 2f;
            }

            health.current -= damage;

            // 마지막으로 때린 쪽을 남긴다. 이 적이 쓰러지면 그 아군의 처치가 된다.
            // 흘려내기(피해 0)로는 갱신하지 않는다 — 쳐낸 것이 처치의 공은 아니다.
            if (damage > 0) health.lastAttackerAllyIndex = hit.attackerAllyIndex;

            if (health.current <= 0)
            {
                health.current = 0;
                action.kind = EnemyActionKind.Dead;

                // 처치를 알린다. 실제 귀속은 메인 스레드가 큐를 비우며 한다(DrainKills).
                bridge.kills.Enqueue(new EnemyWorldBridge.EnemyKill
                {
                    allyIndex = health.lastAttackerAllyIndex,
                });
                // 쓰러지는 모션이 끝나면 엔티티를 지운다(EnemyCleanupSystem).
                action.timer = 1.2f;
                animation.clip = EnemyClip.Death;
                animation.normalizedTime = 0f;

                healthLookup[hit.enemy] = health;
                actionLookup[hit.enemy] = action;
                animationLookup[hit.enemy] = animation;
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

            if (broken)
            {
                // 무너진 뒤에는 잠깐 면역이다. 이게 없으면 여럿에게 둘러싸인 순간
                // 다시 일어나지 못한다 — 아군 쪽 poiseBreakImmunity와 같은 이유다.
                health.poise = stats.maxPoise;
                health.poiseImmuneUntil = now + stats.poiseBreakImmunity;

                action.kind = EnemyActionKind.Stagger;
                action.timer = math.max(0.3f, staggerDuration);
                action.struckThisSwing = true;
                animation.clip = EnemyClip.Stagger;
                animation.normalizedTime = 0f;
            }
            else if (action.kind != EnemyActionKind.Stagger)
            {
                // 강인도가 안 깨졌으면 짧게 움찔하고 만다. 이미 나간 스윙은 끊지 않는다 —
                // 아군 쪽 "슈퍼아머는 아니지만 콤보 마무리만 진짜 경직을 준다"와 같은 무게다.
                if (action.kind != EnemyActionKind.Windup && action.kind != EnemyActionKind.Recover)
                {
                    action.kind = EnemyActionKind.HitReact;
                    action.timer = stats.hitReactionDuration;
                    animation.clip = EnemyClip.Hit;
                    animation.normalizedTime = 0f;
                }
            }

            healthLookup[hit.enemy] = health;
            actionLookup[hit.enemy] = action;
            animationLookup[hit.enemy] = animation;
        }
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

        foreach (var (action, animation, entity) in
                 SystemAPI.Query<RefRW<EnemyAction>, RefRW<EnemyAnimation>>().WithAll<EnemyTag>().WithEntityAccess())
        {
            if (action.ValueRO.kind != EnemyActionKind.Dead) continue;

            action.ValueRW.timer -= deltaTime;
            animation.ValueRW.normalizedTime = math.saturate(animation.ValueRO.normalizedTime + deltaTime / 1.2f);

            if (action.ValueRO.timer <= 0f) ecb.DestroyEntity(entity);
        }
    }
}
