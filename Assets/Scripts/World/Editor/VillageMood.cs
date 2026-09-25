using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 메인 거점(MainScene) 다크 판타지 분위기 — 해·환경광·하늘·안개·후처리 볼륨·지형 풀 색(2026-09-25 사용자 요청).
// "차가운 어두운 세계 + 따뜻한 건축 재질 + 청록색 마법광"(VillagePalette). 배치·카메라·메시·콜라이더는 건드리지 않는다.
// 값은 여기서만 정한다 — 메뉴를 다시 누르면 이 값으로 돌아온다(씬에서 손으로 바꾼 조명은 덮인다).
//
// 원칙:
//   - 해는 약간 푸른 회색, 세기는 건물 형태·명암이 읽히는 정도. 그림자가 새까맣지 않게 환경광을 조금 남긴다.
//   - 볼륨은 약간 어둡게·대비 조금 올리고 차가운 필터. 파랗게 물들지 않게 필터는 옅게.
//   - 블룸은 문턱 1(HDR)이라 발광 1을 넘는 것(구슬·횃불·등불·창불·화덕)만 번진다. 일반 건물은 번지지 않는다.
//   - 안개는 성벽 밖 숲을 안개 속으로 밀 만큼(선형, 성벽 바깥부터).
// 같은 날 사용자가 저녁 성채 참고 그림을 주며 "이런 식으로" — 해질녘으로 낮췄다: 달빛 같은 푸른 해를 약하게, 하늘·안개는 어두운
// 청록 회색, 대신 창불·횃불·등불(VillageDecor·VillageGaiaSkin)이 따뜻하게 떠오르게 블룸을 조금 올렸다.
// 이 볼륨 프로필(Settings/SampleSceneProfile)과 하늘(Materials/MainSceneSky)은 MainScene만 쓴다(전투 맵은 BattleMapBuilder가 따로 짠다).
public static class VillageMood
{
    // 해(Directional Light) — 해질녘 푸른 빛. 회전은 그대로 둔다.
    private static readonly Color SunColor = new Color(0.78f, 0.82f, 0.88f);
    private const float SunIntensity = 1.25f;

    // 환경광(삼색). 밝은 곳은 차가운 회청, 그림자는 짙은 청회. 해가 남동쪽에서 들어와 동쪽 성벽 안쪽 면이 통째로 그늘인데,
    // 적도색이 해 세기에 비해 너무 낮으면 그 면이 새까맣게 죽는다.
    private static readonly Color AmbientSky = new Color(0.22f, 0.25f, 0.27f);
    private static readonly Color AmbientEquator = new Color(0.19f, 0.21f, 0.22f);
    private static readonly Color AmbientGround = new Color(0.15f, 0.17f, 0.19f);

    // 하늘(절차적). 화면 위쪽에 보이므로 해질녘 어두운 청록 회색. 섬이 하늘에 떠 있어(VillageIsland) 지평선 아래도 하늘이다 —
    // 아래쪽(ground) 색을 지평선 색에 가깝게 둬야 섬 밖이 땅처럼 어둡게 막히지 않는다.
    private static readonly Color SkyTint = new Color(0.20f, 0.27f, 0.33f);
    private static readonly Color SkyGround = new Color(0.50f, 0.57f, 0.65f);
    private const float SkyExposure = 0.55f;

    // 안개(선형). 성벽(반지름 124)까지는 거의 없고, 섬 가장자리·구름 바다가 하늘색 안개 속으로 흐려진다(하늘 아래쪽과 같은 색).
    private static readonly Color FogColor = new Color(0.31f, 0.36f, 0.42f);
    private const float FogStart = 150f;
    private const float FogEnd = 700f;

