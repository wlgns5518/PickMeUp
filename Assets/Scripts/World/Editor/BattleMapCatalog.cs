using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 전투 맵 테마의 이름(GroundTexture·PropGroup·SkyPreset …)이 실제로 가리키는 에셋.
//
// 전부 Procedural Worlds(Gaia) 샘플과 Book of the Dead 팩에서 가져온다. 마을 지형과 1~5층 평야가
// 이미 쓰고 있어 URP에서 제대로 그려진다는 것이 확인된 것들이다.
// 경로가 틀렸거나 에셋이 빠졌으면 그 항목만 경고를 남기고 건너뛴다.
public static class BattleMapCatalog
{
    private const string Packs = "Assets/Procedural Worlds/Packages - Install/";
    private const string NatureManufacture = Packs + "Asset Samples/NatureManufacture/";
    private const string Megascans = Packs + "Book of The Dead/Content Resources/Environment/_ExternalContent/Quixel/Megascans/TerrainTextures/";
    private const string Environment = Packs + "Book of The Dead/Prefabs/Environment/";
    private const string Pines = Environment + "Vegetation/Trees/Pines/";
    private const string ForestQuixel = Environment + "_ExternalContent/Quixel/Forest-quixel/";
    private const string MegascansPrefabs = Environment + "_ExternalContent/Quixel/Megascans/";
    private const string Cliffs = Environment + "Cliffs/";
    private const string Skyboxes = Packs + "Sky & Lighting Presets/Skyboxes/";

    public readonly struct GroundTextureSet
    {
        public readonly string Diffuse;
        public readonly string Normal;
        // 한 장이 덮는 크기(m).
        public readonly float Tile;
        // NatureManufacture 텍스처는 알파가 매끈함이다. Megascans 알베도는 알파를 믿을 수 없어 상수로 준다.
        public readonly bool SmoothnessInAlpha;

        public GroundTextureSet(string diffuse, string normal, float tile, bool smoothnessInAlpha)
        {
            Diffuse = diffuse;
            Normal = normal;
            Tile = tile;
            SmoothnessInAlpha = smoothnessInAlpha;
        }
    }

    public static GroundTextureSet Ground(GroundTexture texture)
    {
        switch (texture)
        {
            case GroundTexture.Grass:
                return new GroundTextureSet(NatureManufacture + "T_ground_grass_01_A_SM.tga", NatureManufacture + "T_ground_grass_01_N.png", 8f, true);
            case GroundTexture.RockA:
                return new GroundTextureSet(NatureManufacture + "T_ground_rock_01_A_SM.tga", NatureManufacture + "T_ground_rock_01_N.png", 10f, true);
            case GroundTexture.RockB:
                return new GroundTextureSet(NatureManufacture + "T_ground_rock_02_A_SM.tga", NatureManufacture + "T_ground_rock_02_N.png", 10f, true);
            case GroundTexture.Sand:
                return new GroundTextureSet(NatureManufacture + "T_Ground_Sand_02_A_Sm.tga", NatureManufacture + "T_Ground_Sand_02_N.png", 8f, true);
            case GroundTexture.Snow:
                return new GroundTextureSet(NatureManufacture + "T_Ground_Snow_1_A_Sm.tga", NatureManufacture + "T_Ground_Snow_1_N.tga", 10f, true);
            case GroundTexture.DriedGrass:
                return new GroundTextureSet(Megascans + "Grass_Dried_omgra0/Grass_Dried_omgra0_Albedo.tif", Megascans + "Grass_Dried_omgra0/Grass_Dried_omgra0_normal.tif", 4f, false);
            case GroundTexture.LushGrass:
                return new GroundTextureSet(Megascans + "Grass_pe1jvwp0/Grass_pe1jvwp0_Albedo.tif", Megascans + "Grass_pe1jvwp0/Grass_pe1jvwp0_normal.tif", 4f, false);
            case GroundTexture.Stones:
                return new GroundTextureSet(Megascans + "ScatteredStones_rmsih0p/ScatteredStones_rmsih0p_Albedo.tif", Megascans + "ScatteredStones_rmsih0p/ScatteredStones_rmsih0p_Normal.tif", 4f, false);
            default:
                return new GroundTextureSet(Megascans + "FloorSticks_olsgr/FloorSticks_olsgr_Albedo.tif", Megascans + "FloorSticks_olsgr/FloorSticks_olsgr_Normal.tif", 4f, false);
        }
    }

    // 발밑 풀. 마을 지형이 디테일로 쓰는 것과 같은 계열이다 — 루트에 메시 하나만 달린 프리팹이어야 한다.
    public readonly struct CoverSet
    {
        public readonly string[] Prefabs;
        public readonly float MinSize;
        public readonly float MaxSize;
        // 풀이 나는 자리 1m²에 서는 포기 수의 평균.
        public readonly float PerSquareMeter;

        public CoverSet(float minSize, float maxSize, float perSquareMeter, params string[] prefabs)
        {
            Prefabs = prefabs;
            MinSize = minSize;
            MaxSize = maxSize;
            PerSquareMeter = perSquareMeter;
        }
    }

