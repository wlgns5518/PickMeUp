using UnityEngine;

// 겨눈 상대에게 붙는다. 교전 갈래의 마지막 후보라 조건이 없다 —
// 위의 어느 것도 성립하지 않으면 일단 붙는 것이 답이다.
//
// 예전 ChaseState가 매 프레임 돌리던 전이 검사(후퇴/스킬/도약/사거리)는 전부 트리의
// 형제 가지로 올라갔다. 여기 남은 것은 "어디로 어떻게 붙을 것인가"뿐이다.
public class ChaseBehavior : UnitBehavior
{
    // 앞이 막혔다고 볼 속도(m/s). 회피에 밀려 떠는 유닛은 이 아래에서 오래 머문다.
    private const float StalledSpeed = 0.35f;
    // 이만큼 계속 못 나아가야 "막혔다"고 본다. 출발 가속(8m/s^2)이 이 안에 끝나므로
    // 방금 달리기 시작한 유닛이 잘못 걸리지 않는다.
    private const float StalledGrace = 0.5f;
    // 막혔을 때 한 번 물러나 기다리는 시간. 이 동안은 목적지를 다시 잡지 않는다.
    private const float WaitForRoomDuration = 0.6f;

    private float destinationTimer;
    private float stalledTimer;
    private float waitTimer;

    public ChaseBehavior(UnitController context) : base(context)
    {
    }

    // 쫓아가다 앞이 막혀 멈춰 선 것도 제자리다(아래 TickStall 주석 참조).
    // 이때까지 회피를 켜 두면, 정작 더 갈 수도 없는 유닛이 앞줄에 계속 떠밀리며 떤다.
    // 동작 중에 유일하게 조건부로 답하는 자리다 — 쫓는 중에도 서 있을 때가 있다.
    public override bool HoldsGround =>
        unit.Agent != null && unit.Agent.enabled && unit.Agent.isOnNavMesh && unit.Agent.isStopped;

    protected override void OnEnter()
    {
        // 예측 위치로 달리면서 상대를 본다 — 그 둘이 어긋나므로 회전은 코드가 잡는다.
        unit.SetCodeDrivenFacing(true);
        // 0으로 두어 이번 틱에 곧바로 길을 잡게 한다. 남겨 두면 첫 간격만큼 목적지 없이 서 있다.
        destinationTimer = 0f;
        stalledTimer = 0f;
        waitTimer = 0f;
    }

    protected override BTStatus OnTick()
    {
        unit.FaceTarget();

        // 자리가 나기를 기다리는 중. 이 동안은 목적지를 잡지 않는다 —
        // 여기서 다시 SetDestination을 걸면 곧바로 앞줄을 다시 밀기 시작한다.
        if (waitTimer > 0f)
        {
            waitTimer -= Time.deltaTime;
            return BTStatus.Running;
        }

        destinationTimer -= Time.deltaTime;
        if (destinationTimer <= 0f)
        {
            destinationTimer = unit.DestinationUpdateInterval;
            UpdateDestination();
        }

        // 이번 프레임에 멈춰 섰으면 방금 잡은 전투 대기 자세를 달리기로 덮지 않는다.
        if (TickStall()) return BTStatus.Running;

        unit.SetMoveAnimationFromGroundSpeed(true);
        return BTStatus.Running;
    }

