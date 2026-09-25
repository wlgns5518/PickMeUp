using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Slot = FacilityBuilding.Slot;

// 구워 둔 파츠(VillagePartBaker)로 시설 프리팹을 조립하고, 마을(VillageBlockout)의 구역에 건다.
//
//   Facility_xxx.prefab  (FacilityBuilding — 레벨에 따라 파츠를 켜고 끈다)
//   ├── Foundation   기단              ├── Stairs        계단
//   ├── MainBody     본체              ├── SideModule    탑·옆 건물
//   ├── Wall         부벽(2레벨~)       ├── Decoration    깃발·오벨리스크·짐
//   ├── Roof         레벨별 지붕        ├── Lighting      등불·화로·점광원(3레벨)
//   ├── Entrance     문                └── UpgradeModule 윗층·테라스(3레벨)
//   └── Window       창
//
// 파츠는 전부 발밑 가운데가 원점이고 정면이 +Z, 크기는 미터다(VillagePartBaker가 그렇게 앉힌다).
// 그래서 여기서는 "본체 윗면에 지붕", "벽면에 문"처럼 실제 메시를 재서 붙인다 — 숫자로 짐작해 띄우면
// Meshy가 파츠마다 조금씩 다른 비율로 뽑아 주는 탓에 떠 있거나 파묻힌다.
//   - 쌓기: 아래 파츠의 윗면(메시 높이 × 배율)에서 조금 내려 얹는다.
//   - 붙이기: 벽 쪽으로 정점을 투영해 그 높이·폭 안에서 가장 바깥 면을 찾고, 붙일 파츠의 뒷면을 거기 댄다.
//
// 레벨 원칙(발자국 유지): 기단·본체·입구 자리는 모든 레벨에서 같다. 2레벨은 벽 둘레(부벽·깃발·화로)와
// 조금 큰 지붕, 3레벨은 위로(테라스·윗층·큰 지붕)와 탑·오벨리스크·점광원이 붙는다.
//
// 기준 반지름(radius)은 지금 마을 구역의 size와 같게 짜 두었다 — 구역 루트를 늘리지 않은 실제 크기다.
// 구역 size를 바꾸면 VillageBlockout이 그 비로 통째로 늘린다.
//
// "씬에 걸기"는 성벽(PolygonWall)에도 성벽 한 칸·모서리 탑 파츠를 건다. 성벽은 레벨이 없다.
public static class VillagePrefabAssembler
{
    public const string PrefabRoot = "Assets/Environment/Village/Prefabs";
    private const string MaterialRoot = "Assets/Environment/Village/Materials";

    private static readonly Vector2Int All = new Vector2Int(1, 3);
    private static readonly Vector2Int Lv1 = new Vector2Int(1, 1);
    private static readonly Vector2Int Lv2 = new Vector2Int(2, 2);
    private static readonly Vector2Int Upto2 = new Vector2Int(1, 2);
    private static readonly Vector2Int From2 = new Vector2Int(2, 3);
    private static readonly Vector2Int Lv3 = new Vector2Int(3, 3);

    private readonly struct Recipe
    {
        public readonly VillageBlockout.Kind kind;
        public readonly string file;
        public readonly float radius;
        public readonly Action<Builder> build;

        public Recipe(VillageBlockout.Kind kind, string file, float radius, Action<Builder> build)
        {
            this.kind = kind; this.file = file; this.radius = radius; this.build = build;
        }
    }

    private static readonly Recipe[] Recipes =
    {
        new Recipe(VillageBlockout.Kind.Rift,              "Facility_Rift",      18f,  BuildRift),
        new Recipe(VillageBlockout.Kind.Synthesis,         "Facility_Synthesis", 22f,  BuildSynthesis),
        new Recipe(VillageBlockout.Kind.Summoning,         "Facility_Summoning", 19f,  BuildSummoning),
        new Recipe(VillageBlockout.Kind.Armory,            "Facility_Armory",    9.6f, BuildArmory),
        new Recipe(VillageBlockout.Kind.EquipmentWorkshop, "Facility_Workshop",  20f,  BuildWorkshop),
        new Recipe(VillageBlockout.Kind.Training,          "Facility_Training",  30f,  BuildTraining),
        new Recipe(VillageBlockout.Kind.Airdock,           "Facility_Airdock",   18f,  BuildAirdock),
    };

    // 하는 일 없이 빈 땅을 채우는 거리(VillageBlockout.Kind.Street). 시설과 달리 구역이 씬에 없을 수 있어서
    // 처음 걸 때 쓸 자리(방위·거리·크기·방향)를 같이 들고 있다. 한 번 들어간 뒤에는 씬의 값이 우선이다 —
    // 씬에서 옮기고 "지금 놓인 자리로 값 맞추기"를 하면 그대로 남는다.
    // 시공의 틈 둘레(북동·북서 성벽길, 북동·북서 뒷골목)에는 집을 두지 않는다(2026-09-25 사용자) — 틈 앞은 비워 둔다.
    private readonly struct Street
    {
        public readonly string file, label;
        public readonly float bearing, distance, size, facing;
        public readonly Action<Builder> build;

        public Street(string file, string label, float bearing, float distance, float size, float facing, Action<Builder> build)
        {
            this.file = file; this.label = label; this.bearing = bearing; this.distance = distance;
            this.size = size; this.facing = facing; this.build = build;
        }
    }

    private static readonly Street[] Streets =
    {
        new Street("Street_East",      "동쪽 거리",   90f,  76f,  24f, 0f, BuildEastStreet),
        new Street("Street_West",      "서쪽 거리",  270f,  80f,  30f, 0f, BuildWestStreet),
        new Street("Street_SouthWest", "남서 거리",  212f,  70f,  22f, 0f, BuildSouthWestStreet),
        new Street("Street_Yard",      "짐마당",      62f,  45f,  12f, 0f, BuildYard),
        new Street("Street_SouthFarm", "남쪽 농장",  241.9f, 34f, 17f, -61.9f, b => BuildFarm(b, "Small Farm 1")),   // 서쪽 길과 서남 마을 길 사이
        new Street("Street_SouthEastFarm", "남동 농장", 125f, 55f, 16f, 0f, b => BuildFarm(b, "Small Farm 6")),
        new Street("Street_EastWall",  "동쪽 성벽길",  70f, 104f,  12f, 0f, BuildEastWallStreet),
        new Street("Street_WestWall",  "서쪽 성벽길", 290f, 104f,  12f, 0f, BuildWestWallStreet),
        new Street("Street_SouthWestVillage", "서남 마을", 237.1f, 101.2f, 22f, 2.9f, BuildSouthWestVillage),   // 숙소를 걷어 낸 자리
    };

    public static string PrefabPath(string file) => $"{PrefabRoot}/{file}.prefab";

    // ---- 메뉴 -----------------------------------------------------------------

    [MenuItem("PickMeUp/Village/4. 시설 프리팹 조립", priority = 60)]
    public static void AssembleAll()
    {
        foreach (Recipe recipe in Recipes) TryAssemble(recipe.file, recipe.radius, recipe.build);
        foreach (Street street in Streets) TryAssemble(street.file, street.size, street.build);
        AssetDatabase.SaveAssets();
    }

    private static void TryAssemble(string file, float radius, Action<Builder> build)
    {
        try
        {
            Assemble(file, radius, build);
        }
        catch (Exception e)
        {
            Debug.LogError($"[VillagePrefabAssembler] {file} 조립 실패: {e.Message}");
        }
    }

    /// 열린 씬의 마을 구역에 조립한 프리팹을 건다. 구역의 배치 값(방위·거리·크기·방향)은 건드리지 않는다.
    [MenuItem("PickMeUp/Village/5. 열린 씬 마을에 시설 프리팹 걸기", priority = 61)]
    public static void HookIntoScene() => SetScenePrefabs(true);

    /// 프리팹 칸을 비워 임시 도형으로 되돌린다. 임시 도형 코드(VillageBlockout.BuildXxx)는 그대로 남아 있다.
    [MenuItem("PickMeUp/Village/6. 열린 씬 마을을 임시 도형으로 되돌리기", priority = 62)]
    public static void UnhookFromScene() => SetScenePrefabs(false);

    private static void SetScenePrefabs(bool hook)
    {
        var village = UnityEngine.Object.FindAnyObjectByType<VillageBlockout>();
        if (village == null) { Debug.LogWarning("[VillagePrefabAssembler] 열린 씬에 VillageBlockout이 없다."); return; }

        var so = new SerializedObject(village);
        SerializedProperty districts = so.FindProperty("districts");
        for (int i = 0; i < districts.arraySize; i++)
        {
            SerializedProperty district = districts.GetArrayElementAtIndex(i);
            var kind = (VillageBlockout.Kind)district.FindPropertyRelative("kind").enumValueIndex;
            SerializedProperty prefab = district.FindPropertyRelative("prefab");

            GameObject asset = null;
            if (hook)
            {
                foreach (Recipe recipe in Recipes)
                    if (recipe.kind == kind) asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(recipe.file));
                if (asset == null) continue;   // 프리팹이 없는 구역(광장)은 임시 도형 그대로 둔다.
            }
            prefab.objectReferenceValue = asset;

            // 새로 생긴 칸이라 0으로 읽혀 있을 수 있다.
            SerializedProperty level = district.FindPropertyRelative("level");
            if (level.intValue < 1) level.intValue = 1;
        }

