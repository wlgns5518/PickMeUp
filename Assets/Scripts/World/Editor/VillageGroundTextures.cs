using System.IO;
using UnityEditor;
using UnityEngine;

// 마을 바닥에 까는 재질 넷: 도로(밝은 돌 포장), 도로 연석, 부지(잔디), 땅(어두운 돌 포장).
//
// 돌 포장 무늬는 코드로 만든다. 프로젝트와 Gaia 어디에도 이어 붙일 수 있는 포장 무늬가 없었고(판석 텍스처는 메시 전용
// 아틀라스), 새로 그리는 데 크레딧을 쓰지 않기로 했다. 4m x 4m 한 장에 판석을 엇갈려 쌓고, 줄눈·돌마다 색 편차·
// 모서리 깎임·잔무늬를 넣는다. 가로 줄은 폭의 합이 한 장 폭과 같고 끝에서 처음으로 이어지며, 잔무늬도 주기 격자
// 노이즈라 가로세로 어디로 이어 붙여도 이음새가 없다. 같은 씨앗이면 언제 다시 만들어도 같은 무늬다.
//
// 잔디는 Gaia에 딸린 Megascans 지형 무늬를 쓴다(저장소 밖 — Gaia가 없으면 색만 남는다).
// 무늬는 전부 마을 좌표(미터)로 입힌다(VillageBlockout 길·부지 메시가 그렇게 UV를 준다) — 겹친 길의 이음매가 보이지 않는 이유.
public static class VillageGroundTextures
{
    private const string Folder = "Assets/Environment/Village/Materials";
    private const string TextureFolder = Folder + "/Textures";
    private const string AlbedoPath = TextureFolder + "/Flagstone_albedo.png";
    private const string NormalPath = TextureFolder + "/Flagstone_normal.png";

    private const int Size = 1024;
    private const float TileMeters = 4f;
    private const int Seed = 20260924;

    private const string Megascans = "Assets/Procedural Worlds/Packages - Install/Book of The Dead/Content Resources/Environment/" +
                                     "_ExternalContent/Quixel/Megascans/TerrainTextures/";
    private const string GrassAlbedo = Megascans + "Grass_pe1jvwp0/Grass_pe1jvwp0_Albedo.tif";

    [MenuItem("PickMeUp/Village/돌 포장 무늬 다시 만들기", priority = 70)]
    private static void RegenerateMenu()
    {
        Generate();
        Road();
        RoadEdge();
    }

    // ---- 재질 --------------------------------------------------------------------

