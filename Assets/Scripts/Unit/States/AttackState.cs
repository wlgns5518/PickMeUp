using UnityEngine;

public class AttackState : UnitBattleState
{
    public AttackState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        context.StopMovement();

        if (TrySwitchToBetterState()) return;
        context.FaceTarget();
        TryAttack();
    }

    public override void Update()
    {
        if (TrySwitchToDead()) return;

        if (TrySwitchToBetterState()) return;

        context.FaceTarget();

        if (context.IsAttackAnimationLocked) return;

        TryAttack();
    }

    private bool TrySwitchToBetterState()
    {
        if (!context.HasUsableTarget())
        {
            context.ChangeState(context.SearchState);
            return true;
        }

        if (!context.IsTargetInAttackRange())
        {
            context.ChangeState(context.ChaseState);
            return true;
        }

        if (!context.IsAttackAnimationLocked && context.CanUseSkill())
        {
            context.ChangeState(context.SkillState);
            return true;
        }

        if (context.CanBlock())
        {
            context.ChangeState(context.BlockState);
            return true;
        }

        return false;
    }

    private void TryAttack()
    {
        if (context.CanAttack())
        {
            context.TriggerAttack();
        }
    }
}