        // 거리: 이름으로 구역을 찾고, 없으면 기본 자리로 새로 넣는다. 되돌릴 때는 프리팹 칸만 비운다(거리는 임시 도형이 없어 사라진다).
        foreach (Street street in Streets)
        {
            SerializedProperty district = FindDistrict(districts, street.label);
            if (district == null)
            {
                if (!hook) continue;
                districts.arraySize++;
                district = districts.GetArrayElementAtIndex(districts.arraySize - 1);
                district.FindPropertyRelative("label").stringValue = street.label;
                district.FindPropertyRelative("kind").enumValueIndex = (int)VillageBlockout.Kind.Street;
                district.FindPropertyRelative("role").stringValue = "";
                district.FindPropertyRelative("bearing").floatValue = street.bearing;
                district.FindPropertyRelative("distance").floatValue = street.distance;
                district.FindPropertyRelative("size").floatValue = street.size;
                district.FindPropertyRelative("facing").floatValue = street.facing;
                district.FindPropertyRelative("useOtherBuilding").boolValue = false;
                district.FindPropertyRelative("level").intValue = 1;
                district.FindPropertyRelative("build").boolValue = true;
            }
            district.FindPropertyRelative("prefab").objectReferenceValue =
                hook ? AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(street.file)) : null;
        }

        // 길·부지: 걸 때는 재질을 걸고 큰길(없으면 기본 큰길)·부지·입구 길을 지금 배치에 맞춰 다시 깐다
        // (VillageEntrancePaths). 되돌릴 때도 길은 남긴다 — 임시 도형 위에서도 그대로 쓸 수 있다.
        so.ApplyModifiedProperties();
        if (hook) VillageEntrancePaths.Connect();
        else village.Rebuild();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(village.gameObject.scene);

        // 성벽도 같은 방식이다: 칸을 넣으면 모듈로, 비우면 상자 벽으로. 원작 색을 입힌 파츠 사본을 건다(VillageWallPalette).
        var wall = UnityEngine.Object.FindAnyObjectByType<PolygonWall>();
        if (wall == null) return;
        var wallSo = new SerializedObject(wall);
        wallSo.FindProperty("segmentPrefab").objectReferenceValue =
            hook ? VillageWallPalette.WallPrefab("wall_segment", "Wall_Segment") : null;
        wallSo.FindProperty("cornerPrefab").objectReferenceValue =
            hook ? VillageWallPalette.WallPrefab("wall_tower", "Wall_Tower") : null;
        wallSo.ApplyModifiedProperties();
        wall.Rebuild();
    }

    private static SerializedProperty FindDistrict(SerializedProperty districts, string label)
    {
        for (int i = 0; i < districts.arraySize; i++)
        {
            SerializedProperty district = districts.GetArrayElementAtIndex(i);
            if (district.FindPropertyRelative("label").stringValue == label) return district;
        }
        return null;
    }

    // ---- 조립 -----------------------------------------------------------------

    private static void Assemble(string file, float radius, Action<Builder> build)
    {
        Directory.CreateDirectory(PrefabRoot);
        var builder = new Builder(file);
        try
        {
            build(builder);
            GameObject root = builder.Finish(radius);
            VillageGaiaSkin.Apply(root, file);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(file));
            Debug.Log($"[VillagePrefabAssembler] {file} 조립 — 파츠 {builder.Count}개.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(builder.Root);
        }
    }

    // ---- 둥근 전당: 합성소·소환소 ------------------------------------------------

    private static void BuildSynthesis(Builder b)
    {
        // 합성소는 본체를 기둥 열이 두른다(임시 도형의 열두 기둥).
        Hall hall = BuildHall(b, bodyDiameter: 20f, plinthDiameter: 30f, plinthHeight: 1.6f,
            buttressBearings: new[] { 45f, 90f, 135f, 180f, 225f, 270f, 315f });

        float ring = hall.bodyRadius + 2.4f;
        for (int i = 0; i < 8; i++)
        {
            float bearing = 22.5f + i * 45f;
            b.Put(Slot.Decoration, "kit_pillar", Dir(bearing) * ring + Vector3.up * hall.floorY, bearing,
                b.Uniform("kit_pillar", hall.bodyHeight * 0.92f / b.Shape("kit_pillar").Height), All, true);
        }
    }

    private static void BuildSummoning(Builder b)
    {
        Hall hall = BuildHall(b, bodyDiameter: 16f, plinthDiameter: 23f, plinthHeight: 1.4f,
            buttressBearings: new[] { 60f, 120f, 180f, 240f, 300f });

        // 소환의 구슬. 문을 가리지 않게 앞 오른쪽 땅 위에 둔다(임시 도형에서 가장 눈에 띄던 것).
        Vector3 spot = Dir(38f) * (hall.plinthRadius + 3.5f);
        Placed orb = b.Put(Slot.SideModule, "summon_orb", spot, 38f, b.Uniform("summon_orb", 8f / b.Shape("summon_orb").Height), All, true);
        // 거점에서 가장 먼저 눈에 들어와야 하는 자리 — 구슬의 청록 빛이 둘레 바닥·벽에 번지게 모든 레벨에서 켠다(그림자 없음).
        Vector3 crystal = b.GlowCenter(orb, spot + Vector3.up * 6f);
        b.PointLight(crystal, VillagePalette.MagicCyan, 16f, 3f, All);
        b.PutFx(Slot.Lighting, "FX_OrbMotes", crystal, All);
    }

    private readonly struct Hall
    {
        public readonly float floorY, bodyRadius, bodyHeight, plinthRadius;

        public Hall(float floorY, float bodyRadius, float bodyHeight, float plinthRadius)
        {
            this.floorY = floorY; this.bodyRadius = bodyRadius; this.bodyHeight = bodyHeight; this.plinthRadius = plinthRadius;
        }
    }

    private static Hall BuildHall(Builder b, float bodyDiameter, float plinthDiameter, float plinthHeight, float[] buttressBearings)
    {
        Placed plinth = b.Put(Slot.Foundation, "kit_plinth", Vector3.zero, 0f,
            b.Fit("kit_plinth", plinthDiameter, plinthHeight, plinthDiameter), All, true);
        float floorY = plinth.Top - 0.05f;

        Placed body = b.Put(Slot.MainBody, "hall_body", new Vector3(0f, floorY, 0f), 0f,
            b.Uniform("hall_body", bodyDiameter / b.Shape("hall_body").Width), All, true);
        float bodyHeight = body.Height;
        float bodyRadius = bodyDiameter * 0.5f;

        // 입구: 본체의 문 구멍 앞에 문틀째 붙인다. 모든 레벨에서 같은 자리다.
        b.Attach(Slot.Entrance, "kit_door", body, 0f, floorY, bodyHeight * 0.72f, All, collider: true);
        Placed stairs = b.Put(Slot.Stairs, "kit_stairs", Vector3.zero, 0f,
            b.Fit("kit_stairs", bodyDiameter * 0.3f, plinthHeight, float.NaN), All);
        b.Move(stairs, new Vector3(0f, 0f, plinth.Reach(0f, 0f, plinthHeight, 1f) + stairs.Depth * 0.5f - 0.2f));

        // 창: 1레벨은 문 양옆 둘.
        foreach (float bearing in new[] { -55f, 55f })
            b.Attach(Slot.Window, "kit_window", body, bearing, floorY + bodyHeight * 0.3f, bodyHeight * 0.36f, All);

        // 지붕: 1레벨 기본, 2레벨은 처마가 더 나온 큰 지붕, 3레벨은 윗층 위로 올라간다.
        Shape roof = b.Shape("hall_roof");
        b.Put(Slot.Roof, "hall_roof", new Vector3(0f, body.Top - 0.25f, 0f), 0f, b.Uniform("hall_roof", bodyDiameter * 1.12f / roof.Width), Lv1, true);
        b.Put(Slot.Roof, "hall_roof", new Vector3(0f, body.Top - 0.25f, 0f), 0f, b.Uniform("hall_roof", bodyDiameter * 1.26f / roof.Width), Lv2, true);

        // 2레벨: 벽을 받치는 부벽, 입구 양옆 깃발과 화로.
        foreach (float bearing in buttressBearings)
            b.Attach(Slot.Wall, "kit_buttress", body, bearing, floorY, bodyHeight * 0.82f, From2);

        float plinthRadius = plinthDiameter * 0.5f;
        foreach (float side in new[] { -1f, 1f })
        {
            b.Put(Slot.Decoration, "kit_banner", Dir(side * 24f) * (plinthRadius - 1.4f) + Vector3.up * floorY, 0f,
                b.Uniform("kit_banner", bodyHeight * 0.7f / b.Shape("kit_banner").Height), From2);
            b.Put(Slot.Lighting, "kit_brazier", Dir(side * 40f) * (plinthRadius - 1.3f) + Vector3.up * floorY, 0f,
                b.Uniform("kit_brazier", 1f), From2);
            // 1레벨 조명: 계단 양옆 등불.
            b.Put(Slot.Lighting, "kit_lantern", new Vector3(side * (stairs.Width * 0.5f + 1.4f), 0f, plinthRadius + 1.2f), 0f,
                b.Uniform("kit_lantern", 1f), All);
        }

        // 3레벨: 옥상 테라스, 윗층, 큰 지붕, 옆 탑, 오벨리스크, 점광원.
        Placed terrace = b.Put(Slot.UpgradeModule, "kit_plinth", new Vector3(0f, body.Top - 0.15f, 0f), 0f,
            b.Fit("kit_plinth", bodyDiameter * 1.06f, 1.1f, bodyDiameter * 1.06f), Lv3, true);
        // 윗층은 본체를 줄여 얹고 문 구멍을 뒤로 돌린다 — 테라스로 나가는 문처럼 보이고, 앞에서는 가려진다.
        float upperDiameter = bodyDiameter * 0.7f;
        Placed upper = b.Put(Slot.UpgradeModule, "hall_body", new Vector3(0f, terrace.Top - 0.05f, 0f), 180f,
            b.Uniform("hall_body", upperDiameter / b.Shape("hall_body").Width), Lv3, true);
        b.Put(Slot.Roof, "hall_roof", new Vector3(0f, upper.Top - 0.25f, 0f), 0f,
            b.Uniform("hall_roof", upperDiameter * 1.3f / roof.Width), Lv3, true);
        foreach (float bearing in new[] { 0f, -70f, 70f })
            b.Attach(Slot.Window, "kit_window", upper, bearing, upper.Bottom + upper.Height * 0.3f, upper.Height * 0.4f, Lv3);

        b.Put(Slot.SideModule, "kit_tower", Dir(140f) * (bodyRadius * 0.95f) + Vector3.up * floorY, 140f,
            b.Uniform("kit_tower", (upper.Top - floorY) * 1.02f / b.Shape("kit_tower").Height), Lv3, true);

        foreach (float side in new[] { -1f, 1f })
        {
            b.Put(Slot.Decoration, "kit_obelisk", Dir(side * 68f) * (plinthRadius - 1.6f) + Vector3.up * floorY, side * 68f,
                b.Uniform("kit_obelisk", 1f), Lv3);
            b.PointLight(Dir(side * 40f) * (plinthRadius - 1.3f) + Vector3.up * (floorY + 2.2f), new Color(1f, 0.6f, 0.3f), 11f, 2.2f, Lv3);
        }

        return new Hall(floorY, bodyRadius, bodyHeight, plinthRadius);
    }

    // ---- 시공의 틈 --------------------------------------------------------------

    // 성벽 안쪽 면(로컬 z=0)에 등을 대고 선다. 단과 계단은 두지 않는다(사용자가 씬에서 걷어 낸 것).
    private static void BuildRift(Builder b)
    {
        Shape archShape = b.Shape("rift_arch");
        Vector3 archScale = b.Uniform("rift_arch", 26f / archShape.Height);
        float archZ = archShape.Depth * archScale.z * 0.5f + 0.4f;
        Placed arch = b.Put(Slot.MainBody, "rift_arch", new Vector3(0f, 0f, archZ), 0f, archScale, All, false);

        // 문 구멍 크기를 메시에서 잰다. 가운데가 비어 있으므로 문 높이 띠에서 가장 안쪽 정점이 기둥 안쪽 면이다.
        Vector2 opening = archShape.Opening();
        float halfWidth = opening.x * archScale.x;
        float top = opening.y * archScale.y;

        // 눌러서 원정을 떠나는 자리. 콜라이더는 어두운 판에만 둔다 — FacilityGate가 구역 루트에서 클릭을 받는다.
        Transform portal = b.Group(Slot.Entrance, "Portal", new Vector3(0f, 0f, archZ), All);
        b.Box(portal, "Void", new Vector3(0f, top * 0.5f, 0f), new Vector3(halfWidth * 2f, top, 0.6f), b.Material("RiftVoid"), true);
        b.Box(portal, "Glow", new Vector3(0f, top * 0.46f, 0.45f), new Vector3(halfWidth * 1.7f, top * 0.86f, 0.2f), b.Material("RiftGlow"), false);

        float side = arch.Width * 0.5f;
        foreach (float s in new[] { -1f, 1f })
        {
            // 1레벨: 등불. 2레벨: 화로·깃발, 성벽에 부벽. 3레벨: 양옆 망루와 결계 오벨리스크, 점광원.
            b.Put(Slot.Lighting, "kit_lantern", new Vector3(s * (side + 2f), 0f, archZ + 4f), 0f, b.Uniform("kit_lantern", 1.2f), All);
            b.Put(Slot.Lighting, "kit_brazier", new Vector3(s * (side + 1.5f), 0f, archZ + 8f), 0f, b.Uniform("kit_brazier", 1.2f), From2);
            b.Put(Slot.Decoration, "kit_banner", new Vector3(s * (side + 4.5f), 0f, 1.2f), 0f, b.Uniform("kit_banner", 1.5f), From2);
            b.Put(Slot.Wall, "kit_buttress", new Vector3(s * (side + 0.8f), 0f, b.Shape("kit_buttress").Depth * 1.9f * 0.5f), 0f,
                b.Uniform("kit_buttress", 1.9f), From2, true);

            b.Put(Slot.SideModule, "kit_tower", new Vector3(s * (side + 9f), 0f, 3.2f), 0f,
                b.Uniform("kit_tower", 30f / b.Shape("kit_tower").Height), Lv3, true);
            b.Put(Slot.Decoration, "kit_obelisk", new Vector3(s * (side - 1f), 0f, archZ + 13f), 0f, b.Uniform("kit_obelisk", 1f), Lv3);
            b.Put(Slot.Decoration, "kit_obelisk", new Vector3(s * (side + 5f), 0f, archZ + 17f), 0f, b.Uniform("kit_obelisk", 0.8f), Lv3);
            b.PointLight(new Vector3(s * (side + 1.5f), 2.6f, archZ + 8f), new Color(1f, 0.6f, 0.3f), 12f, 2.5f, Lv3);
        }
        // 포탈 빛 — 문 앞 바닥과 문틀을 청록으로 물들인다. 모든 레벨(그림자 없음). 3레벨 푸른 점광원은 그 위에 더해진다.
        b.PointLight(new Vector3(0f, top * 0.35f, archZ + 4f), VillagePalette.MagicCyan, 28f, 3.5f, All);
        b.PointLight(new Vector3(0f, top * 0.5f, archZ + 3f), new Color(0.55f, 0.75f, 1f), 24f, 3f, Lv3);

        // 문 앞에서 떠올라 문으로 빨려 드는 빛 알갱이, 문 안쪽 반짝임(문 구멍 크기에 맞춘다).
        GameObject motes = b.PutFx(Slot.Lighting, "FX_PortalMotes", new Vector3(0f, 0f, archZ + 7f), All);
        if (motes != null)
        {
            Transform sparkle = motes.transform.Find("Sparkle");
            if (sparkle != null)
            {
                var shape = sparkle.GetComponent<ParticleSystem>().shape;
                shape.position = new Vector3(0f, top * 0.5f, -6.2f);
                shape.scale = new Vector3(halfWidth * 1.8f, top * 0.9f, 1f);
            }
        }
    }

    // ---- 네모난 집: 무기창고 -----------------------------------------------------------

    // 집 한 채. 본체 위에 지붕, 앞에 문. 3레벨에서 윗층을 올리는 집이면 지붕이 한 층 위로 옮겨 간다.
    private static Placed House(Builder b, Vector3 spot, float yaw, float width, float height, float depth,
        bool raiseAtLevel3, bool door = true)
    {
        Placed body = b.Put(Slot.MainBody, "house_body", spot, yaw, b.Fit("house_body", width, height, depth), All, true);
        if (door) b.Attach(Slot.Entrance, "kit_door", body, yaw, spot.y, height * 0.78f, All, collider: true);

        if (!raiseAtLevel3)
        {
            b.Roof(body, All);
            return body;
        }

        b.Roof(body, Upto2);
        Placed upper = b.Put(Slot.UpgradeModule, "house_upper", new Vector3(spot.x, body.Top - 0.2f, spot.z), yaw,
            b.Fit("house_upper", width * 1.02f, height * 0.7f, depth * 1.02f), Lv3, true);
        b.Roof(upper, Lv3);
        return body;
    }

    private static void BuildArmory(Builder b)
    {
        Placed plinth = b.Put(Slot.Foundation, "kit_plinth", Vector3.zero, 0f, b.Fit("kit_plinth", 19f, 0.9f, 12.5f), All, true);
        float floorY = plinth.Top - 0.05f;
        Placed body = House(b, new Vector3(0f, floorY, 0f), 0f, 16f, 6f, 9f, true);

        Placed stairs = b.Put(Slot.Stairs, "kit_stairs", Vector3.zero, 0f, b.Fit("kit_stairs", 4.5f, floorY, float.NaN), All);
        b.Move(stairs, new Vector3(0f, 0f, plinth.Reach(0f, 0f, 0.9f, 1f) + stairs.Depth * 0.5f - 0.15f));

        foreach (float s in new[] { -1f, 1f })
        {
            b.Put(Slot.Decoration, "weapon_rack", new Vector3(s * 6.2f, floorY, 5.6f), 0f, b.Uniform("weapon_rack", 1f), All);
            b.Put(Slot.Lighting, "kit_lantern", new Vector3(s * 3.4f, 0f, 8.6f), 0f, b.Uniform("kit_lantern", 1f), All);
            // 2레벨: 긴 벽마다 부벽 둘, 입구 옆 깃발, 화로.
            foreach (float along in new[] { -3.5f, 3.5f })
                b.Attach(Slot.Wall, "kit_buttress", body, s * 90f, floorY, 5f, From2, along: along);
            b.Put(Slot.Decoration, "kit_banner", new Vector3(s * 2.8f, floorY, 5.3f), 0f, b.Uniform("kit_banner", 0.85f), From2);
            b.Put(Slot.Lighting, "kit_brazier", new Vector3(s * 8.4f, 0f, 7.8f), 0f, b.Uniform("kit_brazier", 1f), From2);
            b.PointLight(new Vector3(s * 8.4f, 2.3f, 7.8f), new Color(1f, 0.6f, 0.3f), 9f, 2f, Lv3);
        }
        b.Put(Slot.Decoration, "kit_crates", new Vector3(-8.6f, floorY, -2.5f), 90f, b.Uniform("kit_crates", 1f), All);
        b.Put(Slot.Decoration, "kit_crates", new Vector3(8.4f, floorY, -3.2f), -80f, b.Uniform("kit_crates", 0.9f), From2);
        b.Put(Slot.SideModule, "kit_tower", new Vector3(-7.4f, floorY, -4.4f), 200f,
            b.Uniform("kit_tower", 17f / b.Shape("kit_tower").Height), Lv3, true);
    }

    // 장비제작소 — 마을 한가운데의 대장간. 정면(+Z)이 마을 남쪽(기본 카메라)을 본다.
    // 본채는 Meshy 대장간(화덕·모루·굴뚝을 덮은 지붕) 한 채를 마당 한가운데에 크게 세운 것이다. 2026-09-25 사용자가 씬에서
    // Gaia 대장간 건물을 걷어 내고 옆에 있던 화덕을 가운데로 옮긴 뒤 "이걸 본채로 쓰고 키워 달라"고 했다.
    // 계단도 사용자가 두 줄을 나란히 붙여 정면 가운데를 넓게 열어 두었다.
    // 눌러서 장비를 만드는 것은 구역 루트의 FacilityGate(EquipmentWorkshop → EquipmentWorkshopUI)가 받는다 — 본채의 상자 콜라이더.
    private const float ForgeHeight = 14f;

    private static void BuildWorkshop(Builder b)
    {
        Placed yard = b.Put(Slot.Foundation, "kit_plinth", Vector3.zero, 0f, b.Fit("kit_plinth", 32f, 0.7f, 24f), All, true);
        float floor = yard.Top - 0.05f;

        Placed forge = b.Put(Slot.MainBody, "forge", new Vector3(0f, floor, -0.5f), 0f,
            b.Uniform("forge", ForgeHeight / b.Shape("forge").Height), All, true);
        float front = forge.transform.localPosition.z + forge.Depth * 0.5f;   // 화덕 앞면

        // 불티는 화덕 불(발광 마스크가 가장 밝은 곳)에서, 연기는 굴뚝 꼭대기에서.
        b.PutFx(Slot.Lighting, "FX_ForgeSparks", b.GlowCenter(forge, new Vector3(0f, floor + 2.5f, front - 2f)), All);
        b.PutFx(Slot.Lighting, "FX_ChimneySmoke", forge.TopCenter(0.06f) + Vector3.up * 0.3f, All);

        // 계단 두 줄(각 4.5m)을 붙여 9m 폭으로.
        foreach (float x in new[] { -2.25f, 2.25f })
        {
            Placed stairs = b.Put(Slot.Stairs, "kit_stairs", Vector3.zero, 0f, b.Fit("kit_stairs", 4.5f, floor, float.NaN), All);
            b.Move(stairs, new Vector3(x, 0f, yard.Reach(0f, 0f, 0.7f, 1f) + stairs.Depth * 0.5f - 0.15f));
        }

        // 1레벨: 화덕 앞 양옆 무기걸이, 짐·장작·수레·등불. 계단에서 화덕 앞까지(x ±4.5)는 비워 둔다.
        foreach (float s in new[] { -1f, 1f })
        {
            b.Put(Slot.Decoration, "weapon_rack", new Vector3(s * 6.5f, floor, front + 2.5f), s * 15f, b.Uniform("weapon_rack", 1.1f), All);
            b.Put(Slot.Lighting, "kit_lantern", new Vector3(s * 14f, floor, 10.5f), 0f, b.Uniform("kit_lantern", 1f), All);
        }
        b.Put(Slot.Decoration, "kit_crates", new Vector3(11.5f, floor, 5f), 20f, b.Uniform("kit_crates", 1f), All);
        b.PutAsset(Slot.Decoration, Gaia.Prop("Wood09"), new Vector3(-14.5f, floor, -1f), 90f, All);
        b.PutAsset(Slot.Decoration, Gaia.Prop("Wood08"), new Vector3(-14f, floor, -3.5f), 15f, All);
        b.PutAsset(Slot.Decoration, Gaia.Prop("Wagon07"), new Vector3(18.5f, 0f, 2f), 10f, All);

        // 2레벨: 계단 양옆 깃발, 마당 앞 화로, 본채 옆 헛간(재료 창고).
        foreach (float s in new[] { -1f, 1f })
        {
            b.Put(Slot.Decoration, "kit_banner", new Vector3(s * 7f, floor, 10.8f), 0f, b.Uniform("kit_banner", 0.9f), From2);
            b.Put(Slot.Lighting, "kit_brazier", new Vector3(s * 10.5f, floor, 8.5f), 0f, b.Uniform("kit_brazier", 1f), From2);
        }
        b.PutAsset(Slot.UpgradeModule, Gaia.Prop("Stable02B"), new Vector3(-13.5f, floor, -8f), 90f, From2);
        b.Put(Slot.Decoration, "kit_crates", new Vector3(-10.5f, floor, -1f), 200f, b.Uniform("kit_crates", 0.9f), From2);

        // 화덕 불빛 — 주변 벽·바닥에 은은하게. 모든 레벨에서 켠다(그림자 없음).
        b.PointLight(new Vector3(0f, floor + 2.5f, front + 1.5f), VillagePalette.WarmOrange, 11f, 1.6f, All);

        // 3레벨: 마당 밖 오른쪽 뒤 망루와 화로 불빛.
        b.Put(Slot.SideModule, "kit_tower", new Vector3(18.5f, 0f, -8f), 200f, b.Uniform("kit_tower", 18f / b.Shape("kit_tower").Height), Lv3, true);
        b.PointLight(new Vector3(10.5f, floor + 2.2f, 8.5f), new Color(1f, 0.6f, 0.3f), 10f, 2f, Lv3);
    }

    // ---- 거리: 하는 일 없이 빈 땅을 채운다 --------------------------------------------
    // Gaia 3DForge 집·헛간·수레·장작과 Meshy 공용 키트(짐·등불·깃발)를 섞는다. 로컬 +Z가 마을 한가운데 쪽이다.
    // Gaia 집은 문이 +X 박공 끝에 있어 0/180도로 두면 긴 벽이 길을 본다.

    // 동·서 거리와 남서 농장은 3DForge가 미리 짜 둔 마을 묶음을 통째로 놓는다(집 여러 채·헛간·수레·장작·울타리).
    // 원래 비탈 지형용이라 자식 높이가 1m 안쪽으로 들쭉날쭉한데, 평평한 마을 바닥에서도 티가 나지 않았다.
    // 묶음의 가운데가 원점에서 벗어나 있어 구역 가운데로 옮겨 놓는다. 앞쪽(+Z, 마을 한가운데)에 등불·깃발을 더해
    // Meshy 시설들과 같은 마을로 읽히게 한다.
    private static void BuildEastStreet(Builder b)
    {
        b.PutCluster(Slot.MainBody, Gaia.Complete("Village 2"), 0f, All);
        b.Put(Slot.Decoration, "kit_banner", new Vector3(-6f, 0f, 19f), 0f, b.Uniform("kit_banner", 1f), All);
        b.Put(Slot.Lighting, "kit_lantern", new Vector3(3f, 0f, 19.5f), 0f, b.Uniform("kit_lantern", 1f), All);
        b.Put(Slot.Lighting, "kit_lantern", new Vector3(-14f, 0f, 18f), 0f, b.Uniform("kit_lantern", 1f), All);
    }

    private static void BuildWestStreet(Builder b)
    {
        b.PutCluster(Slot.MainBody, Gaia.Complete("Village 1"), 0f, All);
        b.Put(Slot.Decoration, "kit_banner", new Vector3(6f, 0f, 30f), 0f, b.Uniform("kit_banner", 1f), All);
        b.Put(Slot.Lighting, "kit_lantern", new Vector3(-4f, 0f, 30.5f), 0f, b.Uniform("kit_lantern", 1f), All);
        b.Put(Slot.Lighting, "kit_lantern", new Vector3(14f, 0f, 29f), 0f, b.Uniform("kit_lantern", 1f), All);
    }

    // 서남 마을 옆 작은 농장.
    private static void BuildSouthWestStreet(Builder b)
    {
        b.PutCluster(Slot.MainBody, Gaia.Complete("Small Farm 3"), 0f, All);
        b.Put(Slot.Lighting, "kit_lantern", new Vector3(0f, 0f, 18f), 0f, b.Uniform("kit_lantern", 1f), All);
    }

    // 대장간과 소환소 사이의 짐 부리는 마당.
    private static void BuildYard(Builder b)
    {
        b.PutAsset(Slot.MainBody, Gaia.Prop("Stable02B"), new Vector3(0f, 0f, -5f), 0f, All);
        b.PutAsset(Slot.Decoration, Gaia.Prop("Wagon08"), new Vector3(-5.5f, 0f, 2f), 20f, All);
        b.PutAsset(Slot.Decoration, Gaia.Prop("Wagon05"), new Vector3(5f, 0f, 3f), -30f, All);
        b.PutAsset(Slot.Decoration, Gaia.Prop("Wood09"), new Vector3(-8f, 0f, -4f), 90f, All);
        b.PutAsset(Slot.Decoration, Gaia.Prop("Wood07"), new Vector3(7f, 0f, -3f), 0f, All);
        b.Put(Slot.Decoration, "kit_crates", new Vector3(2f, 0f, 6f), 35f, b.Uniform("kit_crates", 1f), All);
        b.Put(Slot.Decoration, "kit_crates", new Vector3(-2f, 0f, 5.5f), 160f, b.Uniform("kit_crates", 0.9f), All);
        b.Put(Slot.Lighting, "kit_lantern", new Vector3(0f, 0f, 8f), 0f, b.Uniform("kit_lantern", 1f), All);
    }

    private static void BuildFarm(Builder b, string cluster)
    {
        GameObject farm = b.PutCluster(Slot.MainBody, Gaia.Complete(cluster), 0f, All);
        // 농장 앞(마을 쪽) 끝에 등불 하나.
        b.Put(Slot.Lighting, "kit_lantern", new Vector3(0f, 0f, Builder.Bounds(farm).max.z + 1.5f), 0f, b.Uniform("kit_lantern", 1f), All);
    }

    // 성벽 안쪽 면이 로컬 z 약 -15에 있다.
    private static void BuildEastWallStreet(Builder b)
    {
        b.PutAsset(Slot.MainBody, Gaia.House("02B"), new Vector3(-1f, 0f, -6f), 0f, All);
        b.PutAsset(Slot.SideModule, Gaia.Prop("Stable01"), new Vector3(9f, 0f, -6.5f), 0f, All);
        b.PutAsset(Slot.Decoration, Gaia.Prop("Wagon06"), new Vector3(4f, 0f, 3f), 25f, All);
        b.PutAsset(Slot.Decoration, Gaia.Prop("Wood08"), new Vector3(-8.5f, 0f, 1f), 70f, All);
        b.Put(Slot.Decoration, "kit_crates", new Vector3(-4f, 0f, 3.5f), 10f, b.Uniform("kit_crates", 1f), All);
        b.Put(Slot.Lighting, "kit_lantern", new Vector3(1f, 0f, 5.5f), 0f, b.Uniform("kit_lantern", 1f), All);
    }

    private static void BuildWestWallStreet(Builder b)
    {
        b.PutAsset(Slot.MainBody, Gaia.House("01C"), new Vector3(1f, 0f, -6f), 180f, All);
        b.PutAsset(Slot.SideModule, Gaia.Prop("Stable02"), new Vector3(-9.5f, 0f, -6f), 0f, All);
        b.PutAsset(Slot.Decoration, Gaia.Prop("Wagon04"), new Vector3(-4f, 0f, 3f), -20f, All);
        b.PutAsset(Slot.Decoration, Gaia.Prop("Wood09"), new Vector3(8.5f, 0f, 1f), 90f, All);
        b.Put(Slot.Decoration, "kit_crates", new Vector3(4f, 0f, 3.5f), -15f, b.Uniform("kit_crates", 1f), All);
        b.Put(Slot.Lighting, "kit_lantern", new Vector3(-1f, 0f, 5.5f), 0f, b.Uniform("kit_lantern", 1f), All);
    }


    // Gaia(에셋 스토어 패키지, 저장소 밖) 3DForge 마을 에셋. 지형 나무와 마찬가지로 Gaia가 설치돼 있어야 보인다.
    private static class Gaia
    {
        private const string Root = "Assets/Procedural Worlds/Packages - Install/Asset Samples/3DForge/Prefabs/";
        public static string House(string variant) => Root + "fi_vil_GaiaHouse" + variant + ".prefab";
        public static string Prop(string name) => Root + "Props/fi_vil_Gaia" + name + ".prefab";
        // 미리 짜 둔 묶음: "Village 1~3", "Small Farm 1~6", "Medium Farm 1~2".
        public static string Complete(string name) => Root + "Complete/" + name + ".prefab";
    }

    // 서남 마을 — Meshy 집 여덟 채였던 숙소를 걷어 낸 자리(2026-09-25 사용자). 다른 거리처럼 Gaia 집으로 채운다.
    // 가운데 골목(로컬 Z, +Z가 마을 한가운데)을 사이에 두고 집 여섯 채가 문(+X 박공 끝)을 골목으로 향해 늘어선다.
    // 성벽 안쪽 면은 로컬 z 약 -18이다. 골목은 입구 길이 지나가므로 비워 두고, 짐은 골목 바깥 끝에 둔다.
    private static void BuildSouthWestVillage(Builder b)
    {
        string[] west = { "01A", "03A", "02B" };
        string[] east = { "04A", "02A", "03C" };
        float[] rows = { -10f, 1f, 12f };
        for (int i = 0; i < rows.Length; i++)
        {
            LaneHouse(b, Gaia.House(west[i]), rows[i], -1f);
            LaneHouse(b, Gaia.House(east[i]), rows[i], 1f);
        }

        // 집 사이 틈마다 골목 가장자리에 등불, 바깥 끝에 수레·장작.
        foreach (float s in new[] { -1f, 1f })
        {
            b.Put(Slot.Lighting, "kit_lantern", new Vector3(s * (LaneHalfWidth - 0.5f), 0f, -4.5f), 0f, b.Uniform("kit_lantern", 1f), All);
            b.Put(Slot.Lighting, "kit_lantern", new Vector3(-s * (LaneHalfWidth - 0.5f), 0f, 6.5f), 0f, b.Uniform("kit_lantern", 1f), All);
        }
        b.PutAsset(Slot.Decoration, Gaia.Prop("Wagon05"), new Vector3(-19f, 0f, 6.5f), 80f, All);
        b.PutAsset(Slot.Decoration, Gaia.Prop("Wood09"), new Vector3(-19.5f, 0f, -4.5f), 90f, All);
        b.PutAsset(Slot.Decoration, Gaia.Prop("Wagon03"), new Vector3(19f, 0f, -4.5f), -100f, All);
        b.PutAsset(Slot.Decoration, Gaia.Prop("Wood06"), new Vector3(19.5f, 0f, 6.5f), 0f, All);
        b.Put(Slot.Decoration, "kit_crates", new Vector3(-17f, 0f, 17f), 20f, b.Uniform("kit_crates", 1f), All);
        b.Put(Slot.Decoration, "kit_banner", new Vector3(LaneHalfWidth + 1.5f, 0f, 17.5f), 0f, b.Uniform("kit_banner", 0.9f), All);
    }

    private const float LaneHalfWidth = 3.5f;

    // 골목 한쪽(side -1 서쪽, +1 동쪽)에 집을 세운다. 문이 있는 +X 끝이 골목을 보게 돌리고,
    // 렌더러 범위로 재서 그 끝을 골목 가장자리에, 앞뒤 가운데를 줄(z)에 맞춘다 — 집마다 원점이 제각각이다.
    private static void LaneHouse(Builder b, string path, float z, float side)
    {
        GameObject house = b.PutAsset(Slot.MainBody, path, new Vector3(0f, 0f, z), side < 0f ? 0f : 180f, All);
        Bounds bounds = Builder.Bounds(house);
        float laneEnd = side < 0f ? bounds.max.x : bounds.min.x;
        house.transform.localPosition += new Vector3(side * LaneHalfWidth - laneEnd, 0f, z - bounds.center.z);
    }

    // ---- 훈련소 ---------------------------------------------------------------

    // 곧은 변이 성벽에 닿고 둥근 쪽(+Z)이 마을을 향한 반원. 둘레 숲은 VillageBlockout이 심는다.
        // 반원 마당 윗면 높이. 마당 위에 서는 것들은 전부 이 높이에 발을 둔다.
    private const float Yard = 0.36f;

