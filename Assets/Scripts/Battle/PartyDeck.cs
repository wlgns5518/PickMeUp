using System;
using System.Collections.Generic;

// 전투에 내보낼 파티 편성 — 1파티부터 3파티까지.
//
// 입구일 뿐이다. 편성 규칙(한 사람은 한 파티에만, 빈칸 없음, 잠긴 파티 …)은 PartyDeckStore가,
// 세이브와의 연결은 GameServices가 든다. 생성자로 의존성을 받을 수 있는 코드는 IPartyDeck(GameServices.Deck)을 받는다.
public static class PartyDeck
{
    public const int PartyCount = PartyDeckStore.PartyCount;
    public const int DefaultCapacity = PartyDeckStore.DefaultCapacity;

    private static PartyDeckStore State => GameServices.DeckState;

    public static int ActiveIndex => State.ActiveIndex;
    public static IReadOnlyList<CharacterSO> Members => State.Members;
    public static int Count => State.Count;
    public static int Capacity => State.Capacity;
    public static bool IsFull => State.IsFull;
    public static bool IsRestored => State.IsRestored;

    // 편성이 바뀌면 카드 UI가 다시 그려야 한다.
    public static event Action Changed
    {
        add => State.Changed += value;
        remove => State.Changed -= value;
    }

    public static void Restore(IReadOnlyList<IReadOnlyList<CharacterSO>> saved, int activeIndex) =>
        State.Restore(saved, activeIndex);

    public static IReadOnlyList<CharacterSO> Party(int index) => State.Party(index);
    public static int CountOf(int index) => State.CountOf(index);
    public static void SetActive(int index) => State.SetActive(index);
    public static int PartyIndexOf(CharacterSO character) => State.PartyIndexOf(character);
    public static bool IsInOtherParty(CharacterSO character) => State.IsInOtherParty(character);
    public static void SetCapacity(int capacity) => State.SetCapacity(capacity);
    public static bool Contains(CharacterSO character) => State.Contains(character);
    public static int IndexOf(CharacterSO character) => State.IndexOf(character);
    public static bool Add(CharacterSO character) => State.Add(character);
    public static bool Remove(CharacterSO character) => State.Remove(character);
    public static bool PlaceAt(int index, CharacterSO character) => State.PlaceAt(index, character);
    public static bool RemoveAt(int index) => State.RemoveAt(index);
    public static void Clear() => State.Clear();
    public static bool RemoveEverywhere(CharacterSO character) => State.RemoveEverywhere(character);
    public static void PruneFallen() => State.PruneFallen();
}
