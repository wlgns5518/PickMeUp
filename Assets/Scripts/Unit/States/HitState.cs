using UnityEngine;

public class HitState : UnitBattleState
{
    private float stateTimer;

    public HitState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        stateTimer = context.Stats.hitAnimationDuration;
        context.InterruptCurrentAction();
        context.TriggerHit();
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
}
