using UnityEngine;

public class SkillState : UnitBattleState
{
    private float stateTimer;

    public SkillState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        stateTimer = context.Stats.skillAnimationDuration;

        if (!context.HasUsableTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        if (!context.IsTargetInAttackRange())
        {
            context.ChangeState(context.ChaseState);
            return;
        }

        context.StopMovement();
        context.TriggerSkill();
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
