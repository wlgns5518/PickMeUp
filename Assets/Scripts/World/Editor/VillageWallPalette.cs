using System.IO;
using UnityEditor;
using UnityEngine;

// 성벽에 원작 마을 색을 입힌다(2026-09-25 사용자가 원작 웹툰 마을 그림을 주며 "이런 색으로").
// 원작 성벽은 푸른 기가 도는 밝은 회색 돌에 이끼가 위에서 흘러내린다. Meshy 성벽 파츠는 어두운 회색이다.
// 시설에도 같은 색을 입혀 봤지만 사용자가 "벽만 이 색"이라고 했다. 시설은 마을 다른 건물의 재질을 쓴다(VillageGaiaSkin).
//
// 파츠 머티리얼은 그대로 두고, 다시 칠한 텍스처·머티리얼(Materials/Wall/)과 그것을 끼운 파츠 사본 프리팹
// (Prefabs/Wall_Segment·Wall_Tower)을 만들어 PolygonWall의 칸에 건다.
//   - 돌: 원래 텍스처의 밝기를 밝은 청회색 돌 사다리에 옮긴다. 명암·결은 그대로 남는다.
//   - 이끼: 텍스처 칸마다 3D 위치·면 방향(VillagePartSurface)에서 정한다 — 위를 보는 면에 덩어리,
//           세운 면에는 위에서 아래로 흐르는 줄, 꼭대기(성가퀴)에 띠.
public static class VillageWallPalette
{
    private const string Folder = "Assets/Environment/Village/Materials/Wall";
    private const string PartsRoot = "Assets/Environment/Village/Parts";

    // 원작 그림에서 뽑은 색(sRGB, k-평균). 밝기 사다리는 어두움 → 중간 → 밝음.
    private static readonly Color[] StoneRamp = { C(0.20f, 0.25f, 0.29f), C(0.44f, 0.52f, 0.58f), C(0.72f, 0.80f, 0.85f) };
    private static readonly Color MossDark = C(0.17f, 0.23f, 0.14f);
    private static readonly Color MossLight = C(0.32f, 0.40f, 0.24f);
    // 채도 있는 곳(나무 비계 등)은 원래 색을 이만큼 식혀 남긴다.
    private const float KeepColored = 0.7f;
    private const float MossAmount = 0.75f;

    private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

    [MenuItem("PickMeUp/Village/8. 성벽 색 입히기 (원작 마을 색)", priority = 64)]
    public static void Apply()
    {
        GameObject segment = WallPrefab("wall_segment", "Wall_Segment");
        GameObject tower = WallPrefab("wall_tower", "Wall_Tower");
        AssetDatabase.SaveAssets();

        var wall = Object.FindAnyObjectByType<PolygonWall>();
        if (wall == null) return;
        var so = new SerializedObject(wall);
        // 성벽을 임시 상자로 되돌려 둔 씬이면 그대로 둔다.
        if (so.FindProperty("segmentPrefab").objectReferenceValue == null) return;
        so.FindProperty("segmentPrefab").objectReferenceValue = segment;
        so.FindProperty("cornerPrefab").objectReferenceValue = tower;
        so.ApplyModifiedProperties();
        wall.Rebuild();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(wall.gameObject.scene);
    }