    // 지형 풀(디테일)과 MainScene 전용 풀 레이어 색 — 어두운 숲 녹색 쪽으로 곱한다.
    // Floor1 지형 레이어(Forest_Grass 등)는 전투 맵도 쓰므로 건드리지 않는다.
    private static readonly Color GrassHealthy = new Color(0.52f, 0.64f, 0.55f);
    private static readonly Color GrassDry = new Color(0.46f, 0.52f, 0.44f);
    private static readonly Color GrassLayerTint = new Color(0.55f, 0.64f, 0.56f);
    private const string SessionLayers = "Assets/Environment/Gaia/Sessions/GS-20260819 - 164805/Terrain Layers";

    // 광장 마법진 — 새로 만드는 머티리얼은 이 둘뿐이다(나머지는 도구가 굽는 기존 머티리얼의 색을 팔레트로 옮긴다).
    private const string MaterialFolder = "Assets/Materials/Environment";

    [MenuItem("PickMeUp/Village/10. 다크 판타지 분위기 (조명·볼륨·하늘·바닥)", priority = 66)]
    public static void Apply()
    {
        Sun();
        Ambient();
        Sky();
        Fog();
        Volume();
        Terrain();
        Materials();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    /// 굽기가 필요한 것까지 전부: 시설·집 재질(VillageGaiaSkin, 1분쯤), 파츠 발광 색, 꾸미기(VillageDecor), 그리고 위의 분위기.
    [MenuItem("PickMeUp/Village/10-1. 다크 판타지 전부 다시 입히기 (굽기 포함)", priority = 67)]
    public static void ApplyEverything()
    {
        VillagePartBaker.RetintGlow();
        VillageGaiaSkin.ApplyAll();
        VillageDecor.Apply();
        Apply();
    }

    // 바닥·길·잔디(VillageGroundTextures), 포탈 판·훈련장 바닥(VillagePrefabAssembler) 색을 팔레트로 다시 적고,
    // 광장 마법진 머티리얼을 걸어 마을을 다시 세운다.
    private static void Materials()
    {
        VillagePrefabAssembler.RefreshNamedMaterials();

        var village = Object.FindAnyObjectByType<VillageBlockout>();
        if (village == null) return;
        VillageEntrancePaths.ApplyMaterials(village);

        // 바깥 고리·가운데 원 모두 블룸에 걸리게 — 2026-09-25 "좀 더 화려하게"로 은은하던 값(0.45·0.6)에서 올렸다.
        // 명세 초기엔 포탈 쪽이 소환소보다 튀지 않게 문턱 아래로 눌렀었다(0.9·1.15는 하얗게 떴다).
        Material circle = MagicMaterial("M_Magic_Cyan", VillagePalette.MagicCyan * 1.2f);
        Material core = MagicMaterial("M_Magic_Bright", VillagePalette.MagicCyan * 1.6f);
        var so = new SerializedObject(village);
        so.FindProperty("magicCircleMaterial").objectReferenceValue = circle;
        so.FindProperty("magicCoreMaterial").objectReferenceValue = core;
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        village.Rebuild();
    }

    private static Material MagicMaterial(string name, Color emission)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            System.IO.Directory.CreateDirectory(MaterialFolder);
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        // 바탕은 어두운 돌에 청록을 조금 섞은 색 — 발광을 끄면 광장 돌과 이어진다.
        material.SetColor("_BaseColor", Color.Lerp(VillagePalette.DarkStone, VillagePalette.MagicCyan, 0.15f));
        material.SetFloat("_Smoothness", 0.3f);
        material.SetFloat("_Metallic", 0f);
        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        material.SetColor("_EmissionColor", emission);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void Sun()
    {
        Light sun = RenderSettings.sun;
        if (sun == null) return;
        Undo.RecordObject(sun, "다크 판타지 해");
        sun.useColorTemperature = false;
        sun.color = SunColor;
        sun.intensity = SunIntensity;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;   // 그림자 속이 새까맣지 않게
        EditorUtility.SetDirty(sun);
    }

    private static void Ambient()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = AmbientSky;
        RenderSettings.ambientEquatorColor = AmbientEquator;
        RenderSettings.ambientGroundColor = AmbientGround;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = 0.6f;   // 하늘 반사가 돌·지붕을 뿌옇게 띄우지 않게
    }

