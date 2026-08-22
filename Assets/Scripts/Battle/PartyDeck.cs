using System;
using System.Collections.Generic;
using UnityEngine;

// 전투에 내보낼 파티 편성 — 1파티부터 3파티까지.
//
// 보유 명단(CharacterRosterSO)이 "가진 캐릭터 전부"라면 이쪽은 "누구를 어느 파티에 넣었는지"다.
// 결과창의 "1파티가 전멸했습니다" 문구가 가리키는 그 파티 번호이기도 하다.
//
// 한 캐릭터는 한 파티에만 들어갈 수 있다. 파티는 순차로 내보내는 개념이라
// 같은 사람이 두 파티에 들어 있으면 한 판에 두 번 싸우는 셈이 된다.
//
// 다른 파티에 있는 캐릭터는 이쪽에서 받지 않는다 — 그 파티에서 직접 뺀 다음에만 넣을 수 있다.
// 자동으로 옮겨 주면 2파티를 만지다가 1파티가 조용히 헐거워지고, 언제 빠졌는지 알 수 없다.
//
// 메인 씬에서 고른 결과를 전투 씬까지 들고 가야 하므로 FloorProgress와 같은 static으로 둔다.
public static class PartyDeck
{
    public const int PartyCount = 3;
    public const int DefaultCapacity = 5;

    private static readonly List<CharacterSO>[] parties = CreateParties();

    // 지금 편성 화면에서 만지고 있고, 층에 들어갈 때 출전하는 파티.
    public static int ActiveIndex { get; private set; }

    // 활성 파티의 명단. 고른 순서대로 — 스포너가 이 순서로 스폰 지점을 배정한다.
    public static IReadOnlyList<CharacterSO> Members => parties[ActiveIndex];

    public static int Count => parties[ActiveIndex].Count;

    public static int Capacity { get; private set; } = DefaultCapacity;

    public static bool IsFull => Count >= Capacity;

    // 편성이 바뀌면 카드 UI가 다시 그려야 한다.
    public static event Action Changed;

    private static List<CharacterSO>[] CreateParties()
    {
        var created = new List<CharacterSO>[PartyCount];
        for (int i = 0; i < PartyCount; i++) created[i] = new List<CharacterSO>();
        return created;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 이전 플레이의 편성이 남지 않도록 비운다.
        for (int i = 0; i < parties.Length; i++) parties[i].Clear();
        ActiveIndex = 0;
        Capacity = DefaultCapacity;
        // 씬과 함께 사라진 UI의 구독이 남아 있으면 죽은 참조를 계속 부르게 된다.
        Changed = null;
    }

    // 1파티는 0, 2파티는 1 ... 화면에 보이는 번호는 +1이다.
    public static IReadOnlyList<CharacterSO> Party(int index) => parties[Clamp(index)];

    public static int CountOf(int index) => parties[Clamp(index)].Count;

    public static void SetActive(int index)
    {
        int clamped = Clamp(index);
        if (clamped == ActiveIndex) return;

        ActiveIndex = clamped;
        Changed?.Invoke();
    }

    // 이 캐릭터가 들어 있는 파티 번호. 어디에도 없으면 -1.
    public static int PartyIndexOf(CharacterSO character)
    {
        if (character == null) return -1;

        for (int i = 0; i < parties.Length; i++)
            if (parties[i].Contains(character)) return i;

        return -1;
    }

    // 활성 파티가 아닌 다른 파티에 이미 들어 있는지. UI가 "왜 안 들어가는지" 알려줄 때 쓴다.
    public static bool IsInOtherParty(CharacterSO character)
    {
        int party = PartyIndexOf(character);
        return party >= 0 && party != ActiveIndex;
    }

    public static void SetCapacity(int capacity)
    {
        Capacity = Mathf.Max(1, capacity);

        bool trimmed = false;
        for (int i = 0; i < parties.Length; i++)
        {
            List<CharacterSO> party = parties[i];
            while (party.Count > Capacity)
            {
                party.RemoveAt(party.Count - 1);
                trimmed = true;
            }
        }
        if (trimmed) Changed?.Invoke();
    }

    public static bool Contains(CharacterSO character) => character != null && parties[ActiveIndex].Contains(character);

    // 활성 파티에서 몇 번째로 골랐는지. 고르지 않았으면 -1.
    public static int IndexOf(CharacterSO character) => character == null ? -1 : parties[ActiveIndex].IndexOf(character);

