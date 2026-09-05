using UnityEngine;

// 직군 스킬 한 번. 쓸 수 있는지(CanUseSkill)와 지금이 쓸 때인지(콤보 레커버리)는 전부
// 트리의 가지가 판단하므로, 여기는 모션을 끝까지 재생하는 일만 한다.
public class SkillBehavior : UnitBehavior
{
    private float stateTimer;
    private float duration;

    public SkillBehavior(UnitController context) : base(context)
    {
    }

    public override bool HoldsGround => true;

    // 스킬 모션이 향한 쪽은 이미 정해졌다.
    public override bool LocksTarget => true;

    public override bool AcceptsCombatRedirect => false;

    // 한 번 나간 스킬은 끝까지 간다. 시작하는 순간 쿨다운과 마나를 이미 썼으므로
    // 도중에 끊기면 그것만 버린다(예전 SkillState.Update에 다른 전이 검사가 없던 이유).
    public override bool AllowsReprioritize => false;

    protected override void OnEnter()
    {
        duration = Mathf.Max(0.05f, unit.SkillAnimationDuration);
        stateTimer = unit.SkillAnimationDuration;

        unit.StopMovement();
        unit.TriggerSkill();
        // 스킬도 영창이다. 마법사는 이 구간에 받는 피해가 2.2배가 된다(castVulnerabilityMultiplier) —
        // 원작에서 "마력을 모으는 동안 완전히 무방비"라는 설정이 여기 걸려 있다.
        // 시전 취약성이 없는 직군은 배율이 1이라 아무것도 달라지지 않는다.
        unit.BeginCast();
        // 물고 늘어지는 스킬(고블린)만 여기서 상대에게 달라붙는다. 그 밖의 직군은
        // ClingsWhileUsingSkill이 거짓이라 BeginCling이 아무 일도 하지 않는다.
        unit.BeginCling();
    }

    protected override BTStatus OnTick()
    {
        stateTimer -= AnimationDeltaTime;

        // 매달려 있는 동안은 상대의 목을 따라간다. 동작이 끝나기 전에 먼저 갱신해야
        // 마지막 프레임에 한 번 떨어졌다 붙는 것처럼 보이지 않는다.
        unit.UpdateCling(1f - Mathf.Clamp01(stateTimer / duration));

        return stateTimer > 0f ? BTStatus.Running : BTStatus.Success;
    }

    protected override void OnExit()
    {
        unit.EndCast();
        // 도중에 끊겨도(피격·사망·패닉) 반드시 NavMesh 위로 되돌려 놓는다.
        unit.EndCling();
    }
}
