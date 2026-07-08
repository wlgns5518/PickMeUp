using UnityEngine;

public class GoblinIdleState : State<EnemyAIContext>
{
    private GoblinAliveState parent;

    public GoblinIdleState(EnemyAIContext context, GoblinAliveState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        context.Play("Idle");
    }

    public override void Update() { }
    public override void Exit()   { }
}
