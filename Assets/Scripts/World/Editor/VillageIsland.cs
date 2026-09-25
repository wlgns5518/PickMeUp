using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 거점을 하늘에 떠 있는 섬(천공의 성 라퓨타처럼)으로 짓는다 — Gaia 지형이 윗면, 코드로 만든 바위가 아랫면.
// 2026-09-25 사용자: "섬과 가이아 평면이 안 맞는다, 섬을 없애고 가이아로 다시" → 바다 섬으로 지었더니 "라퓨타처럼 떠 있게, 섬이 아니야".
// 예전 떠 있는 섬(FloatingIsland 메시, 둥근 윗면)은 네모난 Gaia 지형과 윗면 높이·윤곽이 어긋났다. 이제 윤곽 하나로 둘을 맞춘다:
//   - 윤곽 R(θ): Gaia 도장(Stamps/Islands "2k Island 1")의 땅이 가운데에서 어디까지 이어지는지 각도마다 재서 매끈하게 편 것.
//                성벽·숲 띠(185m)를 담도록 222m보다 안쪽으로는 들어오지 않는다.
//   - 윗면(지형): 가운데 약 205m 원판은 마을 높이(월드 0 — 마을이 지형 높이를 따른다, VillageBlockout.GroundY), 바깥은 도장의 낮은
//                 구릉, 가장자리는 살짝 말려 내려간다. R 밖은 지형 구멍(holes)으로 뚫어 네모 지형이 보이지 않는다.
//   - 아랫면(바위 메시): R을 따라 가장자리 바로 밑에서 시작하는 둥근 사발 — 테두리는 절벽처럼 떨어지고 바닥은 둥글게 닫히며,
//                        바닥 깊이가 자리마다 흔들려 바위 덩이가 들쭉날쭉 매달린다(뾰족한 원뿔은 "너무 뾰족하다"고 했다).
//                        세로 골·굴곡은 예전 FloatingIsland 방식. 무늬는 지형 바위 레이어 텍스처.
//   - 하늘: 바다를 걷고, 섬 아래에 구름 바다(입자, VillageFx)를 깔고, 하늘 아래쪽·안개를 하늘색으로(VillageMood).
public static class VillageIsland
{
    private const string Stamp = "Assets/Procedural Worlds/Packages - Install/Stamps/Islands/2k Island 1.exr";
    private const string MeshPath = "Assets/Environment/Village/Meshes/IslandUnderside.asset";
    private const string RockMaterialPath = "Assets/Materials/Environment/M_IslandRock.mat";

    private const float Size = 800f;          // 지형 한 변(미터)
    private const float Floor = -40f;         // 지형 바닥(월드 y)
    private const float Top = 30f;            // 지형 꼭대기(월드 y)
    private const float Plateau = 0f;         // 마을 높이
    private const float PlateauRadius = 205f; // 성벽 124 + 숲 띠 185 바깥까지 평평
    private const float MinRadius = 222f;     // 윤곽이 이보다 안으로 들어오지 않는다
    private const float MaxRadius = 370f;     // 지형(±400) 안
    private const float LandThreshold = 0.12f;
    private const int Angles = 360;

    private const float Depth = 220f;         // 아랫면 사발 바닥까지(월드 0에서, 자리마다 ±40%)
    private const int SideRings = 40;
    private const float BowlPower = 2.2f;     // 초타원 지수 — 2면 반구(둥근 배), 클수록 옆이 곧게 서고 바닥이 넓게 평평(2.6은 납작한 판 같았다)
    private const float RockNoise = 0.14f;
    private const float GrooveDepth = 0.09f;
    private const float RockTile = 12f;       // 바위 무늬 한 장(미터)
    private const int Seed = 20260925;

    // 레이어(지형에 이미 걸린 순서): 0 풀, 4 바위2, 5 바위1, 7 잔가지, 10 마른 풀
    private const int GrassLayer = 0, RockLayer = 4, RockAltLayer = 5, SticksLayer = 7, DryGrassLayer = 10;

