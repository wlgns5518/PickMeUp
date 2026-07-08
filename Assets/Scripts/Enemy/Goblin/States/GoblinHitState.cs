using UnityEngine;

public class GoblinHitState : State<EnemyAIContext>
{
    private GoblinAliveState parent;
    private float hitEndTime;
    private bool hasHitAnimation;

    public GoblinHitState(EnemyAIContext context, GoblinAliveState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        hitEndTime = Time.time + context.config.hitDuration;
        hasHitAnimation = context.Play("Hit", true);
    }

    public override void Update()
    {
        if (!hasHitAnimation || (Time.time >= hitEndTime && context.IsAnimationFinished("Hit")))
            parent.FinishHit();
    }

    public override void Exit() { }
}
