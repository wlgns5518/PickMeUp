public class SearchState : UnitBattleState
{
    public SearchState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        context.StopMovement();
        if (!context.HasUsableTarget())
        {
            context.ClearTarget();
        }
    }

    public override void Update()
    {
        if (TrySwitchToDead()) return;
        if (TrySwitchToIdleWhenNoEnemy()) return;

        UnitController target = context.HasUsableTarget() ? context.CurrentTarget : null;
        if (target == null && context.Scanner != null)
        {
            target = context.Scanner.Target;
        }

        if (target == null && context.Scanner != null)
        {
            target = context.Scanner.FindTargetNow();
        }

        if (target != null)
        {
            if (!context.TrySetTarget(target)) return;

            if (context.CanEvade())
            {
                context.ChangeState(context.EvadeState);
                return;
            }

            if (context.CanBlock())
            {
                context.ChangeState(context.BlockState);
                return;
            }

            context.ChangeState(context.ChaseState);
            return;
        }

        if (context.HasMoveDestination)
        {
            context.ChangeState(context.MoveState);
            return;
        }

        if (context.TrySetRoamDestination())
        {
            context.ChangeState(context.MoveState);
        }
    }
}
