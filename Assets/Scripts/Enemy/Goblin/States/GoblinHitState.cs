using UnityEngine;

public class GoblinHitState : State<EnemyAIContext>
{
    private GoblinAliveState parent;
    private float hitEndTime;

    public GoblinHitState(EnemyAIContext context, GoblinAliveState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        hitEndTime = Time.time + context.config.hitDuration;
        context.Play("Hit", true);
    }

    public override void Update()
    {
        if (Time.time >= hitEndTime && context.IsAnimationFinished("Hit"))
            parent.GoToIdle();
    }

    public override void Exit() { }
}
