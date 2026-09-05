using UnityEngine;

// 잡아 둔 목적지까지 걸어간다. 닿으면 Success.
//
// 예전 MoveState가 매 프레임 하던 세 가지 검사(적이 전멸했는가, 스캐너가 표적을 잡았는가,
// 목적지가 사라졌는가) 중 앞의 둘은 트리의 위쪽 갈래로 올라갔다. 여기 남은 것은
// "도착했는가"뿐이다.
public class MoveBehavior : UnitBehavior
{
    public MoveBehavior(UnitController context) : base(context)
    {
    }

    protected override void OnEnter()
    {
        // 여기서는 가는 쪽을 보고 달린다. 회전은 NavMeshAgent에게 돌려준다.
        unit.SetCodeDrivenFacing(false);
    }

    protected override BTStatus OnTick()
    {
        if (!unit.HasMoveDestination || unit.HasReachedDestination(unit.MoveDestination))
        {
            unit.ClearMoveDestination();
            return BTStatus.Success;
        }

        UpdateMovement();
        return BTStatus.Running;
    }

    private void UpdateMovement()
    {
        Vector3 toDestination = unit.MoveDestination - unit.transform.position;
        toDestination.y = 0f;
        float sqrDistance = toDestination.sqrMagnitude;

        bool shouldJump = unit.Agent != null &&
                          unit.Agent.enabled &&
                          unit.Agent.isOnNavMesh &&
                          unit.Agent.isOnOffMeshLink;
        bool shouldRun = !unit.IsRoamingMoveDestination && sqrDistance > unit.RunDistance * unit.RunDistance;
        float speed = shouldRun ? unit.Stats.runSpeed : unit.Stats.walkSpeed;

        unit.MoveTo(unit.MoveDestination, speed);

        // 재생 배속은 요청 속도가 아니라 실제로 나아가는 속도에서 나온다.
        // 예전에는 여기만 요청 속도(고정값)를 넘겨서, 무리에 막히거나 가속 중일 때
        // 다리만 전속력으로 돌았고 추격으로 넘어가는 순간 재생 속도가 눈에 띄게 달라졌다.
        if (shouldJump) unit.SetMoveAnimation(speed, shouldRun, true);
        else unit.SetMoveAnimationFromGroundSpeed(shouldRun);
    }
}
