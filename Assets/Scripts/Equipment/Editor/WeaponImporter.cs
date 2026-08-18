using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// 무기 모델 폴더를 훑어 WeaponDefinition 에셋을 만들어 주는 에디터 도구.
//
// 손으로 만들면 스물아홉 자루를 일일이 클릭해야 하고, 무기 팩을 하나 더 사 오면 처음부터 다시 한다.
// 무엇보다 성가신 건 쥐는 자세다. 모델마다 피벗이 자루에 있기도, 한가운데 있기도, 칼끝에 있기도 해서
// 그냥 손에 붙이면 전부 다른 데서 튀어나온다.
// 그래서 메시 정점을 직접 읽어 "가는 쪽이 자루"라는 사실로 자루 위치와 날 방향을 찾아낸다.
// 완벽하진 않지만 스물아홉 자루를 맨손으로 맞추는 것보다 훨씬 가까운 지점에서 시작할 수 있다.
public static class WeaponImporter
{
    private const string SourceFolder = "Assets/Hivemind";
    private const string OutputFolder = "Assets/Equipment/Definitions";
    private const string CatalogFolder = "Assets/Resources";

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

            definition.model = model;
            definition.type = InferType(file);
            definition.slot = definition.type == WeaponType.Shield ? EquipSlot.OffHand : EquipSlot.MainHand;
            definition.representsType = Representative.Contains(assetName);
            if (string.IsNullOrEmpty(definition.displayName)) definition.displayName = KoreanName(assetName);

            // 이미 있는 에셋의 쥐는 자세는 건드리지 않는다. 손으로 다듬어 둔 값을 덮어쓰면
            // 무기를 하나 추가할 때마다 지금까지 맞춰 둔 게 전부 날아간다.
            if (isNew) ApplyMeasuredGrip(definition);
            else reused++;

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

