using UnityEngine;
using Random = UnityEngine.Random;

// UnitController의 "지휘관이 내린 명령을 받는" 쪽.
//
// 원작의 한이스는 파티원을 하나하나 조종하지 않는다. 쿨타임과 적의 모션을 읽고 "저놈부터", "물러나",
// "버텨"를 외칠 뿐이고, 칼을 어떻게 휘두를지는 각자가 정한다. 그래서 명령은 동작을 지목하지 않는다 —
// 이 파일은 값(명령 종류, 지킬 자리, 집중 표적)만 들고 있고, 그 값을 보고 무엇을 할지는 행동 트리의
// 우선순위가 정한다(UnitBehaviorTree). 후퇴 가지가 반응 계층 바로 아래에 있으므로 명령이 서는 다음
// 틱에 공격·추격·영창이 접히고, 뒷정리(방패 내리기, 공중에 뜬 몸 내려놓기, 영창 흩기)는 각 동작의
// OnExit가 평소처럼 한다.
//
// 반응은 명령보다 위다. 무너져 있는 유닛은 후퇴 명령을 들어도 일어나기 전에는 달리지 못한다.
public partial class UnitController
{
    // 진형을 지키는 동안 손 닿는 적을 다시 찾는 간격(초). 매 프레임 훑지 않는다 —
    // 이 질의는 엔티티 1000마리를 선형으로 훑는다(EnemyWorldBridge.AccumulateEnemyInArc).
    private const float HoldRetargetInterval = 0.25f;

    private PartyOrder commandOrder = PartyOrder.Engage;
    private Vector3 commandAnchor;

    // 지휘관이 찍은 표적. 쓰러질 때까지 다른 표적 판단(스캐너·어그로·공유 표적)이 덮지 못한다.
    private TargetRef commandFocus;

    // 받았지만 아직 갈아타지 못했다. 이미 나간 칼이나 잠긴 동작이 끝나는 틈에 넘어간다.
    private bool commandFocusPending;

    private float nextHoldRetargetTime;

    // 진형을 지키며 싸우는 동안 자리에서 밀려나도 되는 거리(미터). 넘으면 스윙을 마치는 대로 돌아간다.
    //
    // 싸우는 중에는 제자리에 못 박히지 않는다 — 발놀림이 간격을 재고, 넉백(0.6m)과 즉시 밀림이 몸을 민다.
    // 그걸 전부 무시하면 발이 미끄러지고, 전부 받아 주면 진형이 녹는다(실측: 고블린 스물넷에게 둘러싸인
    // 채 버티라고 했더니 싸우는 동안 제자리에서 3.8m 밀려나 그대로 머물렀다).
    private const float HoldLeash = 1.5f;

    // 자리로 돌아가는 중인가. 다 돌아올 때까지 싸움으로 넘어가지 않는다 — 경계 거리에서 돌아가다
    // 싸우다를 매 틱 오가지 않게 하는 문턱이다. 동작이 아니라 유닛이 들고 있는 이유는, 돌아가다
    // 피격 리액션에 끊겨도 일어나서 이어 돌아가야 하기 때문이다.
    private bool holdReturning;

    public PartyOrder CommandOrder => commandOrder;
    public bool IsRetreatOrdered => commandOrder == PartyOrder.Retreat;
    public bool IsHoldOrdered => commandOrder == PartyOrder.Hold;

    // 후퇴할 곳, 또는 지킬 자리.
    public Vector3 CommandAnchor => commandAnchor;

    public TargetRef CommandFocus => commandFocus;

    // 돌아가는 데 이보다 오래 걸리면 거기서 멈추고 싸운다(초). 자리가 적에게 둘러싸여 닿지 못하는 채로
    // 맞기만 하면서 걸어가는 것보다 그 자리에서 칼을 드는 편이 낫다.
    private const float MaxHoldReturnTime = 2.5f;
    private float holdReturnStartTime = -999f;

