using UnityEngine;

public class PlayerAIHeavyAttackState : State<PlayerAIContext>
{
    private PlayerAIAttackGroupState parent;
    private float minMotionEndTime;
    private string currentClip;

    private static readonly string[] HeavyAttackClips = { "HeavyAttack1", "HeavyAttack2", "HeavyAttack3", "HeavyAttack4" };

    public PlayerAIHeavyAttackState(PlayerAIContext context, PlayerAIAttackGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        minMotionEndTime = 0f;

        if (context.target == null) return;

        context.Face(context.target.Position);
        currentClip = HeavyAttackClips[Random.Range(0, HeavyAttackClips.Length)];
        minMotionEndTime = Time.time + context.config.heavyAttackMotionDuration;
        context.nextAttackTime = Time.time + context.AttackCooldown;
        context.Play(currentClip, true);
        context.onAttack?.Invoke(context.DirectionTo(context.target.Position), context.target.GameObject);
    }

    public override void Update()
    {
        if (Time.time < minMotionEndTime) return;
        if (!context.IsAnimationFinished(currentClip)) return;

        parent.GoToAttack();
    }

    public override void Exit() { }
}
