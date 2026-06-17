using UnityEngine;

public class PlayerAIJumpState : State<PlayerAIContext>
{
    private PlayerAIIdleGroupState parent;
    private string currentClip;

    private static readonly string[] JumpClips = { "Jump", "Jump2" };

    public PlayerAIJumpState(PlayerAIContext context, PlayerAIIdleGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        currentClip = JumpClips[Random.Range(0, JumpClips.Length)];
        context.Play(currentClip, true);
    }

    public override void Update()
    {
        if (context.IsAnimationFinished(currentClip))
            parent.GoToIdle();
    }

    public override void Exit() { }
}
