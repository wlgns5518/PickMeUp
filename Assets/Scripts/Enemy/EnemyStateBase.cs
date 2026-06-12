using UnityEngine;
using UnityEngine.AI;

public abstract class EnemyStateBase
{
    protected EnemyBlackboard BB;
    protected EnemyController Controller;
    protected EnemyConfig Config;
    protected NavMeshAgent Agent;

    public virtual void Init(EnemyBlackboard bb)
    {
        BB = bb;
        Controller = bb.Controller;
        Config = bb.Config;
        Agent = Controller.GetComponent<NavMeshAgent>();
    }

    public virtual void OnEnter() { }
    public virtual void OnExit() { }
    public virtual void OnUpdate() { }

    public virtual string OnTransition() => null;

    protected void MoveToward(Vector3 target, float speed)
    {
        Vector3 dir = target - Controller.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) dir.Normalize();
        MoveDirect(dir, speed);
    }

    protected void MoveDirect(Vector3 dir, float speed)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        if (Agent != null && Agent.isOnNavMesh)
        {
            Agent.speed = speed;
            Agent.isStopped = dir.sqrMagnitude <= 0.0001f;
            if (!Agent.isStopped)
            {
                Vector3 dest = Controller.transform.position + dir.normalized * Mathf.Max(0.2f, speed * 0.35f);
                if (NavMesh.SamplePosition(dest, out NavMeshHit hit, 1.5f, Agent.areaMask))
                    Agent.SetDestination(hit.position);
                else
                    Agent.SetDestination(dest);

                Controller.transform.forward = Vector3.Slerp(
                    Controller.transform.forward, dir.normalized, Time.deltaTime * 12f);
            }
        }
    }

    protected void StopMove()
    {
        if (Agent != null && Agent.isOnNavMesh)
        {
            Agent.isStopped = true;
            Agent.ResetPath();
        }
    }

    protected static Vector3 Rotate(Vector3 v, float rad)
    {
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector3(v.x * cos - v.z * sin, 0f, v.x * sin + v.z * cos);
    }
}