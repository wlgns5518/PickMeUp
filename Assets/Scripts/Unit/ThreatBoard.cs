// 팀이 공유하는 "마지막으로 발견된 적" 게시판(TeamThreatBoard 주석 참조)의 실제 상태.
//
// 게시판은 전투 하나가 쓰는 값이라, 플레이를 새로 시작할 때마다 GameServices가 새로 만든다.
// 쓰는 쪽은 예전 이름의 입구(TeamThreatBoard)를 그대로 부르거나, 생성자로 IThreatBoard를 받는다.
public interface IThreatBoard
{
    void Report(UnitTeam team, TargetRef target);
    bool TryConsume(UnitTeam team, ref int lastVersion, out TargetRef target);
    int VersionOf(UnitTeam team);
}

public sealed class ThreatBoard : IThreatBoard
{
    private struct Entry
    {
        public TargetRef Target;
        public int Version;
    }

    // UnitTeam은 Ally/Enemy/Neutral 셋뿐이고 값이 0,1,2라 배열 첨자로 그대로 쓴다.
    private readonly Entry[] entries = new Entry[3];

    // 적을 발견했다고 알린다. 같은 적을 다시 알리는 것은 소식이 아니므로 버전을 올리지 않는다.
    public void Report(UnitTeam team, TargetRef target)
    {
        if (!target.Exists || !target.IsAlive) return;

        int index = IndexOf(team);
        if (entries[index].Target == target) return;

        entries[index].Target = target;
        entries[index].Version++;
    }

    // 아직 받아 가지 않은 소식이 있으면 꺼내 간다.
    // lastVersion은 부르는 쪽(유닛)이 들고 있는 값으로, 여기서 갱신해 준다.
    public bool TryConsume(UnitTeam team, ref int lastVersion, out TargetRef target)
    {
        target = TargetRef.None;

        Entry entry = entries[IndexOf(team)];
        if (entry.Version == lastVersion) return false;

        // 소식을 확인한 것 자체는 기록한다. 대상이 이미 죽었더라도 다음 프레임에 또 묻지 않도록.
        lastVersion = entry.Version;

        TargetRef candidate = entry.Target;
        if (!candidate.Exists || !candidate.IsAlive) return false;

        target = candidate;
        return true;
    }

    // 게시판에 올라온 소식의 현재 번호. 새로 스폰된 유닛이 "이미 지난 소식"부터
    // 훑지 않도록 시작값을 맞추는 데 쓴다.
    public int VersionOf(UnitTeam team) => entries[IndexOf(team)].Version;

    private int IndexOf(UnitTeam team)
    {
        int index = (int)team;
        return index >= 0 && index < entries.Length ? index : 0;
    }
}