    public static CoverSet Cover(GroundCover cover)
    {
        switch (cover)
        {
            case GroundCover.GreenGrass:
                return new CoverSet(0.8f, 1.2f, 0.55f,
                    MegascansPrefabs + "Plants/GrassGreen_qheqG2/GrassGreen_qheqG2_02_baked_NoLod.prefab",
                    MegascansPrefabs + "Plants/GrassGreen_qheqG2/GrassGreen_qheqG2_03_baked_NoLod.prefab");
            case GroundCover.DryGrass:
                return new CoverSet(0.6f, 1.1f, 0.7f,
                    ForestQuixel + "Dead_Grass_02/Dead_Grass_02_Var2_baked_NoLod.prefab");
            case GroundCover.ShortGrass:
                return new CoverSet(0.9f, 1.5f, 0.5f,
                    ForestQuixel + "MeadowGrass_01/Meadow_Grass_01_Var1_NoLod.prefab",
                    ForestQuixel + "MeadowGrass_01/Meadow_Grass_01_Var2_NoLod.prefab");
            default:
                return new CoverSet(0.9f, 1.6f, 0.12f,
                    Environment + "Ground/Sticks_debris/sticks_debris_00_Fixed.prefab");
        }
    }

    // 둘레 소품. 크기는 프리팹 원래 크기가 아니라 가로로 가장 긴 변의 목표 길이(m)로 준다 —
    // 같은 "바위"라도 팩마다 2m짜리와 150m짜리가 섞여 있다. 나무는 원래 크기에 배율만 준다.
    public readonly struct PropSet
    {
        public readonly string[] Prefabs;
        public readonly bool IsTree;
        public readonly float MinSize;
        public readonly float MaxSize;
        // 땅에 파묻는 비율(높이 대비).
        public readonly float Sink;

        public PropSet(bool isTree, float minSize, float maxSize, float sink, params string[] prefabs)
        {
            Prefabs = prefabs;
            IsTree = isTree;
            MinSize = minSize;
            MaxSize = maxSize;
            Sink = sink;
        }
    }

