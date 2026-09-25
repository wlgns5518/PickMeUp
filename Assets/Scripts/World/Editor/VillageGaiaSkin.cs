using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// 소환소·합성소·무기창고·비행선착장에 마을 다른 건물(Gaia 3DForge 집·대장간)의 재질을 입힌다(2026-09-25 사용자:
// "다른 건물들에 쓰이고 있는 material을 가져다 써라").
// Gaia 머티리얼은 한 장짜리 아틀라스(fe_village_base)를 Gaia 메시의 UV에 맞춰 쓰므로 Meshy 파츠에 그대로 씌우면
// 무늬가 뒤섞인다. 대신 아틀라스의 네모 견본(돌담·나무판·너와 지붕)을 파츠 표면에 3D로 투영해(삼면 투영)
// 파츠 텍스처를 새로 굽는다 — 같은 돌, 같은 나무, 같은 지붕이 된다. 원래 텍스처는 어디가 돌이고 어디가 나무·쇠인지
// 가르는 데와 음영(홈·틈)에만 쓴다.
//
// 색은 다크 판타지 팔레트(VillagePalette, 2026-09-25)로 옮긴다: 견본마다 목표 색이 있고(돌담=Stone, 기단=DarkStone,
// 널=DarkWood, 갑판=Wood, 너와=DarkRoof), 견본 평균에 견준 밝기로 옮겨 결은 남긴다(VillageColorGrade.Grade).
// 같은 규칙으로 Gaia 집들이 쓰는 아틀라스도 한 장 다시 칠해(BakeHouseAtlas) 집·시설이 같은 팔레트에 앉는다.
//
// 파츠 머티리얼은 시공의 틈·대장간·훈련소·거리도 같이 쓰므로 그대로 두고, 입힌 사본(Materials/GaiaSkin/)을 네 시설에만
// 바꿔 낀다. 등불·화로·깃발·짐·무기대·구슬·오벨리스크·창은 원래대로 둔다(불빛과 소품).
// 조립(VillagePrefabAssembler 4번)도 저장 직전에 이것을 부르므로 다시 조립해도 남는다.
public static class VillageGaiaSkin
{
    private const string Folder = "Assets/Environment/Village/Materials/GaiaSkin";
    private const string PartsRoot = "Assets/Environment/Village/Parts";
    private const string Atlas = "Assets/Procedural Worlds/Packages - Install/Asset Samples/3DForge/Textures/fe_village_base.png";

    private static readonly HashSet<string> Facilities = new HashSet<string>
    {
        "Facility_Summoning", "Facility_Synthesis", "Facility_Armory", "Facility_Airdock",
    };

    private enum Skin
    {
        Wall,   // 돌담(밝은 막돌), 나무는 세로 널
        Base,   // 기단·계단: 옆면은 어두운 막돌, 윗면은 판석
        Tower,  // 돌담 + 위쪽 비탈면은 너와 지붕
        Roof,   // 전부 너와 지붕
        Deck,   // 선착장 갑판: 가로 널
    }

    private static readonly Dictionary<string, Skin> Skins = new Dictionary<string, Skin>
    {
        ["hall_body"] = Skin.Wall,
        ["house_body"] = Skin.Wall,
        ["house_upper"] = Skin.Wall,
        ["kit_buttress"] = Skin.Wall,
        ["kit_door"] = Skin.Wall,
        ["kit_pillar"] = Skin.Wall,
        ["kit_plinth"] = Skin.Base,
        ["kit_stairs"] = Skin.Base,
        ["kit_tower"] = Skin.Tower,
        ["hall_roof"] = Skin.Roof,
        ["house_roof"] = Skin.Roof,
    };

    private static Skin? SkinFor(string facility, string id)
    {
        // 선착장은 기단 파츠를 기둥 위에 얹어 갑판으로 쓴다.
        if (facility == "Facility_Airdock" && id == "kit_plinth") return Skin.Deck;
        return Skins.TryGetValue(id, out Skin skin) ? skin : (Skin?)null;
    }

