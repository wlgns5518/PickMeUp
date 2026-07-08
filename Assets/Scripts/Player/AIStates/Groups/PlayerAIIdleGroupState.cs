using UnityEngine;

/// <summary>Idle/Idle2~4 랜덤 → Patrol → Jump → Roar 를 묶는 그룹</summary>
public class PlayerAIIdleGroupState : State<PlayerAIContext>
{
    private PlayerAIAliveState parent;

    private PlayerAIIdleState   idleState;
    private PlayerAIPatrolState patrolState;
    private PlayerAIJumpState   jumpState;
    private PlayerAIRoarState   roarState;

    public PlayerAIIdleGroupState(PlayerAIContext context, PlayerAIAliveState parent) : base(context)
    {
        this.parent = parent;

        idleState   = new PlayerAIIdleState(context, this);
        patrolState = new PlayerAIPatrolState(context, this);
        jumpState   = new PlayerAIJumpState(context, this);
        roarState   = new PlayerAIRoarState(context, this);

        InitSubStateMachine(idleState);
    }

    public override void Enter()  => base.Enter();
    public override void Update() => base.Update();
    public override void Exit()   => base.Exit();

    public void GoToIdle()   => ChangeSubState(idleState);
    public void GoToPatrol() => ChangeSubState(patrolState);
    public void GoToJump()   => ChangeSubState(jumpState);
    public void GoToRoar()   => ChangeSubState(roarState);
}
