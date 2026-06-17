using UnityEngine;

public class GoblinChaseState : State<EnemyAIContext>
{
    private GoblinAliveState parent;

    public GoblinChaseState(EnemyAIContext context, GoblinAliveState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.Play("Run", speed: 1.5f);
    }

    public override void Update()
    {
        if (context.target == null) return;
        context.MoveTo(context.target.position, context.config.runSpeed);
    }

    public override void Exit()
    {
        context.ResetAnimSpeed();
    }
}
