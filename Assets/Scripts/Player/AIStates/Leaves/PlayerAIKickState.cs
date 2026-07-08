using UnityEngine;

public class PlayerAIKickState : State<PlayerAIContext>
{
    private PlayerAIAttackGroupState parent;
    private float hitTime;
    private bool hitApplied;
    private bool hasAttackAnimation;

    public PlayerAIKickState(PlayerAIContext context, PlayerAIAttackGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();

        if (context.target != null)
            context.Face(context.target.Position);

        hasAttackAnimation = context.Play("Kick", true);
        hitTime = Time.time + context.config.attackHitDelay;
        hitApplied = false;
    }

    public override void Update()
    {
        if (!hasAttackAnimation)
        {
            parent.GoToAttack();
            return;
        }

        TryApplyHit();
        if (!context.IsAnimationFinished("Kick")) return;
        context.nextAttackTime = Time.time + Mathf.Max(context.config.postAttackDelay, 1.5f);
        parent.GoToAttack();
    }

    public override void Exit() { }

    private void TryApplyHit()
    {
        if (!hasAttackAnimation || hitApplied || Time.time < hitTime) return;
        hitApplied = true;
        context.onAttack?.Invoke(
            context.target != null ? context.DirectionTo(context.target.Position) : context.transform.forward,
            context.target?.GameObject
        );
    }
}
