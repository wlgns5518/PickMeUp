public class IdleState : UnitBattleState
{
    public IdleState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        context.StopMovement();
        context.ClearTarget();

        if (UnitRegistry.HasLivingEnemy(context))
        {
            context.ChangeState(context.SearchState);
        }
    }

    public override void Update()
    {
        if (TrySwitchToDead()) return;

        if (UnitRegistry.HasLivingEnemy(context))
        {
            context.ChangeState(context.SearchState);
        }
    }
}
