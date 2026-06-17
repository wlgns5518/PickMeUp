using UnityEngine;

public class PlayerAIDeadState : State<PlayerAIContext>
{
    private static readonly string[] DeathClips = { "Death", "Death2" };

    public PlayerAIDeadState(PlayerAIContext context) : base(context) { }

    public override void Enter()
    {
        context.StopMoving();
        context.Play(DeathClips[Random.Range(0, DeathClips.Length)], true);
    }

    public override void Update() { }
    public override void Exit()   { }
}