    /// 도로 윗면. 한 장이 TileMeters를 덮게 VillageBlockout의 roadTile과 맞춘다.
    public static Material Road()
    {
        EnsureTextures();
        Material m = Lit("Road");
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath));
        SetNormal(m, AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath), 1f);
        m.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.94f));
        m.SetFloat("_Smoothness", 0.12f);
        return Save(m);
    }

    /// 도로 가장자리 연석. 같은 무늬를 어둡게 눌러 윤곽선처럼 읽히게 한다.
    public static Material RoadEdge()
    {
        EnsureTextures();
        Material m = Lit("RoadEdge");
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath));
        SetNormal(m, AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath), 1f);
        m.SetColor("_BaseColor", new Color(0.30f, 0.30f, 0.33f));
        m.SetFloat("_Smoothness", 0.1f);
        return Save(m);
    }

    /// 건물 묶음을 앉히는 잔디 부지. 밝은 풀이 튀지 않게 어두운 녹색으로 누른다.
    public static Material Lot()
    {
        Material m = Lit("Lot");
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(GrassAlbedo));
        SetNormal(m, null, 1f);
        m.SetColor("_BaseColor", new Color(0.50f, 0.58f, 0.46f));
        m.SetFloat("_Smoothness", 0.05f);
        return Save(m);
    }

    /// 길도 부지도 아닌 땅. 참고 그림처럼 성채 안은 전부 돌을 깐 광장이다 — 같은 판석을 길보다 어둡게, 조금 크게 깔아
    /// 밝은 길과 연석 윤곽선이 그 위에 도드라지게 한다(흙을 깔았더니 밭처럼 보였다).
    /// 마을 바닥 메시(ArcSlab)는 UV 0~1이 지름(약 248m)이라 타일 수를 재질에서 준다.
    public static Material Ground(float diameter)
    {
        EnsureTextures();
        Material m = Lit("Ground");
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath));
        SetNormal(m, AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath), 0.8f);
        float tiles = diameter / (TileMeters * 1.5f);
        m.SetTextureScale("_BaseMap", new Vector2(tiles, tiles));
        m.SetColor("_BaseColor", new Color(0.58f, 0.58f, 0.60f));
        m.SetFloat("_Smoothness", 0.08f);
        return Save(m);
    }

    private static Material Lit(string name)
    {
        string path = $"{Folder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Directory.CreateDirectory(Folder);
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private static void SetNormal(Material m, Texture2D normal, float strength)
    {
        m.SetTexture("_BumpMap", normal);
        m.SetFloat("_BumpScale", strength);
        if (normal != null) m.EnableKeyword("_NORMALMAP");
        else m.DisableKeyword("_NORMALMAP");
    }

    private static Material Save(Material m)
    {
        m.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---- 돌 포장 무늬 ---------------------------------------------------------------

    private static void EnsureTextures()
    {
        if (File.Exists(AlbedoPath) && File.Exists(NormalPath)) return;
        Generate();
    }

    public static void Generate()
    {
        var random = new System.Random(Seed);
        float pxPerMeter = Size / TileMeters;

        // 줄 높이(합이 한 장 높이), 줄마다 돌 폭(합이 한 장 폭)과 시작 어긋남.
        const int rows = 6;
        var rowTop = new float[rows + 1];
        {
            var h = new float[rows];
            float sum = 0f;
            for (int r = 0; r < rows; r++) { h[r] = 0.55f + (float)random.NextDouble() * 0.25f; sum += h[r]; }
            for (int r = 0; r < rows; r++) rowTop[r + 1] = rowTop[r] + h[r] / sum * TileMeters;
        }

        var stoneEdges = new float[rows][];
        var stoneShade = new float[rows][];
        var stoneTint = new float[rows][];
        var rowShift = new float[rows];
        for (int r = 0; r < rows; r++)
        {
            var widths = new System.Collections.Generic.List<float>();
            float sum = 0f;
            while (sum < TileMeters - 0.4f)
            {
                float w = 0.55f + (float)random.NextDouble() * 0.7f;
                widths.Add(w);
                sum += w;
            }
            stoneEdges[r] = new float[widths.Count + 1];
            stoneShade[r] = new float[widths.Count];
            stoneTint[r] = new float[widths.Count];
            for (int s = 0; s < widths.Count; s++)
            {
                stoneEdges[r][s + 1] = stoneEdges[r][s] + widths[s] / sum * TileMeters;
                stoneShade[r][s] = 0.30f + (float)random.NextDouble() * 0.18f;
                stoneTint[r][s] = ((float)random.NextDouble() - 0.35f) * 0.05f;   // 조금 따뜻한 쪽으로 기운 회색
            }
            rowShift[r] = (float)random.NextDouble() * TileMeters;
        }

        const float Grout = 0.035f;    // 줄눈 반폭이 아니라 전체 폭(m)
        const float Bevel = 0.07f;     // 모서리가 깎여 내려가는 폭(m)
        var height = new float[Size * Size];
        var albedo = new Color32[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            float my = (y + 0.5f) / pxPerMeter;
            int row = 0;
            while (row < rows - 1 && my >= rowTop[row + 1]) row++;
            float toRowEdge = Mathf.Min(my - rowTop[row], rowTop[row + 1] - my);

            for (int x = 0; x < Size; x++)
            {
                float mx = Mathf.Repeat((x + 0.5f) / pxPerMeter + rowShift[row], TileMeters);
                float[] edges = stoneEdges[row];
                int stone = 0;
                while (stone < edges.Length - 2 && mx >= edges[stone + 1]) stone++;
                float toStoneEdge = Mathf.Min(mx - edges[stone], edges[stone + 1] - mx);

                float u = (float)x / Size, v = (float)y / Size;
                float grain = Fbm(u, v) - 0.5f;                            // 넓은 얼룩
                float speck = Noise(u * 128f, v * 128f, 128, 7) - 0.5f;    // 잔 알갱이
                float wobble = Noise(u * 48f, v * 48f, 48, 11) - 0.5f;     // 닳아서 삐뚤어진 줄눈
                float grime = Noise(u * 24f, v * 24f, 24, 13);             // 때·이끼가 앉은 자리
                float edge = Mathf.Min(toRowEdge, toStoneEdge) + wobble * 0.05f;

                int i = y * Size + x;
                if (edge < Grout * 0.5f)
                {
                    // 줄눈: 흙과 이끼가 찬 어두운 틈
                    float g = 0.09f + speck * 0.04f;
                    albedo[i] = new Color(g * 1.02f, g * 1.06f, g * 0.92f, 1f);
                    height[i] = 0f;
                    continue;
                }

                float bevel = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Grout * 0.5f, Grout * 0.5f + Bevel, edge));
                float shade = stoneShade[row][stone] + grain * 0.16f + speck * 0.07f;
                shade *= Mathf.Lerp(0.6f, 1f, bevel);                   // 모서리는 닳고 그늘진다
                // 줄눈 가까이·때 낀 자리는 흙빛으로 어둡게.
                float dirt = Mathf.Clamp01((1f - bevel) * 0.6f + Mathf.SmoothStep(0.55f, 0.85f, grime) * 0.35f);
                float tint = stoneTint[row][stone];
                var stoneColor = new Color(shade * (1f + tint), shade, shade * (1f - tint) * 1.02f, 1f);
                var dirtColor = new Color(shade * 0.62f, shade * 0.60f, shade * 0.52f, 1f);
                albedo[i] = Color.Lerp(stoneColor, dirtColor, dirt);
                height[i] = bevel * (0.85f + grain * 0.35f) + speck * 0.06f;
            }
        }

        var normals = new Color32[Size * Size];
        const float Strength = 4f;
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float l = height[y * Size + (x + Size - 1) % Size], r = height[y * Size + (x + 1) % Size];
            float d = height[((y + Size - 1) % Size) * Size + x], t = height[((y + 1) % Size) * Size + x];
            var n = new Vector3((l - r) * Strength, (d - t) * Strength, 1f).normalized;
            normals[y * Size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
        }

        Directory.CreateDirectory(TextureFolder);
        Write(AlbedoPath, albedo, false);
        Write(NormalPath, normals, true);
    }

    private static void Write(string path, Color32[] pixels, bool normal)
    {
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        try
        {
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !normal;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = Size;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SaveAndReimport();
    }

    // 주기 격자 값 노이즈. 격자 칸 수(period)마다 되풀이되어 한 장 끝과 끝이 이어진다.
    private static float Noise(float x, float y, int period, int salt)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Hash(x0, y0, period, salt), b = Hash(x0 + 1, y0, period, salt);
        float c = Hash(x0, y0 + 1, period, salt), d = Hash(x0 + 1, y0 + 1, period, salt);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    private static float Fbm(float u, float v)
    {
        float sum = 0f, amp = 0.5f, total = 0f;
        for (int o = 0, period = 8; o < 4; o++, period *= 2, amp *= 0.5f)
        {
            sum += Noise(u * period, v * period, period, 31 + o) * amp;
            total += amp;
        }
        return sum / total;
    }

    private static float Hash(int x, int y, int period, int salt)
    {
        x = ((x % period) + period) % period;
        y = ((y % period) + period) % period;
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + salt * 2147483647 + Seed);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }
}
