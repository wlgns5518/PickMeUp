using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using UnityEngine;

// 판단 계층에 넣은 잔손질이 정말 이득인지 재는 자.
//
// 호출 횟수가 줄었다고 빨라진 것은 아니다. 격자(EnemyQueryBenchmark)에서 그걸 한 번 겪었다 —
// 질의당 99.5% 줄었는데 전체로는 손해였다. 그래서 여기서도 "줄었다"가 아니라 "시간이 줄었다"를 잰다.
//
// 재는 것 셋:
//  1. 공유 전제를 가지 하나로 묶은 것. 검사는 3번에서 1번이 되지만 트리가 한 단 깊어진다.
//     노드를 더 타고 내려가는 값이 아낀 검사보다 크면 손해다.
//  2. 돌고 있는 잎을 잎까지 걸어 내려가 묻는 대신 셀렉터에게 바로 묻는 것.
//  3. 프레임 메모. Time.frameCount는 네이티브 호출이라, 지키려는 검사보다 비쌀 수 있다.
//
// [Explicit]이라 평소 실행에는 끼지 않는다.
public class BehaviorTreeBenchmark
{
    private const int Ticks = 200000;

    // 실제 UnitController의 검사와 같은 모양으로 둔다 — 전부 필드 읽기다.
    private sealed class Ctx
    {
        public bool actionBlocked, attackLocked, staggered, casting;
        public bool canPotion, canHeal, canShield;

        public bool CanTendSelf()
        {
            if (actionBlocked) return false;
            if (attackLocked) return false;
            if (staggered) return false;
            if (casting) return false;
            return true;
        }
    }

    // 아무것도 하지 않고 Running만 돌려주는 잎. 구조 비용만 남기기 위해서다.
    private sealed class Idle : BTNode<Ctx>
    {
        public Idle(Ctx c) : base(c) { }
        protected override BTStatus OnTick() => BTStatus.Running;
    }

    // 조건이 서면 도는 잎.
    private sealed class Act : BTNode<Ctx>
    {
        public Act(Ctx c) : base(c) { }
        protected override BTStatus OnTick() => BTStatus.Running;
    }

    private static BTGuard<Ctx> G(Ctx c, System.Func<bool> cond, BTNode<Ctx> child, bool latch = false)
        => new BTGuard<Ctx>(c, cond, child, latch);

    // 예전 모양: 세 가지가 각자 전제를 다시 묻는다.
    private static BehaviorTree<Ctx> BuildBefore(Ctx c)
    {
        return new BehaviorTree<Ctx>(new BTSelector<Ctx>(c, true,
            G(c, () => c.CanTendSelf() && c.canPotion, new Act(c), true),
            G(c, () => c.CanTendSelf() && c.canHeal, new Act(c), true),
            G(c, () => c.CanTendSelf() && c.canShield, new Act(c), true),
            new Idle(c)));
    }

    // 지금 모양: 전제를 가지 하나로 올리고 셋을 그 아래 셀렉터에 둔다.
    private static BehaviorTree<Ctx> BuildAfter(Ctx c)
    {
        return new BehaviorTree<Ctx>(new BTSelector<Ctx>(c, true,
            G(c, () => c.CanTendSelf(), new BTSelector<Ctx>(c, true,
                G(c, () => c.canPotion, new Act(c), true),
                G(c, () => c.canHeal, new Act(c), true),
                G(c, () => c.canShield, new Act(c), true)), true),
            new Idle(c)));
    }

