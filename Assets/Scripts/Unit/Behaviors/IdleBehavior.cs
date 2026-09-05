// 할 일이 없을 때 서 있는다. 트리 교전 갈래의 마지막 후보다.
//
// 여기로 오는 경우는 둘이다: 살아 있는 적이 없거나, 적은 있는데 갈 곳조차 잡지 못했거나
// (배회 목적지를 NavMesh에서 찾지 못한 경우). 어느 쪽이든 이 동작은 끝나지 않는다 —
// 적이 나타나면 위쪽 갈래가 그 순간 끊고 들어간다.
public class IdleBehavior : UnitBehavior
{
    public IdleBehavior(UnitController context) : base(context)
    {
    }

    protected override void OnEnter()
    {
        // 노릴 상대가 없다. 회전 주도권을 NavMeshAgent에게 돌려준다.
        unit.SetCodeDrivenFacing(false);
        unit.StopMovement();
        unit.ClearTarget();
    }

    protected override BTStatus OnTick() => BTStatus.Running;
}