    public bool IsReturningToHoldAnchor => holdReturning && Time.time < holdReturnStartTime + MaxHoldReturnTime;

    // 지킬 자리에서 떨어진 거리(높이 제외).
    public float HoldAnchorDistance
    {
        get
        {
            Vector3 offset = commandAnchor - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }
    }

    // 붙잡는 거리를 넘었는가. 돌아가기를 포기한 직후(MaxHoldReturnTime)에는 한동안 다시 묻지 않는다 —
    // 안 그러면 포기한 다음 틱에 또 돌아가기 시작해 같은 자리를 맴돈다.
    public bool IsBeyondHoldLeash =>
        HoldAnchorDistance > HoldLeash && Time.time >= holdReturnStartTime + MaxHoldReturnTime * 2f;

    public void SetReturningToHoldAnchor(bool returning)
    {
        if (returning && !holdReturning) holdReturnStartTime = Time.time;
        holdReturning = returning;
    }

    // ---------------------------------------------------------------- 명령을 받는다

    // 집중 공격. 블랙보드의 주 표적(CurrentTarget)을 지휘 표적으로 갈아 끼운다.
    //
    // 갈아 끼우는 것 자체는 곧바로다. 다만 이미 내지른 칼(IsAttackAnimationLocked)과 몸을 던진 동작
    // (스킬·도약·빠지기 — UnitBehavior.YieldsTargetToCommand)은 끝까지 가게 둔다. 그 사이에 표적을 바꾸면
    // 휘두르던 칼이 엉뚱한 쪽을 베고, 매달려 물던 목을 놓친다. 그 틈은 대개 한 스윙(0.3~0.7초)이다.
    public void ReceiveFocusCommand(TargetRef target)
    {
        if (IsDead || !target.IsAlive || !IsHostileTo(target)) return;

        commandFocus = target;
        commandFocusPending = true;
        TryApplyCommandFocus();
    }

    public void ClearFocusCommand()
    {
        commandFocus = TargetRef.None;
        commandFocusPending = false;
    }

    // 후퇴. 하던 것을 접고 destination까지 달린다. 닿으면 그 자리에서 진형을 지킨다.
    public void ReceiveRetreatCommand(Vector3 destination)
    {
        if (IsDead) return;

        commandOrder = PartyOrder.Retreat;
        commandAnchor = destination;
    }

    // 진형 유지. 지금 선 자리를 지킨다. 손 닿는 적과는 싸우지만 쫓아 나가지 않는다.
    public void ReceiveHoldCommand()
    {
        if (IsDead) return;

        commandOrder = PartyOrder.Hold;
        commandAnchor = transform.position;
        nextHoldRetargetTime = 0f;
        holdReturning = false;
        holdReturnStartTime = -999f;
    }

    // 명령을 거두고 각자 판단으로 돌아간다(집중 표적은 따로 거둔다).
    public void ClearOrder()
    {
        commandOrder = PartyOrder.Engage;
        holdReturning = false;
        holdReturnStartTime = -999f;
    }

    // 후퇴 지점에 닿았다. 그 자리가 곧 지킬 자리다 — 원작의 "퇴각 → 보호 → 포격" 리듬에서
    // 퇴각 다음은 다시 달려드는 것이 아니라 버티는 것이다.
    public void CompleteRetreat()
    {
        if (commandOrder != PartyOrder.Retreat) return;

        commandOrder = PartyOrder.Hold;
        commandAnchor = transform.position;
        nextHoldRetargetTime = 0f;
        holdReturning = false;
        holdReturnStartTime = -999f;
    }

    private void ResetCommandRuntime()
    {
        commandOrder = PartyOrder.Engage;
        commandAnchor = Vector3.zero;
        commandFocus = TargetRef.None;
        commandFocusPending = false;
        nextHoldRetargetTime = 0f;
        holdReturning = false;
        holdReturnStartTime = -999f;
    }

    // ---------------------------------------------------------------- 매 프레임