    // 손으로 옮겨 놓은 무기의 자세를 다시 자동 계산으로 되돌리고 싶을 때.
    [MenuItem("PickMeUp/Equipment/Recompute Grip (Selected)")]
    public static void RecomputeSelected()
    {
        int count = 0;
        foreach (Object o in Selection.objects)
        {
            var definition = o as WeaponDefinition;
            if (definition == null || !ApplyMeasuredGrip(definition)) continue;

            EditorUtility.SetDirty(definition);
            count++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[WeaponImporter] " + count + "자루의 쥐는 자세를 다시 계산했다.");
    }

    [MenuItem("PickMeUp/Equipment/Recompute Grip (Selected)", true)]
    private static bool RecomputeSelectedValidate() => Selection.objects.Length > 0;

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

    // 캐릭터가 WeaponType만 지정했을 때 손에 들릴 무기.
    // 정해 두지 않으면 목록 첫 번째(이름순)가 뽑혀서 한손검 자리에 레이피어가 들어간다.
    private static readonly HashSet<string> Representative = new HashSet<string>
    {
        "Sword_1", "HeavySword", "Dagger_1", "Spear", "Axe_1", "WarHammer", "Halberd", "Round_Wood_Shield",
    };

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

    // ------------------------------------------------------------------
    // 메시 → 쥐는 자세
    // ------------------------------------------------------------------

    // 소켓은 +Y가 날이 뻗는 방향이다(WeaponEquipper.GripRotation 참고).
    // 여기서 할 일은 모델을 그 방향으로 세우고, 자루가 손 한가운데 오도록 밀어 넣는 것.
    public static bool ApplyMeasuredGrip(WeaponDefinition definition)
    {
        if (definition == null || definition.model == null) return false;

        Mesh mesh = FindMesh(definition.model);
        if (mesh == null) return false;

        Bounds bounds = mesh.bounds;
        definition.gripScale = 1f;

        if (definition.type == WeaponType.Shield)
        {
            // 방패는 자루가 없다. 가장 얇은 축이 방패 면의 법선이니 그쪽을 손등 바깥(소켓 +Z)으로 돌린다.
            Vector3 thin = ThinnestAxis(bounds.size);
            Quaternion flat = Quaternion.FromToRotation(thin, Vector3.forward);
            definition.gripRotation = flat.eulerAngles;
            definition.gripPosition = -(flat * bounds.center);
            return true;
        }

        // 긴 축이 무기의 축이다. 이 팩은 전부 Y지만 다른 팩을 넣어도 되도록 재어 본다.
        int axis = LongestAxis(bounds.size);
        bool headIsPositive = FindHeadDirection(mesh, axis);

        // 날이 축의 반대쪽을 향해 있으면 뒤집어야 소켓 +Y와 맞는다.
        Quaternion align = AlignToUp(axis, headIsPositive);
        definition.gripRotation = align.eulerAngles;

        float gripAlong = FindGripPoint(mesh, axis, headIsPositive, definition.type);
        Vector3 gripPoint = CrossSectionCenter(mesh, axis, gripAlong);
        gripPoint[axis] = gripAlong;

        definition.gripPosition = -(align * gripPoint);
        return true;
    }

    private static Mesh FindMesh(GameObject model)
    {
        var filter = model.GetComponentInChildren<MeshFilter>();
        if (filter != null && filter.sharedMesh != null) return filter.sharedMesh;

        var skinned = model.GetComponentInChildren<SkinnedMeshRenderer>();
        return skinned != null ? skinned.sharedMesh : null;
    }

    private static int LongestAxis(Vector3 size)
    {
        if (size.y >= size.x && size.y >= size.z) return 1;
        return size.x >= size.z ? 0 : 2;
    }

    private static Vector3 ThinnestAxis(Vector3 size)
    {
        if (size.x <= size.y && size.x <= size.z) return Vector3.right;
        return size.y <= size.z ? Vector3.up : Vector3.forward;
    }

    // 모델 축을 소켓 +Y로 세운다.
    private static Quaternion AlignToUp(int axis, bool headIsPositive)
    {
        if (axis == 1)
        {
            // 이미 세로로 서 있다. 방향이 맞으면 그대로, 거꾸로면 제 축을 중심으로 반 바퀴 돌린다.
            // 옆으로 눕히지 않아야 칼날의 넓은 면이 손바닥과 나란한 채로 남는다.
            return headIsPositive ? Quaternion.identity : Quaternion.Euler(0f, 0f, 180f);
        }

        Vector3 head = axis == 0 ? Vector3.right : Vector3.forward;
        if (!headIsPositive) head = -head;
        return Quaternion.FromToRotation(head, Vector3.up);
    }

    // 자루 쪽은 가늘고 머리(날) 쪽은 두껍다. 축을 따라 잘라 보고 굵은 쪽을 머리로 본다.
    private static bool FindHeadDirection(Mesh mesh, int axis)
    {
        const int slices = 20;
        float[] radius = SliceRadii(mesh, axis, slices);

        // 양 끝 25%씩만 비교한다. 가운데는 어느 무기든 비슷해서 판단에 도움이 안 된다.
        int edge = Mathf.Max(1, slices / 4);
        float low = 0f;
        float high = 0f;
        for (int i = 0; i < edge; i++)
        {
            low += radius[i];
            high += radius[slices - 1 - i];
        }
        return high >= low;
    }

    // 축을 따라 자른 단면의 평균 반지름.
    private static float[] SliceRadii(Mesh mesh, int axis, int slices)
    {
        Vector3[] vertices = mesh.vertices;
        Bounds bounds = mesh.bounds;
        float min = bounds.min[axis];
        float length = Mathf.Max(0.0001f, bounds.size[axis]);

        var sum = new float[slices];
        var count = new int[slices];
        int a = (axis + 1) % 3;
        int b = (axis + 2) % 3;

        for (int i = 0; i < vertices.Length; i++)
        {
            int s = Mathf.Clamp((int)((vertices[i][axis] - min) / length * slices), 0, slices - 1);
            sum[s] += Mathf.Sqrt(vertices[i][a] * vertices[i][a] + vertices[i][b] * vertices[i][b]);
            count[s]++;
        }

        var radius = new float[slices];
        for (int i = 0; i < slices; i++) radius[i] = count[i] > 0 ? sum[i] / count[i] : 0f;
        return radius;
    }

    // 손이 놓일 지점(모델 로컬 좌표의 축 값).
    private static float FindGripPoint(Mesh mesh, int axis, bool headIsPositive, WeaponType type)
    {
        const int slices = 20;
        float[] radius = SliceRadii(mesh, axis, slices);
        Bounds bounds = mesh.bounds;
        float length = bounds.size[axis];
        float buttEnd = headIsPositive ? bounds.min[axis] : bounds.max[axis];
        float toHead = headIsPositive ? 1f : -1f;

        float max = 0f;
        for (int i = 0; i < slices; i++) max = Mathf.Max(max, radius[i]);

        // 손잡이 끝에서 시작해 굵어지기 직전까지가 자루다.
        float haft = 0f;
        for (int i = 0; i < slices; i++)
        {
            int s = headIsPositive ? i : slices - 1 - i;
            if (radius[s] > max * 0.5f) break;
            haft += length / slices;
        }

        switch (type)
        {
            // 장병기는 자루 전체가 가늘어서 위 계산이 무기 길이 전부를 자루로 본다.
            // 한 손으로 들 때 실제로 잡는 지점은 밑동에서 1/3쯤이다.
            case WeaponType.Spear:
            case WeaponType.Polearm:
                return buttEnd + toHead * length * 0.33f;
            case WeaponType.SwordTwoHand:
                return buttEnd + toHead * Mathf.Min(haft * 0.5f, 0.12f);
            default:
                return buttEnd + toHead * Mathf.Min(haft * 0.5f, 0.07f);
        }
    }

    // 자루가 손 한가운데 오도록, 그 높이의 단면 중심을 쓴다.
    // 도끼처럼 날이 한쪽으로 쏠린 무기는 전체 바운즈 중심을 쓰면 자루가 손 밖으로 밀린다.
    private static Vector3 CrossSectionCenter(Mesh mesh, int axis, float along)
    {
        Vector3[] vertices = mesh.vertices;
        float window = Mathf.Max(0.01f, mesh.bounds.size[axis] * 0.05f);

        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (Mathf.Abs(vertices[i][axis] - along) > window) continue;
            sum += vertices[i];
            count++;
        }

        return count > 0 ? sum / count : mesh.bounds.center;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
