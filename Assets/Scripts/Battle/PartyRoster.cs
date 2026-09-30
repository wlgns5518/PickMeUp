using System.Collections.Generic;

// 원작 픽미업에서 캐릭터의 죽음은 되돌릴 수 없다.
//
// 입구일 뿐이다. 기록은 FallenRecord가 든다. 생성자로 의존성을 받을 수 있는 코드는 IFallenRecord(GameServices.Fallen)를 받는다.
public static class PartyRoster
{
    private static FallenRecord State => GameServices.FallenState;

    // 죽은 순서대로. UI가 "이번 층에서 잃은 캐릭터"를 보여줄 때 순서가 의미를 가진다.
    public static IReadOnlyList<CharacterSO> Fallen => State.Fallen;

    public static bool IsFallen(CharacterSO character) => State.IsFallen(character);

    // 이미 기록된 캐릭터면 false. 중복 집계를 막는다.
    public static bool MarkFallen(CharacterSO character) => State.MarkFallen(character);

    public static void Clear() => State.Clear();
}
