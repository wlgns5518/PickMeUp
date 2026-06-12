using UnityEngine;

public class PlayerAICrouchAttackState : State<PlayerAIContext>
{
    private PlayerAICrouchGroupState parent;

    public PlayerAICrouchAttackState(PlayerAIContext context, PlayerAICrouchGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();

        if (context.target != null)
            context.Face(context.target.Position);

        context.Play("CrouchAttack", true);
        context.onAttack?.Invoke(
            context.target != null ? context.DirectionTo(context.target.Position) : context.transform.forward,
            context.target?.GameObject
        );
    }

    public override void Update()
    {
        if (!context.IsAnimationFinished("CrouchAttack")) return;
        parent.GoToCrouch();
    }

    public override void Exit() { }
}
