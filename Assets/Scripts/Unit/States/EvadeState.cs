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
        // 여기서는 가는 쪽을 보고 달린다. 회전은 NavMeshAgent에게 돌려준다.
        context.SetCodeDrivenFacing(false);
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

        // 회피 모션이 있으면 그것으로 물러난다. 예전에는 늘 달리기 클립이라,
        // 적을 마주 본 채 뒷걸음질하는 동안 앞으로 달리는 다리가 나왔다.
        // 모션이 없는 리그(고블린)는 예전처럼 달리기로 물러난다.
        if (context.TriggerDodge())
        {
            // 구르는 동안은 최소 시간을 모션 길이에 맞춘다. 모션 중간에 상태가 바뀌면
            // 구르다 만 자세에서 공격 모션으로 튄다.
            stateTimer = Mathf.Max(MinEvadeDuration, context.DodgeAnimationDuration);
            return;
        }

        context.SetMoveAnimation(context.Stats.runSpeed, true, false);
    }

    public override void Update()
    {
        if (!context.HasUsableTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        stateTimer -= AnimationDeltaTime;
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

        ReturnToCombat();
    }
}
