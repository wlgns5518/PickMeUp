public class IdleState : UnitBattleState
{
    public IdleState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        // 노릴 상대가 없다. 회전 주도권을 NavMeshAgent에게 돌려준다.
        context.SetCodeDrivenFacing(false);
        context.StopMovement();
        context.ClearTarget();

        if (UnitRegistry.HasLivingEnemy(context))
        {
            context.ChangeState(context.SearchState);
        }
    }

    public override void Update()
    {
        if (UnitRegistry.HasLivingEnemy(context))
        {
            context.ChangeState(context.SearchState);
        }
    }
}
