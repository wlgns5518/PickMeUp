using UnityEngine;

public class GoblinAttackState : State<EnemyAIContext>
{
    private GoblinAliveState       parent;
    private float minMotionEndTime;
    private float hitTime;
    private bool isAttacking;
    private bool hitApplied;
    private bool hasAttackAnimation;

    public bool IsMotionPlaying => isAttacking &&
        (Time.time < minMotionEndTime || !context.IsAnimationFinished("Attack"));

    public GoblinAttackState(EnemyAIContext context, GoblinAliveState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        minMotionEndTime = 0f;
        isAttacking = false;
        hasAttackAnimation = false;
        if (context.target == null) return;

        context.Face(context.target.position);
        if (context.CanAttack)
            FireAttack();
    }

    public override void Update()
    {
        if (isAttacking)
        {
            TryApplyHit();
            if (Time.time < minMotionEndTime) return;
            if (!context.IsAnimationFinished("Attack")) return;
            isAttacking = false;
        }

        if (context.target == null) return;

        if (!context.CanAttack)
        {
            context.Face(context.target.position);
            return;
        }

        FireAttack();
    }

    public override void Exit() { }

    private void FireAttack()
    {
        context.Face(context.target.position);
        minMotionEndTime       = Time.time + context.config.attackMotionDuration;
        hitTime                = Time.time + Mathf.Min(context.config.attackHitDelay, context.config.attackMotionDuration);
        context.nextAttackTime = minMotionEndTime + Mathf.Max(context.config.attackCooldown, 1.5f);
        hitApplied = false;
        hasAttackAnimation = context.Play("Attack", true);
        isAttacking = hasAttackAnimation;
    }

    private void TryApplyHit()
    {
        if (!hasAttackAnimation || hitApplied || Time.time < hitTime || context.target == null) return;
        hitApplied = true;
        context.onAttack?.Invoke(context.target);
    }
}