    [MenuItem("PickMeUp/Village/13. 떠 있는 섬 다시 만들기 (Gaia 지형 + 바위 아랫면)", priority = 70)]
    public static void Rebuild()
    {
        RemoveFloatingIsland();
        Terrain terrain = Object.FindAnyObjectByType<Terrain>();
        if (terrain == null) { Debug.LogWarning("[VillageIsland] 지형이 없다."); return; }

        Undo.RegisterCompleteObjectUndo(new Object[] { terrain.terrainData, terrain.transform }, "떠 있는 섬");
        TerrainData data = terrain.terrainData;
        data.size = new Vector3(Size, Top - Floor, Size);
        terrain.transform.position = new Vector3(-Size * 0.5f, Floor, -Size * 0.5f);

        float[,] stamp = LoadStamp(data.heightmapResolution);
        float[] outline = Outline(stamp, data.heightmapResolution);

        Shape(data, stamp, outline);
        Holes(data, outline);
        Paint(data, outline);
        CullTrees(data, outline);
        Underside(terrain, outline);
        RemoveSea(terrain);
        Clouds(terrain);

        VillageDecor.Apply();   // 성벽 밖 숲 띠를 새 지형 위에 다시 심고 마을·성벽을 다시 세운다
        VillageMood.Apply();    // 하늘 아래쪽·안개를 하늘색으로

        EditorUtility.SetDirty(data);
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
    }

    // ---- 예전 섬·바다 걷기 ---------------------------------------------------------------

    private static void RemoveFloatingIsland()
    {
        // 섬 스크립트(FloatingIsland)는 걷은 뒤 지웠다 — 예전 씬에 남아 있을 때만 이름으로 찾는다.
        Component island = null;
        foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (behaviour != null && behaviour.GetType().Name == "FloatingIsland") { island = behaviour; break; }
        if (island == null)
        {
            GameObject missing = GameObject.Find("Environment/FloatingIsland");
            if (missing == null) return;
            island = missing.transform;
        }
        // 마을·성벽이 섬의 자식이다 — 월드 자리를 지킨 채 섬의 부모로 올린 뒤 섬만 지운다.
        Transform parent = island.transform.parent;
        var children = new List<Transform>();
        foreach (Transform child in island.transform) children.Add(child);
        foreach (Transform child in children) Undo.SetTransformParent(child, parent, true, "섬 걷기");
        Undo.DestroyObjectImmediate(island.gameObject);
    }

    private static void RemoveSea(Terrain terrain)
    {
        Transform sea = terrain.transform.parent != null ? terrain.transform.parent.Find("Sea") : null;
        if (sea != null) Undo.DestroyObjectImmediate(sea.gameObject);
    }

    // ---- 윤곽 -----------------------------------------------------------------------

    // 각도(0~360, 정북에서 시계 방향)마다 섬 가장자리 반지름. 가운데 원판 밖으로 도장의 땅이 끊이지 않고 이어지는 데까지.
    private static float[] Outline(float[,] stamp, int res)
    {
        var raw = new float[Angles];
        for (int a = 0; a < Angles; a++)
        {
            float angle = a / (float)Angles * Mathf.PI * 2f;
            float r = PlateauRadius;
            for (; r < MaxRadius; r += 2f)
            {
                float s = StampAt(stamp, res, Mathf.Sin(angle) * r, Mathf.Cos(angle) * r);
                if (s < LandThreshold) break;
            }
            raw[a] = Mathf.Clamp(r, MinRadius, MaxRadius);
        }

        // 뾰족한 곶이 튀지 않게 원형 이동 평균 두 번, 잔 흔들림을 더한다.
        float[] smooth = Smooth(Smooth(raw, 6), 4);
        for (int a = 0; a < Angles; a++)
        {
            float angle = a / (float)Angles * Mathf.PI * 2f;
            smooth[a] += (Mathf.PerlinNoise(Mathf.Cos(angle) * 6f + 41f, Mathf.Sin(angle) * 6f + 9f) - 0.5f) * 8f;
            smooth[a] = Mathf.Clamp(smooth[a], MinRadius, MaxRadius);
        }
        return smooth;
    }

