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

        // 원거리 유닛은 적이 품 안까지 들어오면 쏘던 자리를 버리고 물러선다.
        if (context.ShouldKeepDistance())
        {
            context.ChangeState(context.EvadeState);
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
