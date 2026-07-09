public class DeadState : UnitBattleState
{
    public DeadState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        context.StopMovement();
        context.DisableCollider();
        context.TriggerDead();
        UnitRegistry.Unregister(context);
        context.DisableUnitAfterDeath();
    }

    public override void Update()
    {
    }
}
