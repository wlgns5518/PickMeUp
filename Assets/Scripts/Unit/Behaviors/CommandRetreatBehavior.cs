using UnityEngine;
using UnityEngine.AI;

// 지휘관의 후퇴 명령. 하던 것을 접고 정해 준 자리까지 달린다. 닿으면 그 자리를 지킨다(진형 유지).
//
// 스스로 판단하는 후퇴(FleeBehavior·EvadeBehavior)와 다르다. 그쪽은 "누구에게서 멀어질까"를 매번
// 다시 재고 간격이 벌어지면 멈춘다. 이쪽은 갈 곳이 이미 정해져 있고 적이 쫓아와도 멈추지 않는다 —
// 지휘관이 전장 전체를 보고 물린 것이라, 한 사람이 눈앞의 거리만 재서 돌아서면 명령이 무너진다.
//
// 끊고 들어가는 일은 이 동작이 하지 않는다. 트리에서 자리가 반응 계층 바로 아래라, 명령이 선 다음 틱에
// 공격·추격·영창·치유가 알아서 접힌다(UnitBehaviorTree). 여기서 하는 것은 접힌 뒤의 몸을 정리하는 것뿐이다.
public class CommandRetreatBehavior : UnitBehavior
{
    // 이만큼 가까워지면 닿은 것으로 본다. 파티 전원이 같은 점으로 달리지 않으므로(PartyCommand가
    // 제자리 간격을 살려 흩어 준다) 넉넉해도 서로 밀치지 않는다.
    private const float ArrivalDistance = 0.9f;

    // 목적지를 다시 거는 간격. 목적지는 고정이라 자주 걸 이유가 없지만, 회피에 밀려 경로가
    // 틀어졌을 때 한 번씩 다시 잡아 준다.
    private const float RepathInterval = 0.5f;

    // 이만큼(초) 동안 목표에 조금도 더 다가가지 못하면 막힌 것으로 보고 그 자리를 도착으로 친다.
    //
    // 먼저 도착한 동료가 늦게 오는 동료의 목표 바로 옆에 서는 일이 흔하다 — 도착 판정이 0.9m라
    // 목표에서 그만큼 떨어져 멈출 수 있고, 몸 반경의 합(1m)이 그보다 크다. 그러면 늦은 쪽은 동료를
    // 밀며 제자리걸음만 하고, 후퇴 동작에 묶여 맞아도 반격하지 않은 채 "후퇴 중"이 끝나지 않았다
    // (플레이 테스트 3판 중 2판). 동료·적·지형 어느 것에 막혀도 같은 판정으로 풀린다.
    private const float StallTimeout = 1f;

    // 이만큼(미터)은 다가가야 나아간 것으로 친다. 지역 회피에 밀려 제자리에서 떠는 것은 나아간 게 아니다.
    private const float ProgressEpsilon = 0.05f;

    private float repathTimer;
    private float closestDistance;
    private float lastProgressTime;

    public CommandRetreatBehavior(UnitController context) : base(context)
    {
    }

    // 달아나는 중에 팀이 넘겨준 표적을 받았다고 돌아서면 안 된다.
    public override bool AcceptsCombatRedirect => false;

    protected override void OnEnter()
    {
        // 휘두르던 칼의 잠금과 들어 올린 방패를 내려놓는다. 트리가 접은 동작의 OnExit가 제 뒷정리
        // (영창 흩기, 도약 착지, 매달림 풀기)를 이미 마쳤으므로, 남은 것은 이 둘과 멈춰 있던 에이전트다.
        unit.InterruptCurrentAction();

        // 등을 보이고 달린다. 몸은 가는 쪽을 본다(에이전트에게 회전을 맡긴다).
        unit.SetCodeDrivenFacing(false);
        repathTimer = 0f;

        // 물려 굳었다가(경직) 다시 들어온 경우도 여기서 새로 잰다. 굳어 있던 시간을 막힌 시간으로 세면 안 된다.
        closestDistance = float.MaxValue;
        lastProgressTime = Time.time;
    }

    protected override BTStatus OnTick()
    {
        Vector3 toAnchor = unit.CommandAnchor - unit.transform.position;
        toAnchor.y = 0f;

        float distance = toAnchor.magnitude;
        if (distance < closestDistance - ProgressEpsilon)
        {
            closestDistance = distance;
            lastProgressTime = Time.time;
        }

        bool blocked = Time.time - lastProgressTime > StallTimeout;

        if (distance <= ArrivalDistance || blocked || IsStuck())
        {
            unit.StopMovement();
            unit.CompleteRetreat();
            return BTStatus.Success;
        }

        repathTimer -= Time.deltaTime;
        if (repathTimer <= 0f)
        {
            repathTimer = RepathInterval;
            unit.MoveTo(unit.CommandAnchor, unit.Stats.runSpeed, ArrivalDistance * 0.5f);
        }

        unit.SetMoveAnimationFromGroundSpeed(true);
        return BTStatus.Running;
    }

    // 갈 수 없는 자리다(경로가 끊겼다). 그 자리에서 버티는 편이 벽에 대고 달리는 것보다 낫다.
    private bool IsStuck()
    {
        NavMeshAgent agent = unit.Agent;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return true;
        return !agent.pathPending && agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathInvalid;
    }
}
