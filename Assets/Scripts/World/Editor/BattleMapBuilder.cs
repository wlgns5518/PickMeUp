using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = System.Random;

// 다섯 층마다 바뀌는 전투 맵을 만드는 도구.
//
// 1~5층 평야(Floor1~5)가 원본이다. 나머지 구간은 그 씬을 복사해 전투 장치(스포너·HUD·카메라·볼륨·NavMesh 표면)는
// 그대로 두고, 지형과 바닥·풀·소품·하늘·빛·날씨만 BattleMapThemes의 한 줄대로 갈아 끼운다.
// 씬마다 딸린 에셋(지형 데이터·레이어·NavMesh·하늘·볼륨·재질)은 씬 이름과 같은 폴더에 모인다.
//
// 싸우는 바닥은 어느 맵이든 평평한 y=0이다. 적은 NavMesh도 지형 높이도 모른 채 평면 위를 스티어링으로
// 움직이기 때문이다(EnemyMovementSystem) — 전장에 언덕이 있으면 적이 땅에 파묻히거나 떠오른다.
// 그래서 산·협곡·호수 같은 지형의 성격은 전장(ArenaHalf) 밖에서만 낸다.
// 둘레의 바위와 나무도 NavMesh를 굽는 상자(±40m) 밖에만 둔다. 안에 두면 아군만 돌아가고 적은 뚫고 지나간다.
//
// 쓰는 법: PickMeUp/전투 맵/빠진 맵 만들기 — 아직 없는 구간의 씬만 만든다.
//         PickMeUp/전투 맵/모든 맵 다시 만들기 — 테마 값을 고쳤을 때. 씬에서 손으로 고친 것도 덮어쓴다.
// 어느 쪽이든 마지막에 Build Settings의 층 씬 목록을 다시 채운다.
public static class BattleMapBuilder
{
    private const string TemplateScenePath = "Assets/Scenes/Floor1~5.unity";
    private const string SceneFolder = "Assets/Scenes";
    private const string ParticleMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";

    private const float TerrainSize = 512f;
    private const int HeightmapResolution = 513;
    private const int AlphamapResolution = 512;
    // 디테일 한 칸 = 1m². 칸마다 풀 포기 수를 센다.
    private const int DetailResolution = 512;

    // 평평하게 남기는 전장의 반폭. 거리는 둥근 사각형으로 잰다(ArenaDistance).
    // NavMesh를 굽는 상자(±40m, 모서리까지 47.6m)를 다 덮고, 100층 적 무리(스폰 반경 약 38m)도 담는 크기다.
    private const float ArenaHalf = 48f;
    // 전장 가장자리부터 둘레 지형이 다 솟기까지의 폭.
    private const float RimWidth = 24f;
    // NavMesh를 굽는 상자의 반폭(Floor1~5의 NavMeshSurface 크기 80m의 절반) + 여유 1m.
    // 소품의 바운즈가 이 안으로 들어오면 버린다.
    private const float NavMeshKeepOut = 41f;

    // 날씨 입자를 뿌리는 상자. 전투 카메라에 매달아, 카메라가 내려다보는 앞쪽 부피에만 뿌린다.
    //
    // 처음에는 전장 전체(150×130m, 높이 20m)에 흩뿌렸다. 입자가 수천 개 떠 있어도 카메라가 보는 부피에는
    // 몇십 개만 들어와서 전투 화면에서는 눈도 비도 보이지 않았다(플레이 테스트).
    // 카메라는 발밑 약 55m 앞까지 보므로 상자 중심을 그 절반쯤 앞에 둔다.
    //
    // 카메라는 땅 위 6m에서 36도로 내려다보고 화면 위쪽 끝도 수평보다 6도 아래다. 그래서 카메라보다 높이 뜬 입자는
    // 하나도 보이지 않는다 — 11m에서 떨어뜨린 눈과 10m까지 떠오르던 빛 알갱이는 90%가 화면 밖이었다(실측 605개 중 552개).
    // 날씨마다 높이·속도·수명을 맞춰 입자가 땅에서 카메라 높이 사이에서만 살게 한다.
    private const float WeatherAhead = 26f;
    // 옆 폭, 앞뒤 길이. 카메라 발밑 2m 뒤부터 54m 앞까지.
    private static readonly Vector2 WeatherArea = new Vector2(70f, 56f);