    private static void Sky()
    {
        Material sky = RenderSettings.skybox;
        if (sky == null) return;
        if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", SkyTint);
        if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", SkyGround);
        if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", SkyExposure);
        EditorUtility.SetDirty(sky);
    }

    private static void Fog()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = FogColor;
        RenderSettings.fogStartDistance = FogStart;
        RenderSettings.fogEndDistance = FogEnd;
    }

    private static void Volume()
    {
        Volume volume = null;
        foreach (Volume v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            if (v.isGlobal) { volume = v; break; }
        if (volume == null)
        {
            var go = new GameObject("Global Volume");
            Undo.RegisterCreatedObjectUndo(go, "Global Volume");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
        }
        if (volume.sharedProfile == null)
        {
            var created = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(created, "Assets/Settings/MainSceneVolume.asset");
            volume.sharedProfile = created;
        }
        VolumeProfile profile = volume.sharedProfile;

        Tonemapping tonemapping = Get<Tonemapping>(profile);
        tonemapping.mode.Override(TonemappingMode.Neutral);

        // 대비 조금, 채도는 조금만 낮게(참고 그림의 숲 녹색·불빛은 살아 있다), 아주 옅은 청회 필터.
        ColorAdjustments color = Get<ColorAdjustments>(profile);
        color.postExposure.Override(0.1f);
        color.contrast.Override(18f);
        color.saturation.Override(6f);
        color.colorFilter.Override(new Color(1f, 1f, 1f));

        // 그림자는 청록 회색, 밝은 곳(불빛)은 따뜻하게 — 차가운 저녁 속에 창불·횃불이 떠오르게.
        SplitToning split = Get<SplitToning>(profile);
        split.shadows.Override(new Color(0.48f, 0.51f, 0.51f));
        split.highlights.Override(new Color(0.56f, 0.52f, 0.46f));
        split.balance.Override(-20f);

        // 문턱 1(HDR): 발광 1을 넘는 불빛·마법광만 번진다.
        Bloom bloom = Get<Bloom>(profile);
        bloom.threshold.Override(1f);
        bloom.intensity.Override(1.25f);
        bloom.scatter.Override(0.75f);
        bloom.tint.Override(Color.white);
        bloom.highQualityFiltering.Override(false);

        Vignette vignette = Get<Vignette>(profile);
        vignette.color.Override(Color.black);
        vignette.intensity.Override(0.35f);
        vignette.smoothness.Override(0.45f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssetIfDirty(profile);
    }

    private static T Get<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet(out T component))
        {
            component = profile.Add<T>(true);
            // 프로필 에셋 안에 하위 에셋으로 넣어야 저장된다.
            AssetDatabase.AddObjectToAsset(component, profile);
        }
        component.active = true;
        return component;
    }

    private static void Terrain()
    {
        foreach (Terrain terrain in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
        {
            TerrainData data = terrain.terrainData;
            DetailPrototype[] details = data.detailPrototypes;
            for (int i = 0; i < details.Length; i++)
            {
                details[i].healthyColor = GrassHealthy;
                details[i].dryColor = GrassDry;
            }
            data.detailPrototypes = details;
            EditorUtility.SetDirty(data);

            foreach (TerrainLayer layer in data.terrainLayers)
            {
                if (layer == null || !AssetDatabase.GetAssetPath(layer).StartsWith(SessionLayers)) continue;
                string texture = layer.diffuseTexture != null ? layer.diffuseTexture.name.ToLowerInvariant() : "";
                if (!texture.Contains("grass")) continue;
                layer.diffuseRemapMax = new Vector4(GrassLayerTint.r, GrassLayerTint.g, GrassLayerTint.b, 1f);
                EditorUtility.SetDirty(layer);
            }
        }
    }
}
