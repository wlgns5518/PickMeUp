using System.Collections.Generic;

// 보유 영웅 목록을 늘어놓는 순서. 편성 화면과 캐릭터 합성소가 같은 규칙을 쓴다.
public static class HeroOrder
{
    // 보유 영웅을 into에 담아, 고른 기준(성급 또는 레벨)이 높은 순으로 늘어놓는다.
    // 기준이 같으면 다른 쪽이 높은 순, 그래도 같으면 이름 순. fallenLast면 쓰러진 영웅을 맨 뒤로 보낸다.
    public static void SortOwned(List<CharacterSO> into, bool byLevel, bool fallenLast)
    {
        into.Clear();
        IReadOnlyList<CharacterSO> members = OwnedRoster.Members;
        for (int i = 0; i < members.Count; i++) if (members[i] != null) into.Add(members[i]);

        into.Sort((a, b) =>
        {
            if (fallenLast)
            {
                int byFallen = PartyRoster.IsFallen(a).CompareTo(PartyRoster.IsFallen(b));
                if (byFallen != 0) return byFallen;
            }
            int primary = byLevel ? b.Level.CompareTo(a.Level) : b.starCount.CompareTo(a.starCount);
            if (primary != 0) return primary;
            int secondary = byLevel ? b.starCount.CompareTo(a.starCount) : b.Level.CompareTo(a.Level);
            return secondary != 0 ? secondary : string.CompareOrdinal(HeroLabel.Name(a), HeroLabel.Name(b));
        });
    }
}
