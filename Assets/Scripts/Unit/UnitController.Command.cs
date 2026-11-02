using UnityEngine;

// UnitController의 "지휘관이 내린 명령을 받는" 입구. 명령의 값과 규칙은 CommandPart가 든다.
public partial class UnitController
{
    private CommandPart commandPart;

    // 도메인 리로드 뒤에는 Awake 없이 필드가 비므로 처음 물을 때 만든다(다른 부품도 같다).
    private CommandPart Command => commandPart ?? (commandPart = new CommandPart(this));

    public bool IsRetreatOrdered => Command.Order == PartyOrder.Retreat;
    public bool IsHoldOrdered => Command.Order == PartyOrder.Hold;

    // 후퇴할 곳, 또는 지킬 자리.
    public Vector3 CommandAnchor => Command.Anchor;

    // 자리를 지키며 싸우는 중인가 — 진형 유지 명령이거나, 스스로 고른 지형 자리(TerrainPart)다.
    public bool IsHoldingPosition => Command.IsHoldingPosition;

    // 지금 지키는 자리. 명령의 자리가 먼저다.
    public Vector3 HoldAnchor => Command.HoldAnchor;

    // 스스로 고른 싸울 자리가 있는가(11층부터, 건물이 선 전장).
    public bool HasTerrainAnchor => Command.HasTerrainAnchor;

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

    private void ResetCommandRuntime()
    {
        Command.Reset();
        Terrain.Reset();
    }

    // 받아 둔 집중 표적을 적용하고, 명령이 없으면 스스로 싸울 자리를 고른다(TerrainPart, 11층부터).
    private void TickCommand()
    {
        Command.Tick();
        Terrain.Tick();
    }

    private TerrainPart terrainPart;
    private TerrainPart Terrain => terrainPart ?? (terrainPart = new TerrainPart(this));

    private bool IsCommandBlockingRetarget(TargetRef candidate) => Command.IsBlockingRetarget(candidate);
}
