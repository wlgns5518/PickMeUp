using System.Collections.Generic;

// 캐릭터 성장과 영구 사망 칸. 로스터 명단을 쥔 쪽(전투 끝, 로스터 부트스트랩)만 적고 읽는다.
//
// 에셋이 아니라 런타임 진행도를 저장한다(CharacterProgress 주석 참조).
internal sealed class CharacterSaveSection
{
    private readonly CharacterProgressStore progress;
    private readonly FallenRecord fallenRecord;
    private readonly StressLedger stressLedger;

    public CharacterSaveSection(CharacterProgressStore progress, FallenRecord fallen, StressLedger stress)
    {
        this.progress = progress;
        this.fallenRecord = fallen;
        this.stressLedger = stress;
    }

    public void WriteTo(SaveData data, IReadOnlyList<CharacterSO> roster)
    {
        for (int i = 0; i < roster.Count; i++)
        {
            CharacterSO so = roster[i];
            if (so == null) continue;
            // 같은 에셋이 로스터에 여러 번 들어가 있을 수 있으므로 중복은 건너뛴다.
            if (Find(data.characters, so) != null) continue;

            CharacterProgressStore.Entry entry = progress.Of(so);

            var record = new CharacterRecord
            {
                id = so.Id,
                assetName = so.name,
                level = entry.Level,
                exp = entry.Exp,
                expToNext = entry.ExpToNext,
                strength = entry.Strength,
                intelligence = entry.Intelligence,
                vitality = entry.Vitality,
                agility = entry.Agility,
                fallen = fallenRecord.IsFallen(so),
                stress = stressLedger.Get(so),
                battlesFought = entry.BattlesFought,
            };
            record.skillIds.AddRange(entry.SkillIds);
            data.characters.Add(record);
        }
    }

    public void ReadFrom(SaveData data, IReadOnlyList<CharacterSO> roster)
    {
        for (int i = 0; i < roster.Count; i++)
        {
            CharacterSO so = roster[i];
            if (so == null) continue;

            CharacterRecord p = Find(data.characters, so);
            if (p == null) continue;

            progress.Restore(so, p.level, p.exp, p.expToNext,
                p.strength, p.intelligence, p.vitality, p.agility, p.skillIds);
            progress.RestoreBattlesFought(so, p.battlesFought);

            if (p.fallen) fallenRecord.MarkFallen(so);
            // 저장된 값은 저장 시점 기준이다. Set으로 넣으면 그 사이 흐른 자리비움 시간이
            // 통째로 버려지므로 반드시 Restore를 쓴다.
            stressLedger.Restore(so, p.stress);
        }
    }

    // 안정적 식별자로 먼저 찾고, 없으면 에셋 이름으로 되짚는다.
    // 두 번째 경로는 id가 도입되기 전에 쓰던 세이브(그 시절 id 칸에는 에셋 이름이 들어 있다)를
    // 그대로 읽기 위한 것이다. 새로 저장할 때 id가 채워지므로 한 번 저장하면 첫 경로로 넘어간다.
    private static CharacterRecord Find(List<CharacterRecord> list, CharacterSO character)
    {
        if (character == null) return null;

        string id = character.Id;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && !string.IsNullOrEmpty(list[i].id) && list[i].id == id) return list[i];
        }

        string assetName = character.name;
        for (int i = 0; i < list.Count; i++)
        {
            CharacterRecord record = list[i];
            if (record == null) continue;
            if (record.assetName == assetName || record.id == assetName) return record;
        }

        return null;
    }
}
