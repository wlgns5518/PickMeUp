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
        // 스킬도 영창이다. 마법사는 이 구간에 받는 피해가 2.2배가 된다(castVulnerabilityMultiplier) —
        // 원작에서 "마력을 모으는 동안 완전히 무방비"라는 설정이 여기 걸려 있다.
        // 시전 취약성이 없는 직군은 배율이 1이라 아무것도 달라지지 않는다.
        context.BeginCast();
    }

    public override void Update()
    {
        stateTimer -= AnimationDeltaTime;
        if (stateTimer > 0f) return;

        ReturnToCombat();
    }

    public override void Exit()
    {
        context.EndCast();
        base.Exit();
    }
}
