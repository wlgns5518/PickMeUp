using System;
using System.Collections.Generic;
using UnityEngine;

// 지금 가지고 있는 캐릭터 전원 — 런타임 판.
//
// CharacterRosterSO는 에셋이라 "시작 명단 템플릿"으로만 두고, 실제 보유 목록은 여기서 굴린다.
// 소환으로 늘고 합성으로 줄어드는 목록을 에셋에 직접 쓰면 에디터에서 한 번 플레이할 때마다
// 원본 명단이 영구히 바뀐다. PartyRoster가 사망 기록을 런타임에만 들고 있는 것과 같은 이유다.
//
// 보유 명단(여기) / 출전 편성(PartyDeck) / 사망 기록(PartyRoster)은 서로 다른 것이다.
// 이쪽은 "가진 캐릭터 전부", 저쪽은 "이번에 내보낼 사람", 나머지는 "다시는 못 쓰는 사람".
public static class OwnedRoster
{
    private static readonly List<CharacterSO> members = new List<CharacterSO>();

    public static IReadOnlyList<CharacterSO> Members => members;

    public static int Count => members.Count;

    // 명단이 바뀌면 카드 UI가 다시 그려야 한다.
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 이전 플레이의 명단이 남지 않도록 비운다.
        members.Clear();
        // 씬과 함께 사라진 UI의 구독이 남아 있으면 죽은 참조를 계속 부르게 된다.
        Changed = null;
    }

    /// 시작 명단을 얹는다. 두 번 불려도 결과가 같도록 이미 있는 캐릭터는 건너뛴다
    /// (메인 씬과 전투 씬 양쪽에 RosterBootstrap이 하나씩 있다).
    public static void Seed(IReadOnlyList<CharacterSO> roster)
    {
        if (roster == null) return;

        bool changed = false;
        for (int i = 0; i < roster.Count; i++)
        {
            CharacterSO so = roster[i];
            if (so == null || members.Contains(so)) continue;

            members.Add(so);
            changed = true;
        }

        if (changed) Changed?.Invoke();
    }

    public static bool Contains(CharacterSO character) => character != null && members.Contains(character);

    public static bool Add(CharacterSO character)
    {
        if (character == null || members.Contains(character)) return false;

        members.Add(character);
        Changed?.Invoke();
        return true;
    }

    /// 명단에서 뺀다. 합성 재료가 사라지는 통로다.
    /// 편성에 올라가 있던 캐릭터면 거기서도 함께 뺀다 — 가지고 있지도 않은 사람이
    /// 출전 슬롯에 남아 있으면 전투에 그대로 끌려 나간다.
    public static bool Remove(CharacterSO character)
    {
        if (character == null || !members.Remove(character)) return false;

        PartyDeck.RemoveEverywhere(character);
        Changed?.Invoke();
        return true;
    }
}
