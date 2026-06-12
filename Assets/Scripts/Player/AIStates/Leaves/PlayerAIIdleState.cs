using UnityEngine;

public class PlayerAIIdleState : State<PlayerAIContext>
{
    private PlayerAIIdleGroupState parent;
    private float timer;

    private static readonly string[] IdleClips = { "Idle1", "Idle2", "Idle3", "Idle4" };

    public PlayerAIIdleState(PlayerAIContext context, PlayerAIIdleGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        timer = 0f;
        context.StopMoving();
        context.Play(IdleClips[Random.Range(0, IdleClips.Length)]);
    }

    public override void Update()
    {
        timer += Time.deltaTime;
        if (timer >= 1.5f)
            parent.GoToPatrol();
    }

    public override void Exit() { }
}