    private static float[] Smooth(float[] values, int half)
    {
        var result = new float[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            float sum = 0f;
            for (int k = -half; k <= half; k++) sum += values[(i + k + values.Length) % values.Length];
            result[i] = sum / (half * 2 + 1);
        }
        return result;
    }

    // 월드 (x,z)에서의 윤곽 반지름(각도 보간).
    private static float RadiusAt(float[] outline, float x, float z)
    {
        float angle = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
        if (angle < 0f) angle += 360f;
        float f = angle / 360f * Angles;
        int i0 = Mathf.FloorToInt(f) % Angles;
        int i1 = (i0 + 1) % Angles;
        return Mathf.Lerp(outline[i0], outline[i1], f - Mathf.Floor(f));
    }

    // ---- 윗면(지형) -------------------------------------------------------------------

    private static void Shape(TerrainData data, float[,] stamp, float[] outline)
    {
        int res = data.heightmapResolution;
        var heights = new float[res, res];
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float wx = x / (float)(res - 1) * Size - Size * 0.5f;
            float wz = y / (float)(res - 1) * Size - Size * 0.5f;
            heights[y, x] = Mathf.InverseLerp(Floor, Top, HeightAt(wx, wz, stamp[y, x], outline));
        }
        data.SetHeights(0, 0, heights);
    }

    private static float HeightAt(float x, float z, float stamp, float[] outline)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        float edge = RadiusAt(outline, x, z);
        float angle = Mathf.Atan2(x, z);
        // 원판 가장자리를 각도마다 흔든다(늘 윤곽보다 10m 안쪽).
        float plateauEdge = Mathf.Min(PlateauRadius + (Mathf.PerlinNoise(Mathf.Cos(angle) * 2f + 11f, Mathf.Sin(angle) * 2f + 7f) - 0.3f) * 14f,
                                      edge - 10f);

        // 원판 밖 낮은 땅: 도장이 높을수록 원판에 가깝게(-1.5), 낮으면 -8까지. 잔 기복을 준다.
        float land = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LandThreshold, 0.45f, stamp));
        float bumps = (Mathf.PerlinNoise(x * 0.03f + 101f, z * 0.03f + 57f) - 0.5f) * 3f;
        float lowland = Mathf.Min(Mathf.Lerp(-8f, -1.5f, land) + bumps, Plateau - 1.5f);

        float h = Mathf.Lerp(Plateau, lowland, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(plateauEdge, plateauEdge + 12f, r)));
        // 가장자리가 살짝 말려 내려간다(바위 아랫면이 그 밑에서 시작한다).
        h -= 3f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge - 14f, edge, Mathf.Min(r, edge)));
        return h;
    }

    // R 밖을 뚫는다. 구멍 칸은 높이 칸 사이의 네모(해상도 -1).
    private static void Holes(TerrainData data, float[] outline)
    {
        int res = data.holesResolution;
        var solid = new bool[res, res];
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float wx = (x + 0.5f) / res * Size - Size * 0.5f;
            float wz = (y + 0.5f) / res * Size - Size * 0.5f;
            solid[y, x] = Mathf.Sqrt(wx * wx + wz * wz) <= RadiusAt(outline, wx, wz);
        }
        data.SetHoles(0, 0, solid);
    }

    private static void Paint(TerrainData data, float[] outline)
    {
        int res = data.alphamapResolution, layers = data.alphamapLayers;
        var maps = new float[res, res, layers];
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float u = (x + 0.5f) / res, v = (y + 0.5f) / res;
            float h = data.GetInterpolatedHeight(u, v) + Floor;
            float slope = data.GetSteepness(u, v);
            float wx = u * Size - Size * 0.5f, wz = v * Size - Size * 0.5f;
            float r = Mathf.Sqrt(wx * wx + wz * wz);
            float edge = RadiusAt(outline, wx, wz);

            var w = new float[layers];
            // 가장자리 몇 미터는 바위가 드러난다 — 풀 윗면과 바위 아랫면이 이어져 보이게.
            float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge - 7f, edge - 1f, r));
            float rock = Mathf.Max(rim, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(22f, 38f, slope)));
            float plateau = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-2.5f, -0.3f, h));
            float dry = Mathf.PerlinNoise(wx * 0.02f + 13f, wz * 0.02f + 29f);
            float sticks = Mathf.PerlinNoise(wx * 0.05f + 71f, wz * 0.05f + 3f);

            float land = 1f - rock;
            // 평지(마을 높이)는 원래 풀 그대로, 낮은 땅은 풀·마른 풀·잔가지를 노이즈로 섞는다.
            w[GrassLayer] = land * Mathf.Lerp(1f - dry * 0.6f, 1f, plateau);
            w[DryGrassLayer] = land * (1f - plateau) * dry * 0.6f;
            w[SticksLayer] = land * (1f - plateau) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.8f, sticks)) * 0.5f;
            w[RockLayer] = rock * (0.6f + 0.4f * sticks);
            w[RockAltLayer] = rock * (0.4f - 0.4f * sticks);

            float sum = 0f;
            for (int k = 0; k < layers; k++) sum += w[k];
            for (int k = 0; k < layers; k++) maps[y, x, k] = sum > 0f ? w[k] / sum : (k == GrassLayer ? 1f : 0f);
        }
        data.SetAlphamaps(0, 0, maps);
    }

    // 섬 밖(구멍)·가장자리 3m 안·가파른 자리의 나무를 지운다. 지형 크기가 바뀌면 나무 자리(0~1 비율)가 같이 늘어난다.
    private static void CullTrees(TerrainData data, float[] outline)
    {
        var kept = new List<TreeInstance>();
        foreach (TreeInstance tree in data.treeInstances)
        {
            float wx = tree.position.x * Size - Size * 0.5f, wz = tree.position.z * Size - Size * 0.5f;
            float r = Mathf.Sqrt(wx * wx + wz * wz);
            if (r > RadiusAt(outline, wx, wz) - 3f) continue;
            if (data.GetSteepness(tree.position.x, tree.position.z) > 35f) continue;
            kept.Add(tree);
        }
        data.SetTreeInstances(kept.ToArray(), true);
    }

    // ---- 아랫면(바위 메시) ----------------------------------------------------------------

    private static void Underside(Terrain terrain, float[] outline)
    {
        TerrainData data = terrain.terrainData;
        var random = new System.Random(Seed);
        float phaseA = (float)random.NextDouble() * Mathf.PI * 2f;
        float phaseC = (float)random.NextDouble() * Mathf.PI * 2f;
        float noiseOffset = (float)random.NextDouble() * 100f;

        int columns = Angles;   // 1도마다 한 줄, 이음매 열을 하나 더 둬 무늬가 한 바퀴 돌아 이어진다
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        // 윗고리: 가장자리 바로 안쪽, 지형 높이보다 조금 밑 — 지형 가장자리가 그 위를 덮는다.
        var rimHeights = new float[columns];
        float circumference = 0f;
        for (int a = 0; a < columns; a++)
        {
            float angle = a / (float)columns * Mathf.PI * 2f;
            float r = outline[a] - 0.8f;
            float wx = Mathf.Sin(angle) * r, wz = Mathf.Cos(angle) * r;
            rimHeights[a] = terrain.SampleHeight(new Vector3(wx, 0f, wz)) + terrain.transform.position.y - 0.3f;
            circumference += outline[a] * Mathf.PI * 2f / columns;
        }

        // 둥근 사발(라퓨타 아랫면): 테두리(s=1)에서 거의 수직으로 떨어지다 바닥(s=0)에서 둥글게 닫힌다 — 초타원 단면.
        // 고리는 단면 곡선의 각도(phi)로 나눠 가파른 테두리 쪽에도 고리가 촘촘하다. 바닥 깊이는 자리마다 흔들려
        // 바위 덩이가 들쭉날쭉 매달린다. 예전엔 원뿔을 한 점으로 모아 아래가 뾰족했다(사용자: "아래가 너무 뾰족해").
        for (int ring = 0; ring <= SideRings; ring++)
        {
            float phi = ring / (float)SideRings * Mathf.PI * 0.5f;
            // cos(90도)가 부동소수로 아주 작은 음수가 되면 분수 거듭제곱이 NaN이 되어 메시가 통째로 사라졌다 — 0 아래를 자른다.
            float s = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(phi)), 2f / BowlPower);    // 반지름 비율 1 → 0
            float down = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(phi)), 2f / BowlPower); // 깊이 비율 0 → 1
            // 테두리 바로 밑은 살짝 불룩(처마 바위).
            s *= 1f + 0.035f * Mathf.Sin(Mathf.Clamp01(down * 6f) * Mathf.PI);
            for (int a = 0; a <= columns; a++)
            {
                int i = a % columns;
                float angle = i / (float)columns * Mathf.PI * 2f;
                float baseRadius = outline[i] - 0.8f;
                float top = rimHeights[i];

                float bump = Mathf.PerlinNoise(Mathf.Cos(angle) * 2.2f + noiseOffset, Mathf.Sin(angle) * 2.2f + noiseOffset + down * 3.5f) - 0.5f;
                // 잔 바위 결 — 큰 굴곡만 있으면 매끈한 사발로 보였다.
                bump += (Mathf.PerlinNoise(Mathf.Cos(angle) * 9f + noiseOffset * 2f, Mathf.Sin(angle) * 9f + down * 14f) - 0.5f) * 0.45f;
                float groove = Mathf.Sin(angle * 13f + phaseA) * 0.6f + Mathf.Sin(angle * 21f + phaseC) * 0.4f;
                float r = ring == 0
                    ? baseRadius
                    : baseRadius * s * (1f + RockNoise * bump * 2f * Mathf.Clamp01(down * 3f)) * (1f + GrooveDepth * groove * Mathf.Clamp01(down * 4f) * (1f - down));
                // 풀 가장자리보다 바깥으로 삐져나오면 지느러미처럼 보였다 — 테두리 반지름을 넘지 않게.
                r = Mathf.Clamp(r, 0f, baseRadius * 1.01f);

                float x = Mathf.Sin(angle) * r, z = Mathf.Cos(angle) * r;
                // 바닥 깊이: 자리(x,z)마다 덩이진 노이즈 — 어떤 곳은 더 깊이 매달리고 어떤 곳은 얕다.
                float lumps = (Mathf.PerlinNoise(x * 0.008f + 17f, z * 0.008f + 43f) - 0.5f) * 2f * 0.3f
                            + (Mathf.PerlinNoise(x * 0.025f + 5f, z * 0.025f + 71f) - 0.5f) * 2f * 0.12f;
                float bottom = -Depth * (1f + lumps);
                float y = Mathf.Lerp(top, bottom, down);

                vertices.Add(new Vector3(x, y, z));
                uvs.Add(new Vector2(a / (float)columns * circumference / RockTile, (y + r * 0.3f) / RockTile));
            }
        }

        int stride = columns + 1;
        for (int ring = 0; ring < SideRings; ring++)
        for (int a = 0; a < columns; a++)
        {
            int i0 = ring * stride + a, i1 = i0 + 1, j0 = i0 + stride, j1 = j0 + 1;
            // 바깥을 보는 면(시계 방향 각도 증가 = +x 쪽)
            triangles.Add(i0); triangles.Add(i1); triangles.Add(j0);
            triangles.Add(i1); triangles.Add(j1); triangles.Add(j0);
        }

        // 이미 있는 에셋이면 그 메시에 바로 쓴다. 새 메시를 만들어 CopySerialized로 덮으면 데이터는 바뀌어도 GPU 버퍼가
        // 다시 올라가지 않아, 에디터를 다시 열 때까지 옛 모양(뾰족한 원뿔)이 그려졌다.
        Directory.CreateDirectory(Path.GetDirectoryName(MeshPath));
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        bool created = mesh == null;
        if (created) mesh = new Mesh { name = "IslandUnderside" };
        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        // 바깥을 보게 감았는지 확인 — 윗고리 첫 정점의 법선이 가운데 반대쪽이어야 한다.
        if (Vector3.Dot(mesh.normals[0], new Vector3(vertices[0].x, 0f, vertices[0].z)) < 0f)
        {
            for (int k = 0; k < triangles.Count; k += 3) (triangles[k + 1], triangles[k + 2]) = (triangles[k + 2], triangles[k + 1]);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
        }

        if (created) AssetDatabase.CreateAsset(mesh, MeshPath);
        else EditorUtility.SetDirty(mesh);

        Transform environment = terrain.transform.parent;
        Transform underside = environment != null ? environment.Find("Island Underside") : null;
        if (underside == null)
        {
            var go = new GameObject("Island Underside", typeof(MeshFilter), typeof(MeshRenderer));
            Undo.RegisterCreatedObjectUndo(go, "섬 아랫면");
            underside = go.transform;
            underside.SetParent(environment, false);
        }
        underside.position = Vector3.zero;
        underside.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = underside.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = RockMaterial(data);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // 지형 바위 레이어(T_ground_rock_02) 무늬로 만든 바위 머티리얼 — 윗면 가장자리의 바위와 같은 돌이다.
    private static Material RockMaterial(TerrainData data)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(RockMaterialPath);
        if (material == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RockMaterialPath));
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_IslandRock" };
            AssetDatabase.CreateAsset(material, RockMaterialPath);
        }
        TerrainLayer layer = data.terrainLayers.Length > RockLayer ? data.terrainLayers[RockLayer] : null;
        material.SetTexture("_BaseMap", layer != null ? layer.diffuseTexture : null);
        material.SetTexture("_BumpMap", layer != null ? layer.normalMapTexture : null);
        if (layer != null && layer.normalMapTexture != null) material.EnableKeyword("_NORMALMAP");
        material.SetFloat("_BumpScale", 1f);
        // 아랫면은 해를 못 받고 아래쪽 환경광만 받아 거의 검게 죽었다 — 무늬 색을 밝게 끌어올린다.
        material.SetColor("_BaseColor", new Color(1.5f, 1.55f, 1.6f));
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0.08f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---- 구름 바다 ---------------------------------------------------------------------

    private static void Clouds(Terrain terrain)
    {
        VillageFx.BuildAll();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VillageFx.PrefabPath("FX_CloudSea"));
        if (prefab == null) return;
        Transform environment = terrain.transform.parent;
        Transform clouds = environment != null ? environment.Find("Cloud Sea") : null;
        if (clouds != null) Undo.DestroyObjectImmediate(clouds.gameObject);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, environment);
        go.name = "Cloud Sea";
        go.transform.position = Vector3.zero;
        Undo.RegisterCreatedObjectUndo(go, "구름 바다");
    }

    // ---- 도장 ---------------------------------------------------------------------------

    private static float StampAt(float[,] stamp, int res, float x, float z)
    {
        float u = (x + Size * 0.5f) / Size * (res - 1), v = (z + Size * 0.5f) / Size * (res - 1);
        int ix = Mathf.Clamp(Mathf.RoundToInt(u), 0, res - 1), iz = Mathf.Clamp(Mathf.RoundToInt(v), 0, res - 1);
        return stamp[iz, ix];
    }

    // 도장(RFloat EXR)을 지형 높이맵 해상도로 줄여 읽는다. 읽기 불가 텍스처라 GPU로 그려 읽는다.
    private static float[,] LoadStamp(int res)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Stamp);
        var result = new float[res, res];
        if (texture == null) { Debug.LogWarning($"[VillageIsland] Gaia 도장이 없다: {Stamp} — 원판만 세운다."); return result; }
        RenderTexture rt = RenderTexture.GetTemporary(res, res, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        Graphics.Blit(texture, rt);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        var read = new Texture2D(res, res, TextureFormat.RGBAFloat, false, true);
        read.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        read.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        Color[] px = read.GetPixels();
        Object.DestroyImmediate(read);
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
            result[y, x] = px[y * res + x].r;
        return result;
    }
}
