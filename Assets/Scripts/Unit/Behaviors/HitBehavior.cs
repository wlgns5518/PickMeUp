using UnityEngine;

// 한 대 맞아 잠깐 끊긴 리액션. 강인도가 깨졌을 때만 나온다.
//
// 들어오는 길이 조건이 아니라 요청이라는 점이 다른 동작과 다르다. 피해는 바깥에서
// (공격자의 애니메이션 이벤트에서) 들어오므로 트리가 스스로 알아챌 방법이 없다.
// 그래서 UnitController.RequestHitReaction이 표시를 세우고, 이 가지가 그것을 본다 —
// 예전에 TakeDamage가 ChangeState(HitState)로 직접 상태를 지목하던 자리다.
public class HitBehavior : UnitBehavior
{
    private float stateTimer;
    private float previousKnockbackProgress;

    public HitBehavior(UnitController context) : base(context)
    {
    }

    // 넉백은 이 동작이 직접 민다. 지역 회피가 겹치면 궤적이 휜다.
    public override bool HoldsGround => true;

    public override bool AcceptsCombatRedirect => false;

    protected override void OnEnter()
    {
        unit.ConsumeHitReaction();

        stateTimer = unit.HitAnimationDuration;
        previousKnockbackProgress = 0f;
        unit.InterruptCurrentAction();
        unit.TriggerHit();
    }

    protected override BTStatus OnTick()
    {
        UpdateKnockback();

        stateTimer -= AnimationDeltaTime;
        return stateTimer > 0f ? BTStatus.Running : BTStatus.Success;
    }

    private void UpdateKnockback()
    {
        if (unit.Stats.knockbackDistance <= 0f || unit.Stats.knockbackDuration <= 0f) return;

        float elapsed = unit.HitAnimationDuration - stateTimer;
        float knockbackProgress = Mathf.Clamp01(elapsed / unit.Stats.knockbackDuration);
        float deltaProgress = knockbackProgress - previousKnockbackProgress;
        if (deltaProgress <= 0f) return;

        previousKnockbackProgress = knockbackProgress;
        unit.ApplyKnockback(deltaProgress);
    }
}
