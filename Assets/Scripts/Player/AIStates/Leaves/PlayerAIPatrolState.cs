using UnityEngine;
using UnityEngine.AI;

public class PlayerAIPatrolState : State<PlayerAIContext>
{
    private PlayerAIIdleGroupState parent;

    public PlayerAIPatrolState(PlayerAIContext context, PlayerAIIdleGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        PickPatrolTarget();
        context.Play("Walk");
    }

    public override void Update()
    {
        context.MoveTo(context.patrolTarget, context.MoveSpeed * 0.6f);
        if (context.Reached(context.patrolTarget, 0.4f))
            PickPatrolTarget();
    }

    public override void Exit() { }

    private void PickPatrolTarget()
    {
        Vector2 circle = Random.insideUnitCircle * context.config.patrolRadius;
        Vector3 candidate = context.spawnPosition + new Vector3(circle.x, 0f, circle.y);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, context.agent.areaMask))
            context.patrolTarget = hit.position;
        else
            context.patrolTarget = candidate;
    }
}
