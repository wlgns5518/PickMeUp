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
        if (TrySwitchToBetterState()) return;

        context.FaceTarget();

        if (context.IsAttackAnimationLocked) return;

        TryAttack();
    }

    private bool TrySwitchToBetterState()
    {
        // 휘두르는 중에는 상태를 바꾸지 않는다.
        //
        // 예전에는 타깃이 쓰러진 그 프레임에 바로 Search로 빠졌다. 그런데 공격 모션은 아직
        // 재생 중이라, 칼을 반쯤 휘두르다 달리기로 튀었다(실제로 전투 중에 Move 상태인데
        // Attack2가 재생 중인 순간을 잡았다). 무기가 무거울수록 더 눈에 띈다 — 도끼는 1.67초짜리다.
        //
        // 피격은 TakeDamage가 InterruptCurrentAction으로 잠금을 풀고 들어오므로 여전히 끊긴다.
        // 사망과 패닉도 전역 전이라 여기와 무관하게 걸린다. 즉 "끊어야 하는 것"은 그대로 끊긴다.
        if (context.IsAttackAnimationLocked) return false;

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

        // 원거리 유닛의 근접 회피 + HP 위기 후퇴를 함께 본다.
        if (context.ShouldEvade())
        {
            context.ChangeState(context.EvadeState);
            return true;
        }

        // 잠금 검사는 위에서 이미 끝났다.
        if (context.CanUseSkill())
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
