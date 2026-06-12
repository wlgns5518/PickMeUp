public class PlayerAIChaseState : State<PlayerAIContext>
{
    private PlayerAIAliveState parent;

    public PlayerAIChaseState(PlayerAIContext context, PlayerAIAliveState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.Play("Run");
    }

    public override void Update()
    {
        if (context.target == null)
        {
            parent.GoToIdle();
            return;
        }

        context.MoveTo(context.target.Position, context.RunSpeed);    // ← config → context
    }

    public override void Exit() { }
}