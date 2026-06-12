using UnityEngine;

/// <summary>Crouch / CrouchAttack / CrouchBlock / CrouchHit 를 묶는 그룹</summary>
public class PlayerAICrouchGroupState : State<PlayerAIContext>
{
    private PlayerAIAliveState parent;

    private PlayerAICrouchState       crouchState;
    private PlayerAICrouchAttackState crouchAttackState;
    private PlayerAICrouchBlockState  crouchBlockState;
    private PlayerAICrouchHitState    crouchHitState;

    /// <summary>모션 중이면 외부 전환 차단</summary>
    public bool IsLocked => CurrentSubState == crouchAttackState
                         || CurrentSubState == crouchHitState;

    public PlayerAICrouchGroupState(PlayerAIContext context, PlayerAIAliveState parent) : base(context)
    {
        this.parent = parent;

        crouchState       = new PlayerAICrouchState(context, this);
        crouchAttackState = new PlayerAICrouchAttackState(context, this);
        crouchBlockState  = new PlayerAICrouchBlockState(context, this);
        crouchHitState    = new PlayerAICrouchHitState(context, this);

        InitSubStateMachine(crouchState);
    }

    public override void Enter() => base.Enter();
    public override void Update() => base.Update();
    public override void Exit()   => base.Exit();

    public void GoToCrouch()       => ChangeSubState(crouchState);
    public void GoToCrouchAttack() => ChangeSubState(crouchAttackState);
    public void GoToCrouchBlock()  => ChangeSubState(crouchBlockState);
    public void GoToCrouchHit()    => ChangeSubState(crouchHitState);
}
