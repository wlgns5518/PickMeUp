using UnityEngine;

// 붙잡힌 원거리·리치 직군이 상대를 마주 본 채 한 번 물러나는 동작. "사거리를 되찾는다"가 전부다.
//
// 등을 보이고 달아나는 도주(FleeBehavior)와 나눠 두었다. 같은 "물러나기"지만 두 동작이 공유하는
// 것이 하나도 없다 — 방향을 정하는 기준, 속도, 거리, 몸이 향하는 쪽, 재생할 모션, 멈출 조건이
// 전부 다르다. 한때 이 둘(과 위기 도주까지 셋)이 불리언 두 개로 한 클래스에 들어 있었는데,
// keepingDistance && !runningAway 같은 조건이 여섯 번 되풀이되면서 어느 갈래를 읽고 있는지
// 줄마다 다시 따져야 했다. 갈라 놓으면 각 파일이 한 가지 동작만 설명한다.
//
// 어느 쪽으로 갈지는 트리가 고른다(UnitBehaviorTree의 후퇴 가지 — 기억형 셀렉터라
// 한 번 고르면 그 후퇴가 끝날 때까지 바뀌지 않는다).
public class EvadeBehavior : UnitBehavior
{
    // 최소로 물러나는 시간. 이보다 일찍 끝나도 이 시간까지는 다시 덤비지 않는다 —
    // 한 발짝 물러나자마자 바로 돌아서서 때리면 회피가 아니라 그냥 제자리 걸음으로 보인다.
    private const float MinEvadeDuration = 0.35f;

    // 뒷걸음이 끝나지 않는 것을 막는 상한. 길이 막히거나 목적지에 닿지 못해도 여기서 끊는다.
    private const float MaxBackpedalDuration = 1.5f;

    private float stateTimer;
    private float backpedalTimer;
    private Vector3 destination;

    // 시작조차 못 했다(겨눌 상대가 사라졌거나 사방이 막혔다). 다음 틱에 Failure로 답하고 빠진다.
    private bool startFailed;

    // 도약 관련
    private bool leaping;
    private float leapRemaining;
    private float leapSpeed;
    private Vector3 leapDirection;

    public EvadeBehavior(UnitController context) : base(context)
    {
    }

    // 물러나는 중에 겨눌 상대를 바꾸면 물러나는 방향이 그 자리에서 뒤집힌다.
    public override bool LocksTarget => true;

    // 한 번 물러나기로 했으면 그 한 번은 끝까지 간다. 중간에 마음을 바꾸면 제자리 걸음이 된다.
    public override bool AllowsReprioritize => false;

    protected override void OnEnter()
    {
        // 이번 후퇴 뒤에는 반드시 한 발 쏘고 나서야 다시 물러날 수 있다.
        unit.MarkEvadeStarted();

        startFailed = false;
        leaping = false;
        leapRemaining = 0f;
        stateTimer = MinEvadeDuration;
        backpedalTimer = MaxBackpedalDuration;

        if (!unit.HasUsableTarget())
        {
            startFailed = true;
            return;
        }

        // 물러나는 내내 상대를 마주 본다. 가는 쪽과 몸이 어긋나므로 회전은 코드가 잡는다.
        unit.SetCodeDrivenFacing(true);

        // 물러날 쪽은 컨텍스트가 정한다 — 평소에는 적의 반대쪽이지만, 전선에서 떨어져 나온
        // 상태면 아군 쪽으로 물러난다(GetSpacingRetreatDirection 주석 참조).
        Vector3 away = unit.GetSpacingRetreatDirection();

        // 도약 모션이 있으면 뛰고, 없는 리그(고블린)는 걸어서 뒤로 뺀다.
        bool started = unit.DodgeAnimationDuration > 0.01f ? TryBeginLeap(away) : TryBeginBackpedal(away);

        // 어느 쪽도 뚫려 있지 않다 — 구석에 몰렸다. 벽에 붙어 굳어 있느니 돌아서서 싸운다.
        if (!started) startFailed = true;
    }

    protected override BTStatus OnTick()
    {
        if (startFailed) return BTStatus.Failure;

        // 겨누던 상대가 쓰러졌다. 물러날 기준 자체가 사라졌으므로 여기서 접고 교전을 다시 잡는다.
        if (!unit.HasUsableTarget()) return BTStatus.Failure;

        // 도약 중에는 몸을 돌리지 않는다. 가진 회피 모션이 뒷점프 하나뿐이라, 몸이 향한 쪽과
        // 뛰는 쪽이 어긋나면 옆으로 미끄러지듯 흐르는 그림이 된다. 진입할 때 맞춰 둔 방향을
        // 그대로 유지한다. 뒷걸음으로 재는 동안에만 상대를 마주 본다.
        if (!leaping) unit.FaceTarget();

        if (leaping) TickLeap();
        else
        {
            PlayBackpedalAnimation();
            backpedalTimer -= AnimationDeltaTime;
        }

        stateTimer -= AnimationDeltaTime;
        if (stateTimer > 0f) return BTStatus.Running;

        // 도약은 클립이 곧 구간이다. 다 뛰었으면 그것으로 이번 회피가 끝난다.
        if (leaping) return BTStatus.Success;

        // 이미 안전해졌으면 남은 거리를 마저 물러날 이유가 없다.
        if (!unit.ShouldKeepDistance()) return BTStatus.Success;

        // 뒷걸음은 잡아 둔 자리까지 간다. 상한을 넘기면 끊는다.
        //
        // 여기서 unit.HasMoveDestination을 보면 안 된다. 그건 순찰 갈래가 쓰는 목적지 플래그라
        // 이 동작은 세우지도 지우지도 않는다 — 순찰하다 들어오면 그 플래그가 참인 채로 남아
        // 후퇴가 영영 끝나지 않는다.
        if (backpedalTimer > 0f && !unit.HasReachedDestination(destination)) return BTStatus.Running;

        return BTStatus.Success;
    }

