using UnityEngine;

public class HitState : UnitBattleState
{
    private float stateTimer;
    private float previousKnockbackProgress;

    public HitState(UnitController context) : base(context)
    {
    }

    // 넉백은 이 상태가 직접 민다. 지역 회피가 겹치면 궤적이 휜다.
    public override bool HoldsGround => true;

    public override bool AcceptsCombatRedirect => false;

    public override void Enter()
    {
        base.Enter();
        stateTimer = context.HitAnimationDuration;
        previousKnockbackProgress = 0f;
        context.InterruptCurrentAction();
        context.TriggerHit();
    }

    public override void Update()
    {
        UpdateKnockback();

        stateTimer -= AnimationDeltaTime;
        if (stateTimer > 0f) return;

        ReturnToCombat();
    }

    private void UpdateKnockback()
    {
        if (context.Stats.knockbackDistance <= 0f || context.Stats.knockbackDuration <= 0f) return;

        float elapsed = context.HitAnimationDuration - stateTimer;
        float knockbackProgress = Mathf.Clamp01(elapsed / context.Stats.knockbackDuration);
        float deltaProgress = knockbackProgress - previousKnockbackProgress;
        if (deltaProgress <= 0f) return;

        previousKnockbackProgress = knockbackProgress;
        context.ApplyKnockback(deltaProgress);
    }
}
