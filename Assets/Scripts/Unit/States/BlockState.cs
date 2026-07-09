using UnityEngine;

public class BlockState : UnitBattleState
{
    private float stateTimer;

    public BlockState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        stateTimer = context.Stats.blockDuration;
        context.StopMovement();
        context.SetBlocking(true);
    }

    public override void Update()
    {
        if (TrySwitchToDead()) return;

        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f) return;

        if (!context.HasUsableTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        context.ChangeState(context.IsTargetInAttackRange() ? context.AttackState : context.ChaseState);
    }

    public override void Exit()
    {
        context.SetBlocking(false);
        base.Exit();
    }
}
