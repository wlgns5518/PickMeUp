using System;
using System.Collections.Generic;

// 지금 가지고 있는 캐릭터 전원 — 런타임 판.
//
// 입구일 뿐이다. 명단은 OwnedRosterStore가 들고, 명단에서 빠질 때 함께 치울 것(편성, 장비, 3D 몸)은
// GameServices가 Removed에 이어 둔다. 생성자로 의존성을 받을 수 있는 코드는 IOwnedRoster(GameServices.Roster)를 받는다.
public static class OwnedRoster
{
    private static OwnedRosterStore State => GameServices.RosterState;

    public static IReadOnlyList<CharacterSO> Members => State.Members;

    public static int Count => State.Count;

    // 명단이 바뀌면 카드 UI가 다시 그려야 한다.
    public static event Action Changed
    {
        add => State.Changed += value;
        remove => State.Changed -= value;
    }

    /// 시작 명단을 얹는다. 두 번 불려도 결과가 같다.
    public static void Seed(IReadOnlyList<CharacterSO> roster) => State.Seed(roster);

    public static bool Contains(CharacterSO character) => State.Contains(character);

    public static bool Add(CharacterSO character) => State.Add(character);

    /// 명단에서 뺀다. 합성 재료가 사라지는 통로다.
    public static bool Remove(CharacterSO character) => State.Remove(character);
}
