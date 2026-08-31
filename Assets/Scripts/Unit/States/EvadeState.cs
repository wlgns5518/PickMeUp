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
    // 거리 유지인데도 뒷걸음이 아니라 등을 보이고 달려서 떼어놓는가(마법사).
    private bool runningAway;
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

        // 마법사는 붙잡히면 뒷걸음으로 재지 않고 등을 보이고 달린다.
        //
        // 평타가 없고 영창은 붙는 순간 접히므로, 조금씩 물러나 봐야 그동안 할 수 있는 것이
        // 하나도 없다. 아예 달려서 떼어놓고 다시 영창을 시작하는 쪽이 이 직군에게는 이득이다.
        runningAway = keepingDistance && context.Stats.fleeByRunning;

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

        // 뒤로 빠질 때만 회전을 코드가 가져간다. 달려서 달아나는 경우는 가는 쪽을 보고
        // 달려야 하므로(그래야 달리기 모션과 실제 이동이 맞는다) NavMeshAgent에게 돌려준다.
        context.SetCodeDrivenFacing(keepingDistance && !runningAway);

        if (keepingDistance && !runningAway && context.DodgeAnimationDuration > 0.01f)
        {
            // 도약도 벽을 확인한다. 뒤가 막혀 있으면 뛰어 봐야 벽에 붙어 제자리다.
            // 뚫린 쪽이 있으면 그쪽으로 틀고, 아무 데도 없으면 도약을 포기하고 돌아서서 싸운다.
            Vector3 leapSpot;
            if (!context.TryFindRetreatSpot(away, context.Stats.evadeRange, out away, out leapSpot))
            {
                ReturnToCombat();
                return;
            }
            context.CommitRetreatDirection(away);

            // 경로 이동을 끄고 도약으로만 민다(MoveDodge 주석 참조).
            //
            // 남은 속도까지 지우는 것이 핵심이다. 물러나기 직전의 유닛은 대개 적 쪽으로 다가가던
            // 참이라 에이전트에 앞을 향한 속도가 남아 있고, 그대로 두면 뒤로 미는 도약과 겹쳐
            // 몸이 앞으로 간다 — "뒷점프를 하는데 앞으로 이동"이 그것이다.
            context.BeginDodgeMove();
            context.ClearMoveDestination();

            leaping = true;
            leapDirection = away;
            leapRemaining = context.DodgeAnimationDuration;
            leapSpeed = context.DodgeMoveSpeed(context.Stats.evadeRange);
            stateTimer = Mathf.Max(MinEvadeDuration, leapRemaining);

            // 뛰는 쪽의 정반대를 보게 맞춘다.
            //
            // 가진 회피 모션이 뒷점프 하나뿐이라, 몸이 향한 쪽과 뛰는 쪽이 어긋나면 그대로
            // 어색해진다 — 옆으로 미끄러지듯 흐르는 그림이 그것이다. 예전에는 겨누는 상대를
            // 보면서 "위협의 반대쪽"으로 뛰었는데, 그 둘은 자주 다른 방향이다.
            // 물러나는 쪽을 등지고 보게 하면 뒷점프가 언제나 동작과 맞는다.
            context.FaceAwayFrom(away);
            context.TriggerDodge();
            return;
        }

        // 도약 모션이 없는 리그, 마법사의 달아나기, 그리고 위기 도주는 걸어서/달려서 물러난다.
        // 달아나는 경우는 둘 다 달리기 최고 속도다 — 떼어놓는 것이 목적이라 아낄 이유가 없다.
        float speed = keepingDistance && !runningAway ? context.BackpedalSpeed : context.Stats.runSpeed;

        // 얼마나 멀리 잡을지가 도망의 성패를 가른다.
        //
        // 간격 벌리기는 2.5m면 충분하다 — 사거리를 되찾는 것이 목적이라 짧게 끊는 편이 낫다.
        // 반면 목숨이 걸린 도주에 같은 값을 쓰면 도망이 아예 성립하지 않는다. 2.5m마다 목적지에
        // 닿아 감속하고 다시 0에서 가속하기를 반복하므로 최고 속도에 한 번도 닿지 못하기 때문이다.
        // (실측: 최고 4.37m/s인 탱커의 평균 속도가 2.81m/s로 떨어져, 4.0m/s로 꾸준히 달려오는
        //  고블린에게 벌어 놓은 거리를 도로 내줬다.)
        // 달아나는 경우도 멀리 잡는다. 짧게 끊으면 위와 같은 이유로 최고 속도에 닿지 못해
        // 쫓아오는 고블린을 떼어놓지 못한다 — 달리기로 바꾼 의미가 사라진다.
        float retreatDistance = keepingDistance && !runningAway
            ? context.Stats.evadeRange
            : context.SurvivalFleeDistance;
        // 원하는 쪽이 벽이면 뚫린 쪽으로 튼다.
        //
        // 예전에는 목적지를 그대로 NavMesh에 붙였는데, 벽 안을 가리키면 지금 서 있는 자리
        // 바로 옆으로 끌려와 "도착했다"가 되어 유닛이 벽에 붙은 채 굳었다. 방향 유지까지
        // 겹쳐 다음 도망도 같은 벽으로 향했다.
        Vector3 resolved;
        if (!context.TryFindRetreatSpot(away, retreatDistance, out resolved, out destination))
        {
            // 어느 쪽도 뚫려 있지 않다 — 구석에 몰렸다. 벽에 붙어 굳어 있느니 돌아서서 싸운다.
            ReturnToCombat();
            return;
        }

        // 실제로 갈 수 있는 방향으로 유지 대상을 갱신한다. 그러지 않으면 유지 창 동안
        // 막힌 원래 방향이 계속 돌아와 같은 벽으로 다시 향한다.
        away = resolved;
        if (keepingDistance) context.CommitRetreatDirection(away);

        // 달아나기는 출발하는 순간 몸을 가는 쪽으로 돌려 둔다.
        //
        // 그러지 않으면 에이전트가 서서히 도는 동안 달리기 모션은 앞으로 재생되는데 몸은
        // 아직 옆이나 뒤로 밀려, 그 짧은 구간에 발이 눈에 띄게 미끄러진다(실측 -0.96).
        // 어차피 등을 보이고 달아나기로 한 참이라 서서히 돌 이유도 없다.
        if (runningAway) context.SnapFacing(away);

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

        // 도약 중에는 몸을 돌리지 않는다. 뒷점프 모션이 향한 쪽과 뛰는 쪽이 어긋나면
        // 그대로 미끄러져 보이므로, 진입할 때 맞춰 둔 방향을 그대로 유지한다.
        // 뒷걸음으로 재는 동안에만 상대를 마주 본다.
        //
        // 달아나는 경우(마법사)에 이걸 걸면 등을 보이고 달리는데 몸은 적을 향하게 되어,
        // 달리기 모션이 뒤로 재생되며 발이 그대로 미끄러진다. 그때는 회전을 에이전트에게
        // 맡겨 가는 쪽을 보게 둔다(SetCodeDrivenFacing(false)).
        // 도약 중에도 마찬가지로 진입할 때 맞춰 둔 방향을 유지한다.
        if (keepingDistance && !runningAway && !leaping) context.FaceTarget();

        if (leaping)
        {
            // 클립이 끝나면 미는 것도 끝난다. 남은 시간보다 더 밀지 않아야 도약 거리가 정확하다.
            if (leapRemaining > 0f)
            {
                float step = Mathf.Min(AnimationDeltaTime, leapRemaining);
                context.MoveDodge(leapDirection, leapSpeed, step);
                leapRemaining -= AnimationDeltaTime;
            }
            else
            {
                // 클립이 끝났다 — 이동 권한을 에이전트에게 돌려준다.
                context.EndDodgeMove();
            }
        }
        else
        {
            UpdateRetreatAnimation();
            fleeTimer -= AnimationDeltaTime;
        }

        stateTimer -= AnimationDeltaTime;
        if (stateTimer > 0f) return;

        // 이미 안전해졌으면 남은 거리를 마저 도망칠 이유가 없다.
        //
        // 고블린이 표적을 바꿔 더는 나를 쫓지 않는 경우가 여기다. 예전에는 그때도 정해진 거리를
        // 끝까지 물러났다 — 쫓아오지도 않는 적에게서 계속 도망치는 그림이고, 그만큼 쏠 시간을
        // 버린다. 위협이 사라진 순간 멈춰서 다시 겨누는 쪽이 맞다.
        //
        // 달아나기(마법사)는 그 판단을 거리가 아니라 "아직 나를 쫓는가"로 한다.
        //
        // 거리 하나로 끊으면 유지 거리 경계에서 도망과 복귀가 계속 뒤집힌다 — 물러나자마자
        // 다시 붙잡혀 또 물러나는 것이 "공격하려다 버벅이는" 그림이었다. 대신 쫓아오던 놈이
        // 표적을 바꿨는지를 본다(IsBeingChased). 탱커가 도발로 끌어갔거나 그 적이 다른 아군을
        // 물면 그 순간 도망칠 이유가 사라지고, 그때 돌아가 영창을 시작한다.
        bool stillPressured;
        if (runningAway) stillPressured = context.IsBeingChased();
        else if (keepingDistance) stillPressured = context.ShouldKeepDistance();
        else stillPressured = context.ShouldRetreatForSurvival();

        if (!stillPressured)
        {
            ReturnToCombat();
            return;
        }

        // 달려서 물러나는 중이면 목적지에 닿을 때까지 계속 간다. 다만 상한을 넘기면 끊는다.
        //
        // 예전에는 여기서 context.HasMoveDestination을 봤는데, 그건 MoveState/SearchState가
        // 쓰는 순찰 목적지 플래그라 EvadeState는 세우지도 지우지도 않는다. 순찰하다 회피에
        // 들어오면 그 플래그가 참인 채로 남아 후퇴가 영영 끝나지 않았다.
        if (!leaping && fleeTimer > 0f && !context.HasReachedDestination(destination)) return;

        // 여기까지 왔다는 것은 아직 쫓기고 있는데 이번 도주 구간이 끝났다는 뜻이다
        // (목적지에 닿았거나 상한 시간을 넘겼다). 다시 잡아서 이어 달린다.
        //
        // 마법사의 달아나기와 위기 도주가 여기 해당한다. 둘 다 "안전해질 때까지"가 조건이라
        // 한 번 물러나고 마는 것으로는 성립하지 않는다 — 여전히 쫓기는데 멈춰 서면
        // 그 자리에서 붙잡히고, 그것이 곧 도망과 복귀를 반복하는 버벅임이 된다.
        //
        // 거리 유지(뒷걸음)는 여기로 오지 않는다. 위쪽에서 이미 안전 판정을 통과했거나
        // ShouldKeepDistance가 거짓이 되어 돌아갔기 때문이다.
        if (runningAway || context.ShouldRetreatForSurvival())
        {
            Enter();
            return;
        }

        ReturnToCombat();
    }

    public override void Exit()
    {
        // 도약 도중에 끊겨도(피격, 사망) 이동 권한은 반드시 돌려줘야 한다.
        // 그러지 않으면 회피가 끝난 뒤에도 회피가 꺼져 있어 유닛이 서로 겹친다.
        context.EndDodgeMove();
        base.Exit();
    }

    // 재생 배속을 "요청한 속도"가 아니라 지금 실제로 나아가는 속도에 맞춘다.
    // NavMeshAgent는 출발할 때 가속하고 코너와 회피에서 느려지는데, 요청 속도로 배속을 잡으면
    // 그 구간마다 다리가 땅보다 빨리 움직인다.
    private void UpdateRetreatAnimation()
    {
        // 0으로 떨어지면 SetMoveAnimation이 대기 자세로 보내 출발 첫 프레임이 한 번 튄다.
        // 배속 하한(moveSpeedMultiplierRange)이 어차피 걸리므로 아주 작은 값으로 바닥을 깐다.
        float speed = Mathf.Max(context.CurrentMoveSpeed, 0.05f);

        // 뒷걸음일 때만 뒷걸음 모션이다. 달아나는 경우는 등을 보이고 달리므로 달리기 모션이 맞다.
        if (keepingDistance && !runningAway)
        {
            context.PlayRetreatAnimation(speed);
            return;
        }

        context.SetMoveAnimationFromGroundSpeed(true);
    }
}
