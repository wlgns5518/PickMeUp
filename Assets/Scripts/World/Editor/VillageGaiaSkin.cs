using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// 소환소·합성소·무기창고·비행선착장에 마을 다른 건물(Gaia 3DForge 집·대장간)의 재질을 입힌다(2026-09-25 사용자:
// "다른 건물들에 쓰이고 있는 material을 가져다 써라").
// Gaia 머티리얼은 한 장짜리 아틀라스(fe_village_base)를 Gaia 메시의 UV에 맞춰 쓰므로 Meshy 파츠에 그대로 씌우면
// 무늬가 뒤섞인다. 대신 아틀라스의 네모 견본(돌담·나무판·너와 지붕)을 파츠 표면에 3D로 투영해(삼면 투영)
// 파츠 텍스처를 새로 굽는다 — 같은 돌, 같은 나무, 같은 지붕이 된다. 원래 텍스처는 어디가 돌이고 어디가 나무·쇠인지
// 가르는 데와 음영(홈·틈)에만 쓴다. 색 누름도 Gaia 사본 머티리얼과 같은 0.72다(VillagePrefabAssembler.GaiaTone).
//
// 파츠 머티리얼은 시공의 틈·대장간·훈련소·거리도 같이 쓰므로 그대로 두고, 입힌 사본(Materials/GaiaSkin/)을 네 시설에만
// 바꿔 낀다. 등불·화로·깃발·짐·무기대·구슬·오벨리스크·창은 원래대로 둔다(불빛과 소품).
// 조립(VillagePrefabAssembler 4번)도 저장 직전에 이것을 부르므로 다시 조립해도 남는다.
public static class VillageGaiaSkin
{
    private const string Folder = "Assets/Environment/Village/Materials/GaiaSkin";
    private const string PartsRoot = "Assets/Environment/Village/Parts";
    private const string Atlas = "Assets/Procedural Worlds/Packages - Install/Asset Samples/3DForge/Textures/fe_village_base.png";
    private const float GaiaTone = 0.72f;

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
    // meters는 견본 한 장이 덮는 폭(파츠 공간 미터).
    private sealed class Swatch
    {
        public readonly int x0, y0, x1, y1;
        public readonly float meters;

        public Swatch(int x0, int y0, int x1, int y1, float meters)
        {
            this.x0 = x0; this.y0 = y0; this.x1 = x1; this.y1 = y1; this.meters = meters;
        }
    }

    private static readonly Swatch Shingles = new Swatch(1700, 30, 2450, 625, 3f);
    private static readonly Swatch Rubble = new Swatch(30, 2010, 805, 2935, 3f);
    private static readonly Swatch DarkRubble = new Swatch(855, 2010, 1630, 2935, 3f);
    private static readonly Swatch Planks = new Swatch(875, 1020, 1630, 1960, 2.4f);
    private static readonly Swatch DeckPlanks = new Swatch(30, 1020, 805, 1960, 3f);
    private static readonly Swatch Flagstones = new Swatch(2790, 2950, 3115, 3980, 2.4f);   // 네 줄 판석 띠

    // 한 번 입히는 동안 같은 파츠·같은 재질은 한 번만 굽는다.
    private static readonly Dictionary<string, Material> Baked = new Dictionary<string, Material>();
    private static Color32[] atlasPixels;
    private static int atlasSize;

    // ---- 메뉴 -----------------------------------------------------------------

    [MenuItem("PickMeUp/Village/9. 시설에 마을 건물 재질 입히기 (Gaia)", priority = 65)]
    public static void ApplyAll()
    {
        Baked.Clear();
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

        material.SetTexture("_BaseMap", Bake(id, skin, $"{Folder}/{name}.jpg"));
        material.SetColor("_BaseColor", new Color(GaiaTone, GaiaTone, GaiaTone, 1f));
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

            Color painted = Triplanar(swatch, p, n);
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
