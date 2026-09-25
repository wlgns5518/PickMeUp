using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 거점을 화려하게 하는 입자 효과(2026-09-25 사용자: "좀 더 화려하게"). 프로젝트에 불·마법 효과 에셋이 없어 코드로 짓는다
// (전투 맵 날씨와 같은 방식 — URP 입자 재질 + 기본 입자 무늬). 프리팹으로 구워 두고 조립기·성벽·마을이 가져다 쓴다.
//   FX_TorchFire   탑 화로의 불꽃과 불티           FX_PortalMotes  시공의 틈 앞 마법진에서 떠오르는 청록 빛 알갱이
//   FX_OrbMotes    소환 구슬 둘레를 도는 빛 알갱이   FX_ForgeSparks  화덕 불티      FX_ChimneySmoke 대장간 굴뚝 연기
//   FX_PlazaMotes  광장 마법진의 빛 알갱이          FX_Fireflies    마을 위를 떠도는 반딧불
//   Decor_TowerTorch  화로 파츠 + FX_TorchFire (성벽 모서리 탑 꼭대기)
// 모바일: 효과 하나에 입자 수십 개, 전부 그림자·충돌 없음. 불빛은 가산 합성이라 블룸(문턱 1)에 걸린다.
public static class VillageFx
{
    public const string Folder = "Assets/Environment/Village/Prefabs/FX";
    private const string MaterialFolder = "Assets/Environment/Village/Materials/FX";
    private const string ParticleMaterial = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";

    public static string PrefabPath(string name) => $"{Folder}/{name}.prefab";
    public static string TowerTorchPath => "Assets/Environment/Village/Prefabs/Decor_TowerTorch.prefab";

    private static Material additive, smoke;
    private const float AdditiveBoost = 2.4f;

    [MenuItem("PickMeUp/Village/12. 입자 효과 다시 굽기", priority = 69)]
    public static void BuildAll()
    {
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(MaterialFolder);
        additive = ParticleMat("FX_Additive", true);
        smoke = ParticleMat("FX_Smoke", false);

        TorchFire();
        PortalMotes();
        OrbMotes();
        ForgeSparks();
        ChimneySmoke();
        PlazaMotes();
        Fireflies();
        CloudSea();
        TowerTorch();
        AssetDatabase.SaveAssets();
    }

    // ---- 효과 ------------------------------------------------------------------------

