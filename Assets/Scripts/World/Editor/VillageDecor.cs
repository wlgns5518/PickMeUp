using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 거점 꾸미기 — 2026-09-25 사용자가 준 저녁 성채 참고 그림("이런 식으로 꾸며줘").
// 건물·길 배치는 그대로 두고 그림의 장식만 더한다:
//   - 성벽 모서리 탑마다 꼭대기 횃불(화로 파츠)과 안쪽 발치의 파란 깃발(PolygonWall 모서리 장식)
//   - 큰길 가장자리 등불(VillageBlockout 가로등)
//   - 성벽 밖을 두르는 어두운 침엽수 숲(지형 나무) — 안개(VillageMood)와 겹쳐 그림처럼 숲이 성채를 감싼다
//   - 부지 나무를 조금 더 빽빽하게
// 집 창 불빛은 VillageGaiaSkin(집 머티리얼 발광), 저녁 조명·안개·볼륨은 VillageMood가 맡는다.
// 불빛은 전부 발광·블룸이다 — 점광원은 소환 구슬·화덕 둘뿐(모바일).
public static class VillageDecor
{
    private const string Folder = "Assets/Environment/Village/Materials/Decor";
    private const string BannerPrefab = "Assets/Environment/Village/Prefabs/Decor_Banner_Blue.prefab";

    // 참고 그림의 깃발: 짙은 파랑 천에 흰 문장. 원래 깃발은 진홍 천에 은빛 칼 문장이라 천(붉은 곳)만 파랗게 칠한다.
    private static readonly Color BannerBlue = new Color(0.13f, 0.24f, 0.52f);

    private const float LotTreeDensity = 3f;
    private const float LampSpacing = 16f;

    // 성벽 밖 숲. 성벽(반지름 124)과 탑 밑동을 비우고 성벽 뒤로 50m 띠만 — 그 너머는 어두운 안개(VillageMood)가 덮는다.
    // 지형 끝(260)까지 600그루를 심었더니 LOD를 낮춰도 드로우콜이 1,700을 넘었다(나무 한 그루가 재질 셋이라 3~4번 그린다).
    private const float ForestInner = 134f;
    private const float ForestOuter = 185f;
    private const float ForestCell = 11f;   // 칸마다 한 그루(흔들어 심는다) — 약 400그루
    private const int ForestSeed = 20260925;
    // 가벼운 것만: 가까이 4~5천, 멀리서 20삼각형 빌보드로 떨어지는 pine_004와, 한 메시에 여러 그루가 든 묶음(clump).
    // 처음엔 Pine_002_XL(5.4만)·Spruce(1.8만)·Pine_002_L(1.2만)까지 섞어 1,033그루를 심었더니 마을 화면이
    // 삼각형 67만→252만, 드로우콜 577→2,889가 됐다. 같은 이름을 두 번 넣으면 그만큼 자주 나온다.
    private static readonly string[] ForestTrees =
    {
        "pine_004_1_baked_hierarchy", "pine_004_3_baked_hierarchy", "pine_004_4_baked_hierarchy",
        "pine_004_clump01_baked_hierarchy", "pine_004_clump02_baked_hierarchy",
    };
    // 심는 것은 낱 그루만 — 멀리서 20삼각형 빌보드(재질 하나, 한 번 그림)로 떨어진다. 묶음(clump)은 빌보드 단계가 없어
    // 아무리 멀어도 재질 둘로 두 번씩 그려서 400그루에 드로우콜이 천 개 넘게 붙었다.
    private static readonly string[] ForestPicks =
    {
        "pine_004_1_baked_hierarchy", "pine_004_3_baked_hierarchy", "pine_004_4_baked_hierarchy",
    };
    // 지형 나무 LOD 치우침. 성벽 너머 숲은 카메라에서 150m 넘게 떨어져 있어 낮은 LOD·빌보드로 충분하다 — 0.2와 1은 화면에서
    // 구별이 안 됐고 삼각형은 176만→100만이었다(지형 설정 — 다른 씬에는 없다).
    private const float ForestLodBias = 0.2f;
    // 예전에 심었던 무거운 침엽수. 다시 누르면 이것도 지운다.
    private static readonly string[] OldForestTrees =
    {
        "Pine_002_L_baked_hierarchy", "Pine_002_M2_baked_hierarchy", "Pine_002_M3_baked_hierarchy", "Pine_002_XL_baked_hierarchy",
        "pine_004_2_baked_hierarchy", "Pine_005_01_baked_hierarchy", "PW_Tree_Spruce_05",
    };

