using System.Collections.Generic;
using NUnit.Framework;

// 행동 트리 자체의 규칙. 유닛이나 씬 없이 가짜 컨텍스트로 돌린다.
//
// 여기서 고정하는 것은 넷이다:
//  - 조건이 거짓인 가지는 진입조차 하지 않는다(조건을 확인하는 것만으로 동작이 시작되면 안 된다)
//  - 위에 있는 가지가 이긴다. 아래 가지가 돌고 있어도 끊고 들어오며, 그때 뒷정리가 먼저 불린다
//  - 잠근 가지는 제 조건이 무너져도 스스로 끝날 때까지 돈다(더 높은 우선순위에는 그래도 끊긴다)
//  - 시퀀스는 돌고 있는 자식부터 이어 간다
// 넷 다 어긋나도 컴파일은 되고 증상은 "가끔 이상하다"로만 나타나는 종류라 테스트로 묶어 둔다.
public class BehaviorTreeTests
{
    private class Ctx
    {
    }

    private class Recording : BTNode<Ctx>
    {
        private readonly List<string> log;
        private readonly string name;

        // 이번 틱에 돌려줄 값. 테스트가 도중에 바꾼다.
        public BTStatus Result = BTStatus.Running;

        public Recording(Ctx context, string name, List<string> log) : base(context)
        {
            this.name = name;
            this.log = log;
        }

        protected override void OnEnter() => log.Add(name + ".Enter");

        protected override BTStatus OnTick()
        {
            log.Add(name + ".Tick");
            return Result;
        }

        protected override void OnExit() => log.Add(name + ".Exit");
    }

    // 시작하는 순간 값을 치르는 동작(스킬, 도약, 방어 자세). 같은 셀렉터 안에서는
    // 형제 우선순위를 다시 따지지 않는다.
    private class Committed : Recording
    {
        public Committed(Ctx context, string name, List<string> log) : base(context, name, log)
        {
        }

        public override bool AllowsReprioritize => false;
    }

    private List<string> log;
    private Ctx ctx;

    [SetUp]
    public void SetUp()
    {
        log = new List<string>();
        ctx = new Ctx();
    }

    private BTGuard<Ctx> Guard(System.Func<bool> condition, BTNode<Ctx> child, bool latch = false)
    {
        return new BTGuard<Ctx>(ctx, condition, child, latch);
    }

    [Test]
    public void 조건이_거짓이면_자식은_진입조차_하지_않는다()
    {
        var blocked = new Recording(ctx, "Blocked", log);
        var fallback = new Recording(ctx, "Fallback", log);

        var tree = new BehaviorTree<Ctx>(new BTSelector<Ctx>(ctx, true,
            Guard(() => false, blocked),
            fallback));

        tree.Tick();

        Assert.AreEqual(new[] { "Fallback.Enter", "Fallback.Tick" }, log.ToArray());
    }

    [Test]
    public void 앞에_있는_가지가_이긴다()
    {
        var first = new Recording(ctx, "First", log);
        var second = new Recording(ctx, "Second", log);

        var tree = new BehaviorTree<Ctx>(new BTSelector<Ctx>(ctx, true,
            Guard(() => true, first),
            Guard(() => true, second)));

        tree.Tick();

        CollectionAssert.DoesNotContain(log, "Second.Enter");
        Assert.AreEqual(new[] { "First.Enter", "First.Tick" }, log.ToArray());
    }

    [Test]
    public void 위쪽_조건이_성립하면_돌고_있던_아래_가지를_끊는다()
    {
        var high = new Recording(ctx, "High", log);
        var low = new Recording(ctx, "Low", log);
        bool urgent = false;

        var tree = new BehaviorTree<Ctx>(new BTSelector<Ctx>(ctx, true,
            Guard(() => urgent, high),
            low));

        tree.Tick();
        log.Clear();

        urgent = true;
        tree.Tick();

        // 뒷정리가 새 동작의 진입보다 반드시 먼저다 — 순서가 뒤집히면 방어 자세를 올린 채로
        // 다음 동작이 시작되거나, 공중에 뜬 모델을 내려놓기 전에 자리를 잡는 일이 생긴다.
        Assert.AreEqual(new[] { "Low.Exit", "High.Enter", "High.Tick" }, log.ToArray());
    }

