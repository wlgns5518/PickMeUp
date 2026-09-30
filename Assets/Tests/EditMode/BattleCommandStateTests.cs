using System.Collections.Generic;
using NUnit.Framework;

// 지휘관 명령과 위협 게시판을 전투 씬·정적 상태 없이 하나씩 떼어 본다.
// 예전에는 둘 다 정적 클래스라 한 시험이 남긴 명령이 다음 시험으로 넘어갔다.
public class BattleCommandStateTests
{
    [Test]
    public void 파티가_없으면_후퇴도_진형도_받지_않는다()
    {
        var commander = new PartyCommander(() => new List<UnitController>());
        int changes = 0;
        commander.Changed += () => changes++;

        Assert.IsFalse(commander.OrderRetreat(10f));
        Assert.IsFalse(commander.OrderHold());
        Assert.AreEqual(PartyOrder.Engage, commander.Order);
        Assert.AreEqual(0, changes);
    }

    [Test]
    public void 죽은_표적에는_집중하지_않고_초기화는_변경을_알린다()
    {
        var commander = new PartyCommander(() => new List<UnitController>());
        int changes = 0;
        commander.Changed += () => changes++;

        Assert.IsFalse(commander.FocusFire(TargetRef.None));
        Assert.IsFalse(commander.FocusTarget.Exists);

        commander.Reset();
        Assert.AreEqual(1, changes);
        Assert.AreEqual(PartyOrder.Engage, commander.Order);
    }

    [Test]
    public void 명령을_거두면_교전으로_돌아간다()
    {
        var commander = new PartyCommander(() => new List<UnitController>());

        commander.Resume();
        commander.Tick();

        Assert.AreEqual(PartyOrder.Engage, commander.Order);
    }

    [Test]
    public void 게시판은_새로_만들면_비어_있고_없는_적은_올라가지_않는다()
    {
        var board = new ThreatBoard();
        int seen = board.VersionOf(UnitTeam.Ally);

        board.Report(UnitTeam.Ally, TargetRef.None);

        Assert.AreEqual(0, seen);
        Assert.AreEqual(0, board.VersionOf(UnitTeam.Ally));
        Assert.IsFalse(board.TryConsume(UnitTeam.Ally, ref seen, out TargetRef shared));
        Assert.IsFalse(shared.Exists);
    }
}
