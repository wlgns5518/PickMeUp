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
        stateTimer = context.SkillAnimationDuration;

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
        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f) return;

        ReturnToCombat();
    }
}
