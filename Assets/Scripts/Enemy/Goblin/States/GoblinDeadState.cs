using UnityEngine;

public class GoblinDeadState : State<EnemyAIContext>
{
    public GoblinDeadState(EnemyAIContext context) : base(context) { }

    public override void Enter()
    {
        context.StopMoving();
        context.Play("Death", true);
    }

    public override void Update() { }
    public override void Exit()   { }
}
