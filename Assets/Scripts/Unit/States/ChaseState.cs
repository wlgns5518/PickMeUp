using UnityEngine;

public class ChaseState : UnitBattleState
{
    private float destinationTimer;

    public ChaseState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        // 예측 위치로 달리면서 상대를 본다 — 그 둘이 어긋나므로 회전은 코드가 잡는다.
        context.SetCodeDrivenFacing(true);
        destinationTimer = context.DestinationUpdateInterval;

        if (!TryRefreshTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        if (TrySwitchToActionState()) return;
        UpdateDestination();
        context.FaceTarget();
    }

    public override void Update()
    {
        if (!TryRefreshTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        if (TrySwitchToActionState()) return;

        destinationTimer -= Time.deltaTime;
        if (destinationTimer <= 0f)
        {
            destinationTimer = context.DestinationUpdateInterval;
            UpdateDestination();
        }

        context.FaceTarget();
    }

    private bool TrySwitchToActionState()
    {
        // 쫓아가는 도중에도 위기면 방향을 바꾼다 — 사거리 안까지 들어갈 때까지 기다리지 않는다.
        if (context.ShouldEvade())
        {
            context.ChangeState(context.EvadeState);
            return true;
        }

        if (context.CanUseSkill())
        {
            context.ChangeState(context.SkillState);
            return true;
        }

        if (context.IsTargetInAttackRange())
        {
            context.ChangeState(context.AttackState);
            return true;
        }

        return false;
    }

    private void UpdateDestination()
    {
        // 멈춰 설 거리의 하한은 NavMesh 회피가 허용하는 최소 간격이다.
        // 고블린(사거리 1.2)은 이 하한이 없으면 1.02m를 목표로 달려드는데, 회피가 강제하는
        // 최소 간격이 1.0m라 도착하자마자 계속 밀려나며 그 자리에서 떨었다.
        float stoppingDistance = Mathf.Max(
            context.SeparationFromTarget(),
            Mathf.Max(context.Stats.moveStopDistance, context.Stats.attackRange * 0.85f));
        context.MoveTo(context.GetPredictedTargetPosition(), context.Stats.runSpeed, stoppingDistance);
        context.SetMoveAnimation(context.Stats.runSpeed, true, false);
    }
}
