using UnityEngine;

public class HitState : UnitBattleState
{
    private float stateTimer;
    private float previousKnockbackProgress;

    public HitState(UnitController context) : base(context)
    {
    }

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

        stateTimer -= Time.deltaTime;
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