    // 트리보다 먼저 돈다(Update). 받아 둔 집중 표적을 틈이 나는 대로 적용한다.
    private void TickCommand()
    {
        if (commandFocus.Exists && !commandFocus.IsAlive) ClearFocusCommand();
        TryApplyCommandFocus();
    }

    private void TryApplyCommandFocus()
    {
        if (!commandFocusPending) return;

        if (!commandFocus.IsAlive)
        {
            ClearFocusCommand();
            return;
        }

        if (CurrentTarget == commandFocus && IsTargetValid())
        {
            commandFocusPending = false;
            return;
        }

        if (!CanTakeCommandTarget()) return;

        AssignTarget(commandFocus);
        ClearMoveDestination();
        commandFocusPending = false;

        // 공격 중이었다면 접지 않아도 된다 — 새 표적이 사거리 밖이면 공격 가지의 조건이 무너져
        // 트리가 알아서 추격으로 넘어간다. 나머지(추격·배회)는 접어 새 표적을 놓고 다시 고르게 한다.
        UnitBehavior current = RunningBehavior;
        if (current == null || current.AcceptsCombatRedirect) InterruptBehavior();
    }

    // 지금 명령이 준 표적으로 갈아탈 수 있는가. 이미 나간 칼과, 몸을 던진 동작(스킬·도약·빠지기)만 기다린다.
    private bool CanTakeCommandTarget()
    {
        if (IsAttackAnimationLocked) return false;

        UnitBehavior current = RunningBehavior;
        return current == null || current.YieldsTargetToCommand;
    }

    // 다른 표적 판단이 지금 표적을 덮어도 되는가.
    //
    // 지휘 표적을 치는 중이면 안 된다. 이게 없으면 스캐너의 어그로 재평가나 맞은 쪽으로 돌아서기가
    // 한 틱 만에 명령을 지워 버린다 — 집중 사격은 흔들리지 않는 것이 전부다.
    // 진형을 지키는 중에는 손 닿는 표적으로만 갈아탄다(멀리 있는 적을 쫓아 나가지 않게).
    private bool IsCommandBlockingRetarget(TargetRef candidate)
    {
        if (commandFocus.IsAlive && CurrentTarget == commandFocus && candidate != commandFocus) return true;
        if (commandOrder == PartyOrder.Hold && candidate.Exists && !IsWithinStrikeReach(candidate.Position)) return true;
        return false;
    }

    private bool IsWithinStrikeReach(Vector3 position)
    {
        float reach = stats.attackRange + stats.moveStopDistance;
        Vector3 offset = position - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= reach * reach;
    }

    // ---------------------------------------------------------------- 진형 유지 중의 표적

    // 지킬 자리에서 칼이 닿는 적을 고른다. 돌려주는 값은 "지금 싸울 상대가 손 닿는 데 있는가".
    //
    // 지휘 표적이 닿으면 그것이 먼저다. 없으면 지금 표적을, 그것도 닿지 않으면 가장 가까운 적을 고른다.
    // 궁수·마법사는 사거리가 길어서 진형을 지키면서도 멀리 쏜다 — 원작의 "전열이 버티는 동안 후방이 포격한다"다.
    public bool TryAcquireTargetInReach()
    {
        if (commandFocus.IsAlive && IsWithinStrikeReach(commandFocus.Position))
        {
            if (CurrentTarget != commandFocus && CanTakeCommandTarget()) AssignTarget(commandFocus);

            return CurrentTarget == commandFocus;
        }

        if (IsTargetValid() && IsTargetInAttackRange()) return true;

        if (Time.time < nextHoldRetargetTime) return false;
        nextHoldRetargetTime = Time.time + HoldRetargetInterval * Random.Range(0.8f, 1.2f);

        if (!CanTakeCommandTarget()) return false;

        TargetRef nearest = UnitRegistry.FindEnemyInArc(this, stats.attackRange + stats.moveStopDistance, 360f);
        if (!nearest.Exists) return false;

        AssignTarget(nearest);
        return true;
    }
}
