using System.Collections.Generic;
using UnityEngine;

// 프로젝트에 존재하는 무기 에셋 목록.
//
// 무기는 무기고에서 실물을 골라 들고 전투에 나간다. 그래서 캐릭터는 언제나 무기 에셋을 가리키고,
// "종류만 정해 두면 알아서 골라 주는" 길은 두지 않는다 — 무엇을 드는지는 고르는 순간에 정해진다.
// 이 표가 하는 일은 무기고가 "이 슬롯에 들 수 있는 것"을 늘어놓도록 목록을 내주는 것이다(Collect).
//
// Resources에 두는 이유: 스포너나 UI가 인스펙터로 이 에셋을 물고 있어야 할 이유가 없는데
// 참조를 하나 더 만들면 씬마다 연결을 빠뜨릴 여지가 생긴다.
[CreateAssetMenu(fileName = "WeaponCatalog", menuName = "PickMeUp/Weapon Catalog")]
public class WeaponCatalog : ScriptableObject
{
    // Assets/Equipment/Resources/WeaponCatalog.asset
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
                Debug.LogWarning($"[WeaponCatalog] Assets/Equipment/Resources/{ResourceName}.asset 이 없다. " +
                                 "메뉴 PickMeUp/Equipment/Import Weapon Models 로 만들 수 있다.");
            return cached;
        }
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
