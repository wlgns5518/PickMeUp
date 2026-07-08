using UnityEngine;

public class PlayerAIChaseState : State<PlayerAIContext>
{
    private PlayerAIMoveGroupState parent;

    public PlayerAIChaseState(PlayerAIContext context, PlayerAIMoveGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.Play("Run");
    }

    public override void Update()
    {
        if (context.target == null) return;
        context.MoveTo(context.target.Position, context.RunSpeed);
    }

    public override void Exit() { }
}
