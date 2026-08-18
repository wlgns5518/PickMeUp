using UnityEngine;
using UnityEngine.AI;

public class EvadeState : UnitBattleState
{
    private float stateTimer;

    public EvadeState(UnitController context) : base(context)
    {
    }

    // 최소로 등을 보이는 시간. 이보다 일찍 목적지에 도착해도 이 시간까지는 다시 덤비지 않는다 —
    // 한 발짝 물러나자마자 바로 돌아서서 때리면 회피가 아니라 그냥 제자리 걸음으로 보인다.
    private const float MinEvadeDuration = 0.35f;

    public override void Enter()
    {
        base.Enter();
        stateTimer = MinEvadeDuration;

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

        // 목적지에 닿기 전에 최소 시간만 지난 거면, 아직 등을 보이는 중이니 계속 물러난다.
        if (context.HasMoveDestination) return;

        // 여전히 위험하면(적이 다시 붙었거나 HP가 그대로) 등을 돌리는 대신 한 번 더 물러난다.
        // 그대로 Attack으로 꺾으면 방금 벌린 거리를 스스로 반납하는 꼴이 된다.
        if (context.ShouldEvade())
        {
            Enter();
            return;
        }

        context.ChangeState(context.IsTargetInAttackRange() ? context.AttackState : context.ChaseState);
    }
}