    public static PropSet Props(PropGroup group)
    {
        switch (group)
        {
            case PropGroup.TallPines:
                return new PropSet(true, 0.8f, 1.2f, 0f,
                    Pines + "Pine_002_new/Pine_002_L_baked_hierarchy.prefab",
                    Pines + "Pine_002_new/Pine_002_XL_baked_hierarchy.prefab",
                    Pines + "Pine_002_new/Pine_002_U_baked_hierarchy.prefab",
                    Pines + "Pine_004_new/pine_004_1_baked_hierarchy.prefab",
                    Pines + "Pine_004_new/pine_004_2_baked_hierarchy.prefab",
                    Pines + "Pine_004_new/pine_004_3_baked_hierarchy.prefab",
                    Pines + "Pine_005/Pine_005_01_baked_hierarchy.prefab");
            case PropGroup.SmallPines:
                return new PropSet(true, 0.8f, 1.25f, 0f,
                    Pines + "Pine_002_new/Pine_002_M2_baked_hierarchy.prefab",
                    Pines + "Pine_002_new/Pine_002_M3_baked_hierarchy.prefab",
                    Pines + "Pine_002_new/Pine_002_S2_baked_hierarchy.prefab",
                    Pines + "Pine_004_new/pine_004_3_0_baked_hierarchy.prefab",
                    Pines + "Pine_006/pine_006_01_baked_hierarchy.prefab",
                    Pines + "Pine_006/pine_006_02_baked_hierarchy.prefab",
                    Pines + "Pine_006/pine_006_03_baked_hierarchy.prefab");
            case PropGroup.DeadTrees:
                return new PropSet(true, 0.8f, 1.3f, 0f,
                    Pines + "PineDead_001/PineDead_02.prefab",
                    Pines + "PineDead_001/PineDead_03.prefab",
                    Environment + "Vegetation/Trees/Tree_Dead_001/tree_dead_001.prefab",
                    Pines + "Pine_007/pine_007_RootStump_baked_hierarchy.prefab");
            case PropGroup.Bushes:
                return new PropSet(true, 0.8f, 1.3f, 0f,
                    ForestQuixel + "GreenBush/GreenBush_Var01_Prefab_baked_single.prefab",
                    ForestQuixel + "Juniper_Bush_01/Juniper_Bush_01_Var1_baked_hierarchy.prefab",
                    ForestQuixel + "Juniper_Bush_01/Juniper_Bush_01_Var3_baked_hierarchy.prefab",
                    MegascansPrefabs + "Plants/Bush_b/Bush_b1_4x4x4_PF_baked_single.prefab");
            case PropGroup.RedBushes:
                return new PropSet(true, 0.8f, 1.4f, 0f,
                    ForestQuixel + "RedBush/RedBush_Var1_Prefab_baked_single.prefab");
            case PropGroup.Ferns:
                return new PropSet(true, 0.8f, 1.3f, 0f,
                    ForestQuixel + "Ferns/Fern_var01_Prefab_baked_single.prefab",
                    ForestQuixel + "Ferns/Fern_var02_Prefab_baked_single.prefab",
                    ForestQuixel + "Ferns/Fern_var03_Prefab_baked_single.prefab");
            case PropGroup.DeadShrubs:
                return new PropSet(true, 0.8f, 1.4f, 0f,
                    ForestQuixel + "Dead_Plant_03/Dead_Plant3_03_Var6_Prefab_baked_single.prefab",
                    ForestQuixel + "BushTwig_01/Bush_Twig_01_Var3_Prefab_baked_single.prefab",
                    ForestQuixel + "Dead_Grass_02/Dead_Grass_02_Var11_Prefab_baked_single.prefab",
                    ForestQuixel + "Dead_Grass_02/Dead_Grass_02_Var12_Prefab_baked_single.prefab");
            case PropGroup.Boulders:
                return new PropSet(false, 4f, 11f, 0.22f,
                    MegascansPrefabs + "Rocks/Rock_Granite_rgAsy/Aset_rock_granite_M_rgAsy.prefab",
                    MegascansPrefabs + "Rocks/Rock_Granite_rcCwC/Rock_Granite_rcCwC_Prefab.prefab",
                    Cliffs + "SmallCliff_01/SmallCliff_A.prefab");
            case PropGroup.FlatRocks:
                return new PropSet(false, 4f, 9f, 0.15f,
                    Cliffs + "FlatRock_01/FlatRock_01.prefab",
                    Cliffs + "Rock_06/Rock_06.prefab",
                    Cliffs + "Rock_06/Rock_06_B.prefab",
                    Cliffs + "RockSlussen_01/RockSlussen_01.prefab");
            case PropGroup.SmallRocks:
                return new PropSet(false, 1.2f, 3f, 0.2f,
                    MegascansPrefabs + "Rocks/Rock_Sandstone_plras/Rock_Sandstone_plras.prefab",
                    Cliffs + "SmallCliff_01/SmallCliff_01_partA.prefab");
            case PropGroup.Cliffs:
                return new PropSet(false, 30f, 70f, 0.3f,
                    Cliffs + "Yosemite_01/Cliff_01_Prefab.prefab",
                    Cliffs + "Yosemite_01/Cliff_01_Curved_A_Prefab.prefab",
                    Cliffs + "Rock_31/Rock_31.prefab",
                    Cliffs + "Rock_31/Rock_31_B.prefab");
            default:
                return new PropSet(false, 3f, 7f, 0.1f,
                    MegascansPrefabs + "Wood/Wood_Log_rfixH/Aset_wood_log_M_rfixH_prefab.prefab",
                    MegascansPrefabs + "Wood/Wood_Log_rhfdj/Wood_Log_rhfdj.prefab",
                    MegascansPrefabs + "Wood/Wood_Log_rfgxx/wood_log_M_rfgxx_Prefab.prefab",
                    Environment + "Ground/Roots/Roots_System_01_Prefab.prefab",
                    Environment + "Vegetation/Trees/Generic_Stump/Stump_04/Stump_04_Base.prefab");
        }
    }

    // null이면 절차적 하늘(Skybox/Procedural)이다.
    public static string SkyCubemap(SkyPreset sky)
    {
        switch (sky)
        {
            case SkyPreset.Day: return Skyboxes + "GaiaSampleDay.hdr";
            case SkyPreset.EveningTinted: return Skyboxes + "GaiaSampleEveningTinted.hdr";
            case SkyPreset.MorningEvening: return Skyboxes + "GaiaSampleMorningAndEvening.hdr";
            case SkyPreset.Night: return Skyboxes + "GaiaSampleNight.hdr";
            case SkyPreset.OasisSunset: return Skyboxes + "OasisSunset_T_Cube.exr";
            case SkyPreset.Cloudy: return Skyboxes + "PW_Sky_Cloudy.hdr";
            case SkyPreset.EveningClear: return Skyboxes + "PW_Sky_Evening_Clear.hdr";
            case SkyPreset.Clear: return Skyboxes + "TerminalSky_T_HDRI.exr";
            case SkyPreset.Stars: return Skyboxes + "starmap_8k.tif";
            default: return null;
        }
    }

    private static readonly Dictionary<string, Object> Loaded = new Dictionary<string, Object>();
    private static readonly HashSet<string> Warned = new HashSet<string>();

    public static T Load<T>(string path) where T : Object
    {
        if (Loaded.TryGetValue(path, out Object cached) && cached != null) return cached as T;

        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null && Warned.Add(path)) Debug.LogWarning($"[BattleMapCatalog] 에셋을 찾지 못해 건너뜁니다: {path}");
        Loaded[path] = asset;
        return asset;
    }
}
