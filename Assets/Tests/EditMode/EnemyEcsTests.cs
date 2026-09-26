using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// 적 ECS 계층이 실제로 도는지 확인한다. 씬도 프리팹도 없이 월드 하나만 세워 놓고 돌린다.
//
// 여기서 고정하는 것:
//  - 아군 스냅샷에서 표적을 고른다(어그로 가중치와 이미 붙은 수까지 본다)
//  - 붙는다(스티어링이 표적 쪽으로 민다)
//  - 사거리 안에서 준비 동작을 거쳐 타격이 큐에 쌓인다(애니메이션 이벤트 없이 시간으로)
//  - 아군이 준 피해가 강인도를 깎고, 깨지면 무너지고, 0이 되면 죽는다
// 넷 다 브리지를 건너는 경로라, 한쪽만 고쳐도 조용히 어긋나기 쉬운 자리다.
public class EnemyEcsTests
{
    private World world;
    private EntityManager manager;

    private SystemHandle hashSystem;
    private SystemHandle thinkSystem;
    private SystemHandle targetingSystem;
    private SystemHandle patrolSystem;
    private SystemHandle slotSystem;
    private SystemHandle combatSystem;
    private SystemHandle movementSystem;
    private SystemHandle damageSystem;

    // 감정은 켠 테스트에서만 돈다(UseEmotion). 나머지 테스트는 감정 없이 예전 규칙 그대로 돈다.
    private SystemHandle emotionSystem;
    private bool emotionEnabled;

    private static EnemyStats DefaultStats() => new EnemyStats
    {
        maxHp = 100,
        attackDamage = 40,
        poiseDamagePerHit = 15f,

        attackRange = 1.2f,
        attackArcAngle = 130f,
        attackHitTolerance = 0.4f,

        attackWindup = 0.4f,
        attackRecovery = 0.35f,
        attackCooldown = 1.1f,

        detectRange = 8f,
        fieldOfView = 160f,

        moveSpeed = 4f,
        acceleration = 8f,
        turnRate = 720f,
        swingTurnRate = 120f,
        radius = 0.5f,
        standoffDistance = 1.0f,

        maxPoise = 100f,
        poiseBreakImmunity = 2.5f,
        staggerDuration = 1.2f,
        hitReactionDuration = 0.3f,
        knockbackDistance = 0.6f,

        threatWeight = 1f,
    };

    [SetUp]
    public void SetUp()
    {
        world = new World("EnemyEcsTests");
        manager = world.EntityManager;

        EnemyWorldBridge.Initialize();
        EnemyWorldBridge.AllyStates.Clear();
        EnemyWorldBridge.EnemyStates.Clear();
        manager.CreateSingleton(EnemyWorldBridge.AsComponent());

        hashSystem = world.CreateSystem<EnemySpatialHashSystem>();
        thinkSystem = world.CreateSystem<EnemyThinkSystem>();
        targetingSystem = world.CreateSystem<EnemyTargetingSystem>();
        patrolSystem = world.CreateSystem<EnemyPatrolSystem>();
        slotSystem = world.CreateSystem<EnemyAttackSlotSystem>();
        combatSystem = world.CreateSystem<EnemyCombatSystem>();
        movementSystem = world.CreateSystem<EnemyMovementSystem>();
        damageSystem = world.CreateSystem<EnemyDamageSystem>();
    }

    [TearDown]
    public void TearDown()
    {
        if (world != null && world.IsCreated) world.Dispose();
        EnemyWorldBridge.Dispose();
        emotionEnabled = false;

        // 구간표는 월드가 내려가도 따라 사라지지 않는다. 실제로는 렌더 시스템이 치우는데
        // (EnemyAnimationRenderSystem.OnDestroy) 여기서는 그 시스템을 세우지 않는다.
        if (publishedClipRanges.IsCreated) publishedClipRanges.Dispose();
    }

    private NativeArray<float4> publishedClipRanges;

    // 구워 둔 클립 구간표를 흉내 낸다. 이게 있어야 시뮬레이션이 걷기 클립을 쓴다.
    private void PublishClipSpeeds(float walkSpeed, float runSpeed)
    {
        int count = System.Enum.GetValues(typeof(EnemyClip)).Length;
        publishedClipRanges = new NativeArray<float4>(count, Allocator.Persistent);

        // x = 시작 줄, y = 프레임 수, z = 길이(초), w = 이 클립이 표현하는 이동 속도.
        for (int i = 0; i < count; i++) publishedClipRanges[i] = new float4(0f, 2f, 1f, 0f);
        publishedClipRanges[(int)EnemyClip.Walk] = new float4(0f, 48f, 0.8f, walkSpeed);
        publishedClipRanges[(int)EnemyClip.Run] = new float4(0f, 52f, 0.867f, runSpeed);

        manager.CreateSingleton(new EnemyAnimationLookup
        {
            clipRanges = publishedClipRanges,
            textureHeight = 1283f,
        });
    }

    private Entity CreateEnemy(float3 position, EnemyStats stats)
    {
        Entity entity = manager.CreateEntity(
            typeof(EnemyTag), typeof(EnemyStats), typeof(EnemyHealth), typeof(EnemyMotion),
            typeof(EnemyTarget), typeof(EnemyAction), typeof(EnemyAnimation),
            typeof(EnemyTactics), typeof(EnemyImpact),
            typeof(LocalTransform), typeof(LocalToWorld));

        manager.SetComponentData(entity, LocalTransform.FromPosition(position));
        manager.SetComponentData(entity, stats);
        manager.SetComponentData(entity, new EnemyHealth { current = stats.maxHp, poise = stats.maxPoise });
        manager.SetComponentData(entity, new EnemyTarget { allyIndex = EnemyTarget.None });
        manager.SetComponentData(entity, new EnemyAction { kind = EnemyActionKind.Idle });
        manager.SetComponentData(entity, new EnemyAnimation { clip = EnemyClip.Idle });
        return entity;
    }

    private void AddAlly(float3 position, float threatWeight = 1f, int attackerCount = 0, bool canBeBitten = true,
        byte attackSlots = 0)
    {
        EnemyWorldBridge.AllyStates.Add(new EnemyWorldBridge.AllyState
        {
            position = position,
            forward = new float3(0f, 0f, 1f),
            radius = 0.5f,
            hp = 100,
            maxHp = 100,
            threatWeight = threatWeight,
            attackerCount = attackerCount,
            alive = 1,
            canBeBitten = (byte)(canBeBitten ? 1 : 0),
            attackSlots = attackSlots,
        });
    }

    // 시간을 흘려보낸다. 시스템들이 SystemAPI.Time을 읽으므로 월드 시계를 직접 민다.
    private void Tick(float deltaTime, int steps = 1)
    {
        for (int i = 0; i < steps; i++)
        {
            double elapsed = world.Time.ElapsedTime + deltaTime;
            world.SetTime(new TimeData(elapsed, deltaTime));

            // 실제 그룹의 순서와 같게 돌린다(EnemySimulationSystems 머리 주석).
            damageSystem.Update(world.Unmanaged);
            if (emotionEnabled) emotionSystem.Update(world.Unmanaged);
            hashSystem.Update(world.Unmanaged);
            thinkSystem.Update(world.Unmanaged);
            targetingSystem.Update(world.Unmanaged);
            patrolSystem.Update(world.Unmanaged);
            slotSystem.Update(world.Unmanaged);
            combatSystem.Update(world.Unmanaged);
            movementSystem.Update(world.Unmanaged);
            world.EntityManager.CompleteAllTrackedJobs();
        }
    }

