using UnityEngine;

public class PlayerAICrouchBlockState : State<PlayerAIContext>
{
    private PlayerAICrouchGroupState parent;
    private float blockEndTime;
    private bool isBlocking;

    public PlayerAICrouchBlockState(PlayerAIContext context, PlayerAICrouchGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        isBlocking = false;
        blockEndTime = Time.time + context.config.blockDuration;
        context.Play("CrouchBlock", true);
    }

    public override void Update()
    {
        if (!isBlocking && context.IsAnimationFinished("CrouchBlock"))
        {
            isBlocking = true;
            context.Play("CrouchBlockIdle");
        }

        if (Time.time >= blockEndTime)
            parent.GoToCrouch();
    }

    public override void Exit() { }
}
