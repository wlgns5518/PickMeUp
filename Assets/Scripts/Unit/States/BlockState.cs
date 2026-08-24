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

        if (!context.HasUsableTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        context.StopMovement();
        context.SetBlocking(true);
    }

    public override void Update()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f) return;

        ReturnToCombat();
    }

    public override void Exit()
    {
        context.SetBlocking(false);
        base.Exit();
    }
}
