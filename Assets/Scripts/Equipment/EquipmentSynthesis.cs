using System.Collections.Generic;
using UnityEngine;

// 장비 합성 — 장비 세 점을 태워 한 점을 얻는다.
//
// 2026-09-22 사용자 결정: 등급은 따지지 않고 세 점을 넣으면 결과 등급은 세 장비 등급의 평균(내림)에서
// 한 단계 위다. S에서 멈춘다. 평균이 이미 S면 올라갈 곳이 없어 합성하지 않는다(세 점을 한 점으로 줄일 뿐이다).
//
// 결과 무기의 종류는 제작과 같은 규칙으로 정한다 — 재료 세 점 중 과반 계열(금속·목재·방패) 안에서 무작위,
// 셋이 모두 다르면 어떤 무기든(CraftRecipe.Majority). 강화 단계는 이어지지 않는다(결과는 +0).
// 누가 들고 있는 장비는 재료로 쓰지 못한다 — 합성 한 번에 영웅 손의 칼이 말없이 사라지면 안 된다.
public static class EquipmentSynthesis
{
    public const int SlotCount = 3;

    public static bool IsComplete(IReadOnlyList<OwnedEquipment> items) => items != null && items.Count == SlotCount;

    // 평균 등급(내림). 비어 있으면 E.
    public static EquipmentGrade AverageGrade(IReadOnlyList<OwnedEquipment> items)
    {
        if (items == null || items.Count == 0) return EquipmentGrade.E;

        int sum = 0;
        for (int i = 0; i < items.Count; i++) sum += (int)items[i].Grade;
        return (EquipmentGrade)(sum / items.Count);
    }

    public static EquipmentGrade ResultGrade(IReadOnlyList<OwnedEquipment> items) =>
        (EquipmentGrade)Mathf.Min((int)EquipmentGrade.S, (int)AverageGrade(items) + 1);

    public static WeaponFamily ResultFamily(IReadOnlyList<OwnedEquipment> items)
    {
        if (items == null || items.Count == 0) return WeaponFamily.Any;

        int metal = 0, wood = 0, shield = 0;
        for (int i = 0; i < items.Count; i++)
        {
            switch (CraftRecipe.FamilyOfWeapon(items[i].Weapon.type))
            {
                case WeaponFamily.Metal:  metal++; break;
                case WeaponFamily.Wood:   wood++; break;
                case WeaponFamily.Shield: shield++; break;
            }
        }
        return CraftRecipe.Majority(metal, wood, shield, items.Count);
    }

    public static long Cost(IReadOnlyList<OwnedEquipment> items) => GameEconomy.EquipmentSynthesisGold(ResultGrade(items));

    // 한 점을 재료로 넣을 수 있는지. 안 되면 이유(화면에 그대로 띄울 말)를 돌려준다.
    public static bool CanUse(OwnedEquipment item, out string reason)
    {
        reason = null;
        if (item == null || !EquipmentInventory.Contains(item))
        {
            reason = "무기창고에 없는 장비입니다.";
            return false;
        }
        if (item.IsEquipped)
        {
            reason = "장착 중인 장비는 재료로 쓸 수 없습니다.";
            return false;
        }
        return true;
    }

    // 골드를 빼기 전에 세 점이 합성할 수 있는 조합인지 본다.
    public static bool CanSynthesize(IReadOnlyList<OwnedEquipment> items, out string reason)
    {
        reason = null;
        if (!IsComplete(items))
        {
            reason = $"장비 {SlotCount}개를 모두 넣으세요.";
            return false;
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (!CanUse(items[i], out reason)) return false;
            for (int j = 0; j < i; j++)
            {
                if (items[i] != items[j]) continue;
                reason = "같은 장비를 두 번 넣을 수 없습니다.";
                return false;
            }
        }

        if (AverageGrade(items) >= EquipmentGrade.S)
        {
            reason = "평균이 이미 S등급이라 더 오를 수 없습니다.";
            return false;
        }
        return true;
    }

    /// 골드를 내고 세 점을 한 점으로 합친다. 못 하면 false와 이유 — 그때는 골드도 장비도 그대로다.
    public static bool TrySynthesize(IReadOnlyList<OwnedEquipment> items, out OwnedEquipment result, out string reason)
    {
        result = null;
        if (!CanSynthesize(items, out reason)) return false;

        // 나올 무기를 먼저 굴린다. 카탈로그가 비었으면 재료만 사라진다.
        WeaponDefinition weapon = CraftRecipe.RollWeapon(ResultFamily(items));
        if (weapon == null)
        {
            reason = "만들 수 있는 무기가 없습니다. WeaponCatalog를 확인하세요.";
            return false;
        }

        if (!PlayerAccount.TrySpend(Currency.Gold, Cost(items)))
        {
            reason = "골드가 부족합니다.";
            return false;
        }

        // 넣은 목록을 그대로 넘기면 부르는 쪽이 창고 변경 알림 도중에 목록을 비울 수 있다. 복사해 둔다.
        var consumed = new List<OwnedEquipment>(items);
        result = EquipmentInventory.ReplaceWith(consumed, weapon, ResultGrade(consumed));
        return true;
    }
}
