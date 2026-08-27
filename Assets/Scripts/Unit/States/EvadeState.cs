using UnityEngine;
using UnityEngine.AI;

public class EvadeState : UnitBattleState
{
    private float stateTimer;
    private float fleeTimer;

    // 도약 관련
    private bool leaping;
    private float leapRemaining;
    private float leapSpeed;
    private Vector3 leapDirection;

    // 이번 후퇴가 "거리 유지"인가 "위기 도주"인가. 둘은 물러나는 방식이 다르다.
    private bool keepingDistance;
    private Vector3 destination;

    public EvadeState(UnitController context) : base(context)
    {
    }

    // 최소로 물러나는 시간. 이보다 일찍 끝나도 이 시간까지는 다시 덤비지 않는다 —
    // 한 발짝 물러나자마자 바로 돌아서서 때리면 회피가 아니라 그냥 제자리 걸음으로 보인다.
    private const float MinEvadeDuration = 0.35f;

    // 달아나기가 끝나지 않는 것을 막는 상한. 길이 막히거나 목적지에 닿지 못해도 여기서 끊는다.
    private const float MaxFleeDuration = 1.5f;

    public override void Enter()
    {
        base.Enter();

        // 이번 후퇴 뒤에는 반드시 한 발 쏘고 나서야 다시 물러날 수 있다.
        context.MarkEvadeStarted();

        leaping = false;
        leapRemaining = 0f;
        stateTimer = MinEvadeDuration;
        fleeTimer = MaxFleeDuration;

        if (!context.HasUsableTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        // 왜 물러나는지에 따라 물러나는 방식이 갈린다.
        //
        //  - 거리 유지(원거리 유닛이 적에게 붙잡혔을 때): 상대를 마주 본 채 뒤로 한 번 뛴다.
        //    쫓아오는 근접 유닛과 걸음 속도가 비슷해서, 걸어서 빼면 아무리 물러나도 못 벌린다.
        //  - 위기 도주(HP가 바닥났을 때): 등을 보이고 달아난다. 목숨이 걸렸는데 겨누고 있을
        //    이유가 없고, 한 번 뛰는 것으로는 살아날 거리가 안 나온다.
        keepingDistance = context.ShouldKeepDistance();

        // 간격을 벌리는 후퇴는 물러날 쪽을 컨텍스트가 정한다 — 평소에는 적의 반대쪽이지만,
        // 전선에서 떨어져 나온 상태면 아군 쪽으로 물러난다(GetSpacingRetreatDirection 주석 참조).
        // 목숨이 걸린 도주는 예전 그대로 적의 정반대다. 그때는 전열이 아니라 거리가 필요하다.
        Vector3 away;
        if (keepingDistance)
        {
            away = context.GetSpacingRetreatDirection();
        }
        else
        {
            away = context.transform.position - context.CurrentTarget.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude <= 0.0001f) away = -context.transform.forward;
            away.Normalize();
        }

        // 뒤로 빠질 때만 회전을 코드가 가져간다. 도주는 가는 쪽을 보고 달려야 하므로
        // NavMeshAgent에게 돌려준다.
        context.SetCodeDrivenFacing(keepingDistance);

        if (keepingDistance && context.DodgeAnimationDuration > 0.01f)
        {
            // 경로 이동을 끄고 도약으로만 민다(MoveDodge 주석 참조).
            // StopMovement가 대기 자세를 한 번 걸지만 바로 아래 TriggerDodge가 덮는다.
            context.StopMovement();
            context.ClearMoveDestination();

            leaping = true;
            leapDirection = away;
            leapRemaining = context.DodgeAnimationDuration;
            leapSpeed = context.DodgeMoveSpeed(context.Stats.evadeRange);
            stateTimer = Mathf.Max(MinEvadeDuration, leapRemaining);
            context.TriggerDodge();
            return;
        }

        // 도약 모션이 없는 리그, 그리고 위기 도주는 걸어서/달려서 물러난다.
        float speed = keepingDistance ? context.BackpedalSpeed : context.Stats.runSpeed;

        // 얼마나 멀리 잡을지가 도망의 성패를 가른다.
        //
        // 간격 벌리기는 2.5m면 충분하다 — 사거리를 되찾는 것이 목적이라 짧게 끊는 편이 낫다.
        // 반면 목숨이 걸린 도주에 같은 값을 쓰면 도망이 아예 성립하지 않는다. 2.5m마다 목적지에
        // 닿아 감속하고 다시 0에서 가속하기를 반복하므로 최고 속도에 한 번도 닿지 못하기 때문이다.
        // (실측: 최고 4.37m/s인 탱커의 평균 속도가 2.81m/s로 떨어져, 4.0m/s로 꾸준히 달려오는
        //  고블린에게 벌어 놓은 거리를 도로 내줬다.)
        float retreatDistance = keepingDistance ? context.Stats.evadeRange : context.SurvivalFleeDistance;
        destination = context.transform.position + away * retreatDistance;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(destination, out hit, retreatDistance, NavMesh.AllAreas))
        {
            destination = hit.position;
        }

