using System;
using System.Collections.Generic;

// 지금 가지고 있는 캐릭터 전원. 편성·합성·장비창 화면이 이것으로 본다.
public interface IOwnedRoster
{
    IReadOnlyList<CharacterSO> Members { get; }
    int Count { get; }
    bool Contains(CharacterSO character);
    bool Add(CharacterSO character);

    /// 명단에서 뺀다. 합성 재료가 사라지는 통로다. 딸린 것(편성 자리, 든 장비, 세워 둔 몸)은
    /// Removed를 듣는 쪽이 정리한다.
    bool Remove(CharacterSO character);

    event Action Changed;

    /// 명단에서 빠졌다. Changed보다 먼저 울린다 — 화면이 다시 그릴 때는 딸린 것까지 이미 정리돼 있어야 한다.
    event Action<CharacterSO> Removed;
}

// 보유 명단의 런타임 판.
//
// CharacterRosterSO는 에셋이라 "시작 명단 템플릿"으로만 두고, 실제 보유 목록은 여기서 굴린다.
// 소환으로 늘고 합성으로 줄어드는 목록을 에셋에 직접 쓰면 에디터에서 한 번 플레이할 때마다
// 원본 명단이 영구히 바뀐다.
//
// 보유 명단(여기) / 출전 편성(PartyDeckStore) / 사망 기록(FallenRecord)은 서로 다른 것이다.
// 이쪽은 "가진 캐릭터 전부", 저쪽은 "이번에 내보낼 사람", 나머지는 "다시는 못 쓰는 사람".
//
// 예전에는 Remove가 편성·무기창고·몸 굽기를 직접 불러 정리했다. 명단에서 빠질 때 치울 것이 하나 늘 때마다
// 이 클래스를 고쳐야 했다. 지금은 Removed를 울리기만 하고, 치울 쪽이 구독한다(GameServices가 잇는다).
public sealed class OwnedRosterStore : IOwnedRoster
{
    private readonly List<CharacterSO> members = new List<CharacterSO>();

    public IReadOnlyList<CharacterSO> Members => members;

    public int Count => members.Count;

    public event Action Changed;
    public event Action<CharacterSO> Removed;

    /// 시작 명단을 얹는다. 두 번 불려도 결과가 같도록 이미 있는 캐릭터는 건너뛴다
    /// (메인 씬과 전투 씬 양쪽에 RosterBootstrap이 하나씩 있다).
    public void Seed(IReadOnlyList<CharacterSO> roster)
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

    public bool Contains(CharacterSO character) => character != null && members.Contains(character);

    public bool Add(CharacterSO character)
    {
        if (character == null || members.Contains(character)) return false;

        members.Add(character);
        Changed?.Invoke();
        return true;
    }

    public bool Remove(CharacterSO character)
    {
        if (character == null || !members.Remove(character)) return false;

        Removed?.Invoke(character);
        Changed?.Invoke();
        return true;
    }
}
