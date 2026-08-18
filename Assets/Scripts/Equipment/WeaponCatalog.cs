using System.Collections.Generic;
using UnityEngine;

// 프로젝트에 존재하는 무기 에셋 목록.
//
// CharacterSO가 모델을 직접 가리키는 게 기본이지만, 기존 캐릭터들은 WeaponType만 들고 있고
// 앞으로 만들 상점/인벤토리 UI도 "한손검 목록"처럼 종류로 물어보게 된다.
// 그래서 종류 → 대표 모델을 찾아 주는 표를 하나 둔다.
//
// Resources에 두는 이유: 스포너나 UI가 인스펙터로 이 에셋을 물고 있어야 할 이유가 없는데
// 참조를 하나 더 만들면 씬마다 연결을 빠뜨릴 여지가 생긴다.
[CreateAssetMenu(fileName = "WeaponCatalog", menuName = "PickMeUp/Weapon Catalog")]
public class WeaponCatalog : ScriptableObject
{
    // Assets/Resources/WeaponCatalog.asset
    public const string ResourceName = "WeaponCatalog";

    public List<WeaponDefinition> weapons = new List<WeaponDefinition>();

    private static WeaponCatalog cached;
    private static bool searched;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        cached = null;
        searched = false;
    }

    public static WeaponCatalog Instance
    {
        get
        {
            if (cached != null) return cached;
            if (searched) return null;

            searched = true;
            cached = Resources.Load<WeaponCatalog>(ResourceName);
            if (cached == null)
                Debug.LogWarning($"[WeaponCatalog] Assets/Resources/{ResourceName}.asset 이 없다. " +
                                 "메뉴 PickMeUp/Equipment/Import Weapon Models 로 만들 수 있다.");
            return cached;
        }
    }

    // 종류에 맞는 대표 무기. 캐릭터가 모델을 안 골랐을 때의 기본값으로 쓴다.
    // 대표로 찍힌 게 없으면 목록 순서대로 첫 번째를 쓴다 — 한손검을 달랬는데 레이피어가
    // 나오는 식이 되지만, 적어도 아무것도 안 들리는 것보다는 낫다.
    public static WeaponDefinition FindByType(WeaponType type, EquipSlot slot)
    {
        if (type == WeaponType.None) return null;

        WeaponCatalog catalog = Instance;
        if (catalog == null) return null;

        WeaponDefinition fallback = null;
        for (int i = 0; i < catalog.weapons.Count; i++)
        {
            WeaponDefinition w = catalog.weapons[i];
            if (w == null || w.type != type || w.slot != slot) continue;
            if (w.representsType) return w;
            if (fallback == null) fallback = w;
        }
        return fallback;
    }

    public static WeaponDefinition Find(string weaponName)
    {
        WeaponCatalog catalog = Instance;
        if (catalog == null || string.IsNullOrEmpty(weaponName)) return null;

        for (int i = 0; i < catalog.weapons.Count; i++)
        {
            WeaponDefinition w = catalog.weapons[i];
            if (w != null && (w.name == weaponName || w.DisplayName == weaponName)) return w;
        }
        return null;
    }

    // 장비 선택 UI가 "이 캐릭터가 들 수 있는 것"만 추리는 데 쓴다.
    public static void Collect(List<WeaponDefinition> results, EquipSlot slot)
    {
        if (results == null) return;
        results.Clear();

        WeaponCatalog catalog = Instance;
        if (catalog == null) return;

        for (int i = 0; i < catalog.weapons.Count; i++)
        {
            WeaponDefinition w = catalog.weapons[i];
            if (w != null && w.slot == slot) results.Add(w);
        }
    }
}
