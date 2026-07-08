using UnityEngine;

/// <summary>Chase(추격) / Flee(도주) 를 묶는 그룹</summary>
public class PlayerAIMoveGroupState : State<PlayerAIContext>
{
    private PlayerAIAliveState parent;

    private PlayerAIChaseState chaseState;
    private PlayerAIFleeState  fleeState;

    /// <summary>현재 도주 중인지 AliveState에서 확인용</summary>
    public bool IsFleeing => CurrentSubState == fleeState;

    public PlayerAIMoveGroupState(PlayerAIContext context, PlayerAIAliveState parent) : base(context)
    {
        this.parent = parent;

        chaseState = new PlayerAIChaseState(context, this);
        fleeState  = new PlayerAIFleeState(context, this);

        InitSubStateMachine(chaseState);
    }

    public override void Enter()
    {
        // 도주 조건이면 Flee, 아니면 Chase로 진입
        if (parent.ShouldFlee())
            ChangeSubState(fleeState);
        else
            ChangeSubState(chaseState);

        base.Enter();
    }

    public override void Update() => base.Update();
    public override void Exit()   => base.Exit();

    public void GoToChase()
    {
        if (CurrentSubState != chaseState)
            ChangeSubState(chaseState);
    }

    public void GoToFlee()
    {
        if (CurrentSubState != fleeState)
            ChangeSubState(fleeState);
    }

    public void FinishFlee()
    {
        parent.FinishFlee();
    }
}