    private static double TimeTicks(BehaviorTree<Ctx> tree)
    {
        for (int i = 0; i < 20000; i++) tree.Tick();

        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < Ticks; i++) tree.Tick();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds * 1e6 / Ticks;
    }

    [Test, Explicit]
    public void 공유_전제를_묶은_것이_이득인지_잰다()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("== 회복약·치유·보호막의 공유 전제 (틱 하나, " + Ticks + "회 평균) ==");
        sb.AppendLine();
        sb.AppendLine("상황                       |     예전 |     지금 |    차이");
        sb.AppendLine("---------------------------+----------+----------+--------");

        // (1) 가장 흔한 경우: 손은 비었는데 셋 다 쓸 일이 없다.
        //     예전은 전제를 3번, 지금은 1번 묻는다. 대신 지금은 트리를 한 단 더 내려간다.
        Row(sb, "손 비었고 셋 다 안 씀", c => { });

        // (2) 손이 바쁘다(휘두르는 중). 예전은 세 가지가 각각 두 번째 검사에서 걸리고,
        //     지금은 바깥 가드 하나에서 걸려 아래를 아예 보지 않는다.
        Row(sb, "휘두르는 중(전제 거짓)", c => c.attackLocked = true);

        // (3) 회복약을 마시는 중. 양쪽 다 잠금으로 첫 가지에서 끝난다.
        Row(sb, "회복약 진행 중(잠금)", c => c.canPotion = true);

        UnityEngine.Debug.Log(sb.ToString());
        Assert.Pass(sb.ToString());
    }

    private static void Row(StringBuilder sb, string label, System.Action<Ctx> setup)
    {
        Ctx a = new Ctx(); setup(a);
        Ctx b = new Ctx(); setup(b);

        double before = TimeTicks(BuildBefore(a));
        double after = TimeTicks(BuildAfter(b));

        sb.AppendLine(string.Format("{0,-26} | {1,6:F1}ns | {2,6:F1}ns | {3,6:F1}%",
            label, before, after, (after - before) * 100.0 / before));
    }

    // ------------------------------------------------------------------

    private sealed class Committed : BTNode<Ctx>
    {
        public Committed(Ctx c) : base(c) { }
        public override bool AllowsReprioritize => false;
        protected override BTStatus OnTick() => BTStatus.Running;
    }

    [Test, Explicit]
    public void 돌고_있는_잎을_묻는_두_방법을_잰다()
    {
        Ctx c = new Ctx();

        // 교전 셀렉터와 같은 모양 — 가드 아래 잠근 잎이 돈다.
        var selector = new BTSelector<Ctx>(c, true,
            G(c, () => true, new Committed(c), true),
            new Idle(c));

        selector.Tick();

        const int Runs = 2000000;

        bool sink = false;
        for (int i = 0; i < 200000; i++)
        {
            BTNode<Ctx> leaf = selector.FindRunningLeaf();
            sink ^= leaf != null && !leaf.AllowsReprioritize;
            sink ^= selector.RunningChildLocked;
        }

        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < Runs; i++)
        {
            BTNode<Ctx> leaf = selector.FindRunningLeaf();
            sink ^= leaf != null && !leaf.AllowsReprioritize;
        }
        sw.Stop();
        double walkNs = sw.Elapsed.TotalMilliseconds * 1e6 / Runs;

        sw.Restart();
        for (int i = 0; i < Runs; i++) sink ^= selector.RunningChildLocked;
        sw.Stop();
        double askNs = sw.Elapsed.TotalMilliseconds * 1e6 / Runs;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("== 돌고 있는 잎이 잠겼는가 (한 번, " + Runs + "회 평균) ==");
        sb.AppendLine();
        sb.AppendLine(string.Format("  잎까지 걸어 내려가 묻기 : {0,6:F2} ns", walkNs));
        sb.AppendLine(string.Format("  셀렉터에게 바로 묻기    : {0,6:F2} ns", askNs));
        sb.AppendLine(string.Format("  차이                    : {0,6:F1}%", (askNs - walkNs) * 100.0 / walkNs));
        sb.AppendLine();
        sb.AppendLine("  (프레임당 유닛 하나에 한 번 돈다. sink=" + sink + ")");

        UnityEngine.Debug.Log(sb.ToString());
        Assert.Pass(sb.ToString());
    }

    // ------------------------------------------------------------------

    private sealed class MemoProbe
    {
        public float threshold;      // 0 이하면 스캔까지 가지 않는다(근접 직군)

        // 에디터에서는 Time.frameCount가 멈춰 있어 메모가 영영 적중한다. 프레임이 넘어가는
        // 것은 이 값으로 흉내 내되, 네이티브 호출 비용은 실제로 태운다.
        public int simFrame;
        public int enemyCount;
        public double sink;

        private int frame = -1;
        private bool cached;

        // 메모 없이. 예전 모양이다.
        public bool Direct()
        {
            if (threshold <= 0f) return false;
            return Scan() > 0;
        }

        // 메모만 있는 모양. 싼 게이트가 메모 뒤에 있어 근접 직군이 Time.frameCount를 태운다.
        public bool Memoized()
        {
            int f = Time.frameCount + simFrame;
            if (frame == f) return cached;

            frame = f;
            cached = threshold > 0f && Scan() > 0;
            return cached;
        }

        // 지금 모양. 싼 게이트를 메모 앞으로 뺐다.
        public bool GatedMemo()
        {
            if (threshold <= 0f) return false;

            int f = Time.frameCount + simFrame;
            if (frame == f) return cached;

            frame = f;
            cached = Scan() > 0;
            return cached;
        }

        private int Scan()
        {
            int n = 0;
            for (int i = 0; i < enemyCount; i++)
            {
                sink += i * 0.25;
                if ((i & 63) == 0) n++;
            }

            return n;
        }
    }

    [Test, Explicit]
    public void 프레임_메모가_이득인지_잰다()
    {
        const int Runs = 500000;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("== ShouldKeepDistance의 프레임 메모 ==");
        sb.AppendLine();
        sb.AppendLine("Time.frameCount 자체를 먼저 잰다 — 네이티브 호출이라 공짜가 아니다.");

        int acc = 0;
        for (int i = 0; i < 100000; i++) acc += Time.frameCount;
        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < Runs; i++) acc += Time.frameCount;
        sw.Stop();
        sb.AppendLine(string.Format("  Time.frameCount : {0,6:F2} ns  (acc={1})",
            sw.Elapsed.TotalMilliseconds * 1e6 / Runs, acc == int.MinValue ? 1 : 0));
        sb.AppendLine();

        sb.AppendLine("직군/상황                        |   메모 없음 |   메모만 |  게이트+메모");
        sb.AppendLine("---------------------------------+-------------+----------+-------------");

        // 근접 직군: 유지 거리가 0이라 스캔까지 가지 않는다. 메모가 지킬 것이 없다.
        MemoRow(sb, "근접(스캔 안 감), 틱당 1회", 0f, 1000, 1);

        // 원거리 직군: 스캔을 돈다. 틱당 한 번만 물으면 메모는 순수 오버헤드다.
        MemoRow(sb, "원거리 1000마리, 틱당 1회", 2.6f, 1000, 1);

        // 같은 틱에 두 번, 네 번 물을 때. 실제 트리는 후퇴를 검토할 때 2~4번 묻는다.
        MemoRow(sb, "원거리 1000마리, 틱당 2회", 2.6f, 1000, 2);
        MemoRow(sb, "원거리 1000마리, 틱당 4회", 2.6f, 1000, 4);

        UnityEngine.Debug.Log(sb.ToString());
        Assert.Pass(sb.ToString());
    }

    private static void MemoRow(StringBuilder sb, string label, float threshold, int enemies, int callsPerTick)
    {
        const int TickCount = 20000;

        var direct = new MemoProbe { threshold = threshold, enemyCount = enemies };
        var memo = new MemoProbe { threshold = threshold, enemyCount = enemies };

        bool sink = false;

        for (int t = 0; t < 500; t++)
        {
            memo.simFrame = t;
            for (int i = 0; i < callsPerTick; i++) { sink ^= direct.Direct(); sink ^= memo.Memoized(); }
        }

        var gated = new MemoProbe { threshold = threshold, enemyCount = enemies };
        for (int t = 0; t < 500; t++)
        {
            gated.simFrame = t;
            for (int i = 0; i < callsPerTick; i++) sink ^= gated.GatedMemo();
        }

        Stopwatch sw = Stopwatch.StartNew();
        for (int t = 0; t < TickCount; t++)
        {
            for (int i = 0; i < callsPerTick; i++) sink ^= direct.Direct();
        }
        sw.Stop();
        double directNs = sw.Elapsed.TotalMilliseconds * 1e6 / TickCount;

        sw.Restart();
        for (int t = 0; t < TickCount; t++)
        {
            memo.simFrame = t;   // 틱마다 프레임이 넘어간다 — 메모는 틱당 한 번만 적중을 놓친다
            for (int i = 0; i < callsPerTick; i++) sink ^= memo.Memoized();
        }
        sw.Stop();
        double memoNs = sw.Elapsed.TotalMilliseconds * 1e6 / TickCount;

        sw.Restart();
        for (int t = 0; t < TickCount; t++)
        {
            gated.simFrame = t;
            for (int i = 0; i < callsPerTick; i++) sink ^= gated.GatedMemo();
        }
        sw.Stop();
        double gatedNs = sw.Elapsed.TotalMilliseconds * 1e6 / TickCount;

        sb.AppendLine(string.Format("{0,-32} | {1,8:F1} ns | {2,6:F1} ns | {3,8:F1} ns", 
            label, directNs, memoNs, gatedNs));
    }
}
