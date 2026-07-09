using UnityEngine;
using UnityEngine.AI;

public class EvadeState : UnitBattleState
{
    private float stateTimer;

    public EvadeState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        stateTimer = 0.5f;

        if (!context.HasUsableTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        Vector3 away = context.transform.position - context.CurrentTarget.transform.position;
        away.y = 0f;
        if (away.sqrMagnitude <= 0.0001f) away = -context.transform.forward;

        Vector3 destination = context.transform.position + away.normalized * context.Stats.evadeRange;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(destination, out hit, context.Stats.evadeRange, NavMesh.AllAreas))
        {
            destination = hit.position;
        }

        context.MoveTo(destination, context.Stats.runSpeed);
        context.SetMoveAnimation(context.Stats.runSpeed, true, false);
    }

    public override void Update()
    {
        if (TrySwitchToDead()) return;

        if (!context.HasUsableTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f) return;

        context.ChangeState(context.IsTargetInAttackRange() ? context.AttackState : context.ChaseState);
    }
}
