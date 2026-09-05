using UnityEngine;

// 등을 보이고 달아나는 동작. 안전해질 때까지 반복해서 달린다.
//
// 뒷걸음으로 사거리를 되찾는 회피(EvadeBehavior)와 나눠 두었다 — 그쪽은 상대를 마주 본 채
// 짧게 한 번 물러나고 바로 교전으로 돌아가는 동작이고, 이쪽은 떼어놓는 것 자체가 목적이라
// 몸도 모션도 거리도 멈추는 조건도 전부 다르다.
//
// 달아나는 이유는 둘이다:
//  - 붙잡혀서(마법사). 평타가 없고 영창은 붙는 순간 접히므로, 조금씩 물러나 봐야 그동안
//    할 수 있는 것이 하나도 없다. 달려서 떼어놓고 다시 영창을 시작하는 쪽이 이 직군에게 이득이다.
//  - HP가 바닥나서. 목숨이 걸렸는데 겨누고 있을 이유가 없고, 한 번 뛰는 것으로는 살아날
//    거리가 안 나온다.
// 둘은 멈추는 조건만 다르다(StillPressured 참조). 달리는 방식은 같으므로 한 동작에 둔다.
public class FleeBehavior : UnitBehavior
{
    // 한 번 달리는 구간의 상한. 길이 막히거나 목적지에 닿지 못해도 여기서 끊고 다시 잡는다.
    private const float MaxFleeDuration = 1.5f;

    // 최소로 달아나는 시간. 이보다 일찍 안전해져도 이 시간까지는 돌아서지 않는다.
    private const float MinFleeDuration = 0.35f;

    private float stateTimer;
    private float fleeTimer;
    private Vector3 destination;
    private bool startFailed;

    // 붙잡혀서 달아나는가(마법사), 아니면 HP가 바닥나서인가.
    // 이 값이 멈출 조건과 물러날 쪽을 함께 정한다.
    private bool chasedOff;

    public FleeBehavior(UnitController context) : base(context)
    {
    }

    // 달아나는 중에 겨눌 상대를 바꾸면 달아나는 방향이 그 자리에서 뒤집힌다.
    public override bool LocksTarget => true;

    // 안전해질 때까지가 조건이다. 중간에 다른 판단으로 멈춰 서면 그 자리에서 붙잡힌다.
    public override bool AllowsReprioritize => false;

    protected override void OnEnter()
    {
        startFailed = !BeginLeg();
    }

    protected override BTStatus OnTick()
    {
        if (startFailed) return BTStatus.Failure;

        if (!unit.HasUsableTarget()) return BTStatus.Failure;

        unit.SetMoveAnimationFromGroundSpeed(true);
        fleeTimer -= AnimationDeltaTime;

        stateTimer -= AnimationDeltaTime;
        if (stateTimer > 0f) return BTStatus.Running;

        // 이미 안전해졌으면 남은 거리를 마저 도망칠 이유가 없다. 쫓아오지도 않는 적에게서
        // 계속 도망치는 그림이고, 그만큼 쏠 시간을 버린다.
        if (!StillPressured()) return BTStatus.Success;

        // 목적지에 닿을 때까지 계속 간다. 다만 상한을 넘기면 끊는다.
        //
        // 여기서 unit.HasMoveDestination을 보면 안 된다. 그건 순찰 갈래가 쓰는 목적지 플래그라
        // 이 동작은 세우지도 지우지도 않는다 — 순찰하다 들어오면 그 플래그가 참인 채로 남아
        // 도주가 영영 끝나지 않는다.
        if (fleeTimer > 0f && !unit.HasReachedDestination(destination)) return BTStatus.Running;

        // 아직 쫓기는데 이번 구간이 끝났다. 다시 잡아서 이어 달린다.
        //
        // "안전해질 때까지"가 조건이라 한 번 물러나고 마는 것으로는 성립하지 않는다 —
        // 여전히 쫓기는데 멈춰 서면 그 자리에서 붙잡히고, 그것이 곧 도망과 복귀를 반복하는
        // 버벅임이 된다.
        if (!BeginLeg()) return BTStatus.Failure;
        return BTStatus.Running;
    }