    [Test]
    public void 잠근_가지는_조건이_무너져도_스스로_끝날_때까지_돈다()
    {
        var action = new Recording(ctx, "Action", log);
        var fallback = new Recording(ctx, "Fallback", log);
        bool wanted = true;

        var tree = new BehaviorTree<Ctx>(new BTSelector<Ctx>(ctx, true,
            Guard(() => wanted, action, true),
            fallback));

        tree.Tick();
        log.Clear();

        // 회복약처럼 시작하는 순간 제 조건이 무너지는 동작. 잠금이 없으면 여기서 잘려 나간다.
        wanted = false;
        tree.Tick();

        Assert.AreEqual(new[] { "Action.Tick" }, log.ToArray());

        // 스스로 끝나면 그때 자리를 넘긴다.
        action.Result = BTStatus.Success;
        log.Clear();
        tree.Tick();

        Assert.AreEqual(new[] { "Action.Tick", "Action.Exit" }, log.ToArray());
    }

    [Test]
    public void 잠근_가지도_더_높은_우선순위에는_끊긴다()
    {
        var dead = new Recording(ctx, "Dead", log);
        var action = new Recording(ctx, "Action", log);
        bool died = false;

        var tree = new BehaviorTree<Ctx>(new BTSelector<Ctx>(ctx, true,
            Guard(() => died, dead, true),
            Guard(() => true, action, true)));

        tree.Tick();
        log.Clear();

        died = true;
        tree.Tick();

        Assert.AreEqual(new[] { "Action.Exit", "Dead.Enter", "Dead.Tick" }, log.ToArray());
    }

    [Test]
    public void 시퀀스는_돌고_있는_자식부터_이어_간다()
    {
        var pick = new Recording(ctx, "Pick", log) { Result = BTStatus.Success };
        var walk = new Recording(ctx, "Walk", log);

        var tree = new BehaviorTree<Ctx>(new BTSequence<Ctx>(ctx, pick, walk));

        tree.Tick();
        Assert.AreEqual(new[] { "Pick.Enter", "Pick.Tick", "Pick.Exit", "Walk.Enter", "Walk.Tick" }, log.ToArray());

        // 걷는 동안 목적지를 다시 고르면 프레임마다 갈 곳이 바뀐다.
        log.Clear();
        tree.Tick();
        Assert.AreEqual(new[] { "Walk.Tick" }, log.ToArray());

        // 다 걸었으면 다음 틱에 다시 처음부터 — 그때 새 목적지를 고른다.
        walk.Result = BTStatus.Success;
        tree.Tick();
        log.Clear();
        tree.Tick();
        Assert.AreEqual("Pick.Enter", log[0]);
    }

    [Test]
    public void 기억형_셀렉터는_한_번_고른_갈래를_유지한다()
    {
        var run = new Recording(ctx, "Run", log);
        var step = new Recording(ctx, "Step", log);
        bool runs = false;

        var tree = new BehaviorTree<Ctx>(new BTSelector<Ctx>(ctx, false,
            Guard(() => runs, run),
            step));

        tree.Tick();
        log.Clear();

        // 물러나는 도중에 조건이 뒤집혀도 뒷걸음이 도주로 갈아타서는 안 된다.
        runs = true;
        tree.Tick();

        Assert.AreEqual(new[] { "Step.Tick" }, log.ToArray());
    }

