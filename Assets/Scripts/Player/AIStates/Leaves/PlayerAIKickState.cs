using UnityEngine;

public class PlayerAIKickState : State<PlayerAIContext>
{
    private PlayerAIAttackGroupState parent;

    public PlayerAIKickState(PlayerAIContext context, PlayerAIAttackGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();

        if (context.target != null)
            context.Face(context.target.Position);

        context.Play("Kick", true);
        context.onAttack?.Invoke(
            context.target != null ? context.DirectionTo(context.target.Position) : context.transform.forward,
            context.target?.GameObject
        );
    }

    public override void Update()
    {
        if (!context.IsAnimationFinished("Kick")) return;
        parent.GoToAttack();
    }

    public override void Exit() { }
}