    [MenuItem("PickMeUp/전투 맵/빠진 맵 만들기", priority = 30)]
    private static void BuildMissingMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildAll(overwrite: false);
    }

    [MenuItem("PickMeUp/전투 맵/모든 맵 다시 만들기", priority = 31)]
    private static void RebuildAllMenu()
    {
        if (!EditorUtility.DisplayDialog("전투 맵 다시 만들기",
                "6층부터 100층까지의 전투 씬을 테마대로 새로 만듭니다.\n씬에서 손으로 고친 내용은 사라집니다.",
                "다시 만들기", "취소")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildAll(overwrite: true);
    }

    // 1~5층(원본)을 뺀 모든 구간. overwrite가 false면 이미 있는 씬은 건너뛴다.
    public static void BuildAll(bool overwrite)
    {
        string returnTo = SceneManager.GetActiveScene().path;
        try
        {
            for (int first = FloorProgress.FirstFloor + FloorProgress.FloorsPerStage;
                 first <= FloorProgress.LastFloor;
                 first += FloorProgress.FloorsPerStage)
            {
                if (!overwrite && File.Exists(ScenePath(first))) continue;
                BuildStage(first);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            RegisterBuildScenes();
            if (!string.IsNullOrEmpty(returnTo) && File.Exists(returnTo)) EditorSceneManager.OpenScene(returnTo);
        }
    }

    // 한 구간의 씬을 (다시) 만든다. firstFloor는 구간 안의 아무 층이어도 된다.
    public static void BuildStage(int firstFloor)
    {
        int first = FloorProgress.StageFirstFloor(firstFloor);
        BattleMapTheme theme = BattleMapThemes.Get(first);
        if (theme == null)
        {
            Debug.LogWarning($"[BattleMapBuilder] {first}층 구간의 테마가 없어 건너뜁니다.");
            return;
        }

        string sceneName = FloorProgress.BattleSceneName(first);
        string scenePath = ScenePath(first);
        string folder = $"{SceneFolder}/{sceneName}";
        EditorUtility.DisplayProgressBar("전투 맵 만들기", $"{sceneName} · {FloorStages.TitleOf(first)}", first / (float)FloorProgress.LastFloor);

        // 원본 씬이 열려 있으면 복사본이 아니라 원본을 고치게 되므로 먼저 닫는다.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AssetDatabase.DeleteAsset(scenePath);
        AssetDatabase.DeleteAsset(folder);
        AssetDatabase.CreateFolder(SceneFolder, sceneName);
        if (!AssetDatabase.CopyAsset(TemplateScenePath, scenePath))
            throw new InvalidOperationException($"원본 씬을 복사하지 못했습니다: {TemplateScenePath} → {scenePath}");

        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var random = new Random(theme.Seed);
        var noise = new MapNoise(theme);

        Material terrainMaterial = StripTemplateLand(scene);
        Transform environment = FindOrCreateRoot(scene, "Environment");

        float[,] heights = BuildHeights(theme, noise, out float minHeight, out float maxHeight);
        TerrainData data = CreateTerrainData(folder, heights, minHeight, maxHeight);
        Terrain terrain = CreateTerrain(data, terrainMaterial, minHeight);

        PaintGround(theme, noise, data, folder, minHeight);
        PlantCover(theme, noise, random, data, minHeight);
        PlantVegetation(theme, noise, random, data, minHeight);
        EditorUtility.SetDirty(data);

        PlaceProps(theme, random, scene, terrain, environment);
        if (theme.HasWater) CreateWater(theme, folder, environment);
        CreateWeather(theme, folder, scene, environment);
        ApplySkyAndLight(theme, folder, environment);
        ApplyGrade(theme, folder, environment);
        BakeNavMesh(environment, folder);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[BattleMapBuilder] {sceneName} · {FloorStages.TitleOf(first)} 을(를) 만들었습니다.");
    }

    private static string ScenePath(int floor) => $"{SceneFolder}/{FloorProgress.BattleSceneName(floor)}.unity";

    // ───────────────────────── 원본에서 걷어낼 것 ─────────────────────────

    // 평야 지형과 Gaia 편집 도구를 걷어낸다. 지형 재질은 새 지형이 그대로 물려받는다.
    private static Material StripTemplateLand(Scene scene)
    {
        Material material = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            var terrain = root.GetComponentInChildren<Terrain>(true);
            if (terrain != null)
            {
                if (material == null) material = terrain.materialTemplate;
                Object.DestroyImmediate(root);
                continue;
            }

            // Gaia Tools(세션 관리)와 Gaia Runtime(지형 로더)은 원본 지형을 만들 때 쓰던 것이다.
            if (root.name.StartsWith("Gaia ", StringComparison.Ordinal)) Object.DestroyImmediate(root);
        }
        return material;
    }

    private static Transform FindOrCreateRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == name) return root.transform;
        return new GameObject(name).transform;
    }

    // ───────────────────────── 지형 높이 ─────────────────────────

    // 전장 중심에서의 "둥근 사각형" 거리. 원으로 재면 NavMesh 상자의 모서리가 전장 밖으로 삐져나오고,
    // 사각형으로 재면 둘레 지형의 모서리가 각져 보인다.
    private static float ArenaDistance(float x, float z)
    {
        float x2 = x * x, z2 = z * z;
        return Mathf.Pow(x2 * x2 + z2 * z2, 0.25f);
    }

    private static float Smooth(float from, float to, float value)
    {
        float t = Mathf.InverseLerp(from, to, value);
        return t * t * (3f - 2f * t);
    }

    // 높이를 계단으로 끊는다. riser는 한 칸 중 솟는 부분의 비율.
    private static float Terrace(float height, float step, float riser)
    {
        if (step <= 0f) return height;
        float k = height / step;
        float floor = Mathf.Floor(k);
        return step * (floor + Smooth(1f - riser, 1f, k - floor));
    }

    private static float AngularBump(float angle, float center, float width)
    {
        float delta = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, center * Mathf.Rad2Deg) * Mathf.Deg2Rad / width;
        return Mathf.Exp(-delta * delta);
    }

    // [z, x] 순서, 미터 단위. 전장은 정확히 0이다.
    private static float[,] BuildHeights(BattleMapTheme theme, MapNoise noise, out float minHeight, out float maxHeight)
    {
        var heights = new float[HeightmapResolution, HeightmapResolution];
        minHeight = 0f;
        maxHeight = 0f;
        float spacing = TerrainSize / (HeightmapResolution - 1);

        for (int zi = 0; zi < HeightmapResolution; zi++)
        {
            float z = -TerrainSize * 0.5f + zi * spacing;
            for (int xi = 0; xi < HeightmapResolution; xi++)
            {
                float x = -TerrainSize * 0.5f + xi * spacing;
                float h = LandHeight(theme, noise, x, z);
                heights[zi, xi] = h;
                if (h < minHeight) minHeight = h;
                if (h > maxHeight) maxHeight = h;
            }
        }
        return heights;
    }

    private static float LandHeight(BattleMapTheme t, MapNoise n, float x, float z)
    {
        float r = ArenaDistance(x, z);
        if (r <= ArenaHalf) return 0f;

        float rim = Smooth(ArenaHalf, ArenaHalf + RimWidth, r);
        float angle = Mathf.Atan2(z, x);
        // 어느 지형에나 얹는 잔기복(-0.5~0.5).
        float detail = n.Fbm(x * 0.04f + 40f, z * 0.04f, 3) - 0.5f;

        switch (t.Landform)
        {
            case MapLandform.Hills:
                return rim * (HillsShape(t, n, x, z, r) + detail * 4f * t.Roughness);

            case MapLandform.Mountains:
            {
                float rise = Mathf.Pow(Smooth(ArenaHalf, 250f, r), 0.8f);
                float ridge = n.Ridged(x * 0.0055f, z * 0.0055f, 5);
                float swell = n.Fbm(x * 0.012f, z * 0.012f, 3);
                return rim * (t.Relief * rise * (0.25f + 0.75f * ridge) + 8f * swell + detail * 5f * t.Roughness);
            }

            case MapLandform.Canyon:
            {
                // 벽이 서는 거리는 방향마다 크게 다르고, 두 갈래 틈으로만 벽이 낮아진다.
                // 계단을 그대로 쓰면 채석장처럼 반듯해서 원래 비탈과 반쯤 섞고 능선 잡음으로 벽면을 깨뜨린다.
                float wallAt = ArenaHalf + 6f + 30f * n.Fbm(Mathf.Cos(angle) * 2.5f + 7f, Mathf.Sin(angle) * 2.5f + 7f, 3);
                float wall = Smooth(wallAt, wallAt + 14f + 10f * n.Perlin(x * 0.02f, z * 0.02f), r);
                float gap = Mathf.Max(AngularBump(angle, n.Angle, 0.28f), AngularBump(angle, n.Angle + 2.6f, 0.2f));
                float top = t.Relief * (0.6f + 0.8f * n.Fbm(x * 0.012f, z * 0.012f, 4));
                float raw = wall * top * (1f - 0.8f * gap);
                float cliff = Mathf.Lerp(raw, Terrace(raw, t.Relief * 0.3f, 0.45f), 0.65f)
                              + wall * (n.Ridged(x * 0.03f, z * 0.03f, 3) - 0.5f) * 6f;
                float far = Smooth(150f, 250f, r) * t.Relief * 0.5f * n.Fbm(x * 0.008f, z * 0.008f, 3);
                float wash = n.Fbm(x * 0.02f, z * 0.02f, 3) - 0.5f;
                return cliff + far + rim * (wash * 2.4f + detail * 2f) * t.Roughness;
            }

            case MapLandform.Dunes:
            {
                float along = x * Mathf.Cos(n.Angle) + z * Mathf.Sin(n.Angle);
                float warp = (n.Fbm(x * 0.005f, z * 0.005f, 3) - 0.5f) * 90f;
                float crest = Mathf.Pow(0.5f + 0.5f * Mathf.Sin((along + warp) * 0.05f), 2.5f);
                float field = n.Fbm(x * 0.006f + 20f, z * 0.006f, 3);
                float rise = Smooth(ArenaHalf, 170f, r);
                return rim * (t.Relief * crest * (0.3f + 0.7f * field) * (0.35f + 0.65f * rise) + detail * 1.5f * t.Roughness);
            }

            case MapLandform.Mesas:
            {
                // 가장자리를 좁게 떨어뜨리면 원기둥 상자가 된다. 넓게 흘러내리고 잡음으로 윤곽을 깎는다.
                float ground = rim * (3f * n.Fbm(x * 0.01f, z * 0.01f, 4) + detail * 2f * t.Roughness);
                float warp = (n.Fbm(x * 0.02f, z * 0.02f, 3) - 0.5f) * 40f;
                float mesa = 0f;
                var p = new Vector2(x, z);
                foreach (MapNoise.Mesa m in n.Mesas)
                {
                    float inside = Smooth(m.Radius + 22f, m.Radius - 4f, Vector2.Distance(p, m.Center) + warp);
                    mesa = Mathf.Max(mesa, Mathf.Pow(inside, 0.6f) * m.Height);
                }
                float erosion = (n.Ridged(x * 0.05f, z * 0.05f, 2) - 0.5f) * 4f * Mathf.Min(1f, mesa * 3f);
                return ground + Terrace(mesa * t.Relief, t.Relief * 0.25f, 0.5f) + erosion;
            }

            case MapLandform.Crater:
            {
                float rimAt = ArenaHalf + 55f + 14f * (n.Perlin(Mathf.Cos(angle) * 2f + 3f, Mathf.Sin(angle) * 2f + 3f) - 0.5f) * 2f;
                float crest = r < rimAt
                    ? Mathf.Pow(Smooth(ArenaHalf, rimAt, r), 1.6f)
                    : 1f - 0.8f * Smooth(rimAt, rimAt + 120f, r);
                float jag = n.Ridged(x * 0.018f, z * 0.018f, 4);
                return t.Relief * crest * (0.7f + 0.3f * jag) + rim * detail * 3f * t.Roughness;
            }

            case MapLandform.Swamp:
            {
                // 전장 턱을 넘으면 곧 물에 잠긴 땅이고, 둔덕만 물 위로 솟는다.
                float bank = Smooth(ArenaHalf + 1f, ArenaHalf + 10f, r);
                float mound = n.Fbm(x * 0.02f, z * 0.02f, 4);
                float lowland = -1.4f + 5f * Mathf.Max(0f, mound - 0.52f);
                float far = Smooth(160f, 250f, r) * t.Relief * n.Fbm(x * 0.008f, z * 0.008f, 3);
                return bank * lowland + far + rim * detail * t.Roughness;
            }

            case MapLandform.Lakeside:
            {
                // 호수는 늘 카메라가 바라보는 쪽(-X 부근)으로 연다(MapNoise.Angle).
                float toward = (x * Mathf.Cos(n.Angle) + z * Mathf.Sin(n.Angle)) / Mathf.Max(1f, Mathf.Sqrt(x * x + z * z));
                float lake = Smooth(-0.1f, 0.45f, toward) * Smooth(ArenaHalf + 2f, ArenaHalf + 22f, r);
                float shore = rim * HillsShape(t, n, x, z, r);
                float farShore = Smooth(170f, 250f, r) * t.Relief * 1.4f * (0.4f + 0.6f * n.Ridged(x * 0.006f, z * 0.006f, 4));
                return Mathf.Lerp(shore, -7f + farShore, lake) + rim * detail * 2f * t.Roughness;
            }

            case MapLandform.Glacier:
            {
                float rise = Smooth(ArenaHalf, 240f, r);
                float swell = n.Fbm(x * 0.0045f, z * 0.0045f, 4);
                float crevasse = Mathf.Pow(n.Ridged(x * 0.01f + 9f, z * 0.01f, 2), 10f);
                return rim * (t.Relief * rise * (0.3f + 0.7f * swell) - crevasse * 5f * rise + 2f * swell + detail * 1.5f * t.Roughness);
            }

            default: // Cliffside
            {
                // 전장만 높이 남고, 가장자리 턱을 넘으면 Depth만큼 꺼진다. 저 멀리 봉우리가 다시 솟는다.
                float edgeAt = ArenaHalf + 12f + 12f * n.Perlin(Mathf.Cos(angle) * 2f + 11f, Mathf.Sin(angle) * 2f + 11f);
                float drop = Smooth(edgeAt, edgeAt + 30f, r);
                float lip = rim * (1f - drop) * (n.Ridged(x * 0.035f, z * 0.035f, 3) * 3f + detail * 2f) * t.Roughness;
                float peaks = t.Relief * Smooth(150f, 250f, r) * (0.3f + 0.7f * n.Ridged(x * 0.006f, z * 0.006f, 4));
                return lip - t.Depth * drop + peaks;
            }
        }
    }

    private static float HillsShape(BattleMapTheme t, MapNoise n, float x, float z, float r)
    {
        float rise = Smooth(ArenaHalf, 230f, r);
        float hills = n.Fbm(x * 0.007f, z * 0.007f, 5);
        return t.Relief * (0.2f + 0.8f * rise) * (0.25f + 0.75f * hills);
    }

    // ───────────────────────── 지형 데이터 ─────────────────────────

    private static TerrainData CreateTerrainData(string folder, float[,] heights, float minHeight, float maxHeight)
    {
        float range = Mathf.Max(1f, maxHeight - minHeight);
        var data = new TerrainData
        {
            heightmapResolution = HeightmapResolution,
            alphamapResolution = AlphamapResolution,
            baseMapResolution = AlphamapResolution,
        };
        data.size = new Vector3(TerrainSize, range, TerrainSize);

        var normalized = new float[HeightmapResolution, HeightmapResolution];
        for (int z = 0; z < HeightmapResolution; z++)
        for (int x = 0; x < HeightmapResolution; x++)
            normalized[z, x] = (heights[z, x] - minHeight) / range;
        data.SetHeights(0, 0, normalized);

        AssetDatabase.CreateAsset(data, folder + "/Terrain.asset");
        return data;
    }

    private static Terrain CreateTerrain(TerrainData data, Material material, float minHeight)
    {
        GameObject go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.isStatic = true;
        go.transform.position = new Vector3(-TerrainSize * 0.5f, minHeight, -TerrainSize * 0.5f);

        // 나무는 전장 밖에만 서므로 부딪힐 일이 없다. 켜 두면 팩의 나무 중 MeshCollider를 단 것이
        // "TerrainCollider: MeshCollider is not supported" 경고를 전투마다 남긴다. 인스펙터의 Enable Tree Colliders에
        // 해당하는 공개 API가 없어 직렬화 필드로 끈다.
        var collider = new SerializedObject(go.GetComponent<TerrainCollider>());
        collider.FindProperty("m_EnableTreeColliders").boolValue = false;
        collider.ApplyModifiedPropertiesWithoutUndo();

        var terrain = go.GetComponent<Terrain>();
        if (material != null) terrain.materialTemplate = material;
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 4f;
        terrain.basemapDistance = 1024f;
        terrain.treeDistance = 500f;
        terrain.treeBillboardDistance = 150f;
        terrain.treeCrossFadeLength = 10f;
        terrain.detailObjectDistance = 70f;
        terrain.detailObjectDensity = 1f;
        return terrain;
    }

    // 바닥 네 겹을 칠한다. 가파른 곳은 비탈, 높은(낮은) 곳은 High, 나머지 평지는 바닥과 얼룩으로 나눈다.
    private static void PaintGround(BattleMapTheme theme, MapNoise noise, TerrainData data, string folder, float minHeight)
    {
        data.terrainLayers = new[]
        {
            CreateLayer(folder, "0_Floor", theme.Floor),
            CreateLayer(folder, "1_Patch", theme.Patch),
            CreateLayer(folder, "2_Slope", theme.Slope),
            CreateLayer(folder, "3_High", theme.High),
        };

        // Fbm은 0.5 부근에 몰려 있어 비율을 그대로 문턱으로 쓰면 얼룩이 거의 안 생긴다. 분포 폭(약 0.12)에 맞춰 옮긴다.
        float patchThreshold = 0.5f + (0.5f - theme.PatchCoverage) * 0.35f;
        float patchFrequency = 0.035f / Mathf.Max(0.1f, theme.PatchScale);

        var maps = new float[AlphamapResolution, AlphamapResolution, 4];
        for (int zi = 0; zi < AlphamapResolution; zi++)
        {
            float v = (zi + 0.5f) / AlphamapResolution;
            float z = -TerrainSize * 0.5f + v * TerrainSize;
            for (int xi = 0; xi < AlphamapResolution; xi++)
            {
                float u = (xi + 0.5f) / AlphamapResolution;
                float x = -TerrainSize * 0.5f + u * TerrainSize;
                float height = data.GetInterpolatedHeight(u, v) + minHeight;
                float steepness = data.GetSteepness(u, v);

                float slope = Smooth(26f, 40f, steepness);
                float high = 0f;
                if (!float.IsInfinity(theme.HighFrom))
                {
                    high = theme.HighBelow
                        ? Smooth(theme.HighFrom + 0.4f, theme.HighFrom - 0.4f, height)
                        : Smooth(theme.HighFrom - 4f, theme.HighFrom + 4f, height);
                }
                float patchNoise = noise.Fbm(x * patchFrequency + 300f, z * patchFrequency, 4);
                float patch = Smooth(patchThreshold - 0.03f, patchThreshold + 0.03f, patchNoise);

                float rest = 1f - slope;
                float highWeight = rest * high;
                rest -= highWeight;
                float patchWeight = rest * patch;

                maps[zi, xi, 0] = rest - patchWeight;
                maps[zi, xi, 1] = patchWeight;
                maps[zi, xi, 2] = slope;
                maps[zi, xi, 3] = highWeight;
            }
        }
        data.SetAlphamaps(0, 0, maps);
    }

    private static TerrainLayer CreateLayer(string folder, string name, GroundPaint paint)
    {
        BattleMapCatalog.GroundTextureSet set = BattleMapCatalog.Ground(paint.Texture);
        var layer = new TerrainLayer
        {
            diffuseTexture = BattleMapCatalog.Load<Texture2D>(set.Diffuse),
            normalMapTexture = BattleMapCatalog.Load<Texture2D>(set.Normal),
            tileSize = Vector2.one * set.Tile * paint.TileScale,
            // 알파(1)는 끄는 값이 아니라 "밀도 블렌드를 쓰지 않음"이다(TerrainLitPasses의 useOpacityAsDensityParam).
            diffuseRemapMax = new Vector4(paint.Tint.r, paint.Tint.g, paint.Tint.b, 1f),
            // 알파의 매끈함을 그대로 쓰면 해가 낮은 맵(붉은 황야·탑의 정상)에서 모래가 해 쪽으로 반짝이 기둥을 세우고
            // 블룸이 번졌다(플레이 테스트). 절반으로 눌러 쓴다.
            smoothnessSource = set.SmoothnessInAlpha
                ? TerrainLayerSmoothnessSource.ConstantMultipliedByDiffuseAlpha
                : TerrainLayerSmoothnessSource.ConstantOnly,
            smoothness = set.SmoothnessInAlpha ? 0.5f : 0.12f,
        };
        AssetDatabase.CreateAsset(layer, $"{folder}/Ground_{name}.terrainlayer");
        return layer;
    }

    // 발밑 풀. 얼룩 모양으로 모여 나고, 가파른 곳과 물속에는 나지 않는다.
    private static void PlantCover(BattleMapTheme theme, MapNoise noise, Random random, TerrainData data, float minHeight)
    {
        var prototypes = new List<DetailPrototype>();
        var densities = new List<float>();
        var layerOffsets = new List<float>();

        for (int c = 0; c < theme.Cover.Length; c++)
        {
            BattleMapCatalog.CoverSet set = BattleMapCatalog.Cover(theme.Cover[c]);
            foreach (string path in set.Prefabs)
            {
                var prefab = BattleMapCatalog.Load<GameObject>(path);
                if (prefab == null || prefab.GetComponent<MeshFilter>() == null)
                {
                    if (prefab != null) Debug.LogWarning($"[BattleMapBuilder] 루트에 메시가 없어 풀로 못 씁니다: {path}");
                    continue;
                }

                prototypes.Add(new DetailPrototype
                {
                    prototype = prefab,
                    usePrototypeMesh = true,
                    renderMode = DetailRenderMode.VertexLit,
                    useInstancing = true,
                    minWidth = set.MinSize,
                    maxWidth = set.MaxSize,
                    minHeight = set.MinSize,
                    maxHeight = set.MaxSize,
                    noiseSpread = 0.3f,
                    healthyColor = Color.white,
                    dryColor = Color.white,
                });
                // 같은 계열의 프리팹 여러 장이 밀도를 나눠 가진다.
                densities.Add(set.PerSquareMeter / set.Prefabs.Length);
                layerOffsets.Add(c * 91.7f);
            }
        }

        data.SetDetailResolution(DetailResolution, 32);
        data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        data.detailPrototypes = prototypes.ToArray();
        if (prototypes.Count == 0) return;

        float threshold = 0.5f + (0.5f - theme.CoverCoverage) * 0.35f;
        float cell = TerrainSize / DetailResolution;

        for (int layer = 0; layer < prototypes.Count; layer++)
        {
            var map = new int[DetailResolution, DetailResolution];
            float offset = layerOffsets[layer];
            for (int zi = 0; zi < DetailResolution; zi++)
            {
                float v = (zi + 0.5f) / DetailResolution;
                float z = -TerrainSize * 0.5f + (zi + 0.5f) * cell;
                for (int xi = 0; xi < DetailResolution; xi++)
                {
                    float u = (xi + 0.5f) / DetailResolution;
                    float x = -TerrainSize * 0.5f + (xi + 0.5f) * cell;

                    float clump = noise.Fbm(x * 0.05f + offset, z * 0.05f - offset, 3);
                    float grow = Smooth(threshold - 0.05f, threshold + 0.05f, clump);
                    if (grow <= 0f) continue;
                    if (data.GetSteepness(u, v) > 28f) continue;

                    float height = data.GetInterpolatedHeight(u, v) + minHeight;
                    if (theme.HasWater && height < theme.WaterLevel + 0.1f) continue;
                    if (theme.Landform == MapLandform.Cliffside && height < -3f) continue;

                    // 기대값이 densities[layer]가 되도록 소수 부분을 확률로 올림한다.
                    float expected = densities[layer] * grow;
                    int count = Mathf.FloorToInt(expected);
                    if (random.NextDouble() < expected - count) count++;
                    map[zi, xi] = count;
                }
            }
            data.SetDetailLayer(0, 0, layer, map);
        }
    }

    // 둘레 나무와 덤불. 지형 나무 인스턴스로 심는다(LOD·빌보드를 지형이 맡는다).
    private static void PlantVegetation(BattleMapTheme theme, MapNoise noise, Random random, TerrainData data, float minHeight)
    {
        var prototypes = new List<TreePrototype>();
        var groupOf = new List<int>();
        for (int g = 0; g < theme.Vegetation.Length; g++)
        {
            BattleMapCatalog.PropSet set = BattleMapCatalog.Props(theme.Vegetation[g]);
            foreach (string path in set.Prefabs)
            {
                var prefab = BattleMapCatalog.Load<GameObject>(path);
                if (prefab == null) continue;
                if (prefab.GetComponent<LODGroup>() == null && prefab.GetComponent<MeshRenderer>() == null)
                {
                    Debug.LogWarning($"[BattleMapBuilder] 루트에 LODGroup도 메시도 없어 나무로 못 씁니다: {path}");
                    continue;
                }
                prototypes.Add(new TreePrototype { prefab = prefab });
                groupOf.Add(g);
            }
        }

        data.treePrototypes = prototypes.ToArray();
        if (prototypes.Count == 0 || theme.VegetationCount <= 0) return;

        var instances = new List<TreeInstance>(theme.VegetationCount);
        int attempts = theme.VegetationCount * 30;
        while (instances.Count < theme.VegetationCount && attempts-- > 0)
        {
            float x = Range(random, -TerrainSize * 0.48f, TerrainSize * 0.48f);
            float z = Range(random, -TerrainSize * 0.48f, TerrainSize * 0.48f);
            // 전장 가장자리 바로 밖부터 심는다. 카메라가 보는 끝(약 55m)에 숲의 첫 줄이 걸려야 한다.
            float r = ArenaDistance(x, z);
            if (r < ArenaHalf + 1f) continue;

            // 화면에 걸리는 전장 가장자리 쪽을 짙게, 멀어질수록 성기게 심는다.
            if (random.NextDouble() > Mathf.Lerp(1f, 0.3f, Smooth(90f, 250f, r))) continue;
            // 숲은 덩어리로 모인다.
            if (noise.Fbm(x * 0.012f + 70f, z * 0.012f, 3) < 0.45f) continue;

            float u = x / TerrainSize + 0.5f;
            float v = z / TerrainSize + 0.5f;
            if (data.GetSteepness(u, v) > 32f) continue;
            float height = data.GetInterpolatedHeight(u, v) + minHeight;
            if (theme.HasWater && height < theme.WaterLevel + 0.3f) continue;
            if (theme.Landform == MapLandform.Cliffside && height < -10f) continue;

            int index = random.Next(prototypes.Count);
            BattleMapCatalog.PropSet set = BattleMapCatalog.Props(theme.Vegetation[groupOf[index]]);
            float scale = Range(random, set.MinSize, set.MaxSize);
            instances.Add(new TreeInstance
            {
                prototypeIndex = index,
                position = new Vector3(u, 0f, v),
                widthScale = scale,
                heightScale = scale * Range(random, 0.9f, 1.1f),
                rotation = Range(random, 0f, Mathf.PI * 2f),
                color = Color.white,
                lightmapColor = Color.white,
            });
        }
        data.SetTreeInstances(instances.ToArray(), true);
    }

    // ───────────────────────── 둘레 소품 ─────────────────────────

    private static void PlaceProps(BattleMapTheme theme, Random random, Scene scene, Terrain terrain, Transform environment)
    {
        if (theme.Rocks.Length == 0 || theme.RockCount <= 0) return;

        var parent = new GameObject("Props").transform;
        parent.SetParent(environment, false);

        int placed = 0;
        int attempts = theme.RockCount * 40;
        while (placed < theme.RockCount && attempts-- > 0)
        {
            PropGroup group = theme.Rocks[random.Next(theme.Rocks.Length)];
            BattleMapCatalog.PropSet set = BattleMapCatalog.Props(group);
            var prefab = BattleMapCatalog.Load<GameObject>(set.Prefabs[random.Next(set.Prefabs.Length)]);
            if (prefab == null) continue;

            // 큰 절벽은 둘레 지형의 벽에, 나머지는 전장 가장자리 띠에 몰아 둔다 — 화면 위쪽에 걸리는 자리다.
            double roll = random.NextDouble();
            float band = group == PropGroup.Cliffs
                ? Mathf.Lerp(ArenaHalf + RimWidth, 190f, (float)roll)
                : ArenaHalf + 4f + 70f * (float)(roll * roll);
            float angle = Range(random, 0f, Mathf.PI * 2f);
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 point = direction * (band / ArenaDistance(direction.x, direction.y));
            if (Mathf.Abs(point.x) > TerrainSize * 0.47f || Mathf.Abs(point.y) > TerrainSize * 0.47f) continue;

            float ground = terrain.SampleHeight(new Vector3(point.x, 0f, point.y)) + terrain.transform.position.y;
            if (theme.HasWater && ground < theme.WaterLevel - 1.5f) continue;
            if (theme.Landform == MapLandform.Cliffside && ground < -10f && group != PropGroup.Cliffs) continue;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.transform.SetParent(parent, true);

            float yaw = group == PropGroup.Cliffs
                ? Mathf.Atan2(-point.x, -point.y) * Mathf.Rad2Deg + Range(random, -40f, 40f)
                : Range(random, 0f, 360f);
            float tilt = group == PropGroup.Cliffs || group == PropGroup.Logs ? 3f : 10f;
            go.transform.rotation = Quaternion.Euler(Range(random, -tilt, tilt), yaw, Range(random, -tilt, tilt));
            go.transform.position = Vector3.zero;

            if (!TryGetBounds(go, out Bounds bounds))
            {
                Object.DestroyImmediate(go);
                continue;
            }
            float longest = Mathf.Max(bounds.size.x, bounds.size.z, 0.01f);
            float scale = Range(random, set.MinSize, set.MaxSize) * theme.RockScale / longest;
            go.transform.localScale *= scale;

            // 발밑 높이는 바운즈 네 귀퉁이 중 가장 낮은 곳으로 잡는다. 가운데 한 점만 재면 비탈에 걸친
            // 큰 바위가 낮은 쪽 허공에 떠 보인다.
            TryGetBounds(go, out bounds);
            Vector2 half = new Vector2(bounds.extents.x, bounds.extents.z) * 0.7f;
            foreach (Vector2 corner in new[] { new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y), new Vector2(-half.x, half.y), new Vector2(half.x, half.y) })
            {
                Vector3 probe = new Vector3(point.x + corner.x, 0f, point.y + corner.y);
                ground = Mathf.Min(ground, terrain.SampleHeight(probe) + terrain.transform.position.y);
            }
            float lift = ground - bounds.min.y - bounds.size.y * set.Sink;
            go.transform.position = new Vector3(point.x - bounds.center.x, lift, point.y - bounds.center.z);

            TryGetBounds(go, out bounds);
            if (bounds.min.x < NavMeshKeepOut && bounds.max.x > -NavMeshKeepOut &&
                bounds.min.z < NavMeshKeepOut && bounds.max.z > -NavMeshKeepOut)
            {
                Object.DestroyImmediate(go);
                continue;
            }

            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            placed++;
        }
    }

    private static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>())
        {
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    // ───────────────────────── 물 ─────────────────────────

    // 둘레의 꺼진 땅을 채우는 넓은 물판. 전장 바닥(0) 아래에 깔리므로 전장 안에서는 보이지 않는다.
    private static void CreateWater(BattleMapTheme theme, string folder, Transform environment)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", theme.WaterColor);
        material.SetFloat("_Smoothness", 0.93f);
        material.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(material, folder + "/Water.mat");

        GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "Water";
        Object.DestroyImmediate(water.GetComponent<Collider>());
        water.transform.SetParent(environment, false);
        water.transform.position = new Vector3(0f, theme.WaterLevel, 0f);
        water.transform.localScale = new Vector3(TerrainSize * 0.1f, 1f, TerrainSize * 0.1f);
        var renderer = water.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        water.isStatic = true;
    }

    // ───────────────────────── 날씨 ─────────────────────────

    private struct WeatherSpec
    {
        public float Height;       // 상자 중심의 높이(땅에서, m). 카메라는 땅 위 6m쯤에 떠 있다.
        public float Thickness;    // 상자 두께
        public float Lifetime;
        public float PerThousandSquareMeters;
        public Vector2 Size;
        public Vector3 VelocityMin;
        public Vector3 VelocityMax;
        public float NoiseStrength;
        public float NoiseFrequency;
        public Color Tint;         // 입자 색(LDR)
        public Color Glow;         // 재질 색(HDR — 블룸에 걸린다)
        public bool Stretch;
        public float StretchScale; // 속도 1m/s당 늘어나는 길이
        public bool Spin;
        public bool Flicker;
    }

    private static void CreateWeather(BattleMapTheme theme, string folder, Scene scene, Transform environment)
    {
        if (theme.Weather == MapWeather.None) return;

        WeatherSpec spec = Weather(theme);
        if (theme.WeatherGlow.a > 0f) spec.Glow = theme.WeatherGlow;
        var source = AssetDatabase.LoadAssetAtPath<Material>(ParticleMaterialPath);
        if (source == null)
        {
            Debug.LogWarning($"[BattleMapBuilder] 입자 재질을 찾지 못해 날씨를 건너뜁니다: {ParticleMaterialPath}");
            return;
        }
        var material = new Material(source);
        material.SetColor("_BaseColor", spec.Glow);
        AssetDatabase.CreateAsset(material, folder + "/Weather.mat");

        // 카메라의 자식으로 두면 따라다니는 스크립트 없이 파티를 쫓아간다. 입자는 월드에서 움직이므로 카메라가
        // 흘러가도 이미 내린 눈이 끌려가지 않는다. 상자는 카메라의 기울기(36도)를 따르지 않도록 수평으로 세운다.
        var go = new GameObject("Weather");
        Camera camera = FindBattleCamera(scene);
        if (camera != null)
        {
            Transform view = camera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
            Vector3 ground = new Vector3(view.position.x, 0f, view.position.z);
            go.transform.SetParent(view, false);
            go.transform.SetPositionAndRotation(ground + forward * WeatherAhead, Quaternion.LookRotation(forward, Vector3.up));
        }
        else
        {
            Debug.LogWarning("[BattleMapBuilder] 전투 카메라를 찾지 못해 날씨를 전장 중앙에 고정합니다.");
            go.transform.SetParent(environment, false);
        }

        var system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        float area = WeatherArea.x * WeatherArea.y / 1000f;
        int max = Mathf.Max(50, Mathf.RoundToInt(spec.PerThousandSquareMeters * area * theme.WeatherAmount));

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.prewarm = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(spec.Lifetime * 0.8f, spec.Lifetime * 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(spec.Size.x, spec.Size.y);
        main.startColor = spec.Tint;
        main.maxParticles = max;
        if (spec.Spin) main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = max / spec.Lifetime;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.position = new Vector3(0f, spec.Height, 0f);
        shape.scale = new Vector3(WeatherArea.x, spec.Thickness, WeatherArea.y);

        ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(spec.VelocityMin.x, spec.VelocityMax.x);
        velocity.y = new ParticleSystem.MinMaxCurve(spec.VelocityMin.y, spec.VelocityMax.y);
        velocity.z = new ParticleSystem.MinMaxCurve(spec.VelocityMin.z, spec.VelocityMax.z);

        if (spec.NoiseStrength > 0f)
        {
            ParticleSystem.NoiseModule turbulence = system.noise;
            turbulence.enabled = true;
            turbulence.strength = spec.NoiseStrength;
            turbulence.frequency = spec.NoiseFrequency;
            turbulence.scrollSpeed = 0.2f;
            turbulence.quality = ParticleSystemNoiseQuality.Low;
        }

        if (spec.Spin)
        {
            ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
        }

        // 생겨나고 사라질 때 흐려진다. 반딧불은 켜졌다 꺼졌다 한다.
        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            spec.Flicker
                ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.1f, 0.35f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0.2f, 0.8f), new GradientAlphaKey(0f, 1f) }
                : new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        color.color = fade;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        // 렌즈 바로 앞을 지나는 입자가 화면을 덮지 않게 화면 높이의 4%에서 자른다.
        renderer.maxParticleSize = 0.04f;
        if (spec.Stretch)
        {
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = spec.StretchScale;
            renderer.lengthScale = 1f;
        }
    }

    private static Camera FindBattleCamera(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                if (camera.CompareTag("MainCamera")) return camera;
        }
        return null;
    }

    private static WeatherSpec Weather(BattleMapTheme theme)
    {
        switch (theme.Weather)
        {
            // 떨어지는 것(눈·재·비·낙엽)은 카메라 높이(6m) 바로 위에서 떨어뜨려 땅에 닿을 즈음 사라지게 수명을 맞춘다.
            // 떠오르는 것(불티·포자·빛 알갱이)은 수명 동안 카메라 높이를 넘지 않을 만큼만 오른다.
            // 흰 눈은 흰 바닥에, 잿빛 재는 잿빛 바닥에 묻히므로 재질 색을 1보다 밝게 줘 바닥보다 한 톤 떠 보이게 한다.
            case MapWeather.Snow:
                return new WeatherSpec { Height = 6.5f, Thickness = 1f, Lifetime = 3.5f, PerThousandSquareMeters = 850f, Size = new Vector2(0.14f, 0.3f),
                    VelocityMin = new Vector3(0.2f, -2.4f, -0.3f), VelocityMax = new Vector3(0.8f, -1.6f, 0.5f), NoiseStrength = 0.6f, NoiseFrequency = 0.3f,
                    Tint = new Color(1f, 1f, 1f, 0.95f), Glow = new Color(1.5f, 1.5f, 1.6f, 1f) };
            case MapWeather.Blizzard:
                // 카메라가 -X를 보므로 바람은 Z로 불어 화면을 가로지른다.
                return new WeatherSpec { Height = 3.5f, Thickness = 7f, Lifetime = 2.5f, PerThousandSquareMeters = 1000f, Size = new Vector2(0.1f, 0.22f),
                    VelocityMin = new Vector3(-1f, -2f, 7f), VelocityMax = new Vector3(0f, -1f, 11f), NoiseStrength = 1.4f, NoiseFrequency = 0.5f,
                    Tint = new Color(1f, 1f, 1f, 0.9f), Glow = new Color(1.4f, 1.45f, 1.5f, 1f) };
            case MapWeather.Ash:
                return new WeatherSpec { Height = 6.5f, Thickness = 1f, Lifetime = 7f, PerThousandSquareMeters = 450f, Size = new Vector2(0.14f, 0.28f),
                    VelocityMin = new Vector3(0.2f, -1.3f, -0.4f), VelocityMax = new Vector3(0.9f, -0.8f, 0.4f), NoiseStrength = 0.5f, NoiseFrequency = 0.25f,
                    Tint = new Color(0.8f, 0.78f, 0.76f, 0.9f), Glow = new Color(1.5f, 1.5f, 1.5f, 1f) };
            case MapWeather.Embers:
                return new WeatherSpec { Height = 1f, Thickness = 2f, Lifetime = 4f, PerThousandSquareMeters = 160f, Size = new Vector2(0.08f, 0.18f),
                    VelocityMin = new Vector3(-0.3f, 0.4f, -0.3f), VelocityMax = new Vector3(0.6f, 1.2f, 0.6f), NoiseStrength = 0.8f, NoiseFrequency = 0.5f,
                    Tint = Color.white, Glow = new Color(6f, 1.8f, 0.4f, 1f), Flicker = true };
            case MapWeather.Rain:
                return new WeatherSpec { Height = 7f, Thickness = 1f, Lifetime = 0.4f, PerThousandSquareMeters = 700f, Size = new Vector2(0.03f, 0.045f),
                    VelocityMin = new Vector3(0.5f, -22f, 1.5f), VelocityMax = new Vector3(1f, -18f, 2.5f),
                    Tint = new Color(0.8f, 0.85f, 0.95f, 0.55f), Glow = new Color(1.3f, 1.3f, 1.35f, 1f), Stretch = true, StretchScale = 0.06f };
            case MapWeather.Dust:
                // 흐릿한 먼지 덩어리는 같은 색 모래에 묻혀 안 보였다. 바람에 날리는 가는 모래 줄로 그린다.
                return new WeatherSpec { Height = 2.5f, Thickness = 5f, Lifetime = 3f, PerThousandSquareMeters = 450f, Size = new Vector2(0.05f, 0.09f),
                    VelocityMin = new Vector3(-0.5f, -0.3f, 5f), VelocityMax = new Vector3(0.5f, 0.3f, 9f), NoiseStrength = 0.8f, NoiseFrequency = 0.3f,
                    Tint = new Color(theme.FogColor.r, theme.FogColor.g, theme.FogColor.b, 0.6f), Glow = new Color(2f, 1.95f, 1.85f, 1f),
                    Stretch = true, StretchScale = 0.2f };
            case MapWeather.Spores:
                return new WeatherSpec { Height = 2f, Thickness = 3f, Lifetime = 8f, PerThousandSquareMeters = 160f, Size = new Vector2(0.12f, 0.22f),
                    VelocityMin = new Vector3(-0.2f, 0.05f, -0.2f), VelocityMax = new Vector3(0.2f, 0.3f, 0.2f), NoiseStrength = 0.5f, NoiseFrequency = 0.2f,
                    Tint = new Color(1f, 1f, 1f, 0.9f), Glow = new Color(2f, 2.4f, 1.3f, 1f) };
            case MapWeather.Fireflies:
                return new WeatherSpec { Height = 1.8f, Thickness = 3f, Lifetime = 7f, PerThousandSquareMeters = 70f, Size = new Vector2(0.14f, 0.24f),
                    VelocityMin = new Vector3(-0.3f, -0.1f, -0.3f), VelocityMax = new Vector3(0.3f, 0.15f, 0.3f), NoiseStrength = 1f, NoiseFrequency = 0.3f,
                    Tint = Color.white, Glow = new Color(3f, 4f, 1.2f, 1f), Flicker = true };
            case MapWeather.Leaves:
                return new WeatherSpec { Height = 6.5f, Thickness = 1f, Lifetime = 5f, PerThousandSquareMeters = 220f, Size = new Vector2(0.3f, 0.5f),
                    VelocityMin = new Vector3(-0.3f, -1.6f, 1f), VelocityMax = new Vector3(0.6f, -1.1f, 2.2f), NoiseStrength = 1.1f, NoiseFrequency = 0.35f,
                    Tint = new Color(0.95f, 0.45f, 0.15f, 1f), Glow = new Color(1.6f, 1.6f, 1.6f, 1f), Spin = true };
            default: // Motes
                return new WeatherSpec { Height = 1f, Thickness = 2f, Lifetime = 7f, PerThousandSquareMeters = 150f, Size = new Vector2(0.14f, 0.26f),
                    VelocityMin = new Vector3(-0.2f, 0.25f, -0.2f), VelocityMax = new Vector3(0.2f, 0.6f, 0.2f), NoiseStrength = 0.4f, NoiseFrequency = 0.3f,
                    Tint = Color.white, Glow = new Color(4f, 3f, 1.4f, 1f), Flicker = true };
        }
    }

    // ───────────────────────── 하늘·빛·안개·화면 색 ─────────────────────────

    private static void ApplySkyAndLight(BattleMapTheme theme, string folder, Transform environment)
    {
        string cubemapPath = BattleMapCatalog.SkyCubemap(theme.Sky);
        var cubemap = cubemapPath != null ? BattleMapCatalog.Load<Cubemap>(cubemapPath) : null;

        Material sky;
        if (cubemap != null)
        {
            sky = new Material(Shader.Find("Skybox/Cubemap"));
            sky.SetTexture("_Tex", cubemap);
            sky.SetColor("_Tint", theme.SkyTint);
            sky.SetFloat("_Exposure", theme.SkyExposure);
            sky.SetFloat("_Rotation", theme.SkyRotation);
        }
        else
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunDisk", 2f);
            sky.SetFloat("_SunSize", theme.SunSize);
            sky.SetFloat("_SunSizeConvergence", 5f);
            sky.SetFloat("_AtmosphereThickness", theme.Atmosphere);
            sky.SetColor("_SkyTint", theme.SkyTint);
            sky.SetColor("_GroundColor", theme.SkyGround);
            sky.SetFloat("_Exposure", theme.SkyExposure);
        }
        AssetDatabase.CreateAsset(sky, folder + "/Sky.mat");

        Light sun = null;
        foreach (Light light in environment.GetComponentsInChildren<Light>(true))
        {
            if (light.type != LightType.Directional) continue;
            sun = light;
            break;
        }
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(theme.SunPitch, theme.SunYaw, 0f);
            sun.useColorTemperature = false;
            sun.color = theme.SunColor;
            sun.intensity = theme.SunIntensity;
            sun.shadowStrength = theme.ShadowStrength;
        }

        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = theme.AmbientSky;
        RenderSettings.ambientEquatorColor = theme.AmbientEquator;
        RenderSettings.ambientGroundColor = theme.AmbientGround;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.fog = theme.FogDensity > 0f;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = theme.FogColor;
        RenderSettings.fogDensity = theme.FogDensity;

        // 반사는 원본 평야와 같이 Skybox로 둔다. 하늘 큐브맵을 반사로 그대로 넣었더니 HDR 태양이 바닥 반사에 박혀,
        // 해 방향과 상관없이 화면 오른쪽 위에 불꽃 기둥 같은 반짝임과 블룸이 섰다(PW_Sky_Evening_Clear, 플레이 테스트).
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.customReflectionTexture = null;
    }

    // 원본 볼륨 프로필(톤매핑·블룸·비네트)을 복사해 색 보정을 얹는다. 원본을 고치면 1~5층까지 물든다.
    private static void ApplyGrade(BattleMapTheme theme, string folder, Transform environment)
    {
        var volume = environment.GetComponentInChildren<Volume>(true);
        if (volume == null || volume.sharedProfile == null) return;

        string path = folder + "/PostProcess.asset";
        if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(volume.sharedProfile), path)) return;
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);

        ColorAdjustments adjust = Override<ColorAdjustments>(profile);
        adjust.postExposure.Override(theme.PostExposure);
        adjust.contrast.Override(theme.Contrast);
        adjust.saturation.Override(theme.Saturation);
        adjust.colorFilter.Override(theme.ColorFilter);

        WhiteBalance balance = Override<WhiteBalance>(profile);
        balance.temperature.Override(theme.Temperature);

        Vignette vignette = Override<Vignette>(profile);
        vignette.intensity.Override(theme.Vignette);

        EditorUtility.SetDirty(profile);
        volume.sharedProfile = profile;
    }

    private static T Override<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T component)) return component;

        component = profile.Add<T>();
        component.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    // ───────────────────────── NavMesh·Build Settings ─────────────────────────

    // 원본의 NavMeshSurface(±40m 상자)를 새 지형 위에서 다시 굽는다. 전장이 평평하므로 원본과 같은 판이 나온다.
    private static void BakeNavMesh(Transform environment, string folder)
    {
        var surface = environment.GetComponentInChildren<NavMeshSurface>(true);
        if (surface == null)
        {
            Debug.LogWarning("[BattleMapBuilder] NavMeshSurface가 없어 NavMesh를 굽지 못했습니다.");
            return;
        }

        surface.RemoveData();
        surface.BuildNavMesh();
        if (surface.navMeshData != null) AssetDatabase.CreateAsset(surface.navMeshData, folder + "/NavMesh.asset");
        EditorUtility.SetDirty(surface);
    }

    // 층 씬은 구간 순서대로, 그 밖의 씬(MainScene 등)은 원래 자리대로 둔다.
    private static void RegisterBuildScenes()
    {
        var scenes = new List<EditorBuildSettingsScene>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (!Path.GetFileNameWithoutExtension(scene.path).StartsWith("Floor", StringComparison.Ordinal)) scenes.Add(scene);
        }

        for (int first = FloorProgress.FirstFloor; first <= FloorProgress.LastFloor; first += FloorProgress.FloorsPerStage)
        {
            string path = ScenePath(first);
            if (File.Exists(path)) scenes.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static float Range(Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

    // 테마의 시드로 고정된 잡음. 같은 테마면 몇 번을 다시 만들어도 같은 땅이 나온다.
    private sealed class MapNoise
    {
        public readonly struct Mesa
        {
            public readonly Vector2 Center;
            public readonly float Radius;
            public readonly float Height;

            public Mesa(Vector2 center, float radius, float height)
            {
                Center = center;
                Radius = radius;
                Height = height;
            }
        }

        // Mathf.PerlinNoise는 float로 도는데 입력이 수만을 넘으면 소수 자리가 뭉개져 계단이 진다. 오프셋은 수백 안에 둔다.
        private readonly float offsetX;
        private readonly float offsetZ;

        // 지형의 방향(협곡의 틈, 모래 언덕의 바람, 호수 쪽).
        public readonly float Angle;
        public readonly List<Mesa> Mesas = new List<Mesa>();

        public MapNoise(BattleMapTheme theme)
        {
            var random = new Random(theme.Seed * 7919 + 17);
            offsetX = random.Next(10, 400) + 0.37f;
            offsetZ = random.Next(10, 400) + 0.71f;
            Angle = Range(random, 0f, Mathf.PI * 2f);

            // 카메라는 -X 쪽을 본다. 호수는 그쪽에 있어야 화면에 들어온다.
            if (theme.Landform == MapLandform.Lakeside) Angle = Mathf.PI + Range(random, -0.4f, 0.4f);

            for (int i = 0; i < 18; i++)
            {
                float angle = Range(random, 0f, Mathf.PI * 2f);
                float distance = Range(random, 100f, 235f);
                float radius = Range(random, 16f, 42f);
                if (distance - radius < ArenaHalf + RimWidth + 10f) continue;
                Mesas.Add(new Mesa(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance, radius, Range(random, 0.55f, 1f)));
            }
        }

        public float Perlin(float x, float z) => Mathf.PerlinNoise(x + offsetX, z + offsetZ);

        // 0~1
        public float Fbm(float x, float z, int octaves)
        {
            float sum = 0f, amplitude = 1f, norm = 0f, frequency = 1f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amplitude * Mathf.PerlinNoise(x * frequency + offsetX + i * 17.3f, z * frequency + offsetZ - i * 31.7f);
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }
            return sum / norm;
        }

        // 0~1. 잡음이 0.5를 지나는 선을 따라 날카로운 능선이 선다.
        public float Ridged(float x, float z, int octaves)
        {
            float sum = 0f, amplitude = 1f, norm = 0f, frequency = 1f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(Mathf.PerlinNoise(x * frequency + offsetX - i * 13.1f, z * frequency + offsetZ + i * 23.9f) * 2f - 1f);
                sum += amplitude * n * n;
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }
            return sum / norm;
        }
    }
}