    public static bool Add(CharacterSO character)
    {
        if (character == null || IsFull || Contains(character)) return false;

        // 영구 사망한 캐릭터는 스포너가 어차피 걸러낸다.
        // 여기서 막지 않으면 "5명을 채웠는데 4명만 나가는" 편성이 만들어진다.
        if (PartyRoster.IsFallen(character)) return false;

        // 다른 파티에 있으면 받지 않는다. 그 파티에서 직접 빼야 한다.
        if (IsInOtherParty(character)) return false;

        parties[ActiveIndex].Add(character);
        Changed?.Invoke();
        return true;
    }

    public static bool Remove(CharacterSO character)
    {
        if (character == null || !parties[ActiveIndex].Remove(character)) return false;

        Changed?.Invoke();
        return true;
    }

    // 드래그해서 특정 자리에 떨어뜨렸을 때. 새 캐릭터면 그 자리에 끼워 넣고,
    // 이미 이 파티에 있던 캐릭터면 그 자리로 순서를 옮긴다.
    //
    // 명단에 빈칸을 허용하지 않는 이유: 스포너가 순서대로 스폰 지점을 배정하고
    // 세이브/결과 정산도 이 목록을 그대로 훑기 때문에, 중간에 null이 끼면 전부 예외 처리가 붙는다.
    // 빈 슬롯에 떨어뜨리면 뒤에 붙는 것으로 충분하다.
    public static bool PlaceAt(int index, CharacterSO character)
    {
        if (character == null || PartyRoster.IsFallen(character)) return false;

        List<CharacterSO> party = parties[ActiveIndex];
        int current = party.IndexOf(character);
        if (current >= 0)
        {
            int target = Mathf.Clamp(index, 0, party.Count - 1);
            if (target == current) return false;

            party.RemoveAt(current);
            party.Insert(target, character);
            Changed?.Invoke();
            return true;
        }

        if (IsFull) return false;

        // 다른 파티에 있으면 받지 않는다. 그 파티에서 직접 빼야 한다.
        if (IsInOtherParty(character)) return false;

        party.Insert(Mathf.Clamp(index, 0, party.Count), character);
        Changed?.Invoke();
        return true;
    }

    public static bool RemoveAt(int index)
    {
        List<CharacterSO> party = parties[ActiveIndex];
        if (index < 0 || index >= party.Count) return false;

        party.RemoveAt(index);
        Changed?.Invoke();
        return true;
    }

    // 카드 클릭 한 번으로 넣고 뺀다. 반환값은 클릭 뒤에 활성 파티에 들어 있는지 여부다.
    public static bool Toggle(CharacterSO character)
    {
        if (Contains(character))
        {
            Remove(character);
            return false;
        }
        return Add(character);
    }

    // 활성 파티만 비운다.
    public static void Clear()
    {
        if (parties[ActiveIndex].Count == 0) return;

        parties[ActiveIndex].Clear();
        Changed?.Invoke();
    }

    public static void ClearAll()
    {
        bool changed = false;
        for (int i = 0; i < parties.Length; i++)
        {
            if (parties[i].Count == 0) continue;
            parties[i].Clear();
            changed = true;
        }
        if (changed) Changed?.Invoke();
    }

    // 어느 파티에 있든 통째로 뺀다. 합성 재료처럼 캐릭터 자체가 사라질 때 쓴다 —
    // 활성 파티만 보는 Remove로는 2파티에 얹어둔 재료가 그대로 남는다.
    public static bool RemoveEverywhere(CharacterSO character)
    {
        if (character == null) return false;

        bool removed = false;
        for (int i = 0; i < parties.Length; i++)
            removed |= parties[i].Remove(character);

        if (removed) Changed?.Invoke();
        return removed;
    }

    // 전투에서 돌아왔을 때 죽은 채로 편성에 남아 있는 캐릭터를 걷어낸다. 세 파티 모두.
    public static void PruneFallen()
    {
        int removed = 0;
        for (int i = 0; i < parties.Length; i++) removed += parties[i].RemoveAll(PartyRoster.IsFallen);
        if (removed > 0) Changed?.Invoke();
    }

    private static int Clamp(int index) => Mathf.Clamp(index, 0, PartyCount - 1);
}
