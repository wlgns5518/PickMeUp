using UnityEngine;

public class PlayerAIBlockHitState : State<PlayerAIContext>
{
    private PlayerAIBlockGroupState parent;

    public PlayerAIBlockHitState(PlayerAIContext context, PlayerAIBlockGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        context.Play("BlockHit", true);
    }

    public override void Update()
    {
        if (context.IsAnimationFinished("BlockHit"))
            parent.GoToBlock();
    }

    public override void Exit() { }
}
