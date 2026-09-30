using System.Collections.Generic;

// 되돌릴 수 없는 죽음의 기록. 편성(PartyDeckStore)과 세이브가 "이 사람은 이제 못 쓴다"를 여기에 묻는다.
public interface IFallenRecord
{
    // 죽은 순서대로. UI가 "이번 층에서 잃은 캐릭터"를 보여줄 때 순서가 의미를 가진다.
    IReadOnlyList<CharacterSO> Fallen { get; }
    bool IsFallen(CharacterSO character);

    // 이미 기록된 캐릭터면 false. 중복 집계를 막는다.
    bool MarkFallen(CharacterSO character);
}

// 원작 픽미업에서 캐릭터의 죽음은 되돌릴 수 없다. 이 클래스가 그 규칙을 들고 있다.
//
// CharacterSO는 에셋이라 필드에 사망 플래그를 넣으면 에디터에서 플레이할 때마다
// 원본 에셋이 더럽혀진다(플레이 종료 후에도 죽은 채로 남는다). 그래서 사망 기록은 런타임에만 들고,
// 저장은 세이브(CharacterSaveSection)가 여기서 꺼내 간다.
public sealed class FallenRecord : IFallenRecord
{
    private readonly HashSet<CharacterSO> fallen = new HashSet<CharacterSO>();
    private readonly List<CharacterSO> fallenOrder = new List<CharacterSO>();

    public IReadOnlyList<CharacterSO> Fallen => fallenOrder;

    public bool IsFallen(CharacterSO character) => character != null && fallen.Contains(character);

    public bool MarkFallen(CharacterSO character)
    {
        if (character == null || !fallen.Add(character)) return false;

        fallenOrder.Add(character);
        return true;
    }

    public void Clear()
    {
        fallen.Clear();
        fallenOrder.Clear();
    }
}
