using UnityEngine;

public class PlayerAIHeavyAttackState : State<PlayerAIContext>
{
    private PlayerAIAttackGroupState parent;
    private float minMotionEndTime;
    private float hitTime;
    private string currentClip;
    private bool hitApplied;
    private bool hasAttackAnimation;

    private static readonly string[] HeavyAttackClips = { "HeavyAttack", "HeavyAttack2", "HeavyAttack3", "HeavyAttack4" };

    public PlayerAIHeavyAttackState(PlayerAIContext context, PlayerAIAttackGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        minMotionEndTime = 0f;
        hasAttackAnimation = false;

        if (context.target == null) return;

        context.Face(context.target.Position);
        currentClip = HeavyAttackClips[Random.Range(0, HeavyAttackClips.Length)];
        minMotionEndTime = Time.time + context.config.heavyAttackMotionDuration;
        hitTime = Time.time + Mathf.Min(context.config.attackHitDelay, context.config.heavyAttackMotionDuration);
        hitApplied = false;
        context.nextAttackTime = minMotionEndTime + Mathf.Max(context.config.postAttackDelay, 1.5f);
        hasAttackAnimation = context.Play(currentClip, true);
    }

    public override void Update()
    {
        if (!hasAttackAnimation)
        {
            parent.GoToAttack();
            return;
        }

        TryApplyHit();
        if (Time.time < minMotionEndTime) return;
        if (!context.IsAnimationFinished(currentClip)) return;

        parent.GoToAttack();
    }

    public override void Exit() { }

    private void TryApplyHit()
    {
        if (!hasAttackAnimation || hitApplied || Time.time < hitTime || context.target == null) return;
        hitApplied = true;
        context.onAttack?.Invoke(context.DirectionTo(context.target.Position), context.target.GameObject);
    }
}
