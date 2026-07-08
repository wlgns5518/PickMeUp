using UnityEngine;

public class PlayerAICrouchHitState : State<PlayerAIContext>
{
    private PlayerAICrouchGroupState parent;

    public PlayerAICrouchHitState(PlayerAIContext context, PlayerAICrouchGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        context.Play("CrouchHit", true);
    }

    public override void Update()
    {
        if (context.IsAnimationFinished("CrouchHit"))
            parent.GoToCrouch();
    }

    public override void Exit() { }
}