    [MenuItem("PickMeUp/Village/11. 거점 꾸미기 (횃불·깃발·가로등·숲·효과)", priority = 68)]
    public static void Apply()
    {
        VillageFx.BuildAll();
        Wall();
        Village();
        Forest();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    // ---- 성벽 모서리 ----------------------------------------------------------------

    private static void Wall()
    {
        var wall = Object.FindAnyObjectByType<PolygonWall>();
        if (wall == null) return;
        var so = new SerializedObject(wall);
        // 화로 + 불꽃·불티 입자(VillageFx). 효과를 아직 안 구웠으면 화로만.
        var torch = AssetDatabase.LoadAssetAtPath<GameObject>(VillageFx.TowerTorchPath);
        so.FindProperty("cornerTopPrefab").objectReferenceValue =
            torch != null ? torch : AssetDatabase.LoadAssetAtPath<GameObject>(VillagePartBaker.PrefabPath("kit_brazier"));
        so.FindProperty("cornerTopScale").floatValue = 2.6f;
        so.FindProperty("cornerBannerPrefab").objectReferenceValue = BlueBanner();
        so.FindProperty("cornerBannerScale").floatValue = 1.4f;

        // 탑 꼭대기 성가퀴 안쪽 바닥까지 얼마나 내려가야 하는지 탑 메시에서 잰다.
        var corner = so.FindProperty("cornerPrefab").objectReferenceValue as GameObject;
        if (corner != null)
        {
            Mesh mesh = corner.GetComponentInChildren<MeshFilter>().sharedMesh;
            float height = so.FindProperty("height").floatValue;
            float rise = so.FindProperty("cornerRise").floatValue;
            float sink = so.FindProperty("moduleSink").floatValue;
            float scaleY = (height + rise + sink) / mesh.bounds.size.y;
            so.FindProperty("cornerTopDrop").floatValue = (mesh.bounds.max.y - PlatformHeight(mesh)) * scaleY;
        }
        so.ApplyModifiedProperties();
        wall.Rebuild();
    }

    // 탑 윗면 한가운데(성가퀴 안쪽)의 바닥 높이 — 가운데 칸 안에서 위를 보는 면 중 가장 높은 것.
    private static float PlatformHeight(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Bounds b = mesh.bounds;
        float best = b.max.y * 0.85f;
        bool found = false;
        for (int t = 0; t < triangles.Length; t += 3)
        {
            Vector3 a = vertices[triangles[t]], c = vertices[triangles[t + 1]], d = vertices[triangles[t + 2]];
            Vector3 normal = Vector3.Cross(c - a, d - a).normalized;
            if (normal.y < 0.9f) continue;
            Vector3 center = (a + c + d) / 3f;
            if (Mathf.Abs(center.x - b.center.x) > b.extents.x * 0.3f || Mathf.Abs(center.z - b.center.z) > b.extents.z * 0.3f) continue;
            if (!found || center.y > best) { best = center.y; found = true; }
        }
        return best;
    }

    // 깃발 파츠에 파란 천 머티리얼을 끼운 사본 프리팹.
    private static GameObject BlueBanner()
    {
        var part = AssetDatabase.LoadAssetAtPath<GameObject>(VillagePartBaker.PrefabPath("kit_banner"));
        if (part == null) return null;
        Directory.CreateDirectory(Folder);

        Material source = part.GetComponent<MeshRenderer>().sharedMaterial;
        string materialPath = $"{Folder}/M_Banner_Blue.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(source) { name = "M_Banner_Blue" };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        else material.CopyPropertiesFromMaterial(source);
        material.SetTexture("_BaseMap", BlueCloth(source.GetTexture("_BaseMap") as Texture2D));
        EditorUtility.SetDirty(material);

        var go = (GameObject)PrefabUtility.InstantiatePrefab(part);
        try
        {
            go.name = "Decor_Banner_Blue";
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return PrefabUtility.SaveAsPrefabAsset(go, BannerPrefab);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // 붉은 천만 파랗게. 채도 있고 색상이 빨강 근처인 칸이 천이다 — 쇠 장대·돌 받침·은빛 문장은 무채색이라 그대로 남는다.
    private static Texture2D BlueCloth(Texture2D albedo)
    {
        string path = $"{Folder}/kit_banner_blue.jpg";
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(albedo)));
        Color32[] px = texture.GetPixels32();
        for (int i = 0; i < px.Length; i++)
        {
            Color c = px[i];
            Color.RGBToHSV(c, out float h, out float s, out float v);
            float fromRed = Mathf.Min(h, 1f - h);
            float weight = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.04f, 0.09f, fromRed))) *
                           Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.45f, s)) *
                           Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.12f, v));
            Color painted = BannerBlue * (v / 0.38f);   // 천의 원래 밝기(약 0.38)가 목표 파랑이 되게
            painted.a = 1f;
            px[i] = Color.Lerp(c, painted, weight);
        }
        texture.SetPixels32(px);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToJPG(90));
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---- 마을 안 ----------------------------------------------------------------------

    private static void Village()
    {
        var village = Object.FindAnyObjectByType<VillageBlockout>();
        if (village == null) return;
        var so = new SerializedObject(village);
        so.FindProperty("roadLampPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(VillagePartBaker.PrefabPath("kit_lantern"));
        so.FindProperty("roadLampSpacing").floatValue = LampSpacing;
        so.FindProperty("roadBannerPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(BannerPrefab);
        so.FindProperty("lotTreeDensity").floatValue = LotTreeDensity;

        // 자리를 정한 효과: 광장 마법진 빛 알갱이(광장 구역의 가운데·배율), 마을 위 반딧불.
        SerializedProperty extras = so.FindProperty("extras");
        extras.ClearArray();
        Transform plaza = village.transform.Find("광장");
        if (plaza != null)
            AddExtra(extras, "광장 마법진 효과", VillageFx.PrefabPath("FX_PlazaMotes"),
                village.transform.InverseTransformPoint(plaza.position) - Vector3.up * plaza.localPosition.y, plaza.lossyScale.x);
        AddExtra(extras, "반딧불", VillageFx.PrefabPath("FX_Fireflies"), Vector3.zero, 1f);

        so.ApplyModifiedProperties();
        village.Rebuild();
    }

    private static void AddExtra(SerializedProperty list, string label, string prefabPath, Vector3 position, float scale)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return;
        list.arraySize++;
        SerializedProperty item = list.GetArrayElementAtIndex(list.arraySize - 1);
        item.FindPropertyRelative("label").stringValue = label;
        item.FindPropertyRelative("prefab").objectReferenceValue = prefab;
        item.FindPropertyRelative("position").vector3Value = new Vector3(position.x, 0f, position.z);
        item.FindPropertyRelative("yaw").floatValue = 0f;
        item.FindPropertyRelative("scale").floatValue = scale;
    }

    // ---- 성벽 밖 숲 -------------------------------------------------------------------

    // 지형 나무로 심는다(지형이 멀리서 LOD·빌보드로 그린다). 이미 있던 고사리·덤불은 그대로 두고, 여기서 심는 침엽수만
    // 지우고 다시 심는다 — 같은 씨앗이라 몇 번을 눌러도 같은 숲이다.
    private static void Forest()
    {
        var terrain = Object.FindAnyObjectByType<Terrain>();
        if (terrain == null) return;
        TerrainData data = terrain.terrainData;
        TreePrototype[] prototypes = data.treePrototypes;

        var ours = new List<int>();   // 여기서 심는(또는 예전에 심었던) 침엽수 — 다시 누르면 지운다
        var picks = new List<int>();
        for (int i = 0; i < prototypes.Length; i++)
        {
            string name = prototypes[i].prefab != null ? prototypes[i].prefab.name : null;
            if (name == null) continue;
            if (System.Array.IndexOf(ForestTrees, name) >= 0 || System.Array.IndexOf(OldForestTrees, name) >= 0) ours.Add(i);
            foreach (string pick in ForestPicks) if (pick == name) picks.Add(i);
        }
        if (picks.Count == 0) return;

        var kept = new List<TreeInstance>();
        foreach (TreeInstance tree in data.treeInstances)
            if (!ours.Contains(tree.prototypeIndex)) kept.Add(tree);

        var random = new System.Random(ForestSeed);
        Vector3 origin = terrain.transform.position, size = data.size;
        for (float x = -ForestOuter; x <= ForestOuter; x += ForestCell)
        for (float z = -ForestOuter; z <= ForestOuter; z += ForestCell)
        {
            var spot = new Vector3(x + ((float)random.NextDouble() - 0.5f) * ForestCell,
                                   0f,
                                   z + ((float)random.NextDouble() - 0.5f) * ForestCell);
            float r = new Vector2(spot.x, spot.z).magnitude;
            // 안쪽 가장자리는 성기게 시작해 바깥으로 갈수록 빽빽하게.
            float keep = Mathf.InverseLerp(ForestInner, ForestInner + 10f, r);
            if (r > ForestOuter || random.NextDouble() > keep) continue;

            float scale = 0.9f + (float)random.NextDouble() * 0.45f;
            kept.Add(new TreeInstance
            {
                prototypeIndex = picks[random.Next(picks.Count)],
                position = new Vector3((spot.x - origin.x) / size.x, 0f, (spot.z - origin.z) / size.z),
                widthScale = scale,
                heightScale = scale,
                rotation = (float)(random.NextDouble() * Mathf.PI * 2f),
                color = Color.white,
                lightmapColor = Color.white,
            });
        }

        Undo.RegisterCompleteObjectUndo(data, "성벽 밖 숲");
        data.SetTreeInstances(kept.ToArray(), true);
        EditorUtility.SetDirty(data);

        // treeLODBiasMultiplier는 씬에 저장되지 않아 런타임 부품(TerrainTreeLod)이 켤 때마다 적는다.
        var lod = terrain.GetComponent<TerrainTreeLod>();
        if (lod == null) lod = Undo.AddComponent<TerrainTreeLod>(terrain.gameObject);
        var lodSo = new SerializedObject(lod);
        lodSo.FindProperty("lodBias").floatValue = ForestLodBias;
        lodSo.ApplyModifiedProperties();
    }
}
