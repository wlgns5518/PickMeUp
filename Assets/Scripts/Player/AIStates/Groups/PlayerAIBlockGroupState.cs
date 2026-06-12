using UnityEngine;

/// <summary>Block → BlockIdle 루프 / BlockHit 를 묶는 그룹</summary>
public class PlayerAIBlockGroupState : State<PlayerAIContext>
{
    private PlayerAIAliveState parent;

    private PlayerAIBlockState    blockState;
    private PlayerAIBlockHitState blockHitState;

    /// <summary>BlockHit 모션 중이면 외부 전환 차단</summary>
    public bool IsLocked => CurrentSubState == blockHitState;

    public PlayerAIBlockGroupState(PlayerAIContext context, PlayerAIAliveState parent) : base(context)
    {
        this.parent = parent;

        blockState    = new PlayerAIBlockState(context, this);
        blockHitState = new PlayerAIBlockHitState(context, this);

        InitSubStateMachine(blockState);
    }

    public override void Enter() => base.Enter();
    public override void Update() => base.Update();
    public override void Exit()   => base.Exit();

    public void GoToBlock()    => ChangeSubState(blockState);
    public void GoToBlockHit() => ChangeSubState(blockHitState);
}
