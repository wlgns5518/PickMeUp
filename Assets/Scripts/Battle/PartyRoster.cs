using System.Collections.Generic;
using UnityEngine;

// 원작 픽미업에서 캐릭터의 죽음은 되돌릴 수 없다. 이 클래스가 그 규칙을 들고 있다.
//
// CharacterSO는 에셋이라 필드에 사망 플래그를 넣으면 에디터에서 플레이할 때마다
// 원본 에셋이 더럽혀진다(플레이 종료 후에도 죽은 채로 남는다).
// 그래서 사망 기록은 런타임 전용 컬렉션으로만 들고 있고, 저장이 필요해지면
// 여기서 Fallen 목록을 꺼내 세이브 데이터에 넣으면 된다.
public static class PartyRoster
{
    private static readonly HashSet<CharacterSO> fallen = new HashSet<CharacterSO>();
    private static readonly List<CharacterSO> fallenOrder = new List<CharacterSO>();

    // 죽은 순서대로. UI가 "이번 층에서 잃은 캐릭터"를 보여줄 때 순서가 의미를 가진다.
    public static IReadOnlyList<CharacterSO> Fallen => fallenOrder;

    public static int FallenCount => fallenOrder.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 이전 플레이의 사망 기록이 남지 않도록 비운다.
        Clear();
    }

    public static bool IsFallen(CharacterSO character)
    {
        return character != null && fallen.Contains(character);
    }

    // 이미 기록된 캐릭터면 false. 중복 집계를 막는다.
    public static bool MarkFallen(CharacterSO character)
    {
        if (character == null || !fallen.Add(character)) return false;

        fallenOrder.Add(character);
        return true;
    }

    public static void Clear()
    {
        fallen.Clear();
        fallenOrder.Clear();
    }
}
