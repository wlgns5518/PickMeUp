using UnityEngine;

/// <summary>LightAttack / HeavyAttack / Kick / Hit 를 묶는 그룹</summary>
public class PlayerAIAttackGroupState : State<PlayerAIContext>
{
    private PlayerAIAliveState parent;

    private PlayerAIAttackState      attackState;
    private PlayerAIHeavyAttackState heavyAttackState;
    private PlayerAIKickState        kickState;
    private PlayerAIHitState         hitState;

    /// <summary>모션 중이면 AliveState에서 외부 전환 차단</summary>
    public bool IsLocked => CurrentSubState == heavyAttackState
                         || CurrentSubState == kickState
                         || CurrentSubState == hitState;

    public PlayerAIAttackGroupState(PlayerAIContext context, PlayerAIAliveState parent) : base(context)
    {
        this.parent = parent;

        attackState      = new PlayerAIAttackState(context, this);
        heavyAttackState = new PlayerAIHeavyAttackState(context, this);
        kickState        = new PlayerAIKickState(context, this);
        hitState         = new PlayerAIHitState(context, this);

        InitSubStateMachine(attackState);
    }

    public override void Enter() => base.Enter();
    public override void Update() => base.Update();
    public override void Exit()   => base.Exit();

    public void GoToAttack()      => ChangeSubState(attackState);
    public void GoToHeavyAttack() => ChangeSubState(heavyAttackState);
    public void GoToKick()        => ChangeSubState(kickState);
    public void GoToHit()         => ChangeSubState(hitState);
}
