using UnityEngine;
using UnityEngine.AI;

public class GoblinPatrolState : State<EnemyAIContext>
{
    private GoblinAliveState parent;
    private float waitTimer;
    private bool  isWaiting;

    public GoblinPatrolState(EnemyAIContext context, GoblinAliveState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        isWaiting = false;
        waitTimer = 0f;
        context.Play("Run", speed: 1f);
        PickPatrolTarget();
    }

    public override void Update()
    {
        if (isWaiting)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f)
            {
                isWaiting = false;
                PickPatrolTarget();
                context.Play("Run", speed: 1f);
            }
            return;
        }

        context.MoveTo(context.patrolTarget, context.config.patrolSpeed);

        if (context.Reached(context.patrolTarget, context.config.patrolArriveThreshold))
        {
            isWaiting = true;
            waitTimer = context.config.patrolWaitTime;
            context.StopMoving();
            context.Play("Idle");
        }
    }

    public override void Exit() { }

    private void PickPatrolTarget()
    {
        Vector2 circle    = Random.insideUnitCircle * context.config.patrolRadius;
        Vector3 candidate = context.spawnPosition + new Vector3(circle.x, 0f, circle.y);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, context.agent.areaMask))
            context.patrolTarget = hit.position;
        else
            context.patrolTarget = candidate;
    }
}
