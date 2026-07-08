using UnityEngine;

public class PlayerAIAttackState : State<PlayerAIContext>
{
    private PlayerAIAttackGroupState parent;
    private float minMotionEndTime;
    private string currentClip;
    private bool isAttacking;

    private static readonly string[] LightAttackClips = { "LightAttack", "LightAttack2" };
    private const float AttackDuration = 1f;

    public PlayerAIAttackState(PlayerAIContext context, PlayerAIAttackGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        minMotionEndTime = 0f;
        currentClip = null;
        isAttacking = false;

        if (context.target == null) return;

        context.Face(context.target.Position);
        if (Time.time >= context.nextAttackTime)
            FireAttack();
    }

    public override void Update()
    {
        if (isAttacking)
        {
            if (Time.time < minMotionEndTime) return;
            if (!context.IsAnimationFinished(currentClip)) return;
            currentClip = null;
            isAttacking = false;
        }

        if (context.target == null) return;

        if (Time.time < context.nextAttackTime)
        {
            context.Face(context.target.Position);
            return;
        }

        context.Face(context.target.Position);
        FireAttack();
    }

    public override void Exit() { }

    private void FireAttack()
    {
        currentClip = LightAttackClips[Random.Range(0, LightAttackClips.Length)];
        minMotionEndTime = Time.time + AttackDuration;
        context.nextAttackTime = minMotionEndTime + Mathf.Max(context.config.postAttackDelay, 1.5f);
        isAttacking = context.Play(currentClip, true);
        if (isAttacking)
        {
            context.onAttack?.Invoke(context.DirectionTo(context.target.Position), context.target.GameObject);
        }
        else
        {
            currentClip = null;
        }
    }
}
