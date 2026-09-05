// 다음에 갈 곳을 한 번 정한다. 정했으면 Success, 갈 곳이 없으면 Failure.
//
// 예전 SearchState에서 "표적을 찾아 교전으로 넘긴다"는 부분은 여기 없다. 표적이 있는지는
// 트리의 교전 갈래가 이 갈래보다 먼저 묻기 때문에, 여기까지 내려왔다는 것은 이미
// 겨눌 상대가 없다는 뜻이다. 남은 일은 "어디로 갈 것인가" 하나뿐이라 조건이 셋으로 줄었다.
//
// Running을 돌려주지 않는 한 번짜리 노드다 — 목적지를 정하는 데 시간이 걸리지 않는다.
// 실제로 걸어가는 것은 뒤에 붙은 MoveBehavior의 몫이고, 둘을 이어 붙인 시퀀스가
// 예전의 Search ↔ Move 왕복을 대신한다.
public class SearchBehavior : UnitBehavior
{
    public SearchBehavior(UnitController context) : base(context)
    {
    }

    protected override BTStatus OnTick()
    {
        // 여기서는 가는 쪽을 보고 달린다. 회전은 NavMeshAgent에게 돌려준다.
        unit.SetCodeDrivenFacing(false);
        unit.StopMovement();

        if (!unit.HasUsableTarget()) unit.ClearTarget();

        if (TryJoinFight()) return BTStatus.Success;
        if (unit.HasMoveDestination) return BTStatus.Success;
        if (unit.TrySetRoamDestination()) return BTStatus.Success;

        return BTStatus.Failure;
    }

    // 시야에 다음 적이 없으면 싸움이 벌어지고 있는 자리로 걸어간다.
    //
    // 예전에는 곧바로 배회(TrySetRoamDestination)로 떨어졌다. 적을 잡은 유닛은 다음 적이
    // 탐지 범위(8~14m) 밖인 경우가 흔해서, 아군이 아직 싸우고 있는데 혼자 제자리를 맴돌았다.
    //
    // 목적지에 닿기 전에 적이 시야에 들어오면 교전 갈래가 이 갈래보다 위에 있으므로 그대로
    // 끊고 들어간다. 닿은 뒤에도 못 찾으면 시퀀스가 다시 처음으로 돌아와 그때의 전선을
    // 다시 잡는다 — 목적지를 한 번 찍고 마는 것이라 상대가 움직여도 따라붙는 비용이 들지 않는다.
    private bool TryJoinFight()
    {
        UnitController rally = UnitRegistry.FindRallyEnemy(unit);
        if (rally == null) return false;

        unit.SetMoveDestination(rally.transform.position);
        return true;
    }
}