    /// 성벽 파츠에 색을 입힌 사본 프리팹(Prefabs/Wall_xxx.prefab). PolygonWall의 칸에 건다.
    public static GameObject WallPrefab(string id, string file)
    {
        var part = AssetDatabase.LoadAssetAtPath<GameObject>(VillagePartBaker.PrefabPath(id));
        if (part == null) return null;
        Material material = Painted(id, part);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(part);
        try
        {
            go.name = file;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return PrefabUtility.SaveAsPrefabAsset(go, VillagePrefabAssembler.PrefabPath(file));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // 파츠 머티리얼을 그대로 베낀 뒤 텍스처만 바꾼다. 칠할 때마다 다시 베껴서, 파츠를 다시 앉혀도 따라간다.
    private static Material Painted(string id, GameObject part)
    {
        Material source = part.GetComponent<MeshRenderer>().sharedMaterial;
        Directory.CreateDirectory(Folder);
        string path = $"{Folder}/{id}.mat";

        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(source) { name = id };
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = source.shader;
            material.CopyPropertiesFromMaterial(source);
            material.shaderKeywords = source.shaderKeywords;
            material.enableInstancing = source.enableInstancing;
        }

        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BaseMap", Paint(id, part.GetComponent<MeshFilter>().sharedMesh));
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Texture2D Paint(string id, Mesh mesh)
    {
        string path = $"{Folder}/{id}_albedo.jpg";
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.LoadImage(File.ReadAllBytes($"{PartsRoot}/{id}/{id}_albedo.jpg"));
        int width = texture.width, height = texture.height;
        VillagePartSurface surface = VillagePartSurface.Unwrap(mesh, width, height);

        Color32[] px = texture.GetPixels32();
        for (int i = 0; i < px.Length; i++)
        {
            float moss = surface != null && surface.covered[i] ? MossAt(surface.positions[i], surface.normals[i], surface.bounds) : 0f;
            Color c = px[i];
            float lum = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            float saturation = max > 0.001f ? (max - min) / max : 0f;

            float t = Mathf.Clamp01(Mathf.InverseLerp(0.05f, 0.55f, lum));
            Color painted = t < 0.5f ? Color.Lerp(StoneRamp[0], StoneRamp[1], t * 2f) : Color.Lerp(StoneRamp[1], StoneRamp[2], (t - 0.5f) * 2f);
            // 원작 그림은 전체가 푸른 기라 남기는 색도 조금 식힌다.
            var cooled = new Color(c.r * 0.86f, c.g * 0.93f, c.b);
            painted = Color.Lerp(painted, cooled, Smooth(0.18f, 0.4f, saturation) * KeepColored);
            painted = Color.Lerp(painted, Color.Lerp(MossDark, MossLight, t), moss * MossAmount);
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

    // 파츠 공간은 미터라 무늬 크기가 파츠마다 비슷하다(세울 때 늘리면 같이 커진다).
    // 첫 값(문턱 0.50, 밝은 연두)은 이끼가 거의 다 덮어 과했다.
    private static float MossAt(Vector3 p, Vector3 n, Bounds bounds)
    {
        float up = Mathf.Clamp01(n.y);
        float height01 = Mathf.InverseLerp(bounds.min.y, bounds.max.y, p.y);

        float patches = Fbm(p * 0.45f + new Vector3(11.3f, 3.7f, 5.1f));
        float lump = Smooth(0.60f, 0.72f, patches + up * 0.15f);

        // 줄: 세로로 길게 늘인 노이즈. 꼭대기에서 시작해 아래로 갈수록 끊긴다.
        float streaks = Fbm(new Vector3(p.x * 1.1f, p.y * 0.16f, p.z * 1.1f) + new Vector3(2.9f, 7.3f, 13.7f));
        float drip = Smooth(0.60f, 0.72f, streaks) * Mathf.Pow(height01, 1.5f) * (1f - up * 0.6f);

        // 꼭대기 테두리(성가퀴)에 띠처럼 붙은 이끼.
        float rim = Smooth(0.82f, 0.97f, height01) * Smooth(0.45f, 0.6f, patches);

        return Mathf.Clamp01(Mathf.Max(lump, Mathf.Max(drip * 0.9f, rim * 0.8f)));
    }

    private static float Smooth(float from, float to, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));

    // 격자 값 노이즈(0~1). 씨앗이 고정이라 다시 칠해도 같은 이끼가 난다.
    private static float Fbm(Vector3 p) => Noise(p) * 0.5f + Noise(p * 2.03f) * 0.3f + Noise(p * 4.07f) * 0.2f;

    private static float Noise(Vector3 p)
    {
        int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
        float fx = p.x - x, fy = p.y - y, fz = p.z - z;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
        float x00 = Mathf.Lerp(Hash(x, y, z), Hash(x + 1, y, z), fx);
        float x10 = Mathf.Lerp(Hash(x, y + 1, z), Hash(x + 1, y + 1, z), fx);
        float x01 = Mathf.Lerp(Hash(x, y, z + 1), Hash(x + 1, y, z + 1), fx);
        float x11 = Mathf.Lerp(Hash(x, y + 1, z + 1), Hash(x + 1, y + 1, z + 1), fx);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
    }

    private static float Hash(int x, int y, int z)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + z * 1274126177;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xffffff) / (float)0xffffff;
        }
    }
}