    [Test]
    public void 시야_안의_아군을_표적으로_잡는다()
    {
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), DefaultStats());
        AddAlly(new float3(0f, 0f, 4f));

        Tick(0.1f);

        Assert.AreEqual(0, manager.GetComponentData<EnemyTarget>(enemy).allyIndex);
    }

    [Test]
    public void 탐지_범위_밖은_잡지_않는다()
    {
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), DefaultStats());
        AddAlly(new float3(0f, 0f, 40f));

        Tick(0.1f);

        Assert.AreEqual(EnemyTarget.None, manager.GetComponentData<EnemyTarget>(enemy).allyIndex);
    }

    [Test]
    public void 어그로가_높은_쪽을_먼저_잡는다()
    {
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), DefaultStats());
        AddAlly(new float3(0f, 0f, 3f));                      // 0번: 가깝지만 평범한 아군
        AddAlly(new float3(0f, 0f, 5f), threatWeight: 3.2f);  // 1번: 조금 멀지만 탱커

        Tick(0.1f);

        Assert.AreEqual(1, manager.GetComponentData<EnemyTarget>(enemy).allyIndex,
            "같은 거리라면 어그로가 높은 쪽이 당겨져야 한다");
    }

    [Test]
    public void 표적_쪽으로_붙는다()
    {
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), DefaultStats());
        AddAlly(new float3(0f, 0f, 6f));

        Tick(0.05f, 20);

        float3 position = manager.GetComponentData<LocalTransform>(enemy).Position;
        Assert.Greater(position.z, 0.5f, "1초 동안 표적 쪽으로 나아가야 한다");
    }

    [Test]
    public void 사거리_안에서_준비_동작을_거쳐_타격이_나간다()
    {
        EnemyStats stats = DefaultStats();
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 1f));

        // 준비 동작(0.4초)이 끝나기 전에는 아무것도 나가지 않는다.
        Tick(0.05f, 4);
        Assert.AreEqual(EnemyActionKind.Windup, manager.GetComponentData<EnemyAction>(enemy).kind);
        Assert.AreEqual(0, EnemyWorldBridge.HitsOnAllies.Count, "준비 동작 도중에 피해가 나가면 안 된다");

        // 지나고 나면 딱 한 번 들어간다.
        Tick(0.05f, 8);
        Assert.AreEqual(1, EnemyWorldBridge.HitsOnAllies.Count);

        EnemyWorldBridge.HitsOnAllies.TryDequeue(out EnemyWorldBridge.HitOnAlly hit);
        Assert.AreEqual(0, hit.allyIndex);
        Assert.AreEqual(stats.attackDamage, hit.damage);
        Assert.AreEqual(enemy, hit.source, "흘려낸 아군이 되받아치려면 때린 놈의 손잡이가 필요하다");
    }

    [Test]
    public void 스윙_도중에_빠져나간_상대는_빗나간다()
    {
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), DefaultStats());
        AddAlly(new float3(0f, 0f, 1f));

        Tick(0.05f, 3);
        Assert.AreEqual(EnemyActionKind.Windup, manager.GetComponentData<EnemyAction>(enemy).kind);

        // 칼이 나가는 사이에 멀리 물러났다.
        EnemyWorldBridge.AllyStates[0] = new EnemyWorldBridge.AllyState
        {
            position = new float3(0f, 0f, 7f),
            forward = new float3(0f, 0f, 1f),
            radius = 0.5f,
            hp = 100,
            maxHp = 100,
            threatWeight = 1f,
            alive = 1,
        };

        Tick(0.05f, 10);

        Assert.AreEqual(0, EnemyWorldBridge.HitsOnAllies.Count,
            "스윙이 시작된 뒤 벗어났으면 빗나가야 한다");
    }

    [Test]
    public void 강인도가_깨지면_무너진다()
    {
        EnemyStats stats = DefaultStats();
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 5f));

        // 강인도 100을 한 번에 깎는다.
        EnemyWorldBridge.DamageEnemy(enemy, 10, 120f, new float3(0f, 0f, 5f));
        Tick(0.05f);

        EnemyAction action = manager.GetComponentData<EnemyAction>(enemy);
        Assert.AreEqual(EnemyActionKind.Stagger, action.kind);
        Assert.AreEqual(EnemyClip.Stagger, manager.GetComponentData<EnemyAnimation>(enemy).clip);

        // 무너진 뒤에는 강인도가 다시 차고 잠깐 면역이다 — 없으면 둘러싸인 순간 못 일어난다.
        EnemyHealth health = manager.GetComponentData<EnemyHealth>(enemy);
        Assert.AreEqual(stats.maxPoise, health.poise);
        Assert.Greater(health.poiseImmuneUntil, 0d);
    }

    [Test]
    public void 흘려내면_강인도와_무관하게_그_자리에서_무너진다()
    {
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), DefaultStats());

        EnemyWorldBridge.StaggerEnemy(enemy, 0.9f, new float3(0f, 0f, 2f));
        Tick(0.05f);

        Assert.AreEqual(EnemyActionKind.Stagger, manager.GetComponentData<EnemyAction>(enemy).kind);
    }

    [Test]
    public void 체력이_0이_되면_죽고_더는_때리지_않는다()
    {
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), DefaultStats());
        AddAlly(new float3(0f, 0f, 1f));

        EnemyWorldBridge.DamageEnemy(enemy, 999, 0f, new float3(0f, 0f, 1f));
        Tick(0.05f);

        Assert.AreEqual(EnemyActionKind.Dead, manager.GetComponentData<EnemyAction>(enemy).kind);
        Assert.AreEqual(0, manager.GetComponentData<EnemyHealth>(enemy).current);

        EnemyWorldBridge.HitsOnAllies.Clear();
        Tick(0.05f, 20);

        Assert.AreEqual(0, EnemyWorldBridge.HitsOnAllies.Count, "시체는 때리지 않는다");
    }

    [Test]
    public void 뒤에서_맞으면_더_아프다()
    {
        EnemyStats stats = DefaultStats();
        Entity front = CreateEnemy(new float3(0f, 0f, 0f), stats);
        Entity back = CreateEnemy(new float3(20f, 0f, 0f), stats);

        // 둘 다 +Z를 보고 있다. 앞에서 한 대, 등 뒤에서 한 대.
        manager.SetComponentData(front, LocalTransform.FromPositionRotation(
            new float3(0f, 0f, 0f), quaternion.identity));
        manager.SetComponentData(back, LocalTransform.FromPositionRotation(
            new float3(20f, 0f, 0f), quaternion.identity));

        EnemyWorldBridge.DamageEnemy(front, 10, 0f, new float3(0f, 0f, 2f));
        EnemyWorldBridge.DamageEnemy(back, 10, 0f, new float3(20f, 0f, -2f));
        Tick(0.05f);

        int frontHp = manager.GetComponentData<EnemyHealth>(front).current;
        int backHp = manager.GetComponentData<EnemyHealth>(back).current;

        Assert.Less(backHp, frontHp, "배후 타격에 배율이 붙어야 한다");
    }

    [Test]
    public void 서로_겹치지_않게_밀어낸다()
    {
        EnemyStats stats = DefaultStats();
        Entity a = CreateEnemy(new float3(0f, 0f, 0f), stats);
        Entity b = CreateEnemy(new float3(0.2f, 0f, 0f), stats);

        Tick(0.05f, 20);

        float3 pa = manager.GetComponentData<LocalTransform>(a).Position;
        float3 pb = manager.GetComponentData<LocalTransform>(b).Position;

        Assert.Greater(math.distance(pa, pb), 0.2f, "겹쳐 있던 둘이 벌어져야 한다");
    }

    [Test]
    public void 한_사람에게_몰려도_반지름_합만큼은_벌어져_선다()
    {
        EnemyStats stats = DefaultStats();
        AddAlly(new float3(0f, 0f, 0f));

        // 마흔 마리가 한 사람에게 몰린다. 칼을 들 자리는 둘뿐이라 나머지는 둘레에서 밀친다.
        //
        // 여기가 밀어내는 힘만으로는 안 되던 자리다. 표적 쪽으로 미는 힘(최대 1)과 밀려나는
        // 힘(겹친 비율 × 2.2)이 맞서는 지점이 겹친 비율 0.45라, 앞뒤로 눌리면 파고든 채로 평형에
        // 들었다 — 이 배치에서 재면 가장 가까운 둘이 0.73m였다. 화면에서는 몸이 서로 통과해 보인다.
        var random = Unity.Mathematics.Random.CreateFromIndex(7);
        var crowd = new Entity[40];
        for (int i = 0; i < crowd.Length; i++)
        {
            float2 offset = random.NextFloat2Direction() * random.NextFloat(0.5f, 4f);
            crowd[i] = CreateEnemyFacing(new float3(offset.x, 0f, offset.y), float3.zero, stats);
        }

        Tick(1f / 60f, 240);

        float minimum = stats.radius * 2f;
        float closest = float.MaxValue;
        for (int i = 0; i < crowd.Length; i++)
        {
            for (int j = i + 1; j < crowd.Length; j++)
            {
                float3 a = manager.GetComponentData<LocalTransform>(crowd[i]).Position;
                float3 b = manager.GetComponentData<LocalTransform>(crowd[j]).Position;
                closest = math.min(closest, math.distance(a, b));
            }
        }

        // 한 프레임 전 자리를 보고 푸는 구조라 순간적으로는 조금 파고들 수 있다.
        Assert.Greater(closest, minimum * 0.8f,
            $"가장 가까운 두 마리가 {closest:F2}m다 — 반지름 합({minimum:F2}m) 가까이는 지켜져야 한다");
    }

    [Test]
    public void 휘두르는_중에도_겹친_이웃에게서_밀려난다()
    {
        EnemyStats stats = DefaultStats();
        AddAlly(new float3(0f, 0f, 0f));

        Entity swinging = CreateEnemyFacing(new float3(0f, 0f, 1f), float3.zero, stats);
        Entity neighbour = CreateEnemyFacing(new float3(0.15f, 0f, 1f), float3.zero, stats);

        // 칼을 들어올린 채로 굳혀 둔다. 이 구간에는 발을 떼지 않으므로(holdsGround),
        // 겹침을 힘으로만 풀던 시절에는 스윙이 끝날 때까지 이웃이 몸을 통과한 채로 서 있었다.
        // 0.75초짜리 스윙을 1.1초마다 도는 무리에서는 그 그림이 거의 늘 떠 있다.
        manager.SetComponentData(swinging, new EnemyAction
        {
            kind = EnemyActionKind.Windup,
            timer = 10f,
            animationLength = 10f,
        });

        Tick(1f / 60f, 60);

        Assert.AreEqual(EnemyActionKind.Windup, manager.GetComponentData<EnemyAction>(swinging).kind,
            "테스트가 성립하려면 스윙이 끝나지 않아야 한다");

        float3 a = manager.GetComponentData<LocalTransform>(swinging).Position;
        float3 b = manager.GetComponentData<LocalTransform>(neighbour).Position;

        Assert.Greater(math.distance(a, b), stats.radius * 2f * 0.8f,
            "휘두르는 놈도 겹친 이웃에게서 밀려나야 한다");
    }

    [Test]
    public void 무리에_막힌_놈은_제자리에서_달리지_않는다()
    {
        EnemyStats stats = DefaultStats();
        AddAlly(new float3(0f, 0f, 0f));

        var random = Unity.Mathematics.Random.CreateFromIndex(11);
        var crowd = new Entity[30];
        for (int i = 0; i < crowd.Length; i++)
        {
            float2 offset = random.NextFloat2Direction() * random.NextFloat(0.5f, 3f);
            crowd[i] = CreateEnemyFacing(new float3(offset.x, 0f, offset.y), float3.zero, stats);
        }

        Tick(1f / 60f, 180);

        // 한 프레임을 재서, 들고 있는 속도와 실제로 간 거리를 맞춰 본다.
        //
        // 걷는 모션과 그 재생 배속은 motion.velocity 크기로 정해지므로(EnemyCombatSystem), 파고들던
        // 속도를 그대로 들고 있으면 몸은 그 자리인데 다리만 돈다 — 발이 미끄러진다.
        // 속도 지우기를 빼고 재면 0.44m/s를 들고 0.02m/s를 갔다.
        var before = new float3[crowd.Length];
        for (int i = 0; i < crowd.Length; i++)
        {
            before[i] = manager.GetComponentData<LocalTransform>(crowd[i]).Position;
        }

        float dt = 1f / 60f;
        Tick(dt);

        // 서 있는 놈(속도 0)까지 포함해 전원을 본다. 조건을 걸어 거르면 무리가 다 멈춘 프레임에
        // 아무것도 재지 않고 통과해 버린다.
        float tolerance = stats.moveSpeed * 0.1f;
        for (int i = 0; i < crowd.Length; i++)
        {
            float speed = math.length(manager.GetComponentData<EnemyMotion>(crowd[i]).velocity);
            float3 after = manager.GetComponentData<LocalTransform>(crowd[i]).Position;
            float travelled = math.distance(before[i], after) / dt;

            Assert.GreaterOrEqual(travelled, speed - tolerance,
                $"들고 있는 속도는 {speed:F2}m/s인데 실제로는 {travelled:F2}m/s만 갔다 — " +
                "그 차이가 제자리에서 도는 달리기 모션이다");
        }
    }

    // ---------------------------------------------------------------- 무리의 움직임이 화면에 보이는 모양
    //
    // 위의 셋이 "간격이 지켜지는가"라면, 아래 둘은 그 간격을 지키는 과정이 어떻게 보이는가다.
    // 둘 다 같은 증상 하나에서 나왔다 — 달리던 고블린이 되감긴 것처럼 뒤로 갔다 다시 달린다.

    // 실전에 가까운 수치. DefaultStats는 표적·피해 규칙을 재려고 시간 값을 비워 둔 것이라,
    // 무리가 실제로 어떻게 움직이는지는 EnemyHordeSpawner의 기본값을 채워야 나온다.
    private static EnemyStats HordeStats()
    {
        EnemyStats stats = DefaultStats();
        stats.detectRange = 30f;
        stats.runClipSpeed = 2.29f;
        stats.leapRange = 3f;
        stats.leapDuration = 1.1f;
        stats.leapCooldown = 6f;
        stats.thinkIntervalMin = 0.18f;
        stats.thinkIntervalMax = 0.36f;
        stats.slotRequestRange = 4.5f;
        stats.swingsPerSlotMin = 1;
        stats.swingsPerSlotMax = 3;
        stats.slotYieldDelay = 0.9f;
        stats.slotHoldTimeout = 2.5f;
        stats.waitDistanceMin = 2.0f;
        stats.waitDistanceMax = 2.9f;
        stats.attackCooldownJitter = 0.2f;
        stats.comboSteps = 1;
        return stats;
    }

    // 끝에서 처음으로 이어 붙여 도는 클립(EnemyCombatSystem.IsLooping과 같은 셋).
    private static bool Loops(EnemyClip clip)
    {
        return clip == EnemyClip.Idle || clip == EnemyClip.GuardIdle ||
               clip == EnemyClip.Walk || clip == EnemyClip.Run;
    }

    // 발놀림으로 본 자세. 서 있는 두 자세(두리번·노려보기)는 하나로 친다.
    private static EnemyClip Stance(EnemyClip clip) => clip == EnemyClip.GuardIdle ? EnemyClip.Idle : clip;

    private Entity[] SpawnCrowd(int count, uint seed, float near, float far, EnemyStats stats)
    {
        var random = Unity.Mathematics.Random.CreateFromIndex(seed);
        var crowd = new Entity[count];
        for (int i = 0; i < count; i++)
        {
            float2 offset = random.NextFloat2Direction() * random.NextFloat(near, far);
            crowd[i] = CreateEnemyFacing(new float3(offset.x, 0f, offset.y), float3.zero, stats);
        }

        return crowd;
    }

    [Test]
    public void 제자리걸음을_스쳐도_달리기_주기가_되감기지_않는다()
    {
        EnemyStats stats = HordeStats();
        PublishClipSpeeds(walkSpeed: 1.517f, runSpeed: 1.671f);
        AddAlly(new float3(0f, 0f, 0f));
        Entity[] crowd = SpawnCrowd(30, 3, 3f, 10f, stats);

        // 재는 것은 "바뀌는 그 프레임에 진행도가 튀는가"다. 도는 클립끼리(제자리걸음 ↔ 걷기 ↔
        // 달리기)는 셋 다 순환하므로 진행도가 이어져야 하고, 끊기면 그 한 프레임이 곧 되감김이다.
        //
        // 마지막으로 본 달리기 진행도와 비교하면 안 된다 — 제자리걸음으로 한참 머무는 동안
        // 진행도가 한 바퀴를 넘어 돌아오는 것(0.9 → 0.2)까지 되감김으로 세게 된다.
        var previousClip = new EnemyClip[crowd.Length];
        var previousPhase = new float[crowd.Length];
        for (int i = 0; i < crowd.Length; i++)
        {
            EnemyAnimation anim = manager.GetComponentData<EnemyAnimation>(crowd[i]);
            previousClip[i] = anim.clip;
            previousPhase[i] = anim.normalizedTime;
        }

        int jumps = 0;
        int loopSwitches = 0;
        float worstJump = 0f;

        for (int f = 0; f < 600; f++)
        {
            Tick(1f / 60f);

            for (int i = 0; i < crowd.Length; i++)
            {
                EnemyAnimation anim = manager.GetComponentData<EnemyAnimation>(crowd[i]);

                bool bothLooping = Loops(anim.clip) && Loops(previousClip[i]);

                if (bothLooping && anim.clip != previousClip[i])
                {
                    // 서 있는 자세 둘(두리번 ↔ 노려보기)은 표적을 잡고 놓을 때 한 번 바뀌는 것이라 들썩임으로
                    // 세지 않는다. 진행도가 이어지는지는 똑같이 본다.
                    if (Stance(anim.clip) != Stance(previousClip[i])) loopSwitches++;

                    float jump = math.abs(anim.normalizedTime - previousPhase[i]);
                    if (jump > 0.05f)
                    {
                        jumps++;
                        worstJump = math.max(worstJump, jump);
                    }
                }

                previousClip[i] = anim.clip;
                previousPhase[i] = anim.normalizedTime;
            }
        }

        Assert.Greater(loopSwitches, 10, "클립이 이만큼도 안 바뀌면 아무것도 재지 못한 것이다");
        Assert.AreEqual(0, jumps,
            $"도는 클립끼리 바뀌는데 진행도가 {jumps}번 튀었다(가장 큰 것 {worstJump:F2})");

        // 진행도가 이어져도 자세는 한 프레임에 갈아 끼워진다(섞어 주는 구간이 없다).
        // 자주 갈아 끼울수록 무리가 들썩이므로 횟수 자체에도 상한을 둔다.
        //
        // 이 배치에서 잰 값: 이력만 있을 때 0.86회, 속도를 눌러 따라가게 한 뒤 0.44회.
        // 셋으로 나누면 경계가 둘이라 횟수는 늘지만, 한 번의 갈아 끼움이 그만큼 작아진다
        // (제자리걸음 ↔ 달리기보다 걷기 ↔ 달리기가 덜 튄다).
        float switchesPerSecond = loopSwitches / (float)crowd.Length / 10f;
        Assert.Less(switchesPerSecond, 0.5f,
            $"제자리걸음 ↔ 달리기가 마리당 초당 {switchesPerSecond:F2}회 뒤바뀐다");
    }

    [Test]
    public void 무리에_밀려_뒤로_가더라도_걸음보다_빠르지_않다()
    {
        EnemyStats stats = HordeStats();
        AddAlly(new float3(0f, 0f, 0f));
        Entity[] crowd = SpawnCrowd(30, 3, 3f, 10f, stats);

        const float dt = 1f / 60f;
        var previous = new float3[crowd.Length];
        for (int i = 0; i < crowd.Length; i++)
        {
            previous[i] = manager.GetComponentData<LocalTransform>(crowd[i]).Position;
        }

        float worst = 0f;
        for (int f = 0; f < 600; f++)
        {
            Tick(dt);

            for (int i = 0; i < crowd.Length; i++)
            {
                LocalTransform t = manager.GetComponentData<LocalTransform>(crowd[i]);
                float3 step = t.Position - previous[i];
                step.y = 0f;
                previous[i] = t.Position;

                float3 facing = math.mul(t.Rotation, new float3(0f, 0f, 1f));
                facing.y = 0f;
                if (math.lengthsq(facing) < 1e-6f) continue;

                float along = math.dot(step, math.normalizesafe(facing)) / dt;
                if (along < 0f) worst = math.max(worst, -along);
            }
        }

        // 겨눈 쪽을 보면서 뒤로 밀리는 것 자체는 난전의 일부다(이웃에게 밀리고, 겹침이 풀린다).
        // 다만 달리는 모션 위에서 몸만 제 걸음 속도로 뒤로 튀면 되감긴 것처럼 보인다.
        Assert.Less(worst, stats.moveSpeed * 0.5f,
            $"가장 빠른 후진이 {worst:F2}m/s다 — 걸음 속도({stats.moveSpeed:F1}m/s)의 절반을 넘으면 튄다");
    }

    [Test]
    public void 느리게_가면_걷고_빠르게_가면_달린다()
    {
        // 구운 고블린의 실제 값(EnemyAnimationBaker가 잰 것).
        PublishClipSpeeds(walkSpeed: 1.517f, runSpeed: 1.671f);
        AddAlly(new float3(0f, 0f, 0f));

        // 걸음 속도만 다른 둘. 멀리 세워 두면 각자 제 속도로 붙으러 온다.
        EnemyStats slow = HordeStats();
        slow.moveSpeed = 1.2f;
        EnemyStats fast = HordeStats();

        Entity walker = CreateEnemyFacing(new float3(0f, 0f, 20f), float3.zero, slow);
        Entity runner = CreateEnemyFacing(new float3(20f, 0f, 0f), float3.zero, fast);

        Tick(1f / 60f, 60);

        Assert.AreEqual(EnemyClip.Walk, manager.GetComponentData<EnemyAnimation>(walker).clip,
            "1.2m/s는 걷기 클립이 1배속 가까이 도는 속도다");
        Assert.AreEqual(EnemyClip.Run, manager.GetComponentData<EnemyAnimation>(runner).clip,
            "4m/s는 걷기 클립이 견디는 윗선을 한참 넘는다");
    }

    [Test]
    public void 걷기를_굽지_않은_리그는_제자리걸음과_달리기만_쓴다()
    {
        // 구간표를 올리지 않았다 — 걷기 클립이 없는 리그와 같은 상태다.
        AddAlly(new float3(0f, 0f, 0f));

        EnemyStats slow = HordeStats();
        slow.moveSpeed = 1.2f;
        Entity enemy = CreateEnemyFacing(new float3(0f, 0f, 20f), float3.zero, slow);

        Tick(1f / 60f, 60);

        Assert.AreNotEqual(EnemyClip.Walk, manager.GetComponentData<EnemyAnimation>(enemy).clip,
            "굽지 않은 클립을 가리키면 그 줄이 비어 몸이 통째로 사라진다");
    }

    // ---------------------------------------------------------------- 구운 자산
    //
    // 아래는 시뮬레이션이 아니라 구워 놓은 결과물을 본다. 베이커는 에디터에서만 도는 도구라
    // 여기서 다시 굽지 않고, 저장소에 들어 있는 고블린 한 벌을 그대로 검사한다.

    [Test]
    public void 구운_도는_클립은_제자리에서_돈다()
    {
        var library = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyAnimationLibrary>(
            "Assets/Enemy/Baked/GoblinAnimation.asset");

        if (library == null || !library.IsBaked) Assert.Ignore("구워 둔 고블린 한 벌이 없다");

        Color[] pixels = library.boneTexture.GetPixels();
        int width = library.boneTexture.width;

        // 0번 뼈의 스키닝 행렬 평행이동(m03, m13, m23). 클립 안에서 몸이 흘러가면 이 값이 흘러간다.
        float3 Translation(int row)
        {
            Color c0 = pixels[row * width + 0];
            Color c1 = pixels[row * width + 1];
            Color c2 = pixels[row * width + 2];
            return new float3(c0.a, c1.a, c2.a);
        }

        int checkedLooping = 0;
        foreach (EnemyAnimationLibrary.ClipRange range in library.clips)
        {
            float drift = math.distance(
                Translation(range.startFrame),
                Translation(range.startFrame + range.frameCount - 1));

            bool loops = range.clip == EnemyClip.Idle || range.clip == EnemyClip.GuardIdle ||
                         range.clip == EnemyClip.Walk || range.clip == EnemyClip.Run;

            if (loops)
            {
                checkedLooping++;

                // 도는 클립이 한 바퀴 동안 앞으로 흘러가면, 진행도가 처음으로 감기는 그 프레임에
                // 그만큼 뒤로 튄다. 고블린 Run이 1.447m였다 — 배속 1.75배라 초당 두 번씩 되감겼다.
                Assert.Less(drift, 0.05f,
                    $"{range.clip}이 한 바퀴에 {drift:F3}m 흘러간다 — 처음으로 감길 때 그만큼 튄다");
            }
            else if (range.clip == EnemyClip.Death)
            {
                // 뒤집어 잡는 쪽. 한 번만 재생하는 클립에서 앞으로 나아가는 것은 동작의 일부라
                // 걷어내면 안 된다 — 쓰러지며 앞으로 무너지는 것이 사라진다.
                Assert.Greater(drift, 0.1f, "쓰러지는 동작이 제자리에서 일어나면 안 된다");
            }
        }

        Assert.AreEqual(4, checkedLooping, "두리번·노려보기·걷기·달리기 넷 다 구워져 있어야 한다");
    }

    // ---------------------------------------------------------------- 아군이 엔티티를 읽는 쪽
    //
    // 위의 테스트들이 "적이 스스로 도는가"라면, 아래는 "아군이 그 적을 볼 수 있는가"다.
    // 아군은 시뮬레이션이 아니라 브리지의 스냅샷을 읽으므로(UnitRegistry의 합쳐진 질의들)
    // 출력 시스템을 세우지 않고 그 스냅샷을 직접 채워 읽는 쪽만 떼어 확인한다.
    //
    // 셋 다 답 하나를 돌려주지 않고 진행 중인 계산에 제 몫을 더하는 모양이다. 게임오브젝트로
    // 남은 적과 같은 누적값을 이어받아야 "가장 가까운 하나"와 무게중심이 두 세계에 걸쳐
    // 하나의 규칙으로 남기 때문이다 — 여기서 고정하는 것이 그 이어받기다.

    private Entity AddEnemyState(float3 position, int targetAllyIndex = EnemyTarget.None)
    {
        Entity entity = manager.CreateEntity();
        EnemyWorldBridge.EnemyStates.Add(new EnemyWorldBridge.EnemyState
        {
            entity = entity,
            position = position,
            forward = new float3(0f, 0f, 1f),
            radius = 0.5f,
            hp = 100,
            maxHp = 100,
            poise = 100f,
            threatWeight = 1f,
            targetAllyIndex = targetAllyIndex,
            action = EnemyActionKind.Approach,
        });

        return entity;
    }

    private static EnemyStats BiterStats()
    {
        EnemyStats stats = DefaultStats();
        stats.biteDamage = 24;
        stats.biteDuration = 2.08f;
        stats.biteCooldown = 5f;
        return stats;
    }

    [Test]
    public void 붙으면_물고_늘어진다()
    {
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), BiterStats());
        AddAlly(new float3(0f, 0f, 1f));

        Tick(0.05f, 4);

        Assert.AreEqual(EnemyActionKind.Bite, manager.GetComponentData<EnemyAction>(enemy).kind);
        Assert.AreEqual(EnemyClip.Bite, manager.GetComponentData<EnemyAnimation>(enemy).clip);
    }

    [Test]
    public void 이미_물린_아군은_다시_물지_않는다()
    {
        // 이게 없으면 한 명에게 여럿이 동시에 물고 늘어져 그 자리에서 녹는다.
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), BiterStats());
        AddAlly(new float3(0f, 0f, 1f), canBeBitten: false);

        Tick(0.05f, 4);

        Assert.AreEqual(EnemyActionKind.Windup, manager.GetComponentData<EnemyAction>(enemy).kind,
            "물 수 없으면 평타로 떨어져야 한다");
    }

    [Test]
    public void 무는_동안_상대를_따라간다()
    {
        // 2초가 넘는 동작이라 제자리에 서서 물면 상대가 걸어 나가는 동안 허공을 문다.
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), BiterStats());
        AddAlly(new float3(0f, 0f, 1f));
        Tick(0.05f, 4);
        Assert.AreEqual(EnemyActionKind.Bite, manager.GetComponentData<EnemyAction>(enemy).kind);

        // 아군이 걸어 나간다.
        var moved = EnemyWorldBridge.AllyStates[0];
        moved.position = new float3(0f, 0f, 4f);
        EnemyWorldBridge.AllyStates[0] = moved;

        float before = manager.GetComponentData<LocalTransform>(enemy).Position.z;
        Tick(0.05f, 10);
        float after = manager.GetComponentData<LocalTransform>(enemy).Position.z;

        Assert.Greater(after, before + 0.3f, "붙잡은 쪽을 따라가야 한다");
    }

    [Test]
    public void 무는_동작이_끝나면_크게_한_번_들어간다()
    {
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), BiterStats());
        AddAlly(new float3(0f, 0f, 1f));

        Tick(0.05f, 60);   // 3초 — 2.08초짜리 동작이 끝난다

        Assert.IsTrue(EnemyWorldBridge.HitsOnAllies.Count > 0, "물었으면 피해가 큐에 쌓여야 한다");
        bool bit = false;
        while (EnemyWorldBridge.HitsOnAllies.TryDequeue(out var hit))
        {
            if (hit.damage == 24 && hit.skillVictimDuration > 0f) bit = true;
        }

        Assert.IsTrue(bit, "평타(40)가 아니라 물어뜯기(24 + 면역 시간)여야 한다");
    }

    [Test]
    public void 사거리_밖_한_번에_붙을_거리면_덤벼든다()
    {
        EnemyStats stats = DefaultStats();
        stats.leapRange = 3f;
        stats.leapDuration = 1.1f;
        stats.leapCooldown = 6f;

        // 사거리(1.2)는 넘고 도약 거리(3.0) 안이다.
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 2.5f));

        Tick(0.05f, 4);

        Assert.AreEqual(EnemyActionKind.Leap, manager.GetComponentData<EnemyAction>(enemy).kind);
        Assert.AreEqual(EnemyClip.LeapAttack, manager.GetComponentData<EnemyAnimation>(enemy).clip);
    }

    [Test]
    public void 이미_닿는_상대에게는_뛰지_않는다()
    {
        // 뛰면 뒤로 물러났다 덤비는 꼴이 된다. 사거리 안이면 그냥 휘두른다.
        EnemyStats stats = DefaultStats();
        stats.leapRange = 3f;
        stats.leapDuration = 1.1f;

        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 1f));

        Tick(0.05f, 4);

        Assert.AreEqual(EnemyActionKind.Windup, manager.GetComponentData<EnemyAction>(enemy).kind);
    }

    [Test]
    public void 도약은_상대_쪽으로_실제로_나아간다()
    {
        EnemyStats stats = DefaultStats();
        stats.leapRange = 3f;
        stats.leapDuration = 1.1f;

        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 2.5f));

        Tick(0.05f, 4);
        float before = manager.GetComponentData<LocalTransform>(enemy).Position.z;
        Tick(0.05f, 12);   // 도약 도중
        float after = manager.GetComponentData<LocalTransform>(enemy).Position.z;

        Assert.Greater(after, before + 0.3f, "클립 진행도에 맞춰 상대 쪽으로 밀려야 한다");
    }

    [Test]
    public void 도약이_끝나면_회수_구간으로_간다()
    {
        EnemyStats stats = DefaultStats();
        stats.leapRange = 3f;
        stats.leapDuration = 0.8f;   // 착지 직후(0.5)에 끝나므로 0.4초 동안 뛴다

        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 2.5f));

        Tick(0.05f, 4);
        Assert.AreEqual(EnemyActionKind.Leap, manager.GetComponentData<EnemyAction>(enemy).kind);

        Tick(0.05f, 10);  // 0.5초 — 착지해 끝났다
        Assert.AreNotEqual(EnemyActionKind.Leap, manager.GetComponentData<EnemyAction>(enemy).kind,
            "도약이 영영 끝나지 않으면 그 자리에 떠 있게 된다");
    }

    [Test]
    public void 도약은_착지하는_순간에_때린다()
    {
        // 예전에는 클립이 끝나는 1.0에서 때려서, 칼을 다 거둔 뒤 0.6초가 지나서야 피해와 멈칫이 들어갔다.
        EnemyStats stats = DefaultStats();
        stats.leapRange = 3f;
        stats.leapDuration = 1.1f;

        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 2.5f));

        Tick(0.05f, 4);
        Assert.AreEqual(EnemyActionKind.Leap, manager.GetComponentData<EnemyAction>(enemy).kind);

        // 피해가 들어가는 순간을 잡는다. 그때 아직 도약 중이어야 한다(도약은 착지 직후 0.5에서 끝난다).
        EnemyAction atHit = default;
        bool struck = false;
        for (int i = 0; i < 30 && !struck; i++)
        {
            Tick(0.05f);
            struck = EnemyWorldBridge.HitsOnAllies.Count > 0;
            if (struck) atHit = manager.GetComponentData<EnemyAction>(enemy);
        }

        Assert.IsTrue(struck, "내려앉는 순간에 피해가 들어가야 한다");
        Assert.AreEqual(EnemyActionKind.Leap, atHit.kind, "피해는 도약 도중(착지하는 프레임)에 들어간다");
    }

    [Test]
    public void 도약은_착지한_직후에_끝난다()
    {
        // 클립의 뒷부분은 공중에서 칼을 거두는 동작이라, 끝까지 틀면 착지한 뒤에도 공중 자세로 떠 있다.
        EnemyStats stats = DefaultStats();
        stats.leapRange = 3f;
        stats.leapDuration = 1.1f;

        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 2.5f));

        Tick(0.05f, 4);
        Assert.AreEqual(EnemyActionKind.Leap, manager.GetComponentData<EnemyAction>(enemy).kind);

        Tick(0.05f, 13);   // 0.65초 — 클립(1.1초)의 60%. 0.5에서 끝났어야 한다
        Assert.AreNotEqual(EnemyActionKind.Leap, manager.GetComponentData<EnemyAction>(enemy).kind);
    }

    [Test]
    public void 도약을_마치면_굳어_서_있지_않는다()
    {
        // 칼을 거두는 동작은 클립 뒷부분이 이미 보여 준다. 평타처럼 회수 구간(0.35초)과 재사용 대기(1.1초)를
        // 또 걸면 마지막 프레임에 굳은 채 서 있다가 한참 뒤에야 움직였다.
        EnemyStats stats = DefaultStats();
        stats.leapRange = 3f;
        stats.leapDuration = 0.8f;   // 착지 직후(0.5)에 끝나므로 0.4초 동안 뛴다

        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 2.5f));

        Tick(0.05f, 4);
        Assert.AreEqual(EnemyActionKind.Leap, manager.GetComponentData<EnemyAction>(enemy).kind);

        Tick(0.05f, 10);  // 0.5초 — 착지해 끝나고 두어 프레임 더
        EnemyAction action = manager.GetComponentData<EnemyAction>(enemy);
        Assert.AreNotEqual(EnemyActionKind.Recover, action.kind, "도약 뒤에 회수 구간으로 서 있으면 안 된다");
        Assert.LessOrEqual(action.nextAttackTime, world.Time.ElapsedTime, "도약이 평타 재사용 대기를 걸면 착지 뒤 1초 넘게 서 있는다");
    }

    [Test]
    public void 콤보는_단마다_다른_클립을_쓴다()
    {
        // 같은 클립만 반복하면 마리 수가 많을수록 "복사본이 같은 동작을 하는" 것이 눈에 띈다.
        EnemyStats stats = DefaultStats();
        stats.comboSteps = 3;
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 1f));   // 사거리 안이라 계속 휘두른다

        var seen = new System.Collections.Generic.HashSet<EnemyClip>();
        for (int i = 0; i < 200; i++)
        {
            Tick(0.05f);
            var action = manager.GetComponentData<EnemyAction>(enemy);
            if (action.kind == EnemyActionKind.Windup) seen.Add(manager.GetComponentData<EnemyAnimation>(enemy).clip);
        }

        Assert.IsTrue(seen.Contains(EnemyClip.Attack), "1단은 Attack이다");
        Assert.IsTrue(seen.Contains(EnemyClip.Attack2), "2단으로 넘어가야 한다");
        Assert.IsTrue(seen.Contains(EnemyClip.Attack3), "3단까지 돌아야 한다");
        Assert.IsFalse(seen.Contains(EnemyClip.Attack4), "굽지 않은 단으로 넘어가면 안 된다");
    }

    [Test]
    public void 표적을_잃으면_콤보가_처음으로_돌아온다()
    {
        EnemyStats stats = DefaultStats();
        stats.comboSteps = 4;
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        manager.SetComponentData(enemy, new EnemyAction { kind = EnemyActionKind.Idle, comboIndex = 3 });

        Tick(0.05f);   // 아군이 없으므로 표적을 잃은 상태로 돈다

        Assert.AreEqual(0, manager.GetComponentData<EnemyAction>(enemy).comboIndex,
            "다음에 붙는 상대에게 4단부터 시작하면 앞 세 단을 건너뛴 셈이 된다");
    }

    [Test]
    public void 등_뒤에서_맞으면_뒤로_움찔한다()
    {
        EnemyStats stats = DefaultStats();
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        manager.SetComponentData(enemy, LocalTransform.FromPositionRotation(float3.zero, quaternion.identity)); // +Z를 본다

        // 강인도는 건드리지 않는다 — 무너지면 Stagger로 가서 방향 판정이 의미가 없다.
        EnemyWorldBridge.DamageEnemy(enemy, 5, 0f, new float3(0f, 0f, -3f));
        Tick(0.05f);

        Assert.AreEqual(EnemyClip.HitBack, manager.GetComponentData<EnemyAnimation>(enemy).clip);
    }

    [Test]
    public void 옆에서_맞으면_그_쪽으로_움찔한다()
    {
        EnemyStats stats = DefaultStats();
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        manager.SetComponentData(enemy, LocalTransform.FromPositionRotation(float3.zero, quaternion.identity));

        EnemyWorldBridge.DamageEnemy(enemy, 5, 0f, new float3(3f, 0f, 0f));
        Tick(0.05f);

        Assert.AreEqual(EnemyClip.HitRight, manager.GetComponentData<EnemyAnimation>(enemy).clip);
    }

    [Test]
    public void 발이_묶이면_느리게_다가온다()
    {
        // 창수의 부위 억제와 빙결 마법이 이 경로다. 예전에는 엔티티에게 아무 일도 일어나지
        // 않아서, 근접을 붙이지 않는 것이 밥줄인 직군이 엔티티 상대로는 성립하지 않았다.
        EnemyStats stats = DefaultStats();
        Entity free = CreateEnemy(new float3(0f, 0f, 0f), stats);
        Entity slowed = CreateEnemy(new float3(20f, 0f, 0f), stats);

        // 탐지 범위(8m) 안이어야 표적을 잡는다. 그러면서 1초 안에 멈춰 설 거리까지
        // 닿지는 않을 만큼 떨어뜨린다 — 둘 다 도착해 버리면 비교가 사라진다.
        AddAlly(new float3(0f, 0f, 7.5f));   // 0번 — free가 쫓는다
        AddAlly(new float3(20f, 0f, 7.5f));  // 1번 — slowed가 쫓는다

        EnemyWorldBridge.SlowEnemy(slowed, 5f, 0.4f, new float3(20f, 0f, 0f));
        Tick(0.05f, 20);

        float freeGain = manager.GetComponentData<LocalTransform>(free).Position.z;
        float slowedGain = manager.GetComponentData<LocalTransform>(slowed).Position.z;

        Assert.Greater(freeGain, 0.5f, "묶이지 않은 쪽은 평소대로 다가와야 한다");
        Assert.Less(slowedGain, freeGain * 0.75f, "묶인 쪽이 뚜렷하게 덜 나아가야 한다");
    }

    [Test]
    public void 둔화는_시간이_지나면_풀린다()
    {
        EnemyStats stats = DefaultStats();
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 10f));

        EnemyWorldBridge.SlowEnemy(enemy, 0.2f, 0.2f, float3.zero);
        Tick(0.05f, 10);   // 0.5초 — 이미 풀렸다

        Assert.AreEqual(1f, manager.GetComponentData<EnemyMotion>(enemy).SlowFactor(world.Time.ElapsedTime), 0.001f);
    }

    [Test]
    public void 스윙_궤적_안의_엔티티만_걸린다()
    {
        Entity front = AddEnemyState(new float3(0f, 0f, 1.5f));
        AddEnemyState(new float3(0f, 0f, -1.5f));

        Entity best = Entity.Null;
        float bestSqr = 2f * 2f;
        EnemyWorldBridge.AccumulateEnemyInArc(Vector3.zero, Vector3.forward, 130f, ref best, ref bestSqr);

        Assert.AreEqual(front, best, "등 뒤에 선 놈은 부채꼴 밖이라 베이지 않는다");
    }

    [Test]
    public void 더_가까운_게임오브젝트가_이미_있으면_엔티티가_덮지_않는다()
    {
        AddEnemyState(new float3(0f, 0f, 1.5f));

        // 게임오브젝트 쪽에서 이미 1m 거리의 적을 골라 둔 상태를 흉내 낸다.
        Entity best = Entity.Null;
        float bestSqr = 1f;
        EnemyWorldBridge.AccumulateEnemyInArc(Vector3.zero, Vector3.forward, 130f, ref best, ref bestSqr);

        Assert.AreEqual(Entity.Null, best, "더 먼 엔티티가 이미 고른 것을 밀어내면 안 된다");
    }

    [Test]
    public void 무게중심은_평균이_아니라_합과_개수로_넘어온다()
    {
        AddEnemyState(new float3(0f, 0f, 2f));
        AddEnemyState(new float3(0f, 0f, 4f));
        AddEnemyState(new float3(0f, 0f, 40f)); // 반경 밖

        Vector3 sum = Vector3.zero;
        int count = 0;
        EnemyWorldBridge.AccumulateCentroidAround(Vector3.zero, 10f, ref sum, ref count);

        // 합과 개수를 그대로 넘겨야 게임오브젝트 쪽 합과 섞어 하나의 무게중심을 낼 수 있다.
        Assert.AreEqual(2, count);
        Assert.AreEqual(6f, sum.z, 0.001f);
    }

    [Test]
    public void 전선이_가장_가까운_적보다_먼저다()
    {
        Entity idle = AddEnemyState(new float3(0f, 0f, 3f));
        Entity engaged = AddEnemyState(new float3(0f, 0f, 8f), targetAllyIndex: 0);

        Entity engagedFound = Entity.Null;
        Entity nearestFound = Entity.Null;
        float engagedSqr = float.MaxValue;
        float nearestSqr = float.MaxValue;
        EnemyWorldBridge.AccumulateRallyCandidates(Vector3.zero,
            ref engagedFound, ref engagedSqr, ref nearestFound, ref nearestSqr);

        Assert.AreEqual(engaged, engagedFound, "누군가를 물고 있는 적이 곧 전선이다");
        Assert.AreEqual(idle, nearestFound, "가장 가까운 적은 따로 남아 있어야 한다");
    }

    [Test]
    public void 쓰러진_엔티티는_어느_질의에도_잡히지_않는다()
    {
        Entity entity = manager.CreateEntity();
        EnemyWorldBridge.EnemyStates.Add(new EnemyWorldBridge.EnemyState
        {
            entity = entity,
            position = new float3(0f, 0f, 1f),
            forward = new float3(0f, 0f, 1f),
            hp = 0,
            maxHp = 100,
            targetAllyIndex = 0,
            action = EnemyActionKind.Dead,
        });

        Entity best = Entity.Null;
        float bestSqr = 4f;
        EnemyWorldBridge.AccumulateEnemyInArc(Vector3.zero, Vector3.forward, 130f, ref best, ref bestSqr);

        Vector3 sum = Vector3.zero;
        int count = 0;
        EnemyWorldBridge.AccumulateCentroidAround(Vector3.zero, 10f, ref sum, ref count);

        Assert.AreEqual(Entity.Null, best);
        Assert.AreEqual(0, count);
    }

    // 생존 여부는 스냅샷을 훑지 않고 표를 세울 때 함께 센 수로 답한다.
    //
    // 그 수가 전투를 새로 열 때 초기화되지 않으면 아무도 없는 맵에서 참이 나오고,
    // 아군 전원이 없는 적을 찾아 헤매다 전투가 끝나지 않는다. 실제로 그렇게 났던 자리다.
    [Test]
    public void 생존_여부는_훑지_않고_답한다()
    {
        Assert.IsFalse(EnemyWorldBridge.HasLivingEnemy(), "아무도 없으면 거짓이다.");

        AddDeadEnemyState(new float3(3f, 0f, 3f));
        EnemyWorldBridge.RebuildEnemyIndex();
        Assert.IsFalse(EnemyWorldBridge.HasLivingEnemy(), "시체만 있으면 거짓이다.");

        AddEnemyState(new float3(5f, 0f, 5f));
        EnemyWorldBridge.RebuildEnemyIndex();
        Assert.IsTrue(EnemyWorldBridge.HasLivingEnemy());
    }

    // 적 체력바가 매 프레임 묻는 합계도 표를 세울 때 함께 센 값이다.
    // 최대 체력은 시체까지 넣고(바가 실제로 비어 가야 한다), 현재 체력과 생존 수는 산 적만 센다.
    [Test]
    public void 체력_합계는_표를_세울_때_함께_센다()
    {
        AddEnemyState(new float3(1f, 0f, 1f));
        AddEnemyState(new float3(2f, 0f, 2f));
        AddDeadEnemyState(new float3(3f, 0f, 3f));
        EnemyWorldBridge.RebuildEnemyIndex();

        EnemyWorldBridge.SumEnemyHealth(out float current, out float max, out int alive);
        Assert.AreEqual(200f, current, 0.001f);
        Assert.AreEqual(300f, max, 0.001f);
        Assert.AreEqual(2, alive);

        // 스냅샷을 비우고 다시 세우면 지난 프레임의 합이 남지 않는다.
        EnemyWorldBridge.EnemyStates.Clear();
        EnemyWorldBridge.RebuildEnemyIndex();
        EnemyWorldBridge.SumEnemyHealth(out current, out max, out alive);
        Assert.AreEqual(0f, current);
        Assert.AreEqual(0f, max);
        Assert.AreEqual(0, alive);
    }

    // 아군이 읽는 적 목록은 출력 시스템이 매 프레임 엔티티에서 옮겨 적는다(Burst 잡).
    // 칸 하나만 빠져도 그 값을 읽는 아군 판단(방어·집결·체력바)이 조용히 틀어진다. 지난 프레임의 목록이
    // 남지 않는지, 시체도 그대로 실리는지까지 본다 — 시체를 거르는 것은 읽는 쪽(IsAlive)의 몫이다.
    [Test]
    public void 출력_시스템은_엔티티를_빠짐없이_옮겨_적는다()
    {
        // 지난 프레임의 찌꺼기. 새로 쓸 때 남아 있으면 없는 적이 목록에 끼어든다.
        AddEnemyState(new float3(9f, 0f, 9f));

        EnemyStats stats = DefaultStats();
        stats.threatWeight = 2.5f;
        Entity fighter = CreateEnemy(new float3(1f, 0f, 2f), stats);
        manager.SetComponentData(fighter, new EnemyTarget { allyIndex = 3 });
        manager.SetComponentData(fighter, new EnemyHealth { current = 70, poise = 40f });
        manager.SetComponentData(fighter, new EnemyAction { kind = EnemyActionKind.Windup });

        Entity corpse = CreateEnemy(new float3(-4f, 0f, 0f), DefaultStats());
        manager.SetComponentData(corpse, new EnemyHealth { current = 0 });
        manager.SetComponentData(corpse, new EnemyAction { kind = EnemyActionKind.Dead });

        world.CreateSystemManaged<EnemyBridgeOutputSystem>().Update();

        Assert.AreEqual(2, EnemyWorldBridge.EnemyCount);

        Assert.IsTrue(EnemyWorldBridge.TryGetEnemy(fighter, out EnemyWorldBridge.EnemyState state));
        Assert.AreEqual(new float3(1f, 0f, 2f), state.position);
        Assert.AreEqual(new float3(0f, 0f, 1f), state.forward);
        Assert.AreEqual(0.5f, state.radius, 0.0001f);
        Assert.AreEqual(70, state.hp);
        Assert.AreEqual(100, state.maxHp);
        Assert.AreEqual(40f, state.poise, 0.0001f);
        Assert.AreEqual(2.5f, state.threatWeight, 0.0001f);
        Assert.AreEqual(3, state.targetAllyIndex);
        Assert.IsTrue(state.IsTelegraphing);

        Assert.IsTrue(EnemyWorldBridge.TryGetEnemy(corpse, out state));
        Assert.IsFalse(state.IsAlive);

        EnemyWorldBridge.SumEnemyHealth(out float current, out float max, out int alive);
        Assert.AreEqual(1, alive);
        Assert.AreEqual(70f, current, 0.001f);
        Assert.AreEqual(200f, max, 0.001f);
    }

    private void AddDeadEnemyState(float3 position)
    {
        Entity entity = manager.CreateEntity();
        EnemyWorldBridge.EnemyStates.Add(new EnemyWorldBridge.EnemyState
        {
            entity = entity,
            position = position,
            forward = new float3(0f, 0f, 1f),
            radius = 0.5f,
            hp = 0,
            maxHp = 100,
            poise = 0f,
            threatWeight = 1f,
            targetAllyIndex = EnemyTarget.None,
            action = EnemyActionKind.Dead,
        });
    }

    // ---------------------------------------------------------------- 난전의 호흡
    //
    // 여기서 고정하는 것:
    //  - 한 사람에게 동시에 칼을 드는 수는 자리(슬롯) 수를 넘지 않는다
    //  - 자리를 못 얻은 놈은 한 걸음 떨어져 기다린다(둘레 어디인지는 정하지 않는다)
    //  - 다 휘두르거나 끊기면 자리를 돌려주고, 기다리던 놈이 받는다
    //  - 판단 박자가 마리마다 흩어져 있다
    // 자리가 한 번 새면(돌려주지 않은 자리가 쌓이면) 그 아군에게는 영영 아무도 칼을 못 든다.
    // 눈으로는 "고블린이 멍하니 서 있다"로만 보여서 원인을 찾기 어려운 자리다.

    // ---------------------------------------------------------------- 게임오브젝트 고블린에게서 옮겨 온 규칙

    // 감정을 켠다. 게임오브젝트 고블린이 달고 있던 UnitEmotion의 기본값 그대로다.
    private void UseEmotion(params Entity[] enemies)
    {
        if (!emotionEnabled)
        {
            emotionSystem = world.CreateSystem<EnemyEmotionSystem>();
            emotionEnabled = true;
        }

        EnemyEmotionProfile profile = EnemyEmotionProfile.From(new EmotionProfile(), false);
        foreach (Entity enemy in enemies)
        {
            manager.AddComponentData(enemy, profile);
            manager.AddComponentData(enemy, new EnemyEmotion());
        }
    }

    private void HitEnemy(Entity enemy, int damage, float3 from, int attackerAllyIndex = -1, float bleedChance = 0f)
    {
        EnemyWorldBridge.HitsOnEnemies.Enqueue(new EnemyWorldBridge.HitOnEnemy
        {
            enemy = enemy,
            damage = damage,
            fromPosition = from,
            attackerAllyIndex = attackerAllyIndex,
            bleedChance = bleedChance,
            impactWeight = 1f,
        });
    }

    private void HoldAction(Entity enemy, EnemyActionKind kind)
    {
        EnemyAction action = manager.GetComponentData<EnemyAction>(enemy);
        action.kind = kind;
        action.timer = 10f;
        action.animationLength = 10f;
        manager.SetComponentData(enemy, action);
    }

    [Test]
    public void 칼을_거두는_중에_맞으면_더_아프다()
    {
        EnemyStats stats = DefaultStats();
        stats.maxHp = 1000;
        stats.recoveryVulnerabilityMultiplier = 1.35f;
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        HoldAction(enemy, EnemyActionKind.Recover);

        HitEnemy(enemy, 100, new float3(0f, 0f, 5f));   // 정면
        Tick(0.02f);

        Assert.AreEqual(1000 - 135, manager.GetComponentData<EnemyHealth>(enemy).current);
    }

    [Test]
    public void 무너져_있는_동안_맞으면_더_아프다()
    {
        EnemyStats stats = DefaultStats();
        stats.maxHp = 1000;
        stats.staggerDamageMultiplier = 1.4f;
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        HoldAction(enemy, EnemyActionKind.Stagger);

        HitEnemy(enemy, 100, new float3(0f, 0f, 5f));
        Tick(0.02f);

        Assert.AreEqual(1000 - 140, manager.GetComponentData<EnemyHealth>(enemy).current);
    }

    [Test]
    public void 어그로가_더_높은_아군이_때리면_그쪽으로_돌아선다()
    {
        // 탱커가 고블린을 끌어오는 수단이다(게임오브젝트 고블린의 ShouldSwitchAggroTo).
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), DefaultStats());
        AddAlly(new float3(0f, 0f, 1f));                          // 0번: 코앞의 평범한 아군(점수 1.0)
        AddAlly(new float3(7f, 0f, 0f), threatWeight: 3.2f);       // 1번: 옆의 탱커(점수 7/3.2 = 2.2)

        Tick(0.05f, 2);
        Assert.AreEqual(0, manager.GetComponentData<EnemyTarget>(enemy).allyIndex);

        HitEnemy(enemy, 5, new float3(7f, 0f, 0f), attackerAllyIndex: 1);
        Tick(0.05f, 20);   // 휘두르던 중이면 그 스윙을 마친 뒤에 돌아선다

        Assert.AreEqual(1, manager.GetComponentData<EnemyTarget>(enemy).allyIndex);
    }

    [Test]
    public void 어그로가_낮은_아군이_때리면_돌아서지_않는다()
    {
        // 탱커와 맞붙은 놈은 뒤에서 궁수가 쏴도 돌아서지 않는다 — "때리고 어그로를 탱커에게 넘긴다"가 여기서 성립한다.
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), DefaultStats());
        AddAlly(new float3(0f, 0f, 1f), threatWeight: 3.2f);       // 0번: 탱커
        AddAlly(new float3(7f, 0f, 0f), threatWeight: 0.4f);       // 1번: 궁수

        Tick(0.05f, 2);
        Assert.AreEqual(0, manager.GetComponentData<EnemyTarget>(enemy).allyIndex);

        HitEnemy(enemy, 5, new float3(7f, 0f, 0f), attackerAllyIndex: 1);
        Tick(0.05f, 20);

        Assert.AreEqual(0, manager.GetComponentData<EnemyTarget>(enemy).allyIndex);
    }

    [Test]
    public void 은신한_아군은_들킬_거리_밖이면_노리지_않는다()
    {
        Entity far = CreateEnemy(new float3(0f, 0f, 0f), DefaultStats());
        Entity near = CreateEnemy(new float3(10f, 0f, 0f), DefaultStats());
        AddAlly(new float3(0f, 0f, 4f));
        AddAlly(new float3(10f, 0f, 1.5f));

        for (int i = 0; i < 2; i++)
        {
            EnemyWorldBridge.AllyState ally = EnemyWorldBridge.AllyStates[i];
            ally.hidden = 1;
            ally.revealRange = 2.2f;
            EnemyWorldBridge.AllyStates[i] = ally;
        }

        Tick(0.05f, 3);

        Assert.AreEqual(EnemyTarget.None, manager.GetComponentData<EnemyTarget>(far).allyIndex,
            "4m 밖의 은신한 아군은 보이지 않아야 한다");
        Assert.AreEqual(1, manager.GetComponentData<EnemyTarget>(near).allyIndex,
            "코앞(1.5m)까지 오면 들킨다");
    }

    [Test]
    public void 물기는_붙은_지_얼마_안_됐으면_하지_않고_한_전투에_정한_횟수만_한다()
    {
        EnemyStats stats = DefaultStats();
        stats.biteDamage = 24;
        stats.biteDuration = 0.3f;
        stats.biteCooldown = 0.2f;
        stats.biteUsesPerBattle = 1;
        stats.biteEngageDelay = 1f;

        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 1f));

        Tick(0.05f, 10);   // 0.5초 — 붙은 지 1초가 안 됐다
        Assert.AreEqual(0, manager.GetComponentData<EnemyAction>(enemy).biteUsesSpent);

        Tick(0.05f, 100);  // 5초 — 재사용 대기(0.2초)는 수십 번 돌았다
        Assert.AreEqual(1, manager.GetComponentData<EnemyAction>(enemy).biteUsesSpent,
            "한 전투에 한 번으로 정했으면 한 번만 물어야 한다");
    }

    [Test]
    public void 도약하면_몸이_떴다가_땅으로_돌아온다()
    {
        EnemyStats stats = DefaultStats();
        stats.leapRange = 3f;
        stats.leapDuration = 1.1f;
        stats.leapHeight = 0.5f;

        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 2.5f));

        Tick(0.05f, 4);
        Assert.AreEqual(EnemyActionKind.Leap, manager.GetComponentData<EnemyAction>(enemy).kind);

        float peak = 0f;
        for (int i = 0; i < 30; i++)
        {
            Tick(0.05f);
            peak = math.max(peak, manager.GetComponentData<LocalTransform>(enemy).Position.y);
        }

        Assert.Greater(peak, 0.3f, "발이 뜬 구간에서 몸이 올라가야 한다");
        Assert.AreEqual(0f, manager.GetComponentData<LocalTransform>(enemy).Position.y, 0.001f,
            "도약이 끝나면 땅에 붙어 있어야 한다");
    }

    [Test]
    public void 도약_클립이_떠_있는_만큼_착지할_때_눌러_내린다()
    {
        // 공중 공격 클립이라 발끝이 땅에서 떠 있다. 착지하는 순간 몸을 그만큼 내려 발을 땅에 디디게 한다.
        EnemyStats stats = DefaultStats();
        stats.leapRange = 3f;
        stats.leapDuration = 1.1f;
        stats.leapHeight = 0.5f;
        stats.leapClipFloat = 0.1f;

        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 2.5f));

        Tick(0.05f, 4);

        // 피해가 들어가는 프레임(착지)의 높이.
        float atLanding = float.NaN;
        for (int i = 0; i < 30 && float.IsNaN(atLanding); i++)
        {
            Tick(0.05f);
            if (EnemyWorldBridge.HitsOnAllies.Count > 0) atLanding = manager.GetComponentData<LocalTransform>(enemy).Position.y;
        }

        Assert.AreEqual(-0.1f, atLanding, 0.08f, "착지 순간에는 클립이 떠 있는 만큼 눌려 있어야 한다");

        Tick(0.05f, 20);
        Assert.AreEqual(0f, manager.GetComponentData<LocalTransform>(enemy).Position.y, 0.001f,
            "도약이 끝나면 누른 만큼도 되돌려 땅 높이로 돌아온다");
    }

    [Test]
    public void 도약이_끊겨도_땅으로_내려온다()
    {
        EnemyStats stats = DefaultStats();
        stats.leapRange = 3f;
        stats.leapDuration = 1.1f;
        stats.leapHeight = 0.5f;
        stats.maxPoise = 10f;

        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 2.5f));

        Tick(0.05f, 4);

        // 한창 떠 있을 때까지 민다. 도약이 시작되는 프레임은 판단 박자에 따라 한두 프레임 흔들린다.
        float height = 0f;
        for (int i = 0; i < 20 && height < 0.3f; i++)
        {
            Tick(0.05f);
            height = manager.GetComponentData<LocalTransform>(enemy).Position.y;
        }
        Assert.Greater(height, 0.3f);
        Assert.AreEqual(EnemyActionKind.Leap, manager.GetComponentData<EnemyAction>(enemy).kind);

        EnemyWorldBridge.DamageEnemy(enemy, 1, 50f, new float3(0f, 0f, 2.5f));   // 강인도가 깨져 무너진다
        Tick(0.05f);
        float falling = manager.GetComponentData<LocalTransform>(enemy).Position.y;
        Assert.Less(falling, height, "끊긴 순간부터 내려와야 한다");
        Assert.Greater(falling, 0f, "한 프레임에 땅으로 튀지 않고 내려온다");

        Tick(0.05f, 10);   // 0.5초 — 1m/s로 최대 0.4m를 내려온다
        Assert.AreEqual(0f, manager.GetComponentData<LocalTransform>(enemy).Position.y, 0.001f);
    }

    [Test]
    public void 공포가_넘치면_굳어서_아무것도_못_한다()
    {
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), DefaultStats());
        UseEmotion(enemy);
        AddAlly(new float3(0f, 0f, 1f));

        // 공포는 매 프레임 먼저 식고 나서 패닉을 잰다(UnitEmotion과 같은 순서). 그래서 실제로 넘치는 것은
        // HP가 바닥(30% 이하)이라 식지 않고 차오르는 동안이다.
        EnemyHealth health = manager.GetComponentData<EnemyHealth>(enemy);
        health.current = 20;
        manager.SetComponentData(enemy, health);
        manager.SetComponentData(enemy, new EnemyEmotion { fear = 85f });
        Tick(0.05f, 20);   // 1초 — 패닉은 2.5초

        Assert.AreEqual(EnemyActionKind.Panic, manager.GetComponentData<EnemyAction>(enemy).kind);
        Assert.AreEqual(0, EnemyWorldBridge.HitsOnAllies.Count, "굳어 있는 동안에는 코앞의 아군도 때리지 못한다");

        Tick(0.05f, 40);   // 3초 뒤
        Assert.AreNotEqual(EnemyActionKind.Panic, manager.GetComponentData<EnemyAction>(enemy).kind,
            "패닉은 시간이 지나면 풀려야 한다");
    }

    [Test]
    public void 공포에_빠지면_때리는_힘이_약해진다()
    {
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), DefaultStats());
        UseEmotion(enemy);
        AddAlly(new float3(0f, 0f, 1f));

        // 공포(40) 이상, 패닉(85) 미만. 식는 속도(초당 8.4)보다 넉넉하게 둔다.
        manager.SetComponentData(enemy, new EnemyEmotion { fear = 80f, fearful = true });
        Tick(0.05f, 30);

        Assert.IsTrue(EnemyWorldBridge.HitsOnAllies.TryDequeue(out var hit), "휘둘렀어야 한다");
        Assert.AreEqual(28, hit.damage, "공포에 빠지면 공격력이 30% 떨어진다(40 → 28)");
    }

    [Test]
    public void 급소를_베이면_피를_흘린다()
    {
        EnemyStats stats = DefaultStats();
        stats.maxHp = 1000;
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        UseEmotion(enemy);

        Tick(0.05f, 2);   // 판단 박자가 개체의 난수를 채운다
        HitEnemy(enemy, 10, new float3(0f, 0f, 5f), bleedChance: 1f);
        Tick(0.05f);
        Assert.IsTrue(manager.GetComponentData<EnemyEmotion>(enemy).IsBleeding);

        int before = manager.GetComponentData<EnemyHealth>(enemy).current;
        Tick(0.05f, 50);   // 2.5초 — 1초마다 최대 HP의 3%

        Assert.LessOrEqual(manager.GetComponentData<EnemyHealth>(enemy).current, before - 60);
    }

    [Test]
    public void 곁에서_동료가_쓰러지면_겁을_먹는다()
    {
        Entity victim = CreateEnemy(new float3(0f, 0f, 0f), DefaultStats());
        Entity witness = CreateEnemy(new float3(3f, 0f, 0f), DefaultStats());
        Entity faraway = CreateEnemy(new float3(40f, 0f, 0f), DefaultStats());
        UseEmotion(victim, witness, faraway);

        HitEnemy(victim, 1000, new float3(0f, 0f, 5f));
        Tick(0.02f);

        Assert.Greater(manager.GetComponentData<EnemyEmotion>(witness).fear, 10f, "12m 안에서 본 죽음은 공포를 올린다");
        Assert.AreEqual(0f, manager.GetComponentData<EnemyEmotion>(faraway).fear, 0.001f);
    }

    [Test]
    public void 표적이_있으면_서서_노려보고_없으면_두리번거린다()
    {
        // 두리번거리는 대기 자세(Idle)는 몸통이 좌우로 160도 돈다. 겨눈 상대가 있는 동안은 정면만 보는
        // 자세(GuardIdle)로 서야 한다 — 몸은 표적을 보는데 자세가 옆을 둘러보면 딴 데를 보는 것처럼 보인다.
        PublishClipSpeeds(1.5f, 2.3f);

        EnemyStats stats = DefaultStats();
        stats.moveSpeed = 0f;   // 붙으러 가지 못하게 세워 둔다

        Entity watcher = CreateEnemy(new float3(0f, 0f, 0f), stats);
        Entity searcher = CreateEnemy(new float3(40f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 5f));

        Tick(0.05f, 10);

        Assert.AreEqual(0, manager.GetComponentData<EnemyTarget>(watcher).allyIndex);
        Assert.AreEqual(EnemyClip.GuardIdle, manager.GetComponentData<EnemyAnimation>(watcher).clip,
            "겨눈 상대가 있으면 노려보며 선다");
        Assert.AreEqual(EnemyClip.Idle, manager.GetComponentData<EnemyAnimation>(searcher).clip,
            "겨눌 상대가 없으면 두리번거린다");
    }

    private Entity CreateEnemyFacing(float3 position, float3 lookAt, EnemyStats stats)
    {
        Entity entity = CreateEnemy(position, stats);
        float3 forward = lookAt - position;
        forward.y = 0f;
        manager.SetComponentData(entity, LocalTransform.FromPositionRotation(
            position, quaternion.LookRotationSafe(math.normalizesafe(forward, new float3(0f, 0f, 1f)), math.up())));
        return entity;
    }

    private static bool IsSwinging(EnemyActionKind kind)
    {
        return kind == EnemyActionKind.Windup || kind == EnemyActionKind.Recover ||
               kind == EnemyActionKind.Leap || kind == EnemyActionKind.Bite;
    }

    [Test]
    public void 한_사람에게_동시에_칼을_드는_수는_슬롯_수를_넘지_않는다()
    {
        AddAlly(new float3(0f, 0f, 0f));

        var enemies = new Entity[6];
        for (int i = 0; i < enemies.Length; i++)
        {
            float angle = i * math.PI * 2f / enemies.Length;
            float3 position = new float3(math.cos(angle), 0f, math.sin(angle)) * 1.05f;
            enemies[i] = CreateEnemyFacing(position, float3.zero, DefaultStats());
        }

        int maxSwinging = 0;
        for (int step = 0; step < 80; step++)
        {
            Tick(0.05f);

            int swinging = 0;
            int attackers = 0;
            for (int i = 0; i < enemies.Length; i++)
            {
                if (IsSwinging(manager.GetComponentData<EnemyAction>(enemies[i]).kind)) swinging++;
                if (manager.GetComponentData<EnemyTactics>(enemies[i]).role == EnemyCombatRole.Attacker) attackers++;
            }

            Assert.LessOrEqual(attackers, EnemyAttackSlotSystem.DefaultSlotsPerAlly, "자리는 둘뿐이다");
            maxSwinging = math.max(maxSwinging, swinging);
        }

        Assert.LessOrEqual(maxSwinging, EnemyAttackSlotSystem.DefaultSlotsPerAlly,
            "여섯이 둘러싸도 동시에 칼을 드는 것은 자리 수만큼이다");
        Assert.Greater(maxSwinging, 0, "자리를 얻은 놈은 실제로 휘둘러야 한다");
    }

    [Test]
    public void 자리를_못_얻은_놈은_한_걸음_떨어져_기다린다()
    {
        EnemyStats stats = DefaultStats();
        stats.waitDistanceMin = 2.2f;
        stats.waitDistanceMax = 2.2f;

        AddAlly(new float3(0f, 0f, 0f), attackSlots: 1);
        Entity holder = CreateEnemyFacing(new float3(0f, 0f, 1.05f), float3.zero, stats);
        Entity waiter = CreateEnemyFacing(new float3(0f, 0f, -6f), float3.zero, stats);

        Tick(0.05f, 60);

        Assert.AreEqual(EnemyCombatRole.Attacker, manager.GetComponentData<EnemyTactics>(holder).role);
        Assert.AreEqual(EnemyCombatRole.Waiter, manager.GetComponentData<EnemyTactics>(waiter).role);

        float distance = math.length(manager.GetComponentData<LocalTransform>(waiter).Position);
        Assert.Greater(distance, 1.9f, "칼 들 자리가 없으면 붙어 서지 않는다");
        Assert.Less(distance, 3f, "그렇다고 멀찍이 떨어져 구경하지도 않는다");
        Assert.IsFalse(IsSwinging(manager.GetComponentData<EnemyAction>(waiter).kind));
    }

    [Test]
    public void 다_휘두르면_자리를_내주고_기다리던_놈이_받는다()
    {
        EnemyStats stats = DefaultStats();
        stats.swingsPerSlotMin = 1;
        stats.swingsPerSlotMax = 1;
        stats.slotYieldDelay = 1.5f;

        AddAlly(new float3(0f, 0f, 0f), attackSlots: 1);
        Entity first = CreateEnemyFacing(new float3(0f, 0f, 1.05f), float3.zero, stats);
        Entity second = CreateEnemyFacing(new float3(0f, 0f, -2.2f), float3.zero, stats);

        Tick(0.05f);
        Assert.AreEqual(EnemyCombatRole.Attacker, manager.GetComponentData<EnemyTactics>(first).role);

        // 한 번 휘두르고(준비 0.4 + 회수 0.35) 자리를 내준다. 두 번째가 받아서 실제로 휘두른다.
        bool secondSwung = false;
        for (int step = 0; step < 60 && !secondSwung; step++)
        {
            Tick(0.05f);
            secondSwung = IsSwinging(manager.GetComponentData<EnemyAction>(second).kind);
        }

        Assert.IsTrue(secondSwung, "앞의 놈이 휘두르기를 마치면 기다리던 놈이 칼을 든다");
    }

    [Test]
    public void 무너지면_자리를_돌려준다()
    {
        EnemyStats stats = DefaultStats();
        AddAlly(new float3(0f, 0f, 0f), attackSlots: 1);
        Entity first = CreateEnemyFacing(new float3(0f, 0f, 1.05f), float3.zero, stats);
        Entity second = CreateEnemyFacing(new float3(0f, 0f, -2.0f), float3.zero, stats);

        Tick(0.05f, 2);
        Assert.AreEqual(EnemyCombatRole.Attacker, manager.GetComponentData<EnemyTactics>(first).role);
        Assert.AreNotEqual(EnemyCombatRole.Attacker, manager.GetComponentData<EnemyTactics>(second).role);

        EnemyWorldBridge.StaggerEnemy(first, 2f, new float3(0f, 0f, 3f));
        Tick(0.05f, 2);

        Assert.AreNotEqual(EnemyCombatRole.Attacker, manager.GetComponentData<EnemyTactics>(first).role,
            "무너진 놈이 자리를 쥐고 있으면 그 빈틈을 아무도 못 채운다");
        Assert.AreEqual(EnemyCombatRole.Attacker, manager.GetComponentData<EnemyTactics>(second).role);
    }

    [Test]
    public void 표적을_잃으면_자리도_돌려준다()
    {
        AddAlly(new float3(0f, 0f, 0f));
        Entity enemy = CreateEnemyFacing(new float3(0f, 0f, 1.05f), float3.zero, DefaultStats());

        Tick(0.05f);
        Assert.AreEqual(EnemyCombatRole.Attacker, manager.GetComponentData<EnemyTactics>(enemy).role);

        EnemyWorldBridge.AllyState ally = EnemyWorldBridge.AllyStates[0];
        ally.alive = 0;
        EnemyWorldBridge.AllyStates[0] = ally;

        // 이미 나간 스윙은 끝까지 간다. 그 뒤에는 쥐고 있을 이유가 없다.
        Tick(0.05f, 20);
        Assert.AreNotEqual(EnemyCombatRole.Attacker, manager.GetComponentData<EnemyTactics>(enemy).role);
    }

    [Test]
    public void 판단_박자는_마리마다_흩어져_있다()
    {
        EnemyStats stats = DefaultStats();
        stats.thinkIntervalMin = 0.18f;
        stats.thinkIntervalMax = 0.36f;

        var enemies = new Entity[40];
        for (int i = 0; i < enemies.Length; i++)
        {
            enemies[i] = CreateEnemy(new float3(i * 3f, 0f, 0f), stats);
        }

        int maxThinkingInOneFrame = 0;
        int totalThinks = 0;
        for (int step = 0; step < 50; step++)
        {
            Tick(0.02f);

            int thinking = 0;
            for (int i = 0; i < enemies.Length; i++)
            {
                if (manager.GetComponentData<EnemyTactics>(enemies[i]).thinking) thinking++;
            }

            maxThinkingInOneFrame = math.max(maxThinkingInOneFrame, thinking);
            totalThinks += thinking;
        }

        float firstInterval = manager.GetComponentData<EnemyTactics>(enemies[0]).thinkInterval;
        bool anyDifferent = false;
        for (int i = 1; i < enemies.Length; i++)
        {
            float interval = manager.GetComponentData<EnemyTactics>(enemies[i]).thinkInterval;
            Assert.That(interval, Is.InRange(0.18f, 0.36f));
            if (math.abs(interval - firstInterval) > 0.001f) anyDifferent = true;
        }

        Assert.IsTrue(anyDifferent, "판단 주기가 전원 같으면 같은 프레임에 같이 움직인다");
        Assert.Less(maxThinkingInOneFrame, enemies.Length / 2, "한 프레임에 무리의 절반 넘게 같이 판단하면 흩어진 것이 아니다");
        Assert.Greater(totalThinks, enemies.Length * 2, "1초 동안 마리마다 여러 번은 판단해야 한다");
    }

    [Test]
    public void 판단_박자가_오기_전에는_칼을_들지_않는다()
    {
        EnemyStats stats = DefaultStats();
        stats.thinkIntervalMin = 10f;
        stats.thinkIntervalMax = 10f;

        AddAlly(new float3(0f, 0f, 0f));
        Entity enemy = CreateEnemyFacing(new float3(0f, 0f, 1.05f), float3.zero, stats);

        // 성격을 뽑게 한 번 돌린 뒤, 다음 판단을 멀리 밀어 둔다.
        Tick(0.05f);
        EnemyTactics tactics = manager.GetComponentData<EnemyTactics>(enemy);
        tactics.nextThinkTime = world.Time.ElapsedTime + 5d;
        manager.SetComponentData(enemy, tactics);

        EnemyWorldBridge.HitsOnAllies.Clear();
        Tick(0.05f, 10);
        Assert.AreEqual(0, EnemyWorldBridge.HitsOnAllies.Count,
            "사거리 안이라도 판단 박자가 아니면 새 스윙을 시작하지 않는다");
        Assert.AreNotEqual(EnemyActionKind.Windup, manager.GetComponentData<EnemyAction>(enemy).kind);

        // 박자가 오면 그 프레임에 표적을 잡고, 자리를 청하고, 칼을 든다.
        tactics = manager.GetComponentData<EnemyTactics>(enemy);
        tactics.nextThinkTime = world.Time.ElapsedTime;
        manager.SetComponentData(enemy, tactics);

        Tick(0.05f);
        Assert.AreEqual(EnemyActionKind.Windup, manager.GetComponentData<EnemyAction>(enemy).kind);
    }

    // ---------------------------------------------------------------- 타격의 무게

    [Test]
    public void 무너지면_맞은_반대쪽으로_밀려난다()
    {
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), DefaultStats());

        // 앞(+Z)에서 강인도를 통째로 깎는다.
        EnemyWorldBridge.DamageEnemy(enemy, 10, 120f, new float3(0f, 0f, 2f));
        Tick(0.05f, 10);

        float3 position = manager.GetComponentData<LocalTransform>(enemy).Position;
        Assert.Less(position.z, -0.4f, "때린 쪽 반대로 밀려나야 한다(knockbackDistance 0.6)");
        Assert.Greater(position.z, -0.7f, "밀린 거리는 한 방의 넉백을 넘지 않는다");
    }

    [Test]
    public void 여러_번_맞아도_넉백은_합산되지_않는다()
    {
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), DefaultStats());

        for (int i = 0; i < 5; i++)
        {
            EnemyWorldBridge.DamageEnemy(enemy, 1, 120f, new float3(0f, 0f, 2f));
        }

        Tick(0.05f, 20);

        float3 position = manager.GetComponentData<LocalTransform>(enemy).Position;
        Assert.Greater(position.z, -0.7f, "같은 프레임의 다섯 대가 3m를 날려 보내면 난전이 아니라 핀볼이다");
    }

    [Test]
    public void 히트스톱_동안은_준비_동작의_시간도_멈춘다()
    {
        AddAlly(new float3(0f, 0f, 0f));
        Entity enemy = CreateEnemyFacing(new float3(0f, 0f, 1.05f), float3.zero, DefaultStats());

        Tick(0.05f, 2);
        Assert.AreEqual(EnemyActionKind.Windup, manager.GetComponentData<EnemyAction>(enemy).kind);
        float timerBefore = manager.GetComponentData<EnemyAction>(enemy).timer;

        EnemyWorldBridge.HitsOnEnemies.Enqueue(new EnemyWorldBridge.HitOnEnemy
        {
            enemy = enemy,
            damage = 1,
            fromPosition = float3.zero,
            attackerAllyIndex = 0,
            hitStopDuration = 0.5f,
            hitStopScale = 0f,
            impactWeight = 1f,
        });

        Tick(0.05f, 3);

        EnemyAction action = manager.GetComponentData<EnemyAction>(enemy);
        Assert.AreEqual(EnemyActionKind.Windup, action.kind, "스윙 도중에 맞은 한 대는 스윙을 끊지 않는다");
        Assert.AreEqual(timerBefore, action.timer, 0.0001f, "멈칫하는 동안은 칼이 내려오지 않는다");
    }

    [Test]
    public void 휘두른_칼이_닿으면_휘두른_쪽도_멈칫한다()
    {
        EnemyStats stats = DefaultStats();
        stats.hitStopDuration = 0.08f;
        stats.hitStopScale = 0.1f;

        AddAlly(new float3(0f, 0f, 0f));
        Entity enemy = CreateEnemyFacing(new float3(0f, 0f, 1.05f), float3.zero, stats);

        bool struck = false;
        for (int step = 0; step < 30 && !struck; step++)
        {
            Tick(0.05f);
            struck = EnemyWorldBridge.HitsOnAllies.Count > 0;
        }

        Assert.IsTrue(struck);
        Assert.Greater(manager.GetComponentData<EnemyImpact>(enemy).hitStopUntil, world.Time.ElapsedTime,
            "맞은 쪽만 멈추면 부딪힌 것이 아니라 렉으로 보인다");
    }

    [Test]
    public void 피해_없는_둔화는_움찔을_일으키지_않는다()
    {
        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), DefaultStats());

        EnemyWorldBridge.SlowEnemy(enemy, 2f, 0.5f, new float3(0f, 0f, 2f));
        Tick(0.05f);

        Assert.AreNotEqual(EnemyActionKind.HitReact, manager.GetComponentData<EnemyAction>(enemy).kind);
        Assert.Less(manager.GetComponentData<EnemyMotion>(enemy).SlowFactor(world.Time.ElapsedTime), 1f);
    }

    // ---------------------------------------------------------------- 발놀림
    //
    // "고블린이 너무 빠르게 접근하고 마음대로 회전한다"를 잡은 규칙 넷. 게임오브젝트 고블린 시절에는
    // NavMeshAgent와 UnitController가 해 주던 일이라, 엔티티로 옮기면서 조용히 빠졌던 것들이다.

    private static float3 Forward(in LocalTransform transform)
    {
        float3 forward = transform.Forward();
        forward.y = 0f;
        return math.normalizesafe(forward);
    }

    [Test]
    public void 붙은_뒤_이웃에게_밀려도_표적을_본다()
    {
        EnemyStats stats = DefaultStats();
        AddAlly(new float3(0f, 0f, 0f), attackSlots: 1);

        // 둘이 표적 앞에서 옆으로 겹쳐 있다. 서로 밀어내는 힘은 옆으로 난다.
        Entity a = CreateEnemyFacing(new float3(0.25f, 0f, 2.2f), float3.zero, stats);
        Entity b = CreateEnemyFacing(new float3(-0.25f, 0f, 2.2f), float3.zero, stats);

        Tick(0.05f, 20);

        foreach (Entity enemy in new[] { a, b })
        {
            LocalTransform transform = manager.GetComponentData<LocalTransform>(enemy);
            if (IsSwinging(manager.GetComponentData<EnemyAction>(enemy).kind)) continue;

            float3 toTarget = math.normalizesafe(-new float3(transform.Position.x, 0f, transform.Position.z));
            Assert.Greater(math.dot(Forward(transform), toTarget), 0.9f,
                "옆으로 밀리는 방향을 따라 몸이 돌면 무리 전체가 제자리에서 빙글빙글 돈다");
        }
    }

    [Test]
    public void 이웃에게_살짝_밀리는_힘만으로는_전속력으로_튀지_않는다()
    {
        EnemyStats stats = DefaultStats();

        // 표적이 없다. 반지름 합(1.0m)보다 조금 가깝다.
        Entity a = CreateEnemy(new float3(0f, 0f, 0f), stats);
        CreateEnemy(new float3(0.9f, 0f, 0f), stats);

        float maxSpeed = 0f;
        for (int i = 0; i < 20; i++)
        {
            Tick(0.05f);
            maxSpeed = math.max(maxSpeed, math.length(manager.GetComponentData<EnemyMotion>(a).velocity));
        }

        Assert.Less(maxSpeed, stats.moveSpeed * 0.5f, "10cm 겹친 것을 풀자고 4m/s로 튀면 안 된다");
    }

    [Test]
    public void 멈춰_설_거리에_다가갈수록_느려진다()
    {
        EnemyStats stats = DefaultStats();
        AddAlly(new float3(0f, 0f, 0f));
        Entity enemy = CreateEnemyFacing(new float3(0f, 0f, 8f), float3.zero, stats);

        // 휘두르지 못하게 재사용 대기를 멀리 밀어 둔다(휘두르기 시작하면 제자리에 서 버려서 감속을 볼 수 없다).
        // 자리 요청을 막으면 안 된다 — 자리를 못 얻은 놈은 한 걸음 떨어진 데서 기다리므로 멈춰 설 거리까지 오지 않는다.
        EnemyAction action = manager.GetComponentData<EnemyAction>(enemy);
        action.nextAttackTime = 999d;
        manager.SetComponentData(enemy, action);

        float farSpeed = 0f;
        float nearSpeed = -1f;
        float closest = float.MaxValue;
        for (int i = 0; i < 80; i++)
        {
            Tick(0.05f);
            float z = manager.GetComponentData<LocalTransform>(enemy).Position.z;
            float speed = math.length(manager.GetComponentData<EnemyMotion>(enemy).velocity);
            closest = math.min(closest, z);

            if (z > 4f) farSpeed = math.max(farSpeed, speed);
            if (nearSpeed < 0f && z < stats.standoffDistance + 0.3f) nearSpeed = speed;
        }

        Assert.Greater(farSpeed, stats.moveSpeed * 0.8f, "멀 때는 제 속도로 달려온다");
        Assert.GreaterOrEqual(nearSpeed, 0f, "멈춰 설 거리 근처까지는 와야 한다");
        Assert.Less(nearSpeed, stats.moveSpeed * 0.5f, "코앞까지 최고 속도로 달려와 박히면 안 된다");
        Assert.Greater(closest, stats.standoffDistance - 0.25f, "멈춰 설 거리를 크게 지나쳐 파고들지 않는다");
    }

    [Test]
    public void 달리기_클립은_실제_속도에_맞춰_돈다()
    {
        EnemyStats stats = DefaultStats();
        stats.runClipSpeed = 2f;
        AddAlly(new float3(0f, 0f, 0f));
        Entity enemy = CreateEnemyFacing(new float3(0f, 0f, 30f), float3.zero, stats);
        stats.detectRange = 40f;
        manager.SetComponentData(enemy, stats);

        // 최고 속도에 오를 때까지 달리게 둔다.
        Tick(0.05f, 20);
        EnemyAnimation before = manager.GetComponentData<EnemyAnimation>(enemy);
        Assert.AreEqual(EnemyClip.Run, before.clip);
        float speed = math.length(manager.GetComponentData<EnemyMotion>(enemy).velocity);

        Tick(0.05f);
        EnemyAnimation after = manager.GetComponentData<EnemyAnimation>(enemy);

        // 구간표가 없는 월드라 클립 길이는 1초로 본다. 4m/s ÷ 2m/s = 2배속이면 0.05초에 0.1이 간다.
        float advanced = math.frac(after.normalizedTime - before.normalizedTime + 1f);
        float expected = 0.05f * math.clamp(speed / stats.runClipSpeed, 0.6f, 2.2f);
        Assert.AreEqual(expected, advanced, 0.01f, "다리가 몸보다 느리게 돌면 땅을 미끄러진다");
    }

    [Test]
    public void 휘두르는_중에는_천천히_돈다()
    {
        EnemyStats stats = DefaultStats();
        AddAlly(new float3(0f, 0f, 0f));
        Entity enemy = CreateEnemyFacing(new float3(0f, 0f, 1.05f), float3.zero, stats);

        Tick(0.05f, 2);
        Assert.AreEqual(EnemyActionKind.Windup, manager.GetComponentData<EnemyAction>(enemy).kind);
        float3 forwardBefore = Forward(manager.GetComponentData<LocalTransform>(enemy));

        // 표적이 옆으로 90도 돌아갔다.
        EnemyWorldBridge.AllyState ally = EnemyWorldBridge.AllyStates[0];
        ally.position = new float3(1.05f, 0f, 1.05f);
        EnemyWorldBridge.AllyStates[0] = ally;

        Tick(0.05f, 2);
        float3 forwardAfter = Forward(manager.GetComponentData<LocalTransform>(enemy));
        float turned = math.degrees(math.acos(math.clamp(math.dot(forwardBefore, forwardAfter), -1f, 1f)));

        Assert.LessOrEqual(turned, stats.swingTurnRate * 0.1f + 1f,
            "칼을 들어올린 채로 몸이 홱 돌면 발이 미끄러진다");
        Assert.Greater(turned, 0f, "그래도 상대를 따라 조금은 돈다");
    }

    // ---------------------------------------------------------------- 정찰
    //
    // 여기서 고정하는 것:
    //  - 표적이 없으면 한동안 둘러본 뒤 정찰을 나선다(걷기 모션, 정찰 걸음)
    //  - 정찰은 전장 밖으로 나가지 않는다
    //  - 탐지 거리 밖으로 달아난 아군도 결국 찾아낸다 — 이게 없어서 생존자가 도망친 판이 끝나지 않았다
    //  - 정찰 범위가 없으면 예전처럼 제자리에 선다

    private static EnemyStats PatrolStats(float halfExtent)
    {
        EnemyStats stats = DefaultStats();
        stats.patrolCenter = float3.zero;
        stats.patrolHalfExtents = new float2(halfExtent, halfExtent);
        stats.patrolDelay = 0.5f;
        stats.patrolSpeed = 1.5f;
        stats.patrolPauseMin = 0.2f;
        stats.patrolPauseMax = 0.4f;
        stats.searchRadiusStart = 4f;
        stats.searchRadiusGrowth = 6f;
        return stats;
    }

    [Test]
    public void 표적이_없으면_둘러본_뒤_정찰을_나선다()
    {
        PublishClipSpeeds(walkSpeed: 1.52f, runSpeed: 1.67f);
        Entity enemy = CreateEnemy(float3.zero, PatrolStats(30f));

        Tick(0.05f, 6);   // 0.3초 — 아직 둘러보는 중
        Assert.IsFalse(manager.GetComponentData<EnemyTactics>(enemy).patrolling, "표적을 잃자마자 흩어지면 둘러볼 틈이 없다");
        Assert.AreEqual(0f, math.length(manager.GetComponentData<LocalTransform>(enemy).Position), 0.01f);

        bool walked = false;
        for (int i = 0; i < 60; i++)
        {
            Tick(0.05f);
            if (manager.GetComponentData<EnemyAnimation>(enemy).clip == EnemyClip.Walk) walked = true;
        }

        Assert.IsTrue(manager.GetComponentData<EnemyTactics>(enemy).patrolling);
        Assert.Greater(math.length(manager.GetComponentData<LocalTransform>(enemy).Position), 0.5f, "정찰 지점으로 걸어가야 한다");
        Assert.IsTrue(walked, "정찰 걸음은 걷기 클립으로 보여야 한다 — 달리면 쫓는 것처럼 보이고, 서 있으면 미끄러진다");
        Assert.LessOrEqual(math.length(manager.GetComponentData<EnemyMotion>(enemy).velocity), 1.5f + 0.01f);
    }

    [Test]
    public void 정찰은_전장_밖으로_나가지_않는다()
    {
        const float halfExtent = 5f;
        Entity enemy = CreateEnemy(float3.zero, PatrolStats(halfExtent));

        float widest = 0f;
        for (int i = 0; i < 1200; i++)   // 60초 — 반경이 전장을 몇 번이고 넘도록 넓어진다
        {
            Tick(0.05f);
            float3 p = manager.GetComponentData<LocalTransform>(enemy).Position;
            widest = math.max(widest, math.max(math.abs(p.x), math.abs(p.z)));
        }

        Assert.LessOrEqual(widest, halfExtent + 0.05f, "적은 지형 높이를 모르므로 평평한 전장 밖으로 나가면 땅에 파묻힌다");
        Assert.Greater(widest, 2f, "그래도 전장 안은 돌아다녀야 한다");
    }

    [Test]
    public void 탐지_거리_밖으로_달아난_아군도_정찰로_찾아낸다()
    {
        Entity enemy = CreateEnemy(float3.zero, PatrolStats(30f));
        AddAlly(new float3(22f, 0f, -14f));   // 26m — 탐지 거리(8m)의 세 배 밖

        bool found = false;
        for (int i = 0; i < 2400 && !found; i++)   // 최대 120초
        {
            Tick(0.05f);
            found = manager.GetComponentData<EnemyTarget>(enemy).allyIndex == 0;
        }

        Assert.IsTrue(found, "찾으러 가지 않으면 도망친 생존자와 제자리의 적이 영영 닿지 않아 전투가 끝나지 않는다");
    }

    [Test]
    public void 정찰_범위가_없으면_제자리에_선다()
    {
        Entity enemy = CreateEnemy(float3.zero, DefaultStats());

        Tick(0.05f, 100);   // 5초

        Assert.IsFalse(manager.GetComponentData<EnemyTactics>(enemy).patrolling);
        Assert.AreEqual(0f, math.length(manager.GetComponentData<LocalTransform>(enemy).Position), 0.01f);
    }

    [Test]
    public void 싸우는_동안은_정찰하지_않고_그_자리를_기억한다()
    {
        Entity enemy = CreateEnemy(float3.zero, PatrolStats(30f));
        AddAlly(new float3(0f, 0f, 5f));

        Tick(0.05f, 40);   // 2초 — 정찰을 나설 시간은 한참 지났다

        EnemyTactics tactics = manager.GetComponentData<EnemyTactics>(enemy);
        Assert.IsFalse(tactics.patrolling, "겨누는 상대가 있으면 정찰할 일이 없다");
        Assert.AreEqual(0f, math.distance(tactics.searchCenter, EnemyWorldBridge.AllyStates[0].position), 0.01f,
            "놓치면 마지막으로 본 자리부터 찾아야 한다");
    }
}
