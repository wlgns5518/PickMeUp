using System;
using System.Collections.Generic;

// 시설 레벨 칸. 시설은 이름으로 적는다(FacilityRecord 주석 참조).
internal sealed class FacilitySaveSection : ISaveSection
{
    private readonly FacilityLevelStore store;

    public FacilitySaveSection(FacilityLevelStore store)
    {
        this.store = store;
    }

    public void WriteTo(SaveData data)
    {
        var levels = new List<KeyValuePair<VillageBlockout.Kind, int>>();
        store.Snapshot(levels);

        data.facilities = new List<FacilityRecord>();
        for (int i = 0; i < levels.Count; i++)
            data.facilities.Add(new FacilityRecord { kind = levels[i].Key.ToString(), level = levels[i].Value });
    }

    // 세이브가 없거나 깨졌으면 모든 시설이 1레벨이다.
    public void ReadFrom(SaveData data)
    {
        var restored = new List<KeyValuePair<VillageBlockout.Kind, int>>();

        if (data?.facilities != null)
        {
            for (int i = 0; i < data.facilities.Count; i++)
            {
                FacilityRecord record = data.facilities[i];
                if (record == null || !Enum.TryParse(record.kind, out VillageBlockout.Kind kind)) continue;
                restored.Add(new KeyValuePair<VillageBlockout.Kind, int>(kind, record.level));
            }
        }

        store.Restore(restored);
    }

    public void Forget() => store.Forget();
}