    protected override void OnExit()
    {
        // 도약 도중에 끊겨도(피격, 사망) 이동 권한은 반드시 돌려줘야 한다.
        // 그러지 않으면 회피가 끝난 뒤에도 회피가 꺼져 있어 유닛이 서로 겹친다.
        unit.EndDodgeMove();
    }

    // 뒤로 뛴다. 뚫린 쪽을 찾지 못하면 false.
    private bool TryBeginLeap(Vector3 away)
    {
        // 도약도 벽을 확인한다. 뒤가 막혀 있으면 뛰어 봐야 벽에 붙어 제자리다.
        Vector3 leapSpot;
        if (!unit.TryFindRetreatSpot(away, unit.Stats.evadeRange, out away, out leapSpot)) return false;

        unit.CommitRetreatDirection(away);

        // 경로 이동을 끄고 도약으로만 민다(MoveDodge 주석 참조).
        //
        // 남은 속도까지 지우는 것이 핵심이다. 물러나기 직전의 유닛은 대개 적 쪽으로 다가가던
        // 참이라 에이전트에 앞을 향한 속도가 남아 있고, 그대로 두면 뒤로 미는 도약과 겹쳐
        // 몸이 앞으로 간다 — "뒷점프를 하는데 앞으로 이동"이 그것이다.
        unit.BeginDodgeMove();
        unit.ClearMoveDestination();

        leaping = true;
        leapDirection = away;
        leapRemaining = unit.DodgeAnimationDuration;
        leapSpeed = unit.DodgeMoveSpeed(unit.Stats.evadeRange);
        stateTimer = Mathf.Max(MinEvadeDuration, leapRemaining);

        // 뛰는 쪽의 정반대를 보게 맞춘다. 예전에는 겨누는 상대를 보면서 "위협의 반대쪽"으로
        // 뛰었는데, 그 둘은 자주 다른 방향이라 그대로 미끄러져 보였다.
        unit.FaceAwayFrom(away);
        unit.TriggerDodge();
        return true;
    }

    // 걸어서 뒤로 뺀다. 뚫린 쪽을 찾지 못하면 false.
    private bool TryBeginBackpedal(Vector3 away)
    {
        // 원하는 쪽이 벽이면 뚫린 쪽으로 튼다.
        //
        // 예전에는 목적지를 그대로 NavMesh에 붙였는데, 벽 안을 가리키면 지금 서 있는 자리
        // 바로 옆으로 끌려와 "도착했다"가 되어 유닛이 벽에 붙은 채 굳었다. 방향 유지까지
        // 겹쳐 다음 후퇴도 같은 벽으로 향했다.
        Vector3 resolved;
        if (!unit.TryFindRetreatSpot(away, unit.Stats.evadeRange, out resolved, out destination)) return false;

        // 실제로 갈 수 있는 방향으로 유지 대상을 갱신한다. 그러지 않으면 유지 창 동안
        // 막힌 원래 방향이 계속 돌아와 같은 벽으로 다시 향한다.
        unit.CommitRetreatDirection(resolved);

        // 간격을 되찾는 후퇴는 2.5m면 충분하다 — 짧게 끊는 편이 낫다.
        unit.MoveTo(destination, unit.BackpedalSpeed);
        PlayBackpedalAnimation();
        return true;
    }

    private void TickLeap()
    {
        // 클립이 끝나면 미는 것도 끝난다. 남은 시간보다 더 밀지 않아야 도약 거리가 정확하다.
        if (leapRemaining <= 0f)
        {
            // 이동 권한을 에이전트에게 돌려준다.
            unit.EndDodgeMove();
            return;
        }

        float step = Mathf.Min(AnimationDeltaTime, leapRemaining);
        unit.MoveDodge(leapDirection, leapSpeed, step);
        leapRemaining -= AnimationDeltaTime;
    }

    // 재생 배속을 "요청한 속도"가 아니라 지금 실제로 나아가는 속도에 맞춘다.
    // NavMeshAgent는 출발할 때 가속하고 코너와 회피에서 느려지는데, 요청 속도로 배속을 잡으면
    // 그 구간마다 다리가 땅보다 빨리 움직인다.
    private void PlayBackpedalAnimation()
    {
        // 0으로 떨어지면 대기 자세로 보내 출발 첫 프레임이 한 번 튄다.
        // 배속 하한(moveSpeedMultiplierRange)이 어차피 걸리므로 아주 작은 값으로 바닥을 깐다.
        unit.PlayRetreatAnimation(Mathf.Max(unit.CurrentMoveSpeed, 0.05f));
    }
}
