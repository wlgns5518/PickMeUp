using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 타깃 공유가 "팀 전원에게 밀어넣기"에서 "게시판에 쓰고 각자 읽어가기"로 바뀌면서
// 소식을 두 번 반영하지 않는다는 규칙이 버전 번호 하나에 얹혔다. 그 규칙을 고정해 둔다.
public class TeamThreatBoardTests
{
    private readonly List<GameObject> spawned = new List<GameObject>();

    private UnitController NewUnit(string name)
    {
        var go = new GameObject(name);
        spawned.Add(go);
        // UnitController는 TargetScanner를 요구한다. 먼저 붙여 두면 Awake가 그것을 찾아 쓴다.
        go.AddComponent<TargetScanner>();
        return go.AddComponent<UnitController>();
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
        }
        spawned.Clear();
    }

    [Test]
    public void 올린_소식을_한_번_받아_간다()
    {
        UnitController threat = NewUnit("Threat");
        int version = TeamThreatBoard.VersionOf(UnitTeam.Ally);

        TeamThreatBoard.Report(UnitTeam.Ally, threat);

        Assert.IsTrue(TeamThreatBoard.TryConsume(UnitTeam.Ally, ref version, out UnitController received));
        Assert.AreSame(threat, received);
    }

    [Test]
    public void 같은_소식을_두_번_받지_않는다()
    {
        UnitController threat = NewUnit("Threat");
        int version = TeamThreatBoard.VersionOf(UnitTeam.Ally);

        TeamThreatBoard.Report(UnitTeam.Ally, threat);
        TeamThreatBoard.TryConsume(UnitTeam.Ally, ref version, out _);

        Assert.IsFalse(TeamThreatBoard.TryConsume(UnitTeam.Ally, ref version, out _),
            "이미 받아 간 소식은 다시 나오지 않는다");
    }

    [Test]
    public void 같은_적을_다시_올려도_새_소식이_되지_않는다()
    {
        UnitController threat = NewUnit("Threat");
        int version = TeamThreatBoard.VersionOf(UnitTeam.Ally);

        TeamThreatBoard.Report(UnitTeam.Ally, threat);
        TeamThreatBoard.TryConsume(UnitTeam.Ally, ref version, out _);

        // 여러 유닛이 같은 적을 계속 발견해도 팀 전체가 매번 반응하지는 않아야 한다.
        TeamThreatBoard.Report(UnitTeam.Ally, threat);
        TeamThreatBoard.Report(UnitTeam.Ally, threat);

        Assert.IsFalse(TeamThreatBoard.TryConsume(UnitTeam.Ally, ref version, out _));
    }

    [Test]
    public void 다른_적이_올라오면_다시_받아_간다()
    {
        UnitController first = NewUnit("First");
        UnitController second = NewUnit("Second");
        int version = TeamThreatBoard.VersionOf(UnitTeam.Ally);

        TeamThreatBoard.Report(UnitTeam.Ally, first);
        TeamThreatBoard.TryConsume(UnitTeam.Ally, ref version, out _);

        TeamThreatBoard.Report(UnitTeam.Ally, second);

        Assert.IsTrue(TeamThreatBoard.TryConsume(UnitTeam.Ally, ref version, out UnitController received));
        Assert.AreSame(second, received);
    }

    [Test]
    public void 팀끼리는_게시판을_공유하지_않는다()
    {
        UnitController threat = NewUnit("Threat");
        int enemyVersion = TeamThreatBoard.VersionOf(UnitTeam.Enemy);

        TeamThreatBoard.Report(UnitTeam.Ally, threat);

        Assert.IsFalse(TeamThreatBoard.TryConsume(UnitTeam.Enemy, ref enemyVersion, out _));
    }

    [Test]
    public void 사라진_대상은_소식으로_나오지_않지만_번호는_넘어간다()
    {
        UnitController threat = NewUnit("Threat");
        int version = TeamThreatBoard.VersionOf(UnitTeam.Ally);

        TeamThreatBoard.Report(UnitTeam.Ally, threat);
        Object.DestroyImmediate(threat.gameObject);

        Assert.IsFalse(TeamThreatBoard.TryConsume(UnitTeam.Ally, ref version, out _));
        // 번호가 넘어가지 않으면 죽은 대상을 매 스캔마다 다시 묻게 된다.
        Assert.AreEqual(TeamThreatBoard.VersionOf(UnitTeam.Ally), version);
    }
}