    // 아틀라스(4096) 안의 네모 견본. 좌표는 위쪽 원점 픽셀, 가장자리의 검은 틈을 피해 안쪽으로 잡았다.
    // meters는 견본 한 장이 덮는 폭(파츠 공간 미터), target은 팔레트 목표 색.
    private sealed class Swatch
    {
        public readonly int x0, y0, x1, y1;
        public readonly float meters;
        public readonly Color target;
        public Color? mean;   // 아틀라스를 읽은 뒤 한 번 잰다

        public Swatch(int x0, int y0, int x1, int y1, float meters, Color target)
        {
            this.x0 = x0; this.y0 = y0; this.x1 = x1; this.y1 = y1; this.meters = meters; this.target = target;
        }
    }

    private static readonly Swatch Shingles = new Swatch(1700, 30, 2450, 625, 3f, VillagePalette.DarkRoof);
    private static readonly Swatch Rubble = new Swatch(30, 2010, 805, 2935, 3f, VillagePalette.Stone);
    private static readonly Swatch DarkRubble = new Swatch(855, 2010, 1630, 2935, 3f, VillagePalette.DarkStone);
    private static readonly Swatch Planks = new Swatch(875, 1020, 1630, 1960, 2.4f, VillagePalette.DarkWood);
    private static readonly Swatch DeckPlanks = new Swatch(30, 1020, 805, 1960, 3f, VillagePalette.Wood);
    private static readonly Swatch Flagstones = new Swatch(2790, 2950, 3115, 3980, 2.4f, VillagePalette.Stone);   // 네 줄 판석 띠

    // 한 번 입히는 동안 같은 파츠·같은 재질은 한 번만 굽는다.
    private static readonly Dictionary<string, Material> Baked = new Dictionary<string, Material>();
    private static Color32[] atlasPixels;
    private static int atlasSize;

    // ---- 메뉴 -----------------------------------------------------------------

    [MenuItem("PickMeUp/Village/9. 시설에 마을 건물 재질 입히기 (Gaia)", priority = 65)]
    public static void ApplyAll()
    {
        Baked.Clear();
        BakeHouseAtlas();
        foreach (string file in Facilities)
        {
            string path = VillagePrefabAssembler.PrefabPath(file);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Apply(contents, file);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
        atlasPixels = null;
        AssetDatabase.SaveAssets();

        // 마을은 프리팹을 Instantiate로 세우므로(프리팹 연결 없음) 다시 세워야 보인다.
        var village = Object.FindAnyObjectByType<VillageBlockout>();
        if (village != null) village.Rebuild();
    }

    /// root 아래 파츠 렌더러에 facility 재질을 끼운다. 입힐 시설이 아니거나 표에 없는 파츠는 원래 머티리얼로.
    public static void Apply(GameObject root, string facility)
    {
        bool target = Facilities.Contains(facility);
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            string id = PartId(renderer);
            if (id == null) continue;
            Skin? skin = target ? SkinFor(facility, id) : null;
            Material material = skin.HasValue ? Skinned(id, skin.Value) : null;
            renderer.sharedMaterial = material != null ? material : PartMaterial(id);
        }
    }

    // VillagePartBaker가 메시를 "<id>_mesh"로 이름 짓는다. 파츠 프리팹이 있어야 파츠로 친다(Gaia 메시와 섞이지 않게).
    private static string PartId(MeshRenderer renderer)
    {
        var filter = renderer.GetComponent<MeshFilter>();
        string mesh = filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : null;
        if (mesh == null || !mesh.EndsWith("_mesh")) return null;
        string id = mesh.Substring(0, mesh.Length - "_mesh".Length);
        return PartMaterial(id) != null ? id : null;
    }

    private static GameObject PartPrefab(string id) => AssetDatabase.LoadAssetAtPath<GameObject>(VillagePartBaker.PrefabPath(id));

    private static Material PartMaterial(string id)
    {
        GameObject prefab = PartPrefab(id);
        var renderer = prefab != null ? prefab.GetComponent<MeshRenderer>() : null;
        return renderer != null ? renderer.sharedMaterial : null;
    }