    // 앞이 막혀 더 갈 수 없는데도 계속 밀어붙이면, 지역 회피가 매 프레임 되밀어 그 자리에서 떤다.
    //
    // 무리로 몰려가는 쪽에서 늘 생긴다. 목적지는 상대의 발밑인데 그 둘레는 이미 앞줄이
    // 차지하고 있으므로, 뒷줄은 영영 닿지 못하는 지점을 향해 계속 가속한다. NavMesh는
    // "도착할 수 없다"고 말해 주지 않는다 — 경로는 멀쩡히 있고, 다른 에이전트가 막고 있을 뿐이다.
    //
    // 그래서 못 나아가는 것이 확인되면 스스로 멈춰 선다. 멈춘 유닛은 HoldsGround가
    // 참이 되어 회피까지 꺼지므로(TickAvoidance) 떠밀리지도 않는다. 잠시 뒤 다시 밀어 보고,
    // 그 사이에 앞줄이 쓰러지거나 비켜서 자리가 나면 그대로 들어간다.
    //
    // 돌려주는 값은 "이번 프레임에 멈춰 섰는가".
    private bool TickStall()
    {
        if (unit.CurrentMoveSpeed >= StalledSpeed)
        {
            stalledTimer = 0f;
            return false;
        }

        stalledTimer += Time.deltaTime;
        if (stalledTimer < StalledGrace) return false;

        stalledTimer = 0f;
        waitTimer = WaitForRoomDuration;
        // 기다림이 끝나면 곧바로 다시 길을 잡게 해 둔다. 남은 간격을 그대로 두면
        // 그만큼 아무 목적지도 없이 서 있는 시간이 생긴다.
        destinationTimer = 0f;

        unit.StopMovement();
        // StopMovement는 평소 Idle(칼을 내리고 긴장을 푼 자세)로 떨어진다.
        // 눈앞이 교전인데 그 자세로 서 있으면 싸울 마음이 없어 보인다.
        unit.PlayCombatIdle();
        return true;
    }

    private void UpdateDestination()
    {
        // 멈춰 설 거리의 하한은 NavMesh 회피가 허용하는 최소 간격이다.
        // 고블린(사거리 1.2)은 이 하한이 없으면 1.02m를 목표로 달려드는데, 회피가 강제하는
        // 최소 간격이 1.0m라 도착하자마자 계속 밀려나며 그 자리에서 떨었다.
        float standoff = Mathf.Max(
            unit.SeparationFromTarget(),
            Mathf.Max(unit.Stats.moveStopDistance, unit.Stats.attackRange * 0.85f));

        // 어느 쪽에서 붙을지가 직군마다 다르다.
        //
        // 예전에는 전원이 적의 현재 위치로 곧장 달려들었다. 그래서 탱커든 검사든 암살자든
        // 늘 같은 자리 — 적의 정면 — 에서 뒤엉켰고, 배후 피해 배율(backstabDamageMultiplier)이나
        // 방어 각도 같은 위치 관련 규칙이 사실상 쓰이지 않았다.
        //
        // 이제 접근 자체가 진형이다. 탱커는 정면으로 곧장 들어가 어그로를 붙들고, 검사는
        // 측면을 물고, 암살자는 등 뒤로 돌아간다. 방위 성향이 없는 유닛(생산직, 고블린)은
        // GetEngageDestination이 예측 위치를 그대로 돌려주므로 예전 동작 그대로다.
        bool flanking = unit.HasEngagePreference;
        Vector3 destination = flanking
            ? unit.GetEngageDestination(standoff)
            : unit.GetPredictedTargetPosition();

        // 궁수는 같은 값이면 높은 자리를 고른다. 원작의 "고지 선점 → 시야 확보 → 정찰"이
        // 한 묶음이라, 쏠 수 있는 자리 중에서는 위쪽이 언제나 낫다.
        // 사거리 안에 드는 자리만 후보라, 고지를 찾다 전선에서 떨어져 나가지는 않는다.
        Vector3 highGround;
        if (unit.TryFindHighGround(destination, out highGround)) destination = highGround;

        // 파고드는 자리로 갈 때는 그 지점까지 실제로 걸어가야 한다. 여기에 standoff를 다시
        // 걸면 목표에서 한 번 더 물러난 자리에 서게 되어 영영 사거리에 닿지 못한다.
        float stoppingDistance = flanking ? unit.Stats.moveStopDistance : standoff;

        unit.MoveTo(destination, unit.Stats.runSpeed, stoppingDistance);
    }
}