    [Test]
    public void 트리를_접으면_돌고_있던_잎의_뒷정리가_불린다()
    {
        var action = new Recording(ctx, "Action", log);

        var tree = new BehaviorTree<Ctx>(new BTSelector<Ctx>(ctx, true,
            Guard(() => true, action, true)));

        tree.Tick();
        log.Clear();

        tree.Abort();

        Assert.AreEqual(new[] { "Action.Exit" }, log.ToArray());
        Assert.IsNull(tree.RunningLeaf);
    }

    [Test]
    public void RunningLeaf는_실제로_돌고_있는_잎을_가리킨다()
    {
        var idle = new Recording(ctx, "Idle", log);
        var action = new Recording(ctx, "Action", log);
        bool acts = false;

        var tree = new BehaviorTree<Ctx>(new BTSelector<Ctx>(ctx, true,
            Guard(() => acts, action),
            idle));

        tree.Tick();
        Assert.AreSame(idle, tree.RunningLeaf);

        acts = true;
        tree.Tick();
        Assert.AreSame(action, tree.RunningLeaf);
    }

    [Test]
    public void 잠근_동작은_같은_셀렉터_안의_우선순위로는_끊기지_않는다()
    {
        var better = new Recording(ctx, "Better", log);
        var skill = new Committed(ctx, "Skill", log);
        bool wantsBetter = false;

        var tree = new BehaviorTree<Ctx>(new BTSelector<Ctx>(ctx, true,
            Guard(() => wantsBetter, better),
            skill));

        tree.Tick();
        log.Clear();

        // 스킬은 시작하는 순간 쿨다운과 마나를 이미 썼다. 조금 더 나은 수가 생겼다고
        // 여기서 끊으면 그 값만 버린다.
        wantsBetter = true;
        tree.Tick();

        Assert.AreEqual(new[] { "Skill.Tick" }, log.ToArray());
    }

    [Test]
    public void 잠근_동작도_위쪽_계층에는_끊긴다()
    {
        var dead = new Recording(ctx, "Dead", log);
        var skill = new Committed(ctx, "Skill", log);
        bool died = false;

        // 잠금은 자기가 속한 셀렉터 안에서만 효력을 갖는다. 합성 노드는 그 말을 위로
        // 전하지 않으므로, 사망·패닉 같은 위쪽 계층은 그대로 끊고 들어온다.
        var tree = new BehaviorTree<Ctx>(new BTSelector<Ctx>(ctx, true,
            Guard(() => died, dead),
            new BTSelector<Ctx>(ctx, true, skill)));

        tree.Tick();
        log.Clear();

        died = true;
        tree.Tick();

        Assert.AreEqual(new[] { "Skill.Exit", "Dead.Enter", "Dead.Tick" }, log.ToArray());
    }

    [Test]
    public void 잠근_동작이_실패하면_그_아래_형제로_이어진다()
    {
        var retreat = new Committed(ctx, "Retreat", log);
        var attack = new Recording(ctx, "Attack", log);

        var tree = new BehaviorTree<Ctx>(new BTSelector<Ctx>(ctx, true,
            Guard(() => true, retreat, true),
            attack));

        tree.Tick();
        log.Clear();

        // 구석에 몰려 물러날 곳이 없다 — 벽에 붙어 굳어 있느니 돌아서서 싸운다.
        retreat.Result = BTStatus.Failure;
        tree.Tick();

        Assert.AreEqual(new[] { "Retreat.Tick", "Retreat.Exit", "Attack.Enter", "Attack.Tick" }, log.ToArray());
    }

    [Test]
    public void 자식이_모두_실패하면_셀렉터도_실패한다()
    {
        var a = new Recording(ctx, "A", log) { Result = BTStatus.Failure };

        var selector = new BTSelector<Ctx>(ctx, true,
            Guard(() => false, new Recording(ctx, "Blocked", log)),
            a);

        Assert.AreEqual(BTStatus.Failure, selector.Tick());
        Assert.AreEqual(new[] { "A.Enter", "A.Tick", "A.Exit" }, log.ToArray());
    }
}