    // 한 구간을 새로 잡아 달리기 시작한다. 갈 곳을 찾지 못하면 false.
    private bool BeginLeg()
    {
        // 이번 후퇴 뒤에는 반드시 한 발 쏘고 나서야 다시 물러날 수 있다.
        unit.MarkEvadeStarted();

        stateTimer = MinFleeDuration;
        fleeTimer = MaxFleeDuration;

        if (!unit.HasUsableTarget()) return false;

        // 간격을 잃은 쪽을 먼저 본다. 둘 다 해당하면(붙잡힌 데다 HP까지 낮으면) 붙잡힌 것으로
        // 친다 — 그때 필요한 것은 "쫓아오는 놈을 떼어내기"이고, 그게 곧 살아남는 길이다.
        chasedOff = unit.Stats.fleeByRunning && unit.ShouldKeepDistance();

        // 가는 쪽을 보고 달린다. 그래야 달리기 모션과 실제 이동이 맞으므로 회전은 에이전트에게
        // 맡긴다. 여기서 코드가 회전을 가져가면 몸은 적을 향한 채 뒤로 달려 발이 그대로 미끄러진다.
        unit.SetCodeDrivenFacing(false);

        // 붙잡혀서 달아날 때는 물러날 쪽을 컨텍스트가 정한다 — 전선에서 떨어져 나온 상태면
        // 아군 쪽으로 물러난다(GetSpacingRetreatDirection 주석 참조).
        // 목숨이 걸린 도주는 적의 정반대다. 그때는 전열이 아니라 거리가 필요하다.
        Vector3 away = chasedOff ? unit.GetSpacingRetreatDirection() : AwayFromTarget();

        // 원하는 쪽이 벽이면 뚫린 쪽으로 튼다.
        //
        // 예전에는 목적지를 그대로 NavMesh에 붙였는데, 벽 안을 가리키면 지금 서 있는 자리
        // 바로 옆으로 끌려와 "도착했다"가 되어 유닛이 벽에 붙은 채 굳었다. 방향 유지까지
        // 겹쳐 다음 도주도 같은 벽으로 향했다.
        //
        // 거리를 멀리 잡는 것이 도주의 성패를 가른다. 짧게 끊으면 그 거리마다 감속하고 다시
        // 0에서 가속하기를 반복해 최고 속도에 한 번도 닿지 못한다 — 실측에서 최고 4.37m/s인
        // 탱커의 평균 속도가 2.81m/s로 떨어져, 4.0m/s로 꾸준히 달려오는 고블린에게 벌어 놓은
        // 거리를 도로 내줬다.
        Vector3 resolved;
        if (!unit.TryFindRetreatSpot(away, unit.SurvivalFleeDistance, out resolved, out destination))
        {
            // 어느 쪽도 뚫려 있지 않다 — 구석에 몰렸다. 벽에 붙어 굳어 있느니 돌아서서 싸운다.
            return false;
        }

        // 실제로 갈 수 있는 방향으로 유지 대상을 갱신한다. 그러지 않으면 유지 창 동안
        // 막힌 원래 방향이 계속 돌아와 같은 벽으로 다시 향한다.
        if (chasedOff) unit.CommitRetreatDirection(resolved);

        // 출발하는 순간 몸을 가는 쪽으로 돌려 둔다. 그러지 않으면 에이전트가 서서히 도는 동안
        // 달리기 모션은 앞으로 재생되는데 몸은 아직 옆이나 뒤로 밀려, 그 짧은 구간에 발이
        // 눈에 띄게 미끄러진다(실측 -0.96). 어차피 등을 보이기로 한 참이라 서서히 돌 이유도 없다.
        unit.SnapFacing(resolved);

        // 떼어놓는 것이 목적이라 속도를 아낄 이유가 없다.
        unit.MoveTo(destination, unit.Stats.runSpeed);
        unit.SetMoveAnimationFromGroundSpeed(true);
        return true;
    }

    // 아직 달아나야 하는가. 두 이유가 보는 것이 다르다.
    //
    // 붙잡혀서 달아나는 경우를 거리 하나로 끊으면 유지 거리 경계에서 도주와 복귀가 계속
    // 뒤집힌다 — 물러나자마자 다시 붙잡혀 또 물러나는 것이 "공격하려다 버벅이는" 그림이었다.
    // 대신 쫓아오던 놈이 표적을 바꿨는지를 본다(IsBeingChased). 탱커가 도발로 끌어갔거나
    // 그 적이 다른 아군을 물면 그 순간 도망칠 이유가 사라지고, 그때 돌아가 다시 겨눈다.
    private bool StillPressured()
    {
        return chasedOff ? unit.IsBeingChased() : unit.ShouldRetreatForSurvival();
    }

    private Vector3 AwayFromTarget()
    {
        Vector3 away = unit.transform.position - unit.CurrentTarget.Position;
        away.y = 0f;
        if (away.sqrMagnitude <= 0.0001f) return -unit.transform.forward;
        return away.normalized;
    }
}
