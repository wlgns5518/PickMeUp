using UnityEngine;

public class AttackState : UnitBattleState
{
    private float decisionTimer;

    public AttackState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        decisionTimer = 0f;
        context.StopMovement();
    }

    public override void Update()
    {
        if (TrySwitchToDead()) return;

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

        if (context.CanUseSkill())
        {
            context.ChangeState(context.SkillState);
            return;
        }

        if (context.CanBlock())
        {
            context.ChangeState(context.BlockState);
            return;
        }

        context.FaceTarget();

        if (context.IsAttackAnimationLocked) return;

        decisionTimer -= Time.deltaTime;
        if (decisionTimer > 0f) return;

        if (context.CanAttack())
        {
            context.TriggerAttack();
            decisionTimer = context.Stats.attackCooldown;
        }
    }
}
