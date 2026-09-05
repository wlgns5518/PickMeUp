using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

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
    private SystemHandle targetingSystem;
    private SystemHandle combatSystem;
    private SystemHandle movementSystem;
    private SystemHandle damageSystem;

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
        turnSpeed = 8f,
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
        targetingSystem = world.CreateSystem<EnemyTargetingSystem>();
        combatSystem = world.CreateSystem<EnemyCombatSystem>();
        movementSystem = world.CreateSystem<EnemyMovementSystem>();
        damageSystem = world.CreateSystem<EnemyDamageSystem>();
    }

    [TearDown]
    public void TearDown()
    {
        if (world != null && world.IsCreated) world.Dispose();
        EnemyWorldBridge.Dispose();
    }

    private Entity CreateEnemy(float3 position, EnemyStats stats)
    {
        Entity entity = manager.CreateEntity(
            typeof(EnemyTag), typeof(EnemyStats), typeof(EnemyHealth), typeof(EnemyMotion),
            typeof(EnemyTarget), typeof(EnemyAction), typeof(EnemyAnimation),
            typeof(LocalTransform), typeof(LocalToWorld));

        manager.SetComponentData(entity, LocalTransform.FromPosition(position));
        manager.SetComponentData(entity, stats);
        manager.SetComponentData(entity, new EnemyHealth { current = stats.maxHp, poise = stats.maxPoise });
        manager.SetComponentData(entity, new EnemyTarget { allyIndex = EnemyTarget.None });
        manager.SetComponentData(entity, new EnemyAction { kind = EnemyActionKind.Idle });
        manager.SetComponentData(entity, new EnemyAnimation { clip = EnemyClip.Idle });
        return entity;
    }

    private void AddAlly(float3 position, float threatWeight = 1f, int attackerCount = 0)
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
        });
    }

    // 시간을 흘려보낸다. 시스템들이 SystemAPI.Time을 읽으므로 월드 시계를 직접 민다.
    private void Tick(float deltaTime, int steps = 1)
    {
        for (int i = 0; i < steps; i++)
        {
            double elapsed = world.Time.ElapsedTime + deltaTime;
            world.SetTime(new TimeData(elapsed, deltaTime));

            hashSystem.Update(world.Unmanaged);
            damageSystem.Update(world.Unmanaged);
            targetingSystem.Update(world.Unmanaged);
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
}
