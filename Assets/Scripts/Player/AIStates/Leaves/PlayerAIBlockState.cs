using UnityEngine;

public class PlayerAIBlockState : State<PlayerAIContext>
{
    private PlayerAIBlockGroupState parent;
    private float blockEndTime;
    private bool isBlocking;

    public PlayerAIBlockState(PlayerAIContext context, PlayerAIBlockGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        isBlocking = false;
        blockEndTime = Time.time + context.config.blockDuration;
        context.Play("Block", true);
    }

    public override void Update()
    {
        if (!isBlocking && context.IsAnimationFinished("Block"))
        {
            isBlocking = true;
            context.Play("BlockIdle");
        }

        if (Time.time >= blockEndTime)
            parent.GoToBlock(); // 시간 초과 시 Block 재진입 → AliveState가 다른 그룹으로 전환
    }

    public override void Exit() { }
}