private static void BuildTraining(Builder b)
    {
        b.HalfDisc(Slot.Foundation, "Ground", 30f, 0.35f, b.Material("TrainingGround"), 0.16f, All);
        b.HalfDisc(Slot.Foundation, "Yard", 25f, 0.2f, b.Material("TrainingDirt"), 0.36f, All);

        float[] dummies = { -8f, 0f, 8f, -16f, 16f };
        for (int i = 0; i < dummies.Length; i++)
            b.Put(Slot.Decoration, "training_dummy", new Vector3(dummies[i], Yard, 15f), 0f, b.Uniform("training_dummy", 1.2f), i < 3 ? All : From2, true);

        foreach (float s in new[] { -1f, 1f })
        {
            b.Put(Slot.Decoration, "weapon_rack", new Vector3(s * 18f, Yard, 6f), -s * 20f, b.Uniform("weapon_rack", 1.2f), All, true);
            b.Put(Slot.Decoration, "kit_banner", new Vector3(s * 10f, Yard, 2f), 0f, b.Uniform("kit_banner", 1.1f), From2);
            b.Put(Slot.Lighting, "kit_brazier", new Vector3(s * 22f, Yard, 9f), 0f, b.Uniform("kit_brazier", 1.1f), From2);
            b.Put(Slot.Lighting, "kit_lantern", new Vector3(s * 7.5f, Yard, 27f), 0f, b.Uniform("kit_lantern", 1f), All);
            b.Put(Slot.Decoration, "kit_obelisk", new Vector3(s * 9f, Yard, 24f), 0f, b.Uniform("kit_obelisk", 1f), Lv3);
            b.PointLight(new Vector3(s * 22f, 2.4f, 9f), new Color(1f, 0.6f, 0.3f), 11f, 2.2f, Lv3);
        }

        // 교관 단.
        Placed stand = b.Put(Slot.UpgradeModule, "kit_plinth", new Vector3(19f, Yard, 11f), 0f, b.Fit("kit_plinth", 8f, 1.4f, 8f), All, true);
        Placed stairs = b.Put(Slot.Stairs, "kit_stairs", Vector3.zero, 0f, b.Fit("kit_stairs", 3.4f, 1.4f, float.NaN), All);
        b.Move(stairs, new Vector3(19f, Yard, 11f + stand.Reach(0f, Yard, Yard + 1.4f, 1f) + stairs.Depth * 0.5f - 0.15f));
        b.Put(Slot.Decoration, "kit_banner", new Vector3(21.5f, stand.Top - 0.05f, 9f), 0f, b.Uniform("kit_banner", 0.8f), From2);

        b.Put(Slot.SideModule, "kit_tower", new Vector3(-21f, Yard, 3.5f), 150f, b.Uniform("kit_tower", 22f / b.Shape("kit_tower").Height), Lv3, true);
    }

    // ---- 비행선착장 -------------------------------------------------------------

    // 성벽 쪽(-Z)으로 뻗은 높은 갑판. 기둥 위에 돌판을 얹고, 앞쪽 탑으로 오른다.
    private static void BuildAirdock(Builder b)
    {
        const float deckY = 8.6f;
        for (int i = 0; i < 3; i++)
        foreach (float s in new[] { -1f, 1f })
        {
            b.Put(Slot.Foundation, "kit_pillar", new Vector3(s * 8f, 0f, -5f - i * 12f), 0f,
                b.Uniform("kit_pillar", deckY / b.Shape("kit_pillar").Height), All, true);
        }
        Placed deck = b.Put(Slot.MainBody, "kit_plinth", new Vector3(0f, deckY - 0.1f, -16f), 0f, b.Fit("kit_plinth", 21f, 1.3f, 38f), All, true);

        b.Put(Slot.Stairs, "kit_tower", new Vector3(7f, 0f, 4.4f), 0f, b.Uniform("kit_tower", 15f / b.Shape("kit_tower").Height), All, true);

        float top = deck.Top - 0.05f;
        b.Put(Slot.Lighting, "kit_lantern", new Vector3(-8.5f, top, 0f), 0f, b.Uniform("kit_lantern", 1f), All);
        b.Put(Slot.Lighting, "kit_lantern", new Vector3(8.5f, top, -30f), 0f, b.Uniform("kit_lantern", 1f), All);
        b.Put(Slot.Decoration, "kit_crates", new Vector3(-6f, top, -8f), 20f, b.Uniform("kit_crates", 1f), All);
        b.Put(Slot.Decoration, "kit_crates", new Vector3(-5.5f, top, -14f), 160f, b.Uniform("kit_crates", 1f), From2);
        foreach (float s in new[] { -1f, 1f })
        {
            b.Put(Slot.Decoration, "kit_banner", new Vector3(s * 9f, top, -24f), 0f, b.Uniform("kit_banner", 1f), From2);
            b.Put(Slot.Lighting, "kit_brazier", new Vector3(s * 8.5f, top, -12f), 0f, b.Uniform("kit_brazier", 1f), From2);
            b.PointLight(new Vector3(s * 8.5f, top + 2f, -12f), new Color(1f, 0.6f, 0.3f), 10f, 2f, Lv3);
        }
        // 3레벨: 갑판 끝의 계류탑.
        b.Put(Slot.SideModule, "kit_tower", new Vector3(7.5f, top, -32f), 180f, b.Uniform("kit_tower", 16f / b.Shape("kit_tower").Height), Lv3, true);
    }

    // ---- 조립 도구 -------------------------------------------------------------

    private static Vector3 Dir(float bearing)
    {
        float radians = bearing * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
    }

    // 파츠 메시 하나의 치수. 파츠 자기 공간(발밑 원점, 미터)이다.
    private sealed class Shape
    {
        public readonly Mesh mesh;
        public readonly Vector3[] vertices;
        public readonly Bounds bounds;

        public Shape(Mesh mesh)
        {
            this.mesh = mesh;
            vertices = mesh.vertices;
            bounds = mesh.bounds;
        }

        public float Width => bounds.size.x;
        public float Height => bounds.size.y;
        public float Depth => bounds.size.z;

        // 가운데가 빈 문(아치)의 안쪽 반폭과 높이.
        // 기둥 높이 띠(15~50%)의 정점을 가운데에서 바깥으로 칸을 나눠 보고, 앞뒤로 두꺼운(깊이의 35% 이상) 첫 칸을
        // 기둥 안쪽 면으로 친다. 가장 가까운 정점을 쓰면 문 안에 늘어진 사슬(앞뒤 0.5m)을 기둥으로 읽어
        // 문이 3분의 2로 좁아진다. 그 폭 안에서 가장 낮은 윗부분 정점이 아치 꼭대기 안쪽이다.
        public Vector2 Opening()
        {
            const int bins = 64;
            float step = Width * 0.5f / bins;
            var near = new float[bins];
            var far = new float[bins];
            for (int i = 0; i < bins; i++) { near[i] = float.MaxValue; far[i] = float.MinValue; }
            foreach (Vector3 v in vertices)
            {
                if (v.y < Height * 0.15f || v.y > Height * 0.5f) continue;
                int i = Mathf.Min(bins - 1, (int)(Mathf.Abs(v.x) / step));
                near[i] = Mathf.Min(near[i], v.z);
                far[i] = Mathf.Max(far[i], v.z);
            }
            float halfWidth = Width * 0.3f;
            for (int i = 0; i < bins; i++)
            {
                if (far[i] - near[i] < Depth * 0.35f) continue;
                halfWidth = i * step;
                break;
            }

            var tops = new List<float>();
            foreach (Vector3 v in vertices)
                if (Mathf.Abs(v.x) < halfWidth * 0.4f && v.y > Height * 0.4f) tops.Add(v.y);
            tops.Sort();
            float top = tops.Count > 0 ? tops[Mathf.Min(tops.Count - 1, tops.Count / 20)] : Height * 0.75f;
            return new Vector2(halfWidth, top);
        }
    }

    // 놓인 파츠 하나. 건물 공간(슬롯은 모두 원점에 있다) 기준의 값을 준다.
    private readonly struct Placed
    {
        public readonly Transform transform;
        public readonly Shape shape;

        public Placed(Transform transform, Shape shape)
        {
            this.transform = transform; this.shape = shape;
        }

        private Vector3 Scale => transform.localScale;
        public float Bottom => transform.localPosition.y + shape.bounds.min.y * Scale.y;
        public float Top => transform.localPosition.y + shape.bounds.max.y * Scale.y;
        public float Height => shape.Height * Scale.y;
        public float Width => shape.Width * Scale.x;
        public float Depth => shape.Depth * Scale.z;

        /// 파츠 맨 위 fraction(높이 비율) 안 정점들의 가운데(건물 공간) — 굴뚝 꼭대기처럼 가장 높이 솟은 곳.
        public Vector3 TopCenter(float fraction)
        {
            Matrix4x4 m = Matrix4x4.TRS(transform.localPosition, transform.localRotation, Scale);
            float cut = shape.bounds.max.y - shape.Height * fraction;
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (Vector3 v in shape.vertices)
            {
                if (v.y < cut) continue;
                sum += m.MultiplyPoint3x4(v);
                count++;
            }
            return count > 0 ? sum / count : new Vector3(transform.localPosition.x, Top, transform.localPosition.z);
        }

        /// 건물 공간의 bearing 방향으로 이 파츠의 바깥 면이 가운데에서 얼마나 떨어져 있는가.
        /// y 띠(건물 공간 높이)와, 그 방향에 수직인 옆 폭(halfWidth) 안의 정점만 본다.
        public float Reach(float bearing, float y0, float y1, float halfWidth)
        {
            Vector3 dir = Dir(bearing);
            var side = new Vector3(dir.z, 0f, -dir.x);
            Matrix4x4 m = Matrix4x4.TRS(transform.localPosition, transform.localRotation, Scale);
            Vector3 center = transform.localPosition;

            float best = float.NegativeInfinity;
            foreach (Vector3 v in shape.vertices)
            {
                Vector3 p = m.MultiplyPoint3x4(v);
                if (p.y < y0 || p.y > y1) continue;
                Vector3 offset = p - center;
                if (Mathf.Abs(Vector3.Dot(offset, side)) > halfWidth) continue;
                best = Mathf.Max(best, Vector3.Dot(offset, dir));
            }
            // 띠 안에 정점이 없으면(너무 좁게 잡았으면) 상자 크기로 어림한다.
            if (float.IsNegativeInfinity(best)) best = Mathf.Abs(Vector3.Dot(Vector3.Scale(shape.bounds.extents, Scale), dir));
            return best;
        }
    }

    /// 조립하지 않고 단색 머티리얼(포털 판·훈련장 바닥) 값만 다시 적는다(VillageMood가 팔레트를 바꿀 때 부른다).
    public static void RefreshNamedMaterials()
    {
        foreach (string name in new[] { "RiftVoid", "RiftGlow", "TrainingGround", "TrainingDirt" }) NamedMaterial(name);
    }

    // 파츠가 아닌 조각(포털 판, 훈련장 바닥)의 단색 머티리얼. 조립할 때마다 값을 다시 적어
    // 여기 숫자를 고치면 다음 조립에 그대로 반영된다. 색은 다크 판타지 팔레트(VillagePalette).
    private static Material NamedMaterial(string name)
    {
        string path = $"{MaterialRoot}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Directory.CreateDirectory(MaterialRoot);
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }

        material.DisableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", Color.black);
        switch (name)
        {
            case "RiftVoid":
                material.SetColor("_BaseColor", VillagePalette.DarkStone * 0.3f);
                material.SetFloat("_Smoothness", 0.75f);
                break;
            case "RiftGlow":
                // 포탈 속 에너지 — 청록 마법광. "좀 더 화려하게"(2026-09-25)로 블룸에 걸리게 올렸다(1.0→1.8).
                // 판이 커서 너무 올리면 하얗게 날아간다 — 앞에서 1.3도 하얗게 떴었는데, 지금은 해질녘이라 1.8까지 견딘다.
                material.SetColor("_BaseColor", VillagePalette.DarkStone * 0.5f);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetColor("_EmissionColor", VillagePalette.MagicCyan * 1.8f);
                break;
            case "TrainingGround":
                material.SetColor("_BaseColor", Color.Lerp(VillagePalette.DarkStone, VillagePalette.Stone, 0.35f));
                material.SetFloat("_Smoothness", 0.05f);
                break;
            case "TrainingDirt":
                // 다진 흙 — 어두운 목재 빛을 조금 눌러 돌 바닥과 갈리게.
                material.SetColor("_BaseColor", VillagePalette.DarkWood * 0.9f);
                material.SetFloat("_Smoothness", 0.05f);
                break;
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private sealed class Builder
    {
        public readonly GameObject Root;
        private readonly List<FacilityBuilding.Part> parts = new List<FacilityBuilding.Part>();
        private readonly Dictionary<Slot, Transform> slots = new Dictionary<Slot, Transform>();
        private readonly Dictionary<string, Shape> shapes = new Dictionary<string, Shape>();

        public int Count => parts.Count;

        public Builder(string name)
        {
            Root = new GameObject(name);
            foreach (Slot slot in (Slot[])Enum.GetValues(typeof(Slot)))
            {
                var child = new GameObject(slot.ToString()).transform;
                child.SetParent(Root.transform, false);
                slots[slot] = child;
            }
        }

        public GameObject Finish(float radius)
        {
            Root.AddComponent<FacilityBuilding>().EditorSetup(radius, parts);
            return Root;
        }

        public Shape Shape(string id)
        {
            if (shapes.TryGetValue(id, out Shape cached)) return cached;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VillagePartBaker.PrefabPath(id));
            Mesh mesh = prefab != null ? prefab.GetComponent<MeshFilter>().sharedMesh : null;
            if (mesh == null) throw new Exception($"파츠가 아직 없다: {id} (VillagePartBaker로 먼저 굽는다)");
            return shapes[id] = new Shape(mesh);
        }

        public Vector3 Uniform(string id, float scale) => Vector3.one * scale;

        /// 폭·높이·깊이(미터)에 맞추는 배율. NaN인 축은 나머지 두 축 평균으로 둔다(비율을 지킨다).
        public Vector3 Fit(string id, float width, float height, float depth)
        {
            Shape s = Shape(id);
            float x = float.IsNaN(width) ? float.NaN : width / s.Width;
            float y = float.IsNaN(height) ? float.NaN : height / s.Height;
            float z = float.IsNaN(depth) ? float.NaN : depth / s.Depth;
            float known = 0f; int count = 0;
            foreach (float v in new[] { x, y, z }) if (!float.IsNaN(v)) { known += v; count++; }
            float fill = count > 0 ? known / count : 1f;
            return new Vector3(float.IsNaN(x) ? fill : x, float.IsNaN(y) ? fill : y, float.IsNaN(z) ? fill : z);
        }

        public Placed Put(Slot slot, string id, Vector3 position, float yaw, Vector3 scale, Vector2Int levels, bool collider = false)
        {
            Shape shape = Shape(id);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VillagePartBaker.PrefabPath(id));
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slots[slot]);
            go.name = Label(id, levels);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = scale;

            // 클릭(FacilityGate)과 부딪힘용. 이 폴리곤 수에 메시 콜라이더는 비싸서 상자로 단다.
            if (collider)
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = shape.bounds.center;
                box.size = shape.bounds.size;
            }

            Register(slot, go, levels);
            return new Placed(go.transform, shape);
        }

        public void Move(Placed placed, Vector3 position) => placed.transform.localPosition = position;

        /// 입자 효과 프리팹(VillageFx)을 놓는다. 재질을 누르지 않는다(PutAsset은 Gaia 색 누름을 한다). 아직 안 구웠으면 건너뛴다.
        public GameObject PutFx(Slot slot, string name, Vector3 position, Vector2Int levels)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VillageFx.PrefabPath(name));
            if (prefab == null)
            {
                Debug.LogWarning($"[VillagePrefabAssembler] 효과가 아직 없다: {name} (메뉴 12. 입자 효과 다시 굽기)");
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slots[slot]);
            go.name = Label(name, levels);
            go.transform.localPosition = position;
            Register(slot, go, levels);
            return go;
        }

        /// 파츠에서 발광이 가장 센 곳의 가운데(건물 공간). 발광 마스크를 파츠 표면에 펼쳐 밝은 칸의 3D 위치를 평균낸다 —
        /// 화덕 불, 구슬 수정처럼 효과를 얹을 자리. 마스크가 없거나 비었으면 fallback.
        public Vector3 GlowCenter(Placed placed, Vector3 fallback)
        {
            string id = placed.shape.mesh.name.Replace("_mesh", "");
            string path = $"Assets/Environment/Village/Parts/{id}/{id}_emission.jpg";
            if (!File.Exists(path)) return fallback;
            var mask = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            mask.LoadImage(File.ReadAllBytes(path));
            VillagePartSurface surface = VillagePartSurface.Unwrap(placed.shape.mesh, mask.width, mask.height, 0);
            Color32[] px = mask.GetPixels32();
            UnityEngine.Object.DestroyImmediate(mask);
            if (surface == null) return fallback;

            Vector3 sum = Vector3.zero;
            float weight = 0f;
            for (int i = 0; i < px.Length; i++)
            {
                if (!surface.covered[i]) continue;
                float w = Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b)) / 255f;
                if (w < 0.5f) continue;
                sum += surface.positions[i] * w;
                weight += w;
            }
            if (weight <= 0f) return fallback;
            Matrix4x4 m = Matrix4x4.TRS(placed.transform.localPosition, placed.transform.localRotation, placed.transform.localScale);
            return m.MultiplyPoint3x4(sum / weight);
        }

        /// 우리 파츠가 아닌 에셋 프리팹(Gaia)을 그대로 놓는다. 콜라이더·LOD는 원본 것을 쓰고, 색만 마을 톤으로 누른다.
        public GameObject PutAsset(Slot slot, string path, Vector3 position, float yaw, Vector2Int levels, float scale = 1f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new Exception($"에셋이 없다: {path} (Gaia가 설치돼 있어야 한다)");

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slots[slot]);
            go.name = Label(prefab.name, levels);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = Toned(materials[i]);
                renderer.sharedMaterials = materials;
            }
            Register(slot, go, levels);
            return go;
        }

        /// 여러 채가 든 묶음 프리팹을 놓되, 묶음의 한가운데(렌더러 범위의 가운데)가 구역 원점에 오게 옮긴다.
        public GameObject PutCluster(Slot slot, string path, float yaw, Vector2Int levels)
        {
            GameObject go = PutAsset(slot, path, Vector3.zero, yaw, levels);
            Vector3 center = Bounds(go).center;
            go.transform.localPosition = new Vector3(-center.x, 0f, -center.z);
            return go;
        }

        /// 놓인 것의 렌더러 범위. 조립 중인 루트는 원점에 돌리지 않고 있어서 월드 범위가 곧 건물 공간 범위다.
        public static Bounds Bounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        // Gaia 3DForge 에셋은 회벽이 희고 지붕 널이 밝은 갈색이라 Meshy 파츠(어두운 돌·쇠) 옆에서 튄다.
        // 원본 머티리얼은 Gaia 패키지 안(저장소 밖, 재설치하면 되돌아간다)이라 건드리지 않고, 색만 누른 사본을 우리 폴더에 둔다.
        // 처음 만들 때만 0.72로 누른다. 그 뒤 다크 판타지 팔레트로 다시 칠한 아틀라스·색은 VillageGaiaSkin이 넣고, 조립이 덮지 않는다.
        private const float GaiaTone = 0.72f;

        private static Material Toned(Material source)
        {
            if (source == null) return null;
            string folder = $"{MaterialRoot}/Gaia";
            string path = $"{folder}/{source.name}_Village.mat";
            var toned = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (toned != null) return toned;   // 이미 있으면 그대로 — 팔레트 색은 VillageGaiaSkin.BakeHouseAtlas가 넣는다

            Directory.CreateDirectory(folder);
            toned = new Material(source) { name = source.name + "_Village" };
            AssetDatabase.CreateAsset(toned, path);
            if (source.HasProperty("_Color"))
            {
                Color color = source.GetColor("_Color") * GaiaTone;
                color.a = 1f;
                toned.SetColor("_Color", color);
            }
            EditorUtility.SetDirty(toned);
            return toned;
        }

        /// body의 bearing 쪽 벽면에 파츠의 평평한 뒷면을 댄다. 파츠 앞(+Z)이 바깥을 본다.
        /// along은 벽을 따라 옆으로 비킨 거리(미터), y는 파츠 발밑 높이, height는 파츠 높이(미터).
        public Placed Attach(Slot slot, string id, Placed body, float bearing, float y, float height, Vector2Int levels,
            float along = 0f, bool collider = false, float sink = 0.12f)
        {
            Shape shape = Shape(id);
            float scale = height / shape.Height;
            Vector3 dir = Dir(bearing);
            var side = new Vector3(dir.z, 0f, -dir.x);

            // 벽을 따라 비킨 자리에서 재야 둥근 벽이나 비스듬한 면에서도 뒤가 뜨지 않는다.
            Vector3 center = body.transform.localPosition + side * along;
            float reach = ReachAt(body, center, dir, side, y, y + height, shape.Width * scale * 0.5f);

            Vector3 position = center + dir * (reach - shape.bounds.min.z * scale - sink);
            position.y = y;
            return Put(slot, id, position, bearing, Vector3.one * scale, levels, collider);
        }

        // Placed.Reach와 같은데, 재는 가운데를 옮길 수 있다(벽을 따라 비킨 부벽).
        private static float ReachAt(Placed body, Vector3 center, Vector3 dir, Vector3 side, float y0, float y1, float halfWidth)
        {
            Matrix4x4 m = Matrix4x4.TRS(body.transform.localPosition, body.transform.localRotation, body.transform.localScale);
            float best = float.NegativeInfinity;
            foreach (Vector3 v in body.shape.vertices)
            {
                Vector3 p = m.MultiplyPoint3x4(v);
                if (p.y < y0 || p.y > y1) continue;
                Vector3 offset = p - center;
                offset.y = 0f;
                if (Mathf.Abs(Vector3.Dot(offset, side)) > halfWidth) continue;
                best = Mathf.Max(best, Vector3.Dot(offset, dir));
            }
            return float.IsNegativeInfinity(best) ? body.Reach(Vector3.SignedAngle(Vector3.forward, dir, Vector3.up), y0, y1, halfWidth) : best;
        }

        /// 본체 윗면에 지붕을 얹는다. 지붕의 용마루가 본체의 긴 쪽을 따라가게 돌리고, 처마가 사방으로 조금 나오게 늘린다.
        public Placed Roof(Placed body, Vector2Int levels, float overhang = 0.7f)
        {
            Shape roof = Shape("house_roof");
            float yaw = body.transform.localEulerAngles.y;
            bool ridgeAlongX = RidgeAlongX(roof);
            float width = body.Width + overhang * 2f;
            float depth = body.Depth + overhang * 2f;
            // 용마루가 로컬 Z를 따라 뽑혀 나왔으면 90도 돌려 얹고 폭과 깊이를 바꿔 잰다.
            Vector3 scale = ridgeAlongX
                ? new Vector3(width / roof.Width, 1f, depth / roof.Depth)
                : new Vector3(depth / roof.Width, 1f, width / roof.Depth);
            // 지붕 높이는 집 폭에 맞춰 경사가 비슷하게 보이도록.
            scale.y = Mathf.Clamp(Mathf.Min(scale.x, scale.z), 0.6f, 1.6f);
            Vector3 spot = body.transform.localPosition;
            spot.y = body.Top - 0.2f;
            return Put(Slot.Roof, "house_roof", spot, ridgeAlongX ? yaw : yaw + 90f, scale, levels, true);
        }

        // 용마루(꼭대기 모서리)가 X로 뻗었는지. 꼭대기 근처 정점이 어느 쪽으로 더 퍼져 있는지로 본다.
        private static bool RidgeAlongX(Shape roof)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Vector3 v in roof.vertices)
            {
                if (v.y < roof.Height * 0.85f) continue;
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                minZ = Mathf.Min(minZ, v.z); maxZ = Mathf.Max(maxZ, v.z);
            }
            return maxX - minX >= maxZ - minZ;
        }

        /// 여러 조각을 한 묶음으로 켜고 끄고 싶을 때(시공의 틈 입구).
        public Transform Group(Slot slot, string name, Vector3 position, Vector2Int levels)
        {
            var go = new GameObject(Label(name, levels));
            go.transform.SetParent(slots[slot], false);
            go.transform.localPosition = position;
            Register(slot, go, levels);
            return go.transform;
        }

        public void Box(Transform parent, string name, Vector3 center, Vector3 size, Material material, bool collider)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (collider) go.AddComponent<BoxCollider>();
        }

        /// 윗면이 y=top에 오는 반원 판. 곧은 변이 로컬 X축(z=0)에 놓이고 둥근 쪽이 +Z다.
        public void HalfDisc(Slot slot, string name, float radius, float thickness, Material material, float top, Vector2Int levels)
        {
            string path = $"{PrefabRoot}/Meshes/{Root.name}_{name}.asset";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Mesh mesh = HalfDiscMesh(Path.GetFileNameWithoutExtension(path), radius, thickness);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; }
            else AssetDatabase.CreateAsset(mesh, path);

            var go = new GameObject(Label(name, levels), typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(slots[slot], false);
            go.transform.localPosition = new Vector3(0f, top, 0f);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            Register(slot, go, levels);
        }

        /// 3레벨 "고급 조명". 그림자 없는 점광원 — 모바일에서도 몇 개는 견딘다.
        public void PointLight(Vector3 position, Color color, float range, float intensity, Vector2Int levels)
        {
            var go = new GameObject(Label("Light", levels));
            go.transform.SetParent(slots[Slot.Lighting], false);
            go.transform.localPosition = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            Register(Slot.Lighting, go, levels);
        }

        public Material Material(string name) => NamedMaterial(name);

        private void Register(Slot slot, GameObject go, Vector2Int levels)
        {
            parts.Add(new FacilityBuilding.Part { slot = slot, target = go, minLevel = levels.x, maxLevel = levels.y });
        }

        private static string Label(string name, Vector2Int levels)
        {
            if (levels.x == 1 && levels.y == FacilityBuilding.MaxLevel) return name;
            if (levels.x == levels.y) return $"{name} (Lv{levels.x})";
            return $"{name} (Lv{levels.x}-{levels.y})";
        }

        private static Mesh HalfDiscMesh(string name, float radius, float thickness)
        {
            const int steps = 24;
            var vertices = new List<Vector3> { Vector3.zero };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0f) };
            var triangles = new List<int>();

            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = Dir(-90f + 180f * i / steps) * radius;
                vertices.Add(p);
                uvs.Add(new Vector2(p.x / (radius * 2f) + 0.5f, p.z / radius));
            }
            for (int i = 0; i < steps; i++) { triangles.Add(0); triangles.Add(1 + i); triangles.Add(2 + i); }

            // 둥근 옆면. 판이 바닥보다 떠 있으므로 옆이 보인다.
            int ring = vertices.Count;
            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = Dir(-90f + 180f * i / steps) * radius;
                vertices.Add(p); uvs.Add(new Vector2(i / (float)steps, 1f));
                vertices.Add(p + Vector3.down * thickness); uvs.Add(new Vector2(i / (float)steps, 0f));
            }
            for (int i = 0; i < steps; i++)
            {
                int a = ring + i * 2, c = ring + (i + 1) * 2;
                triangles.Add(a); triangles.Add(a + 1); triangles.Add(c);
                triangles.Add(c); triangles.Add(a + 1); triangles.Add(c + 1);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
