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

    // 가는 쪽을 보고 달리는 중인가. 파고드는 접근에서만 참이다(아래 OnTick 주석).
    private bool facingTravel;

    // 가는 쪽으로 몸을 돌리는 중인가. 다 돌 때까지는 회전을 코드가 쥐고 있는다(BeginTravelFacing).
    private bool turningToTravel;

    // 다 돌았다고 볼 각도. 달리기 클립이 어색해 보이지 않을 만큼만 맞추면 된다 —
    // 0에 가깝게 잡으면 상대가 조금만 움직여도 주도권이 넘어가지 못하고 붙들려 있는다.
    private const float TravelFacingTolerance = 20f;

    // 이번 접근에서 돌아 들어갈 것인가. 한 번 정하면 접근이 끝날 때까지 바꾸지 않는다.
    private bool flanking;
    private bool flankDecided;

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
        facingTravel = false;
        turningToTravel = false;
        flankDecided = false;
        // 이번 접근의 파고들 방위를 새로 고르게 한다(GetEngageDestination 주석 참조).
        unit.ClearEngageBearing();
        // 0으로 두어 이번 틱에 곧바로 길을 잡게 한다. 남겨 두면 첫 간격만큼 목적지 없이 서 있다.
        destinationTimer = 0f;
        stalledTimer = 0f;
        waitTimer = 0f;
    }

    protected override BTStatus OnTick()
    {
        TickFacing();

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

        // 실제로 나아가는 쪽에 맞는 다리를 고른다. 몸이 진행방향을 못 따라잡는 구간이
        // 남아 있는데, 거기서 앞으로 달리는 클립을 쓰면 그대로 미끄러진다
        // (SetDirectionalMoveAnimationFromGroundSpeed 주석 참조).
        unit.SetDirectionalMoveAnimationFromGroundSpeed();
        return BTStatus.Running;
    }

    // 몸을 어느 쪽으로 둘 것인가. 곧장 달려드는 접근과 돌아 들어가는 접근이 다르다.
    //
    // 곧장 들어갈 때는 상대를 본다. 목적지가 상대의 발밑이라 가는 쪽과 보는 쪽이 거의 같고,
    // 예측 위치로 달리면서 상대를 겨누는 그림이 맞다.
    //
    // 파고들 때는 그럴 수 없다. 목적지가 상대의 옆이나 등 뒤라 가는 쪽과 보는 쪽이 갈라지는데,
    // 재생되는 것은 앞으로 달리는 클립 하나뿐이다. 암살자(engageAngle 180도)에서 그 어긋남이
    // 정확히 180도가 되어, 상대를 마주 본 채 등 뒤로 미끄러지는 — 앞으로 달리는 모션인데
    // 몸은 뒤로 가는 — 그림이 나온다. 검사(55도)와 창수(28도)는 덜하지만 같은 종류의 어긋남이다.
    //
    // 그래서 파고드는 동안은 가는 쪽을 보고 달린다. 사각지대로 돌아 들어가는 사람은 상대를
    // 노려보며 게걸음치지 않는다. 사거리에 들어서면 공격 동작이 다시 회전을 가져가 상대를
    // 마주 본다(AttackBehavior.OnEnter).
    //
    // 한 번 돌아 들어가기로 했으면 이 접근이 끝날 때까지 유지한다. HasEngagePreference는
    // 상대가 나를 보는 순간 거짓이 되는데, 적이 제 주기마다 표적을 다시 고르므로 그대로 두면
    // 몸의 주도권이 프레임마다 오가며 홱홱 돈다.
    private void TickFacing()
    {
        if (facingTravel) return;

        // 가는 쪽으로 돌아서는 중. 다 돌면 그때 주도권을 에이전트에게 넘긴다.
        if (turningToTravel)
        {
            if (!unit.TurnTowardsMoveDirection(TravelFacingTolerance)) return;

            turningToTravel = false;
            facingTravel = true;
            unit.SetCodeDrivenFacing(false);
            return;
        }

        unit.FaceTarget();
    }

    // 가는 쪽을 보고 달리기로 한다. 다만 주도권은 다 돌고 나서 넘긴다.
    //
    // 여기서 몸을 즉시 돌려서는 안 된다. 도주와 빠지기는 직전에 StopMovement로 속도가 0이라
    // 스냅해도 안전하지만, 추격은 이미 전속력으로 달리는 중에 들어올 수 있다 —
    // 그 상태로 180도를 스냅하면 몸만 돌고 에이전트에는 이전 방향의 속도가 그대로 남아,
    // 감속하는 0.2초 동안 그림이 통째로 역주행이 된다.
    // (실측: 스냅을 넣었더니 dotV가 -0.96까지 떨어지고 어긋난 프레임이 9.2%에서 19.6%로 늘었다.)
    //
    // 그렇다고 곧바로 에이전트에게 맡길 수도 없다. updateRotation은 "지금 내는 속도" 쪽으로
    // 돌리는데, 방향을 뒤집는 구간에서는 그 속도가 0을 지나며 거의 돌지 않기 때문이다.
    // 암살자가 빠졌다가 돌아설 때가 정확히 그 구간이다 — 실측에서 물러나던 쪽을 본 채로
    // 0.3초 넘게 뒷걸음질쳤고(내적 -0.89), 그동안 뒷걸음 클립이 재생됐다.
    //
    // 그래서 그 사이만 코드가 rotationSpeed로 돌린다(TurnTowardsMoveDirection). 스냅도 아니고
    // 에이전트의 느린 회전도 아닌, 720도/초로 0.25초에 도는 그림이다. 다 돌면 넘긴다.
    private void BeginTravelFacing()
    {
        if (facingTravel || turningToTravel) return;

        turningToTravel = true;
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
        // 돌아 들어갈지는 이번 접근에서 한 번만 정한다.
        //
        // HasEngagePreference는 "상대가 나를 안 보고 있는가"라 상대가 제 주기마다 표적을 다시
        // 고를 때마다 뒤집힌다. 그대로 매번 물어보면 목적지가 "등 뒤"와 "상대 발밑" 사이를 오가고,
        // 유닛은 그 둘을 잇는 호를 따라 상대 둘레를 돌게 된다 — 붙는 순간이 특히 심해서, 실측에서
        // 2.5m 안에서는 회전이 초당 202도에 옆걸음 클립이 절반(52%)이었다.
        // 방위를 접근 단위로 붙들어 두는 것(heldEngageBearing)과 같은 이유이고, 거기서 빠져 있던
        // 나머지 반쪽이다 — 방위는 붙들면서 "돌아 들어갈지" 자체는 매번 다시 묻고 있었다.
        if (!flankDecided)
        {
            flanking = unit.HasEngagePreference;
            flankDecided = true;
        }

        Vector3 destination = flanking
            ? unit.GetEngageDestination(standoff)
            : unit.GetPredictedTargetPosition();

        // 궁수는 같은 값이면 높은 자리를 고른다. 원작의 "고지 선점 → 시야 확보 → 정찰"이
        // 한 묶음이라, 쏠 수 있는 자리 중에서는 위쪽이 언제나 낫다.
        // 사거리 안에 드는 자리만 후보라, 고지를 찾다 전선에서 떨어져 나가지는 않는다.
        Vector3 highGround;
        if (unit.TryFindHighGround(destination, out highGround)) destination = highGround;

        // 돌아 들어가기로 했으면 몸의 주도권을 에이전트에게 넘긴다(BeginTravelFacing 주석 참조).
        if (flanking) BeginTravelFacing();

        // 파고드는 자리로 갈 때는 그 지점까지 실제로 걸어가야 한다. 여기에 standoff를 다시
        // 걸면 목표에서 한 번 더 물러난 자리에 서게 되어 영영 사거리에 닿지 못한다.
        float stoppingDistance = flanking ? unit.Stats.moveStopDistance : standoff;

        unit.MoveTo(destination, unit.Stats.runSpeed, stoppingDistance);
    }
}
