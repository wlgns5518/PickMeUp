public abstract class UnitBattleState : State<UnitController>
{
    protected UnitBattleState(UnitController context) : base(context)
    {
    }

    protected bool TrySwitchToDead()
    {
        if (!context.IsDead) return false;
        context.ChangeState(context.DeadState);
        return true;
    }

    protected bool TrySwitchToIdleWhenNoEnemy()
    {
        if (UnitRegistry.HasLivingEnemy(context)) return false;
        context.ChangeState(context.IdleState);
        return true;
    }

    protected bool TryRefreshTarget()
    {
        if (context.HasUsableTarget()) return true;

        UnitController target = context.Scanner != null ? context.Scanner.FindTargetNow() : null;
        return context.TrySetTarget(target);
    }
}
