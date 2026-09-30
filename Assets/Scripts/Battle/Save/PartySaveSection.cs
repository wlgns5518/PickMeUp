using System.Collections.Generic;

// 파티 편성 칸(PartyDeck). 되살릴 때 보유 명단이 있어야 해서 다른 창고류처럼 스스로 읽지 않는다 —
// 세션마다 한 번, 보유 명단을 세운 뒤에 불린다(RosterBootstrap).
internal sealed class PartySaveSection
{
    private readonly PartyDeckStore deck;

    public PartySaveSection(PartyDeckStore deck)
    {
        this.deck = deck;
    }

    public void WriteTo(SaveData data)
    {
        // 이번 세션에 편성을 아직 읽지 않았으면 파일에 있던 편성을 그대로 둔다. Patch는 파일에서 읽은 값을
        // 들고 오므로 손대지 않으면 되고, 새로 짜는 Save는 파일의 편성을 옮겨 담는다(아래).
        if (!deck.IsRestored)
        {
            if (data.party == null || data.party.parties == null || data.party.parties.Count == 0) CarryFromFile(data);
            return;
        }

        var record = new PartyDeckRecord { active = deck.ActiveIndex };
        for (int p = 0; p < PartyDeckStore.PartyCount; p++)
        {
            var party = new PartyRecord();
            IReadOnlyList<CharacterSO> members = deck.Party(p);
            for (int m = 0; m < members.Count; m++)
            {
                if (members[m] != null) party.members.Add(members[m].Id);
            }
            record.parties.Add(party);
        }

        data.party = record;
    }

    // 캐릭터는 보유 명단(owned)에서 Id로 찾는다. 합성 재료로 사라졌거나 명단에서 빠진 사람은 조용히 건너뛴다 —
    // 없는 사람을 붙들고 있으면 편성에 빈칸이 생기고 전투에 끌려 나갈 사람이 없다.
    // 세이브가 없거나 깨졌으면(data가 null) 빈 편성으로 시작한다.
    public void ReadFrom(SaveData data, IReadOnlyList<CharacterSO> owned)
    {
        var restored = new List<IReadOnlyList<CharacterSO>>(PartyDeckStore.PartyCount);
        int active = 0;

        PartyDeckRecord record = data?.party;
        if (record != null && record.parties != null)
        {
            active = record.active;
            for (int p = 0; p < record.parties.Count && p < PartyDeckStore.PartyCount; p++)
            {
                var members = new List<CharacterSO>();
                PartyRecord party = record.parties[p];
                if (party != null && party.members != null)
                {
                    for (int m = 0; m < party.members.Count; m++)
                    {
                        CharacterSO character = FindOwned(owned, party.members[m]);
                        if (character != null) members.Add(character);
                    }
                }
                restored.Add(members);
            }
        }

        deck.Restore(restored, active);
    }

    private static void CarryFromFile(SaveData data)
    {
        if (SaveFile.TryRead(out SaveData existing) && existing.party != null) data.party = existing.party;
    }

    private static CharacterSO FindOwned(IReadOnlyList<CharacterSO> owned, string id)
    {
        if (owned == null || string.IsNullOrEmpty(id)) return null;

        for (int i = 0; i < owned.Count; i++)
        {
            CharacterSO character = owned[i];
            if (character != null && character.Id == id) return character;
        }

        return null;
    }
}
