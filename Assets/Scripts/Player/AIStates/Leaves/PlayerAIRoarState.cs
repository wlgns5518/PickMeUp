using UnityEngine;

public class PlayerAIRoarState : State<PlayerAIContext>
{
    private PlayerAIIdleGroupState parent;

    public PlayerAIRoarState(PlayerAIContext context, PlayerAIIdleGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        context.Play("Roar", true);
    }

    public override void Update()
    {
        if (context.IsAnimationFinished("Roar"))
            parent.GoToIdle();
    }

    public override void Exit() { }
}
