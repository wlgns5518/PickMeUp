using NUnit.Framework;
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

    private void AddAlly(float3 position, float threatWeight = 1f, int attackerCount = 0, bool canBeBitten = true)
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
        stats.leapDuration = 0.3f;

        Entity enemy = CreateEnemy(new float3(0f, 0f, 0f), stats);
        AddAlly(new float3(0f, 0f, 2.5f));

        Tick(0.05f, 4);
        Assert.AreEqual(EnemyActionKind.Leap, manager.GetComponentData<EnemyAction>(enemy).kind);

        Tick(0.05f, 8);   // 0.3초를 넘긴다
        Assert.AreNotEqual(EnemyActionKind.Leap, manager.GetComponentData<EnemyAction>(enemy).kind,
            "도약이 영영 끝나지 않으면 그 자리에 떠 있게 된다");
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
}
