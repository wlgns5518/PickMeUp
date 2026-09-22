using System.Collections.Generic;
using UnityEngine;

// 넣은 재료로 무엇이 나올지 정하는 규칙.
//
// 제작소에는 재료 세 개를 넣는다. 무엇을 만들지는 고르지 않는다 — 재료가 방향만 정하고 나머지는 운이다.
//
//   계열: 가장 많이 넣은 재료 종류가 정한다. 세 개 중 두 개 이상이 같은 종류면 그 종류를 따른다.
//         강철 → 검·도끼·둔기·단검, 참나무 → 활·창·장병기, 가죽 → 방패.
//         셋이 모두 다르면 계열이 서지 않아 어떤 무기든 나온다.
//   무기: 그 계열에 드는 제작 가능한 무기(Forge.CollectCraftable) 중 하나를 같은 확률로 굴린다.
//   등급: 세 재료 등급의 평균(내림)이 밑변이다. 자동 제작은 그대로, 수동 제작은 그 위로 난이도만큼
//         오를 수 있다(EquipmentCraftTable). 좋은 재료 하나로 나쁜 재료를 조금 메울 수 있다.
public enum WeaponFamily
{
    Metal,
    Wood,
    Shield,
    Any,
}

public static class CraftRecipe
{
    public const int SlotCount = 3;

    public static bool IsComplete(IReadOnlyList<CraftMaterial> materials) =>
        materials != null && materials.Count == SlotCount;

    public static WeaponFamily FamilyOf(IReadOnlyList<CraftMaterial> materials)
    {
        if (materials == null || materials.Count == 0) return WeaponFamily.Any;

        int metal = 0, wood = 0, leather = 0;
        for (int i = 0; i < materials.Count; i++)
        {
            switch (materials[i].Kind)
            {
                case MaterialKind.Metal:   metal++; break;
                case MaterialKind.Wood:    wood++; break;
                case MaterialKind.Leather: leather++; break;
            }
        }

        return Majority(metal, wood, leather, materials.Count);
    }

    // 과반을 넘은 계열만 선다. 셋이 하나씩이면 어느 쪽도 이기지 못한다. 장비 합성도 같은 규칙을 쓴다.
    public static WeaponFamily Majority(int metal, int wood, int shield, int total)
    {
        int majority = total / 2 + 1;
        if (metal >= majority) return WeaponFamily.Metal;
        if (wood >= majority) return WeaponFamily.Wood;
        if (shield >= majority) return WeaponFamily.Shield;
        return WeaponFamily.Any;
    }

    // 무기 한 자루가 속한 계열. 장비 합성에서 재료 장비의 계열을 셀 때 쓴다.
    public static WeaponFamily FamilyOfWeapon(WeaponType type)
    {
        if (Allows(WeaponFamily.Shield, type)) return WeaponFamily.Shield;
        if (Allows(WeaponFamily.Wood, type)) return WeaponFamily.Wood;
        if (Allows(WeaponFamily.Metal, type)) return WeaponFamily.Metal;
        return WeaponFamily.Any;
    }

    public static EquipmentGrade BaseGradeOf(IReadOnlyList<CraftMaterial> materials)
    {
        if (materials == null || materials.Count == 0) return EquipmentGrade.E;

        int sum = 0;
        for (int i = 0; i < materials.Count; i++) sum += (int)materials[i].Grade;
        return (EquipmentGrade)(sum / materials.Count);
    }

    public static bool Allows(WeaponFamily family, WeaponType type)
    {
        switch (family)
        {
            case WeaponFamily.Metal:
                return type == WeaponType.SwordOneHand || type == WeaponType.SwordTwoHand ||
                       type == WeaponType.Axe || type == WeaponType.Blunt || type == WeaponType.Dagger;
            case WeaponFamily.Wood:
                return type == WeaponType.Bow || type == WeaponType.Spear || type == WeaponType.Polearm;
            case WeaponFamily.Shield:
                return type == WeaponType.Shield;
            default:
                return true;
        }
    }

    // 이 계열에서 나올 수 있는 무기를 모은다.
    public static void CollectCandidates(WeaponFamily family, List<WeaponDefinition> results)
    {
        var craftable = new List<WeaponDefinition>();
        Forge.CollectCraftable(craftable);

        results.Clear();
        for (int i = 0; i < craftable.Count; i++)
        {
            if (Allows(family, craftable[i].type)) results.Add(craftable[i]);
        }
    }

    // 계열 안에서 무기 하나를 같은 확률로 굴린다. 그 계열에 드는 무기가 카탈로그에 하나도 없으면
    // 어떤 무기든 나오게 한다 — 재료를 태웠는데 아무것도 안 나오는 것보다 낫다.
    public static WeaponDefinition RollWeapon(WeaponFamily family)
    {
        var candidates = new List<WeaponDefinition>();
        CollectCandidates(family, candidates);
        if (candidates.Count == 0 && family != WeaponFamily.Any) CollectCandidates(WeaponFamily.Any, candidates);
        if (candidates.Count == 0) return null;

        return candidates[Random.Range(0, candidates.Count)];
    }

    public static string FamilyName(WeaponFamily family)
    {
        switch (family)
        {
            case WeaponFamily.Metal:  return "금속 무기";
            case WeaponFamily.Wood:   return "목재 무기";
            case WeaponFamily.Shield: return "방패";
            default:                  return "무기";
        }
    }

    // "검·도끼·둔기·단검". 계열에 무엇이 드는지 화면에 풀어 적는다.
    public static string FamilyContents(WeaponFamily family)
    {
        switch (family)
        {
            case WeaponFamily.Metal:  return "검·도끼·둔기·단검";
            case WeaponFamily.Wood:   return "활·창·장병기";
            case WeaponFamily.Shield: return "방패";
            default:                  return "종류 무관";
        }
    }
}
