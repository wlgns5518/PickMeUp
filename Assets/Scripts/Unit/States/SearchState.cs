public class SearchState : UnitBattleState
{
    public SearchState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        // 여기서는 가는 쪽을 보고 달린다. 회전은 NavMeshAgent에게 돌려준다.
        context.SetCodeDrivenFacing(false);
        context.StopMovement();
        if (TrySwitchToIdleWhenNoEnemy()) return;

        if (!context.HasUsableTarget())
        {
            context.ClearTarget();
        }

        if (TrySwitchToTarget()) return;
        TrySwitchToMove();
    }

    public override void Update()
    {
        if (TrySwitchToIdleWhenNoEnemy()) return;

        if (TrySwitchToTarget()) return;
        TrySwitchToMove();
    }

    private bool TrySwitchToTarget()
    {
        UnitController target = context.HasUsableTarget() ? context.CurrentTarget : null;
        if (target == null && context.Scanner != null)
        {
            target = context.Scanner.Target;
        }

        if (target != null)
        {
            if (!context.TrySetTarget(target)) return false;

            if (context.CanEvade())
            {
                context.ChangeState(context.EvadeState);
                return true;
            }

            if (context.CanBlock())
            {
                context.ChangeState(context.BlockState);
                return true;
            }

            context.ChangeState(context.ChaseState);
            return true;
        }

        return false;
    }

    private bool TrySwitchToMove()
    {
        if (context.HasMoveDestination)
        {
            context.ChangeState(context.MoveState);
            return true;
        }

        if (context.TrySetRoamDestination())
        {
            context.ChangeState(context.MoveState);
            return true;
        }

        return false;
    }
}