    // 파츠 머티리얼을 베낀 뒤 텍스처만 바꾼다. Gaia가 없으면(저장소 밖 패키지) 원래 머티리얼로 둔다.
    private static Material Skinned(string id, Skin skin)
    {
        string name = $"{id}_{skin.ToString().ToLowerInvariant()}";
        if (Baked.TryGetValue(name, out Material cached) && cached != null) return cached;
        if (!LoadAtlas()) return null;

        Material source = PartMaterial(id);
        Directory.CreateDirectory(Folder);
        string path = $"{Folder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(source) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = source.shader;
            material.CopyPropertiesFromMaterial(source);
            material.shaderKeywords = source.shaderKeywords;
            material.enableInstancing = source.enableInstancing;
        }

        // 팔레트 색이 텍스처에 이미 들어가 있어 틴트는 1이다.
        material.SetTexture("_BaseMap", Bake(id, skin, $"{Folder}/{name}.jpg"));
        material.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(material);
        return Baked[name] = material;
    }

    private static bool LoadAtlas()
    {
        if (atlasPixels != null) return true;
        if (!File.Exists(Atlas))
        {
            Debug.LogWarning($"[VillageGaiaSkin] Gaia 아틀라스가 없다: {Atlas} (Gaia가 설치돼 있어야 한다)");
            return false;
        }
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.LoadImage(File.ReadAllBytes(Atlas));
        atlasSize = texture.width;
        atlasPixels = texture.GetPixels32();
        Object.DestroyImmediate(texture);
        return true;
    }

    // 견본의 평균 색(검은 틈 빼고). 픽셀을 팔레트로 옮길 때 밝기 비율의 기준이다.
    private static Color SwatchMean(Swatch swatch)
    {
        if (swatch.mean.HasValue) return swatch.mean.Value;
        Color sum = Color.black; int count = 0;
        for (int y = swatch.y0; y < swatch.y1; y += 4)
        for (int x = swatch.x0; x < swatch.x1; x += 4)
        {
            Color c = atlasPixels[(atlasSize - 1 - y) * atlasSize + x];
            if (Luminance(c) < 0.03f) continue;
            sum += c; count++;
        }
        Color mean = count > 0 ? sum / count : Color.gray;
        mean.a = 1f;
        swatch.mean = mean;
        return mean;
    }

    // ---- Gaia 집 아틀라스 ----------------------------------------------------------
    // 거리의 Gaia 3DForge 집·헛간(fe_village_base 아틀라스를 쓰는 것 전부)도 같은 팔레트로. 아틀라스를 한 장 다시 칠해
    // 사본 머티리얼(Materials/Gaia/fe_village_base_Village)에 끼운다. 원본(Gaia 패키지 안)은 건드리지 않는다.
    // 큰 견본은 자리로 목표 색을 정하고(흰 회벽=Stone, 너와=DarkRoof …), 나머지 조각(문·창·기둥·깃발)은 색으로 가른다 —
    // 나무 빛이면 DarkWood, 무채색이면 Stone, 그 밖(창 유리·깃발)은 채도를 빼고 어둡게(일반 집이 화면에서 튀지 않게).
    // 마을 카메라 거리에서 4096은 과해 2048로 줄여 둔다(원본을 쓸 때보다 텍스처 메모리가 1/4).
    private const string HouseAtlas = "Assets/Environment/Village/Materials/Gaia/fe_village_base_dark.jpg";
    private const string HouseMaterial = "Assets/Environment/Village/Materials/Gaia/fe_village_base_Village.mat";
    private const string WagonMaterial = "Assets/Environment/Village/Materials/Gaia/fe_village_base_wagons_Village.mat";
    private const int HouseAtlasSize = 2048;

    private readonly struct Region
    {
        public readonly int x0, y0, x1, y1;   // 아틀라스 4096, 위쪽 원점
        public readonly Color target;

        public Region(int x0, int y0, int x1, int y1, Color target)
        {
            this.x0 = x0; this.y0 = y0; this.x1 = x1; this.y1 = y1; this.target = target;
        }

        public bool Contains(int x, int topY) => x >= x0 && x < x1 && topY >= y0 && topY < y1;
    }

    private static readonly Region[] HouseRegions =
    {
        new Region(1665, 12, 2470, 640, VillagePalette.DarkRoof),    // 너와 지붕
        new Region(1665, 675, 2470, 1300, VillagePalette.Roof),      // 기와 지붕
        new Region(840, 12, 1640, 990, VillagePalette.Stone),        // 흰 회벽
        new Region(18, 12, 820, 990, VillagePalette.DarkStone),      // 어두운 회벽
        new Region(18, 2000, 820, 2945, VillagePalette.Stone),       // 막돌
        new Region(840, 2000, 1640, 2945, VillagePalette.DarkStone), // 어두운 막돌
        new Region(18, 1000, 820, 1980, VillagePalette.DarkWood),    // 가로 널
        new Region(840, 1000, 1640, 1980, VillagePalette.DarkWood),  // 세로 널
    };

    public static void BakeHouseAtlas()
    {
        if (!LoadAtlas()) return;
        int size = HouseAtlasSize, step = atlasSize / size;

        // 1. 줄이기(칸 평균). 행은 아래 원점.
        var src = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Color sum = Color.black;
            for (int j = 0; j < step; j++)
            for (int i = 0; i < step; i++)
                sum += atlasPixels[(y * step + j) * atlasSize + x * step + i];
            src[y * size + x] = sum / (step * step);
        }

        // 2. 칸마다 구역(없으면 -1)과 구역·나무·무채색 평균.
        var region = new int[size * size];
        var regionSum = new Color[HouseRegions.Length];
        var regionCount = new int[HouseRegions.Length];
        Color woodSum = Color.black, neutralSum = Color.black;
        int woodCount = 0, neutralCount = 0;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int index = y * size + x;
            int topY = (size - 1 - y) * step, ax = x * step;
            region[index] = -1;
            for (int r = 0; r < HouseRegions.Length; r++)
                if (HouseRegions[r].Contains(ax, topY)) { region[index] = r; break; }

            Color c = src[index];
            if (Luminance(c) < 0.03f) continue;   // 검은 틈
            if (region[index] >= 0) { regionSum[region[index]] += c; regionCount[region[index]]++; continue; }
            if (IsWood(c)) { woodSum += c; woodCount++; }
            else if (IsNeutral(c)) { neutralSum += c; neutralCount++; }
        }
        Color woodMean = woodCount > 0 ? woodSum / woodCount : Color.gray;
        Color neutralMean = neutralCount > 0 ? neutralSum / neutralCount : Color.gray;

        // 3. 옮기기.
        var dst = new Color32[size * size];
        for (int i = 0; i < src.Length; i++)
        {
            Color c = src[i];
            Color painted;
            if (Luminance(c) < 0.03f) painted = c;
            else if (region[i] >= 0)
                painted = VillageColorGrade.Grade(c, regionSum[region[i]] / Mathf.Max(1, regionCount[region[i]]), HouseRegions[region[i]].target, 0.3f);
            else if (IsWood(c)) painted = VillageColorGrade.Grade(c, woodMean, VillagePalette.DarkWood, 0.3f);
            else if (IsNeutral(c)) painted = VillageColorGrade.Grade(c, neutralMean, VillagePalette.Stone, 0.3f);
            else
            {
                float gray = Luminance(c);
                painted = Color.Lerp(c, new Color(gray, gray, gray), 0.6f) * 0.7f;
            }
            painted.a = 1f;
            dst[i] = painted;
        }

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(dst);
        texture.Apply();
        File.WriteAllBytes(HouseAtlas, texture.EncodeToJPG(90));
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(HouseAtlas, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(HouseAtlas);
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = size;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SaveAndReimport();

        Texture2D windows = BakeWindowMask(src, region, size);
        var house = AssetDatabase.LoadAssetAtPath<Material>(HouseMaterial);
        if (house != null) LitHouse(house, AssetDatabase.LoadAssetAtPath<Texture2D>(HouseAtlas), windows);
        // 수레·짐 아틀라스는 나무가 거의 전부라 평균만 갈색 목재로 옮긴다.
        var wagon = AssetDatabase.LoadAssetAtPath<Material>(WagonMaterial);
        if (wagon != null)
        {
            wagon.SetColor("_Color", VillageColorGrade.TintFor(wagon.GetTexture("_MainTex"), VillagePalette.Wood));
            EditorUtility.SetDirty(wagon);
        }
    }

    // ---- 집 창 불빛 -----------------------------------------------------------------
    // 저녁 거점(2026-09-25 참고 그림)은 집집마다 창에 따뜻한 불이 켜져 있다. Gaia 집 셰이더(PW General)에는 발광이 없어
    // 집 머티리얼을 URP Lit(같은 아틀라스·노멀 + 창 마스크 발광)으로 바꾼다. 점광원은 달지 않는다 — 불빛은 발광·블룸뿐.
    // 창 유리는 아틀라스의 노랑·초록 납유리 조각이다: 밝고 채도 있는 노랑~초록 칸(큰 견본·깃발 자리는 뺀다).
    private const string WindowMask = "Assets/Environment/Village/Materials/Gaia/fe_village_base_windows.jpg";
    private const string HouseNormal = "Assets/Procedural Worlds/Packages - Install/Asset Samples/3DForge/Textures/fe_village_base_NRM.png";
    private static readonly Color WindowGlow = Color.Lerp(VillagePalette.WarmOrange, VillagePalette.Gold, 0.4f) * 5f;

    private static Texture2D BakeWindowMask(Color[] src, int[] region, int size)
    {
        int step = atlasSize / size;
        var mask = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int index = y * size + x;
            int topY = (size - 1 - y) * step, ax = x * step;
            byte v = 0;
            // 깃발 줄(아틀라스 왼쪽 아래의 색 삼각형들)은 유리가 아니다.
            bool flags = ax >= 690 && ax < 850 && topY >= 2950 && topY < 3900;
            if (region[index] < 0 && !flags)
            {
                Color.RGBToHSV(src[index], out float hue, out float saturation, out float value);
                float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.5f, saturation)) *
                               Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 0.65f, value)) *
                               (hue > 0.09f && hue < 0.36f ? 1f : 0f);
                v = (byte)(weight * 255f);
            }
            mask[index] = new Color32(v, v, v, 255);
        }

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(mask);
        texture.Apply();
        File.WriteAllBytes(WindowMask, texture.EncodeToJPG(90));
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(WindowMask, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(WindowMask);
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 1024;   // 마스크라 반으로 충분하다
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(WindowMask);
    }

    private static void LitHouse(Material house, Texture2D atlas, Texture2D windows)
    {
        house.shader = Shader.Find("Universal Render Pipeline/Lit");
        house.shaderKeywords = new string[0];
        house.SetTexture("_BaseMap", atlas);
        house.SetColor("_BaseColor", Color.white);
        house.SetFloat("_Metallic", 0f);
        house.SetFloat("_Smoothness", 0.1f);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(HouseNormal);
        house.SetTexture("_BumpMap", normal);
        if (normal != null) house.EnableKeyword("_NORMALMAP");
        house.SetTexture("_EmissionMap", windows);
        house.SetColor("_EmissionColor", WindowGlow);
        house.EnableKeyword("_EMISSION");
        house.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        house.enableInstancing = true;
        EditorUtility.SetDirty(house);
    }

    private static bool IsWood(Color c)
    {
        Color.RGBToHSV(c, out float hue, out float saturation, out _);
        return saturation > 0.2f && hue > 0.02f && hue < 0.15f;
    }

    private static bool IsNeutral(Color c)
    {
        Color.RGBToHSV(c, out _, out float saturation, out _);
        return saturation < 0.18f;
    }

    // ---- 굽기 -----------------------------------------------------------------

    private static Texture2D Bake(string id, Skin skin, string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.LoadImage(File.ReadAllBytes($"{PartsRoot}/{id}/{id}_albedo.jpg"));
        int width = texture.width, height = texture.height;
        VillagePartSurface surface = VillagePartSurface.Unwrap(PartPrefab(id).GetComponent<MeshFilter>().sharedMesh, width, height);
        Color32[] px = texture.GetPixels32();

        // 원래 텍스처의 음영(홈·틈·그늘)을 견본 위에 얹는다. 평균 밝기에 견준 비율이다.
        float sum = 0f; int count = 0;
        for (int i = 0; i < px.Length; i++)
        {
            if (surface != null && !surface.covered[i]) continue;
            sum += Luminance(px[i]); count++;
        }
        float mean = Mathf.Max(0.05f, count > 0 ? sum / count : 0.3f);

        for (int i = 0; i < px.Length; i++)
        {
            if (surface == null || !surface.covered[i]) continue;
            Color c = px[i];
            Vector3 p = surface.positions[i];
            Vector3 n = surface.normals[i];
            float lum = Luminance(c);
            Color.RGBToHSV(c, out float hue, out float saturation, out _);
            bool wood = saturation > 0.22f && hue > 0.03f && hue < 0.14f && lum > 0.08f;
            bool roofFace = n.y > 0.35f && n.y < 0.95f && Mathf.InverseLerp(surface.bounds.min.y, surface.bounds.max.y, p.y) > 0.65f;

            Swatch swatch;
            switch (skin)
            {
                case Skin.Roof: swatch = Shingles; break;
                case Skin.Deck: swatch = wood ? Planks : DeckPlanks; break;
                case Skin.Tower: swatch = roofFace ? Shingles : wood ? Planks : Rubble; break;
                // 기단 돌은 갈색 기가 있어 나무로 잘못 갈리면 널이 군데군데 박혀 보였다. 기단은 전부 돌이다.
                // 윗면을 막돌로 깔면 길쭉한 돌이 널빤지처럼 읽혀서, 밟는 면은 판석 바닥으로 깐다.
                case Skin.Base: swatch = n.y > 0.7f ? Flagstones : DarkRubble; break;
                default: swatch = wood ? Planks : Rubble; break;
            }

            Color painted = VillageColorGrade.Grade(Triplanar(swatch, p, n), SwatchMean(swatch), swatch.target);
            float shade = Mathf.Clamp(lum / mean, 0.5f, 1.5f);
            painted *= Mathf.Lerp(1f, shade, 0.6f);
            // 쇠띠·깊은 틈은 어두운 채로 남긴다.
            if (lum < 0.06f) painted = Color.Lerp(painted, c, 0.7f);
            painted.a = 1f;
            px[i] = painted;
        }

        texture.SetPixels32(px);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToJPG(90));
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = width;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static float Luminance(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

    // 면 방향으로 세 평면 투영을 섞는다. 세운 면은 v가 높이라 세로 널은 세로로, 돌담 줄눈은 가로로 선다.
    private static Color Triplanar(Swatch swatch, Vector3 p, Vector3 n)
    {
        float wx = Mathf.Pow(Mathf.Abs(n.x), 4f), wy = Mathf.Pow(Mathf.Abs(n.y), 4f), wz = Mathf.Pow(Mathf.Abs(n.z), 4f);
        float total = Mathf.Max(1e-5f, wx + wy + wz);
        Color color = Color.black;
        if (wx > 0.01f) color += Sample(swatch, p.z, p.y) * (wx / total);
        if (wy > 0.01f) color += Sample(swatch, p.x, p.z) * (wy / total);
        if (wz > 0.01f) color += Sample(swatch, p.x, p.y) * (wz / total);
        return color;
    }

    // 견본을 거울 반복으로 깐다 — 견본은 이음매 없이 이어지게 그린 것이 아니라서, 그냥 반복하면 경계에 선이 생긴다.
    private static Color Sample(Swatch swatch, float uMeters, float vMeters)
    {
        int w = swatch.x1 - swatch.x0, h = swatch.y1 - swatch.y0;
        float u = Mirror(uMeters / swatch.meters);
        float v = Mirror(vMeters / (swatch.meters * h / w));

        // 아틀라스 픽셀 좌표(아래 원점). 견본 아래 가장자리 줄이 v=0이다.
        float x = swatch.x0 + u * (w - 1);
        float y = (atlasSize - 1 - swatch.y1) + v * (h - 1);
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float tx = x - ix, ty = y - iy;
        Color a = Pixel(ix, iy), b = Pixel(ix + 1, iy), c = Pixel(ix, iy + 1), d = Pixel(ix + 1, iy + 1);
        return Color.Lerp(Color.Lerp(a, b, tx), Color.Lerp(c, d, tx), ty);
    }

    private static Color Pixel(int x, int y)
    {
        x = Mathf.Clamp(x, 0, atlasSize - 1);
        y = Mathf.Clamp(y, 0, atlasSize - 1);
        return atlasPixels[y * atlasSize + x];
    }

    private static float Mirror(float t)
    {
        t = Mathf.Repeat(t, 2f);
        return t > 1f ? 2f - t : t;
    }
}
