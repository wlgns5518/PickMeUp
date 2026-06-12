using UnityEngine;

public class PlayerAIIdleState : State<PlayerAIContext>
{
    private PlayerAIAliveState parent;
    private float timer;

    public PlayerAIIdleState(PlayerAIContext context, PlayerAIAliveState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        timer = 0f;
        context.StopMoving();
        context.Play("Idle");
    }

    public override void Update()
    {
        timer += Time.deltaTime;
        if (timer >= 1.5f)
            parent.GoToPatrol();
    }

    public override void Exit() { }
}
