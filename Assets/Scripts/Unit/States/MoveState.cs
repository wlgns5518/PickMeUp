using UnityEngine;

public class MoveState : UnitBattleState
{
    public MoveState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        // 여기서는 가는 쪽을 보고 달린다. 회전은 NavMeshAgent에게 돌려준다.
        context.SetCodeDrivenFacing(false);
        if (TrySwitchToIdleWhenNoEnemy()) return;
        if (TrySwitchToDetectedTarget()) return;
        UpdateMovement();
    }

    public override void Update()
    {
        if (TrySwitchToIdleWhenNoEnemy()) return;

        if (TrySwitchToDetectedTarget()) return;

        if (!context.HasMoveDestination || context.HasReachedDestination(context.MoveDestination))
        {
            context.ClearMoveDestination();
            context.ChangeState(context.SearchState);
            return;
        }

        UpdateMovement();
    }

    private void UpdateMovement()
    {
        Vector3 toDestination = context.MoveDestination - context.transform.position;
        toDestination.y = 0f;
        float sqrDistance = toDestination.sqrMagnitude;

        bool shouldJump = context.Agent != null &&
                          context.Agent.enabled &&
                          context.Agent.isOnNavMesh &&
                          context.Agent.isOnOffMeshLink;
        bool shouldRun = !context.IsRoamingMoveDestination && sqrDistance > context.RunDistance * context.RunDistance;
        float speed = shouldRun ? context.Stats.runSpeed : context.Stats.walkSpeed;

        context.MoveTo(context.MoveDestination, speed);

        // 재생 배속은 요청 속도가 아니라 실제로 나아가는 속도에서 나온다.
        // 예전에는 여기만 요청 속도(고정값)를 넘겨서, 무리에 막히거나 가속 중일 때
        // 다리만 전속력으로 돌았고 Chase로 넘어가는 순간 재생 속도가 눈에 띄게 달라졌다.
        if (shouldJump) context.SetMoveAnimation(speed, shouldRun, true);
        else context.SetMoveAnimationFromGroundSpeed(shouldRun);
    }

    private bool TrySwitchToDetectedTarget()
    {
        UnitController target = context.Scanner != null ? context.Scanner.Target : null;
        if (target == null) return false;
        if (!context.TrySetTarget(target)) return false;

        context.ChangeState(context.ChaseState);
        return true;
    }
}
