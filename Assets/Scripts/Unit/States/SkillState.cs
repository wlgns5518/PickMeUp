using UnityEngine;

public class SkillState : UnitBattleState
{
    private float stateTimer;
    private float duration;

    public SkillState(UnitController context) : base(context)
    {
    }

    public override bool HoldsGround => true;

    // 스킬 모션이 향한 쪽은 이미 정해졌다.
    public override bool LocksTarget => true;

    public override bool AcceptsCombatRedirect => false;

    public override void Enter()
    {
        base.Enter();
        duration = Mathf.Max(0.05f, context.SkillAnimationDuration);
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
        // 물고 늘어지는 스킬(고블린)만 여기서 상대에게 달라붙는다. 그 밖의 직군은
        // ClingsWhileUsingSkill이 거짓이라 BeginCling이 아무 일도 하지 않는다.
        context.BeginCling();
    }

    public override void Update()
    {
        stateTimer -= AnimationDeltaTime;

        // 매달려 있는 동안은 상대의 목을 따라간다. 상태가 끝나기 전에 먼저 갱신해야
        // 마지막 프레임에 한 번 떨어졌다 붙는 것처럼 보이지 않는다.
        context.UpdateCling(1f - Mathf.Clamp01(stateTimer / duration));

        if (stateTimer > 0f) return;

        ReturnToCombat();
    }

    public override void Exit()
    {
        context.EndCast();
        // 도중에 끊겨도(피격·사망·패닉) 반드시 NavMesh 위로 되돌려 놓는다.
        context.EndCling();
        base.Exit();
    }
}
