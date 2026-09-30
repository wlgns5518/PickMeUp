// 팀이 공유하는 "마지막으로 발견된 적" 게시판.
//
// 예전에는 적을 발견한 유닛이 팀 전원을 순회하며 직접 알렸다(UnitRegistry.AlertTeam).
// 알림 한 번이 팀 인원 수만큼의 호출이라, 난전처럼 타깃이 자주 바뀌는 구간에서는
// 유닛 수의 제곱으로 커졌다. 게다가 그 비용이 발견한 그 프레임에 통째로 몰렸다.
//
// 쓰는 쪽은 값 하나만 갱신하고(O(1)), 읽는 쪽은 자기 스캔 주기에 한 번 확인한다(O(1)).
// 스캔 주기는 유닛마다 흩어져 있으므로(TargetScanner.ScatterSchedule) 비용도 자연히 흩어진다.
//
// 버전 번호를 두는 이유: "새 소식이 있는지"를 참조 비교 한 번으로 알기 위해서다.
// 각 유닛은 자기가 마지막으로 받아 간 번호만 기억하면 같은 소식을 두 번 받지 않는다.
//
// 소식은 손잡이(TargetRef)다. 게임오브젝트만 담던 동안 적이 엔티티가 되자 게시판이 영영 비어,
// 한 명이 발견한 고블린을 팀이 함께 알아채는 일이 없었다.
//
// 이 클래스는 입구다. 실제 값은 GameServices.Threats(ThreatBoard)가 들고 있고, 플레이를 새로
// 시작할 때마다 새로 만들어진다 — 도메인 리로드를 끈 에디터에서 지난 판의 파괴된 유닛이 남지 않는다.
public static class TeamThreatBoard
{
    public static void Report(UnitTeam team, TargetRef target) => GameServices.Threats.Report(team, target);

    public static bool TryConsume(UnitTeam team, ref int lastVersion, out TargetRef target) =>
        GameServices.Threats.TryConsume(team, ref lastVersion, out target);

    public static int VersionOf(UnitTeam team) => GameServices.Threats.VersionOf(team);
}
