using UnityEngine;

public class MoveState : UnitBattleState
{
    public MoveState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        UpdateMovement();
    }

    public override void Update()
    {
        if (TrySwitchToDead()) return;
        if (TrySwitchToIdleWhenNoEnemy()) return;

        UnitController target = context.Scanner != null ? context.Scanner.Target : null;
        if (target != null)
        {
            if (!context.TrySetTarget(target)) return;
            context.ChangeState(context.ChaseState);
            return;
        }

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
        bool shouldRun = sqrDistance > context.RunDistance * context.RunDistance;
        float speed = shouldRun ? context.Stats.runSpeed : context.Stats.walkSpeed;

        context.MoveTo(context.MoveDestination, speed);
        context.SetMoveAnimation(speed, shouldRun, shouldJump);
    }
}
