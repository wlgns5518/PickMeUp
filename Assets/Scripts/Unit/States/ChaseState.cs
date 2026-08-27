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
        float standoff = Mathf.Max(
            context.SeparationFromTarget(),
            Mathf.Max(context.Stats.moveStopDistance, context.Stats.attackRange * 0.85f));

        // 어느 쪽에서 붙을지가 직군마다 다르다.
        //
        // 예전에는 전원이 적의 현재 위치로 곧장 달려들었다. 그래서 탱커든 검사든 암살자든
        // 늘 같은 자리 — 적의 정면 — 에서 뒤엉켰고, 배후 피해 배율(backstabDamageMultiplier)이나
        // 방어 각도 같은 위치 관련 규칙이 사실상 쓰이지 않았다.
        //
        // 이제 접근 자체가 진형이다. 탱커는 정면으로 곧장 들어가 어그로를 붙들고, 검사는
        // 측면을 물고, 암살자는 등 뒤로 돌아간다. 방위 성향이 없는 유닛(생산직, 고블린)은
        // GetEngageDestination이 예측 위치를 그대로 돌려주므로 예전 동작 그대로다.
        bool flanking = context.HasEngagePreference;
        Vector3 destination = flanking
            ? context.GetEngageDestination(standoff)
            : context.GetPredictedTargetPosition();

        // 파고드는 자리로 갈 때는 그 지점까지 실제로 걸어가야 한다. 여기에 standoff를 다시
        // 걸면 목표에서 한 번 더 물러난 자리에 서게 되어 영영 사거리에 닿지 못한다.
        float stoppingDistance = flanking ? context.Stats.moveStopDistance : standoff;

        context.MoveTo(destination, context.Stats.runSpeed, stoppingDistance);
        context.SetMoveAnimation(context.Stats.runSpeed, true, false);
    }
}
