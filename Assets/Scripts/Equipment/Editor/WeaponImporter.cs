using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// 무기 모델 폴더를 훑어 WeaponDefinition 에셋을 만들어 주는 에디터 도구.
//
// 손으로 만들면 스물아홉 자루를 일일이 클릭해야 하고, 무기 팩을 하나 더 사 오면 처음부터 다시 한다.
//
// 쥐는 자세는 WeaponGripBaker가 맡는다. 여기서 만든 정의는 아트 프리팹이 아니라
// 그 도구가 구워 낸 무기 프리팹(Assets/Equipment/Weapons)을 가리킨다 —
// 손에 맞추는 값은 코드가 아니라 그 프리팹 안에 남는다.
public static class WeaponImporter
{
    private const string SourceFolder = "Assets/Equipment/Source/Hivemind";
    private const string OutputFolder = "Assets/Equipment/Definitions";
    private const string CatalogFolder = "Assets/Equipment/Resources";

    // 칼집은 무기가 아니라 허리에 다는 소품이라 장비 목록에 끼면 곤란하다.
    private static readonly string[] Excluded = { "scabbard" };

    [MenuItem("PickMeUp/Equipment/Import Weapon Models")]
    public static void Import()
    {
        EnsureFolder(OutputFolder);
        EnsureFolder(CatalogFolder);

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { SourceFolder });
        var all = new List<WeaponDefinition>();
        int reused = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string file = Path.GetFileNameWithoutExtension(path);
            if (IsExcluded(file)) continue;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) continue;

            string assetName = CleanName(file);
            string assetPath = OutputFolder + "/" + assetName + ".asset";
            var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(assetPath);

            bool isNew = definition == null;
            if (isNew)
            {
                definition = ScriptableObject.CreateInstance<WeaponDefinition>();
                AssetDatabase.CreateAsset(definition, assetPath);
            }

            // 이미 구워 둔 무기 프리팹을 가리키고 있으면 그대로 둔다. 아트 프리팹으로 되돌리면
            // 손에 맞춰 둔 자세가 통째로 날아간다.
            if (!WeaponGripBaker.IsGripPrefab(definition.model)) definition.model = model;
            definition.type = InferType(file);
            definition.slot = definition.type == WeaponType.Shield ? EquipSlot.OffHand : EquipSlot.MainHand;
            if (string.IsNullOrEmpty(definition.displayName)) definition.displayName = KoreanName(assetName);

            // 아트 팩 프리팹을 그대로 손에 붙이면 무기마다 다른 데서 튀어나온다.
            // 손에 맞춰 구운 무기 프리팹(Assets/Equipment/Weapons)으로 감싸 물려 준다.
            // 이미 감싸 둔 것은 건드리지 않는다 — 손으로 다듬어 둔 자세를 덮어쓰면 안 된다.
            if (!WeaponGripBaker.EnsureGripPrefab(definition)) reused++;

            EditorUtility.SetDirty(definition);
            all.Add(definition);
        }

        all.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        BuildCatalog(all);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[WeaponImporter] " + all.Count + "자루 정리 완료 (새로 만든 것 " + (all.Count - reused) +
                  ", 그대로 둔 것 " + reused + "). → " + OutputFolder);
    }

    private static void BuildCatalog(List<WeaponDefinition> weapons)
    {
        string path = CatalogFolder + "/" + WeaponCatalog.ResourceName + ".asset";
        var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(path);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<WeaponCatalog>();
            AssetDatabase.CreateAsset(catalog, path);
        }

        // 스캔 폴더 밖에서 만든 무기(손으로 추가한 것, 다른 팩)를 지우면 안 된다.
        // 목록을 통째로 갈아끼우지 않고, 이번에 훑은 것만 더한다.
        var merged = new List<WeaponDefinition>(weapons);
        for (int i = 0; i < catalog.weapons.Count; i++)
        {
            WeaponDefinition existing = catalog.weapons[i];
            if (existing != null && !merged.Contains(existing)) merged.Add(existing);
        }

        // 종류로 무기를 찾을 때 대표가 지정돼 있지 않으면 목록의 앞쪽이 뽑힌다.
        // 순서가 스캔 순서(GUID 순)를 타면 같은 프로젝트에서도 사람마다 다른 검이 잡히므로 이름순으로 고정한다.
        merged.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        catalog.weapons = merged;
        EditorUtility.SetDirty(catalog);
    }

    // ------------------------------------------------------------------
    // 이름 → 분류
    // ------------------------------------------------------------------

    private static bool IsExcluded(string file)
    {
        string lower = file.ToLowerInvariant();
        foreach (string e in Excluded)
        {
            if (lower.Contains(e)) return true;
        }
        return false;
    }

    // "SM_Sword_1 (1)" → "Sword_1"
    private static string CleanName(string file)
    {
        string s = Regex.Replace(file, "^SM_", "");
        s = Regex.Replace(s, @"\s*\(\d+\)$", "");
        return s.Trim();
    }

    private static WeaponType InferType(string file)
    {
        string s = file.ToLowerInvariant();

        if (s.Contains("shield")) return WeaponType.Shield;
        if (s.Contains("dagger") || s.Contains("knife")) return WeaponType.Dagger;
        if (s.Contains("spear") || s.Contains("javelin")) return WeaponType.Spear;
        // poleaxe는 axe보다 먼저 걸러야 한다. 도끼가 아니라 장병기다.
        if (s.Contains("poleaxe") || s.Contains("halberd") || s.Contains("scythe") || s.Contains("billhook"))
            return WeaponType.Polearm;
        if (s.Contains("hammer") || s.Contains("bludgeon")) return WeaponType.Blunt;
        if (s.Contains("axe")) return WeaponType.Axe;
        if (s.Contains("heavysword") || s.Contains("flamberge")) return WeaponType.SwordTwoHand;
        return WeaponType.SwordOneHand;
    }

    private static readonly Dictionary<string, string> Korean = new Dictionary<string, string>
    {
        { "Axe_1", "전투도끼" }, { "Axe_2", "손도끼" }, { "BillHook", "빌훅" },
        { "Bludgeon", "철퇴" }, { "Dagger_1", "단검" }, { "Dagger_2", "비수" },
        { "Flamberge", "플랑베르주" }, { "Halberd", "할버드" }, { "HeavySword", "대검" },
        { "Javelin", "투창" }, { "Kite_Wood_Shield", "카이트 방패" }, { "Poleaxe", "폴액스" },
        { "Rapier", "레이피어" }, { "Round_Metal_Shield", "원형 강철 방패" },
        { "Round_Metal_Shield_Small", "소형 강철 방패" }, { "Round_Wood_Shield", "원형 목재 방패" },
        { "Scimtar_1", "시미터" }, { "Scimtar_2", "곡도" }, { "Spear", "창" },
        { "Sword_1", "롱소드" }, { "Sword_2", "브로드소드" }, { "Sword_3", "아밍소드" },
        { "ThrowingKnife", "투척단검" }, { "WarHammer", "워해머" }, { "WarScythe", "전투낫" },
    };

    private static string KoreanName(string assetName)
    {
        string kr;
        return Korean.TryGetValue(assetName, out kr) ? kr : assetName.Replace('_', ' ');
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