        context.MoveTo(destination, speed);
        UpdateRetreatAnimation();
    }

    public override void Update()
    {
        if (!context.HasUsableTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        if (keepingDistance) context.FaceTarget();

        if (leaping)
        {
            // 클립이 끝나면 미는 것도 끝난다. 남은 시간보다 더 밀지 않아야 도약 거리가 정확하다.
            if (leapRemaining > 0f)
            {
                float step = Mathf.Min(AnimationDeltaTime, leapRemaining);
                context.MoveDodge(leapDirection, leapSpeed, step);
                leapRemaining -= AnimationDeltaTime;
            }
        }
        else
        {
            UpdateRetreatAnimation();
            fleeTimer -= AnimationDeltaTime;
        }

        stateTimer -= AnimationDeltaTime;
        if (stateTimer > 0f) return;

        // 달려서 물러나는 중이면 목적지에 닿을 때까지 계속 간다. 다만 상한을 넘기면 끊는다.
        //
        // 예전에는 여기서 context.HasMoveDestination을 봤는데, 그건 MoveState/SearchState가
        // 쓰는 순찰 목적지 플래그라 EvadeState는 세우지도 지우지도 않는다. 순찰하다 회피에
        // 들어오면 그 플래그가 참인 채로 남아 후퇴가 영영 끝나지 않았다.
        if (!leaping && fleeTimer > 0f && !context.HasReachedDestination(destination)) return;

        // 위기 도주는 안전해질 때까지 반복한다 — 한 번만 물러나고 말면 여전히 위험한데
        // 근접전으로 걸어 들어간다(ShouldRetreatForSurvival 주석 참조).
        //
        // 거리 유지는 반복하지 않는다. 임계 거리는 제 사거리의 절반 아래라 그 안이면 이미
        // 충분히 쏠 수 있다. 여기서 반복하면 한 발도 못 쏘고 물러나기만 한다.
        if (context.ShouldRetreatForSurvival())
        {
            Enter();
            return;
        }

        ReturnToCombat();
    }

    // 재생 배속을 "요청한 속도"가 아니라 지금 실제로 나아가는 속도에 맞춘다.
    // NavMeshAgent는 출발할 때 가속하고 코너와 회피에서 느려지는데, 요청 속도로 배속을 잡으면
    // 그 구간마다 다리가 땅보다 빨리 움직인다.
    private void UpdateRetreatAnimation()
    {
        // 0으로 떨어지면 SetMoveAnimation이 대기 자세로 보내 출발 첫 프레임이 한 번 튄다.
        // 배속 하한(moveSpeedMultiplierRange)이 어차피 걸리므로 아주 작은 값으로 바닥을 깐다.
        float speed = Mathf.Max(context.CurrentMoveSpeed, 0.05f);

        if (keepingDistance)
        {
            context.PlayRetreatAnimation(speed);
            return;
        }

        context.SetMoveAnimation(speed, true, false);
    }
}
