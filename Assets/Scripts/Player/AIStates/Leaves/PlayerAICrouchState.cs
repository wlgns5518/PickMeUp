using UnityEngine;

public class PlayerAICrouchState : State<PlayerAIContext>
{
    private PlayerAICrouchGroupState parent;
    private float crouchEndTime;
    private bool isCrouching;

    public PlayerAICrouchState(PlayerAIContext context, PlayerAICrouchGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        isCrouching = false;
        crouchEndTime = Time.time + 2f;
        context.Play("Crouching", true);
    }

    public override void Update()
    {
        if (!isCrouching && context.IsAnimationFinished("Crouching"))
        {
            isCrouching = true;
            context.Play("CrouchIdle");
        }

        if (Time.time >= crouchEndTime)
            parent.GoToCrouch(); // 시간 초과 → AliveState가 다른 그룹으로 전환
    }

    public override void Exit() { }
}
