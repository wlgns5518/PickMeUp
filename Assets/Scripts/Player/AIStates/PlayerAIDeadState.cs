public class PlayerAIDeadState : State<PlayerAIContext>
{
    public PlayerAIDeadState(PlayerAIContext context) : base(context) { }

    public override void Enter()
    {
        context.StopMoving();
        context.Play("Death");
    }

    public override void Update() { }
    public override void Exit()   { }
}
