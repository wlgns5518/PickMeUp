using UnityEngine;

public class PlayerAIAttackState : State<PlayerAIContext>
{
    private PlayerAIAliveState parent;
    private float minMotionEndTime;

    public PlayerAIAttackState(PlayerAIContext context, PlayerAIAliveState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        minMotionEndTime = 0f;

        if (context.target == null) return;

        context.Face(context.target.Position);
        minMotionEndTime = Time.time + context.config.attackMotionDuration;
        context.nextAttackTime = Time.time + context.AttackCooldown;
        context.Play("LightAttack", true);
        context.onAttack?.Invoke(context.DirectionTo(context.target.Position), context.target.GameObject);
    }

    public override void Update()
    {
        // 최소 모션 시간 대기
        if (Time.time < minMotionEndTime) return;

        // 애니메이션 완료 대기
        if (!context.IsAnimationFinished("LightAttack")) return;

        // 전환 판단
        if (context.target == null)
        {
            parent.GoToIdle();
            return;
        }

        // 쿨타임 중이면 타겟 바라보며 대기
        if (Time.time < context.nextAttackTime)
        {
            context.Face(context.target.Position);
            return;
        }

        // 재공격
        context.Face(context.target.Position);
        minMotionEndTime = Time.time + context.config.attackMotionDuration;
        context.nextAttackTime = Time.time + context.AttackCooldown;
        context.Play("LightAttack", true);
        context.onAttack?.Invoke(context.DirectionTo(context.target.Position), context.target.GameObject);
    }

    public override void Exit() { }
}