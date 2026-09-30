using System;

// 지휘관이 파티 전체에 내리는 명령.
public enum PartyOrder : byte
{
    // 명령 없음. 각자 판단대로 싸운다.
    Engage,

    // 정해 준 자리까지 물러난다. 닿으면 그 자리를 지킨다(Hold로 넘어간다).
    Retreat,

    // 지금 선 자리를 지킨다. 손 닿는 적과만 싸우고 쫓아 나가지 않는다.
    Hold,
}

// 지휘관(플레이어)의 명령을 파티원 한 명 한 명에게 나눠 주는 자리.
//
// 원작의 지휘관은 파티원을 조종하지 않는다. "저놈부터", "물러나", "버텨"를 외칠 뿐이고 칼을 어떻게
// 휘두를지는 각자가 정한다. 그래서 여기서는 동작을 지목하지 않고 값만 내려보낸다 — 집중 표적은
// 각자의 주 표적(UnitController.CurrentTarget) 자리에, 후퇴·진형은 각자의 명령 칸에 들어가고,
// 그 칸을 보고 무엇을 할지는 행동 트리의 우선순위가 정한다(UnitBehaviorTree).
//
// 이 클래스는 입구다. 입력(PartyCommandInput)·HUD·유닛이 서로를 찾지 않고 같은 값을 보도록
// 정적 이름을 남겨 두고, 실제 상태와 규칙은 GameServices.Command(PartyCommander)가 들고 있다.
public static class PartyCommand
{
    public static PartyOrder Order => GameServices.Command.Order;

    // 지휘관이 찍은 표적. 쓰러지면 스스로 풀린다(Tick).
    public static TargetRef FocusTarget => GameServices.Command.FocusTarget;

    // 명령이 바뀌었다. HUD가 글자를 다시 쓰는 데만 쓴다 — 매 프레임 문자열을 만들지 않으려고.
    public static event Action Changed
    {
        add => GameServices.Command.Changed += value;
        remove => GameServices.Command.Changed -= value;
    }

    // 전투를 새로 열 때. 지난 판의 명령이 남아 있으면 첫 프레임부터 파티가 물러난다.
    public static void Reset() => GameServices.Command.Reset();

    // 집중 공격. 파티원 전원의 주 표적을 이 적으로 갈아 끼운다.
    public static bool FocusFire(TargetRef target) => GameServices.Command.FocusFire(target);

    public static void ClearFocus() => GameServices.Command.ClearFocus();

    // 긴급 후퇴. 적의 무게중심 반대쪽으로 distance만큼 물러난 자리를 파티의 집결지로 삼는다.
    public static bool OrderRetreat(float distance) => GameServices.Command.OrderRetreat(distance);

    // 진형 유지. 각자 지금 선 자리를 지킨다.
    public static bool OrderHold() => GameServices.Command.OrderHold();

    // 명령을 거두고 각자 판단으로 돌려보낸다. 집중 표적은 그대로 둔다.
    public static void Resume() => GameServices.Command.Resume();

    // 표적이 쓰러졌으면 집중을 풀고, 후퇴가 전원 끝났으면 진형 유지로 넘긴다.
    public static void Tick() => GameServices.Command.Tick();
}
