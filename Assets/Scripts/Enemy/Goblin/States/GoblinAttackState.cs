using UnityEngine;

public class GoblinAttackState : State<EnemyAIContext>
{
    private GoblinAliveState       parent;
    private float minMotionEndTime;

    public bool IsMotionPlaying => Time.time < minMotionEndTime
                                || !context.IsAnimationFinished("Attack");

    public GoblinAttackState(EnemyAIContext context, GoblinAliveState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        minMotionEndTime = 0f;
        if (context.target == null) return;
        FireAttack();
    }

    public override void Update()
    {
        if (Time.time < minMotionEndTime) return;
        if (!context.IsAnimationFinished("Attack")) return;
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
        context.nextAttackTime = Time.time + context.config.attackCooldown;
        context.Play("Attack", true);
        context.onAttack?.Invoke(context.target);
    }
}
