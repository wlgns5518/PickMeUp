using System.Collections.Generic;
using UnityEngine;

// 무기창고 칸 — 제작한 장비와 누가 무엇을 들었는지.
internal sealed class EquipmentSaveSection : ISaveSection
{
    private readonly EquipmentStore store;

    public EquipmentSaveSection(EquipmentStore store)
    {
        this.store = store;
    }

    public void WriteTo(SaveData data)
    {
        data.equipment = new List<EquipmentRecord>();

        IReadOnlyList<OwnedEquipment> items = store.Items;
        for (int i = 0; i < items.Count; i++)
        {
            OwnedEquipment item = items[i];
            if (item == null || item.Weapon == null) continue;

            data.equipment.Add(new EquipmentRecord
            {
                weapon = item.Weapon.name,
                grade = item.Grade,
                owner = item.OwnerId,
                level = item.Level,
            });
        }
    }

    public void ReadFrom(SaveData data)
    {
        var restored = new List<OwnedEquipment>();

        if (data?.equipment != null)
        {
            for (int i = 0; i < data.equipment.Count; i++)
            {
                EquipmentRecord record = data.equipment[i];
                if (record == null) continue;

                WeaponDefinition weapon = WeaponCatalog.Find(record.weapon);
                if (weapon == null)
                {
                    Debug.LogWarning($"[SaveSystem] 무기창고의 '{record.weapon}'을(를) 카탈로그에서 찾지 못해 건너뜁니다.");
                    continue;
                }

                restored.Add(new OwnedEquipment(weapon, record.grade, record.owner, record.level));
            }
        }

        store.Restore(restored);
    }

    public void Forget() => store.Forget();
}
