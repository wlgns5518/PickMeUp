using UnityEngine;

public class EnemyPatrolState : EnemyStateBase
{
    private float waitTimer;
    private bool waiting;

    public override void OnEnter()
    {
        waitTimer = 0f;
        waiting = false;
        PickNewTarget();
        Controller.PlayAnim("Run");
    }

    public override void OnUpdate()
    {
        if (BB.IsHit) return;

        if (waiting)
        {
            waitTimer += Time.deltaTime;
            if (waitTimer >= Config.patrolWaitTime)
            {
                waiting = false;
                PickNewTarget();
                Controller.PlayAnim("Run");
            }
            return;
        }

        MoveToward(BB.PatrolTarget, Config.patrolSpeed);

        float dist = Vector3.Distance(Controller.transform.position, BB.PatrolTarget);
        if (dist < Config.patrolArriveThreshold)
        {
            waiting = true;
            waitTimer = 0f;
            StopMove();
            Controller.PlayAnim("Idle");
        }
    }

    public override string OnTransition()
    {
        if (BB.IsDead) return "hit";
        if (BB.IsHit) return "hit";
        if (BB.IsTargetInDetectRange()) return "detect";
        return null;
    }

    private void PickNewTarget()
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float radius = Config.patrolRadius * Random.Range(0.4f, 1f);
        BB.PatrolTarget = BB.SpawnPosition + new Vector3(
            Mathf.Cos(angle) * radius,
            0f,
            Mathf.Sin(angle) * radius);
    }
}