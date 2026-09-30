using UnityEngine;

// UnitController의 "지휘관이 내린 명령을 받는" 입구. 명령의 값과 규칙은 CommandPart가 든다.
public partial class UnitController
{
    private CommandPart commandPart;

    // 도메인 리로드 뒤에는 Awake 없이 필드가 비므로 처음 물을 때 만든다(다른 부품도 같다).
    private CommandPart Command => commandPart ?? (commandPart = new CommandPart(this));

    public PartyOrder CommandOrder => Command.Order;
    public bool IsRetreatOrdered => Command.Order == PartyOrder.Retreat;
    public bool IsHoldOrdered => Command.Order == PartyOrder.Hold;

    // 후퇴할 곳, 또는 지킬 자리.
    public Vector3 CommandAnchor => Command.Anchor;

    public TargetRef CommandFocus => Command.Focus;

    public bool IsReturningToHoldAnchor => Command.IsReturningToHoldAnchor;

    public float HoldAnchorDistance => Command.HoldAnchorDistance;

    public bool IsBeyondHoldLeash => Command.IsBeyondHoldLeash;

    public void SetReturningToHoldAnchor(bool returning) => Command.SetReturningToHoldAnchor(returning);

    public void ReceiveFocusCommand(TargetRef target) => Command.ReceiveFocus(target);

    public void ClearFocusCommand() => Command.ClearFocus();

    public void ReceiveRetreatCommand(Vector3 destination) => Command.ReceiveRetreat(destination);

    public void ReceiveHoldCommand() => Command.ReceiveHold();

    public void ClearOrder() => Command.ClearOrder();

    public void CompleteRetreat() => Command.CompleteRetreat();

    public bool TryAcquireTargetInReach() => Command.TryAcquireTargetInReach();

    private void ResetCommandRuntime() => Command.Reset();

    private void TickCommand() => Command.Tick();

    private bool IsCommandBlockingRetarget(TargetRef candidate) => Command.IsBlockingRetarget(candidate);
}
