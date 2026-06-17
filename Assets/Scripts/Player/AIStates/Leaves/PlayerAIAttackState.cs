using UnityEngine;

public class PlayerAIAttackState : State<PlayerAIContext>
{
    private PlayerAIAttackGroupState parent;
    private float minMotionEndTime;
    private string currentClip;

    private static readonly string[] LightAttackClips = { "LightAttack", "LightAttack2" };

    public PlayerAIAttackState(PlayerAIContext context, PlayerAIAttackGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        minMotionEndTime = 0f;

        if (context.target == null) return;

        context.Face(context.target.Position);
        currentClip = LightAttackClips[Random.Range(0, LightAttackClips.Length)];
        minMotionEndTime = Time.time + context.config.attackMotionDuration;
        context.nextAttackTime = Time.time + context.AttackCooldown;
        context.Play(currentClip, true);
        context.onAttack?.Invoke(context.DirectionTo(context.target.Position), context.target.GameObject);
    }

    public override void Update()
    {
        if (Time.time < minMotionEndTime) return;
        if (!context.IsAnimationFinished(currentClip)) return;

        if (context.target == null) return;

        if (Time.time < context.nextAttackTime)
        {
            context.Face(context.target.Position);
            return;
        }

        // 재공격
        context.Face(context.target.Position);
        currentClip = LightAttackClips[Random.Range(0, LightAttackClips.Length)];
        minMotionEndTime = Time.time + context.config.attackMotionDuration;
        context.nextAttackTime = Time.time + context.AttackCooldown;
        context.Play(currentClip, true);
        context.onAttack?.Invoke(context.DirectionTo(context.target.Position), context.target.GameObject);
    }

    public override void Exit() { }
}