    // 화로(높이 1.8m 파츠 기준 — 탑에서 2.6배로 키워도 계층 배율로 같이 커진다) 위의 불꽃 + 불티.
    private static void TorchFire()
    {
        var root = new GameObject("FX_TorchFire");
        try
        {
            ParticleSystem flame = Emitter(root, "Flame", additive, 30, loopRate: 26f);
            var main = flame.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.75f, 1.25f);
            main.startColor = new Color(1f, 0.62f, 0.25f, 1f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Shape(flame, ParticleSystemShapeType.Cone, Vector3.zero, 0.25f, angle: 8f);
            SizeOverLife(flame, 1f, 0.2f);
            Fade(flame, new Color(1f, 0.85f, 0.45f), new Color(0.9f, 0.3f, 0.08f));

            ParticleSystem embers = Emitter(root, "Embers", additive, 12, loopRate: 5f);
            main = embers.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.startColor = new Color(1f, 0.55f, 0.2f, 1f);
            Shape(embers, ParticleSystemShapeType.Cone, Vector3.zero, 0.3f, angle: 20f);
            Noise(embers, 0.6f, 0.8f);
            Fade(embers, new Color(1f, 0.75f, 0.35f), new Color(1f, 0.3f, 0.05f));
            Save(root, "FX_TorchFire");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // 시공의 틈 앞 원(지름 12m)에서 청록 알갱이가 천천히 떠올라 문 쪽으로 빨려 든다. 문 안쪽에도 반짝임.
    private static void PortalMotes()
    {
        var root = new GameObject("FX_PortalMotes");
        try
        {
            ParticleSystem rise = Emitter(root, "Rise", additive, 90, loopRate: 22f);
            var main = rise.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            main.startColor = VillagePalette.MagicCyan;
            Shape(rise, ParticleSystemShapeType.Circle, new Vector3(0f, 0.3f, 0f), 6f, rotateX: 90f);
            Velocity(rise, new Vector3(0f, 0.8f, -0.3f), new Vector3(0f, 2f, -0.8f));
            Noise(rise, 0.5f, 0.5f);
            Fade(rise, VillagePalette.MagicBright, VillagePalette.MagicCyan);

            ParticleSystem sparkle = Emitter(root, "Sparkle", additive, 40, loopRate: 14f);
            main = sparkle.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startColor = VillagePalette.MagicBright;
            // 문 구멍 안(너비 약 11m, 높이 약 16m), 원점은 문 앞 바닥 가운데 — 조립기가 맞춰 놓는다.
            Shape(sparkle, ParticleSystemShapeType.Box, new Vector3(0f, 8f, -3f), 1f, box: new Vector3(10f, 15f, 1f));
            Velocity(sparkle, new Vector3(-0.2f, 0.2f, -0.2f), new Vector3(0.2f, 0.8f, 0.2f));
            Fade(sparkle, Color.white, VillagePalette.MagicCyan, flicker: true);
            Save(root, "FX_PortalMotes");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // 소환 구슬(지름 약 4m) 둘레를 돌며 떠오르는 밝은 청록 알갱이.
    private static void OrbMotes()
    {
        var root = new GameObject("FX_OrbMotes");
        try
        {
            ParticleSystem orbit = Emitter(root, "Orbit", additive, 50, loopRate: 14f);
            var main = orbit.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startColor = VillagePalette.MagicBright;
            Shape(orbit, ParticleSystemShapeType.Sphere, Vector3.zero, 2.6f);
            var velocity = orbit.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            // 궤도 속도 세 축은 곡선 방식이 같아야 한다(하나만 두 값 사이로 두면 매 프레임 경고가 난다).
            velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            Fade(orbit, Color.white, VillagePalette.MagicCyan);
            Save(root, "FX_OrbMotes");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // 화덕에서 튀는 불티. 위로 튀었다가 떨어진다.
    private static void ForgeSparks()
    {
        var root = new GameObject("FX_ForgeSparks");
        try
        {
            ParticleSystem sparks = Emitter(root, "Sparks", additive, 40, loopRate: 12f);
            var main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.startColor = new Color(1f, 0.6f, 0.2f, 1f);
            main.gravityModifier = 0.7f;
            Shape(sparks, ParticleSystemShapeType.Cone, Vector3.zero, 0.8f, angle: 35f);
            Fade(sparks, new Color(1f, 0.85f, 0.5f), new Color(1f, 0.3f, 0.05f));
            var renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 1f;

            ParticleSystem glow = Emitter(root, "Glow", additive, 8, loopRate: 4f);
            main = glow.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(2f, 3.2f);
            main.startColor = new Color(1f, 0.45f, 0.15f, 0.35f);
            Shape(glow, ParticleSystemShapeType.Sphere, Vector3.zero, 0.8f);
            Fade(glow, new Color(1f, 0.6f, 0.25f), new Color(0.8f, 0.25f, 0.05f));
            Save(root, "FX_ForgeSparks");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // 굴뚝 연기 — 어두운 회색, 느리게 오르며 커지고 옅어진다(알파 합성).
    private static void ChimneySmoke()
    {
        var root = new GameObject("FX_ChimneySmoke");
        try
        {
            ParticleSystem puff = Emitter(root, "Smoke", smoke, 22, loopRate: 2.8f);
            var main = puff.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            main.startColor = new Color(0.32f, 0.33f, 0.35f, 0.45f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Shape(puff, ParticleSystemShapeType.Cone, Vector3.zero, 0.4f, angle: 6f);
            Velocity(puff, new Vector3(0.3f, 0f, 0.2f), new Vector3(0.8f, 0f, 0.5f));   // 바람에 조금 밀린다
            SizeOverLife(puff, 1f, 3.5f);
            Noise(puff, 0.3f, 0.2f);
            var rotation = puff.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.82f, 0.85f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            var color = puff.colorOverLifetime;
            color.enabled = true;
            color.color = fade;
            Save(root, "FX_ChimneySmoke");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // 광장 마법진(바깥 고리 지름 약 26m 기준 — 광장 구역 배율을 따른다)에서 떠오르는 빛 알갱이.
    private static void PlazaMotes()
    {
        var root = new GameObject("FX_PlazaMotes");
        try
        {
            ParticleSystem ring = Emitter(root, "Ring", additive, 70, loopRate: 16f);
            var main = ring.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            main.startColor = VillagePalette.MagicCyan;
            var shape = Shape(ring, ParticleSystemShapeType.Circle, new Vector3(0f, 0.6f, 0f), 12f, rotateX: 90f);
            shape.radiusThickness = 0.15f;   // 가장자리 고리에서만
            Velocity(ring, new Vector3(0f, 0.6f, 0f), new Vector3(0f, 1.6f, 0f));
            Noise(ring, 0.4f, 0.4f);
            Fade(ring, VillagePalette.MagicBright, VillagePalette.MagicCyan);
            Save(root, "FX_PlazaMotes");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // 마을 위(성벽 안 지름 약 200m)를 떠도는 반딧불 — 따뜻한 노랑이 켜졌다 꺼졌다 한다.
    private static void Fireflies()
    {
        var root = new GameObject("FX_Fireflies");
        try
        {
            ParticleSystem flies = Emitter(root, "Fireflies", additive, 160, loopRate: 26f);
            var main = flies.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 7f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            main.startColor = new Color(1f, 0.85f, 0.45f, 1f);
            Shape(flies, ParticleSystemShapeType.Circle, new Vector3(0f, 3f, 0f), 105f, rotateX: 90f);
            Velocity(flies, new Vector3(-0.3f, -0.2f, -0.3f), new Vector3(0.3f, 0.4f, 0.3f));
            Noise(flies, 0.8f, 0.3f);
            Fade(flies, new Color(1f, 0.9f, 0.55f), new Color(0.9f, 0.75f, 0.3f), flicker: true);
            Save(root, "FX_Fireflies");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // 떠 있는 섬(VillageIsland) 아래·둘레의 구름 바다 — 아주 큰 부드러운 알갱이가 천천히 흐른다(알파 합성).
    // 섬 아랫면 둘레(반지름 250~650m)의 두 겹: 섬보다 한참 아래 넓은 층, 가장자리 가까이 조금 높은 옅은 층.
    // 화면을 넓게 덮는 반투명이라 개수는 적게 둔다(모바일 채움 비용).
    private static void CloudSea()
    {
        var root = new GameObject("FX_CloudSea");
        try
        {
            ParticleSystem low = Emitter(root, "Low", smoke, 70, loopRate: 1f);
            var main = low.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(60f, 80f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(260f, 420f);
            main.startColor = new Color(0.42f, 0.48f, 0.55f, 0.35f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var shape = Shape(low, ParticleSystemShapeType.Circle, new Vector3(0f, -120f, 0f), 650f, rotateX: 90f);
            shape.radiusThickness = 0.62f;   // 가운데 250m 안은 비운다(섬 밑동)
            Velocity(low, new Vector3(0.6f, -0.1f, 0.2f), new Vector3(1.4f, 0.1f, 0.6f));
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            var color = low.colorOverLifetime;
            color.enabled = true;
            color.color = fade;

            ParticleSystem high = Emitter(root, "Wisps", smoke, 20, loopRate: 0.4f);
            main = high.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(45f, 60f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(150f, 240f);
            main.startColor = new Color(0.45f, 0.5f, 0.57f, 0.22f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            shape = Shape(high, ParticleSystemShapeType.Circle, new Vector3(0f, -45f, 0f), 480f, rotateX: 90f);
            shape.radiusThickness = 0.45f;   // 가장자리 바깥 고리(265~480m)
            Velocity(high, new Vector3(0.5f, 0f, 0.1f), new Vector3(1.2f, 0.2f, 0.5f));
            color = high.colorOverLifetime;
            color.enabled = true;
            color.color = fade;
            // 멀리 있는 커다란 반투명판이라 화면 크기 상한을 풀어 준다(기본 0.5는 가까울 때 잘린다).
            foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>())
            {
                renderer.maxParticleSize = 3f;
                renderer.sortingFudge = 50f;   // 섬·불빛보다 뒤로
            }
            Save(root, "FX_CloudSea");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // 성벽 모서리 탑 꼭대기: 화로 파츠 + 불꽃. 화로 윗면(파츠 높이의 90%)에 불꽃을 얹는다.
    private static void TowerTorch()
    {
        var brazier = AssetDatabase.LoadAssetAtPath<GameObject>(VillagePartBaker.PrefabPath("kit_brazier"));
        var fire = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath("FX_TorchFire"));
        if (brazier == null || fire == null) return;
        var root = (GameObject)PrefabUtility.InstantiatePrefab(brazier);
        try
        {
            root.name = "Decor_TowerTorch";
            float top = brazier.GetComponent<MeshFilter>().sharedMesh.bounds.max.y;
            var flame = (GameObject)PrefabUtility.InstantiatePrefab(fire, root.transform);
            flame.transform.localPosition = new Vector3(0f, top * 0.8f, 0f);
            PrefabUtility.SaveAsPrefabAsset(root, TowerTorchPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ---- 도구 -------------------------------------------------------------------------

    private static Material ParticleMat(string name, bool add)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        var source = AssetDatabase.LoadAssetAtPath<Material>(ParticleMaterial);
        if (material == null)
        {
            material = new Material(source) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", add ? 2f : 0f);   // 2 = 가산, 0 = 알파
        // 입자 색은 정점 색(8비트)이라 1을 못 넘는다. 가산 재질 색을 HDR로 올려 블룸 문턱(1)을 넘긴다.
        material.SetColor("_BaseColor", add ? Color.white * AdditiveBoost : Color.white);
        // URP 셰이더 GUI가 블렌드 상태·키워드를 맞춘다(에디터 어셈블리라 리플렉션으로 부른다).
        foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            System.Type gui = assembly.GetType("UnityEditor.BaseShaderGUI");
            if (gui == null) continue;
            gui.GetMethod("SetupMaterialBlendMode", new[] { typeof(Material) })?.Invoke(null, new object[] { material });
            break;
        }
        if (add)
        {
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
        }
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static ParticleSystem Emitter(GameObject root, string name, Material material, int maxParticles, float loopRate)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        var system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.loop = true;
        main.prewarm = true;
        main.playOnAwake = true;
        main.maxParticles = maxParticles;
        // 전부 제자리 효과라 로컬로 돈다. 월드로 두면 프리팹을 원점에 만들 때 미리 돌려 둔(prewarm) 입자가 원점에 남고,
        // 제자리로 옮긴 뒤 몇 초 동안 엉뚱한 곳(대장간 둘레)에 떠 있었다.
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var emission = system.emission;
        emission.rateOverTime = loopRate;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return system;
    }

    private static ParticleSystem.ShapeModule Shape(ParticleSystem system, ParticleSystemShapeType type, Vector3 position, float radius,
        float angle = 25f, float rotateX = 0f, Vector3 box = default)
    {
        var shape = system.shape;
        shape.enabled = true;
        shape.shapeType = type;
        shape.position = position;
        shape.radius = radius;
        shape.angle = angle;
        shape.rotation = new Vector3(type == ParticleSystemShapeType.Cone ? -90f : rotateX, 0f, 0f);
        if (type == ParticleSystemShapeType.Box) shape.scale = box;
        return shape;
    }

    private static void Velocity(ParticleSystem system, Vector3 min, Vector3 max)
    {
        var velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(min.x, max.x);
        velocity.y = new ParticleSystem.MinMaxCurve(min.y, max.y);
        velocity.z = new ParticleSystem.MinMaxCurve(min.z, max.z);
    }

    private static void Noise(ParticleSystem system, float strength, float frequency)
    {
        var noise = system.noise;
        noise.enabled = true;
        noise.strength = strength;
        noise.frequency = frequency;
        noise.scrollSpeed = 0.2f;
        noise.quality = ParticleSystemNoiseQuality.Low;
    }

    private static void SizeOverLife(ParticleSystem system, float start, float end)
    {
        var size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, start, 1f, end));
    }

    private static void Fade(ParticleSystem system, Color from, Color to, bool flicker = false)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
            flicker
                ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.15f, 0.35f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0.2f, 0.8f), new GradientAlphaKey(0f, 1f) }
                : new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
        var color = system.colorOverLifetime;
        color.enabled = true;
        color.color = gradient;
    }

    private static void Save(GameObject root, string name)
    {
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name));
    }
}
