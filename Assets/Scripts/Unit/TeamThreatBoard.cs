using UnityEngine;

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
public static class TeamThreatBoard
{
    private struct Entry
    {
        public UnitController Target;
        public int Version;
    }

    // UnitTeam은 Ally/Enemy/Neutral 셋뿐이고 값이 0,1,2라 배열 첨자로 그대로 쓴다.
    private static readonly Entry[] entries = new Entry[3];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 이전 플레이의 파괴된 유닛이 남지 않도록 비운다.
        for (int i = 0; i < entries.Length; i++) entries[i] = default;
    }

    // 적을 발견했다고 알린다. 같은 적을 다시 알리는 것은 소식이 아니므로 버전을 올리지 않는다.
    public static void Report(UnitTeam team, UnitController target)
    {
        if (target == null || target.IsDead) return;

        int index = IndexOf(team);
        if (entries[index].Target == target) return;

        entries[index].Target = target;
        entries[index].Version++;
    }

    // 아직 받아 가지 않은 소식이 있으면 꺼내 간다.
    // lastVersion은 부르는 쪽(유닛)이 들고 있는 값으로, 여기서 갱신해 준다.
    public static bool TryConsume(UnitTeam team, ref int lastVersion, out UnitController target)
    {
        target = null;

        int index = IndexOf(team);
        Entry entry = entries[index];
        if (entry.Version == lastVersion) return false;

        // 소식을 확인한 것 자체는 기록한다. 대상이 이미 죽었더라도 다음 프레임에 또 묻지 않도록.
        lastVersion = entry.Version;

        UnitController candidate = entry.Target;
        if (candidate == null || candidate.IsDead || !candidate.isActiveAndEnabled) return false;

        target = candidate;
        return true;
    }

    // 게시판에 올라온 소식의 현재 번호. 새로 스폰된 유닛이 "이미 지난 소식"부터
    // 훑지 않도록 시작값을 맞추는 데 쓴다.
    public static int VersionOf(UnitTeam team) => entries[IndexOf(team)].Version;

    private static int IndexOf(UnitTeam team)
    {
        int index = (int)team;
        return index >= 0 && index < entries.Length ? index : 0;
    }
}
