using UnityEngine;
using static GroundCover;
using static GroundTexture;
using static PropGroup;

// 구간마다의 전투 맵 테마. BattleMapBuilder가 이 표대로 씬을 만든다.
//
// 1~5층 평야는 손으로 만든 원본 씬(Floor1~5)이라 여기 없다. 나머지 열아홉 구간은 서로 다른 색으로
// 기억되도록 짰다 — 전투 카메라에는 하늘보다 바닥·빛·안개·화면 색이 먼저 보이므로 그쪽을 겹치지 않게 했다.
// 탑을 오를수록 거칠고 어두워지다가 꼭대기에서 다시 밝아진다.
//
// 값을 고쳤으면 PickMeUp/전투 맵/모든 맵 다시 만들기로 씬에 반영한다.
public static class BattleMapThemes
{
    // 구간 첫 층으로 찾는다. 1~5층이거나 표에 없으면 null.
    public static BattleMapTheme Get(int stageFirstFloor)
    {
        switch (stageFirstFloor)
        {
            case 6: return new BattleMapTheme
            {
                Seed = 6,
                Landform = MapLandform.Canyon, Relief = 42f, Roughness = 0.6f,
                Floor = P(Sand, 1.05f, 0.82f, 0.62f), Patch = P(RockB, 0.95f, 0.7f, 0.52f), PatchCoverage = 0.3f, PatchScale = 1.3f,
                Slope = P(RockA, 0.85f, 0.52f, 0.36f), High = P(Sand, 1f, 0.78f, 0.58f), HighFrom = 30f,
                Cover = new[] { DryGrass }, CoverCoverage = 0.2f,
                Vegetation = new[] { DeadShrubs, DeadTrees }, VegetationCount = 250,
                Rocks = new[] { SmallRocks, FlatRocks, Boulders, Cliffs }, RockCount = 70,
                Sky = SkyPreset.Clear, SkyTint = C(0.55f, 0.5f, 0.45f),
                SunColor = C(1f, 0.88f, 0.72f), SunIntensity = 2.3f, SunPitch = 42f, SunYaw = 300f,
                AmbientSky = C(0.62f, 0.55f, 0.5f), AmbientEquator = C(0.55f, 0.42f, 0.33f), AmbientGround = C(0.35f, 0.24f, 0.17f),
                FogColor = C(0.8f, 0.62f, 0.48f), FogDensity = 0.006f,
                Temperature = 12f, Saturation = 8f, Contrast = 10f,
                Weather = MapWeather.Dust, WeatherAmount = 0.7f,
            };

            case 11: return new BattleMapTheme
            {
                Seed = 11,
                Landform = MapLandform.Hills, Relief = 30f, Roughness = 0.5f,
                Floor = P(ForestFloor, 1f, 0.97f, 0.88f), Patch = P(LushGrass, 0.85f, 0.95f, 0.75f), PatchCoverage = 0.4f,
                Slope = P(RockA, 0.75f, 0.8f, 0.75f), High = P(ForestFloor, 0.85f, 0.85f, 0.8f),
                Cover = new[] { GreenGrass, ShortGrass, Debris }, CoverCoverage = 0.3f,
                Vegetation = new[] { TallPines, SmallPines, Ferns, Bushes }, VegetationCount = 2600,
                Rocks = new[] { Boulders, Logs }, RockCount = 50,
                Sky = SkyPreset.Cloudy, SkyTint = C(0.45f, 0.48f, 0.5f), SkyExposure = 0.8f,
                SunColor = C(0.88f, 0.94f, 1f), SunIntensity = 1.6f, SunPitch = 55f, SunYaw = 20f, ShadowStrength = 0.7f,
                AmbientSky = C(0.58f, 0.66f, 0.66f), AmbientEquator = C(0.48f, 0.54f, 0.5f), AmbientGround = C(0.24f, 0.26f, 0.2f),
                FogColor = C(0.62f, 0.7f, 0.68f), FogDensity = 0.011f,
                Saturation = -10f, Contrast = 5f, Temperature = -8f, ColorFilter = C(0.95f, 1f, 0.97f),
                Weather = MapWeather.Spores, WeatherGlow = new Color(1.8f, 1.9f, 1.7f, 1f),
            };

            case 16: return new BattleMapTheme
            {
                Seed = 16,
                Landform = MapLandform.Dunes, Relief = 24f, Roughness = 0.3f,
                Floor = P(Sand, 1.12f, 0.97f, 0.75f), Patch = P(RockB, 1f, 0.82f, 0.62f), PatchCoverage = 0.15f, PatchScale = 0.7f,
                Slope = P(Sand, 0.95f, 0.78f, 0.58f), High = P(Sand, 1.1f, 0.95f, 0.75f),
                Cover = new[] { DryGrass }, CoverCoverage = 0.08f,
                Vegetation = new[] { DeadShrubs }, VegetationCount = 120,
                Rocks = new[] { SmallRocks, FlatRocks }, RockCount = 25,
                Sky = SkyPreset.Procedural, SkyTint = C(0.6f, 0.62f, 0.7f), SkyGround = C(0.7f, 0.6f, 0.45f), Atmosphere = 0.7f, SkyExposure = 1.4f, SunSize = 0.06f,
                SunColor = C(1f, 0.95f, 0.82f), SunIntensity = 2.8f, SunPitch = 68f, SunYaw = 200f,
                AmbientSky = C(0.75f, 0.72f, 0.65f), AmbientEquator = C(0.7f, 0.6f, 0.45f), AmbientGround = C(0.55f, 0.42f, 0.28f),
                FogColor = C(0.93f, 0.83f, 0.62f), FogDensity = 0.005f,
                PostExposure = 0.15f, Contrast = 12f, Saturation = -5f, Temperature = 18f,
                Weather = MapWeather.Dust,
            };

            case 21: return new BattleMapTheme
            {
                Seed = 21,
                Landform = MapLandform.Mountains, Relief = 120f, Roughness = 0.5f,
                Floor = P(Snow, 0.95f, 0.97f, 1f), Patch = P(RockB, 0.62f, 0.66f, 0.72f), PatchCoverage = 0.18f, PatchScale = 0.8f,
                Slope = P(RockA, 0.6f, 0.63f, 0.68f), High = P(Snow, 1f, 1f, 1.02f), HighFrom = 35f,
                Vegetation = new[] { SmallPines, TallPines }, VegetationCount = 700,
                Rocks = new[] { Boulders, FlatRocks }, RockCount = 45,
                Sky = SkyPreset.Cloudy, SkyTint = C(0.45f, 0.48f, 0.52f), SkyExposure = 0.9f,
                SunColor = C(0.9f, 0.94f, 1f), SunIntensity = 1.5f, SunPitch = 35f, SunYaw = 150f, ShadowStrength = 0.8f,
                AmbientSky = C(0.6f, 0.66f, 0.75f), AmbientEquator = C(0.55f, 0.58f, 0.62f), AmbientGround = C(0.45f, 0.47f, 0.5f),
                FogColor = C(0.78f, 0.83f, 0.9f), FogDensity = 0.011f,
                PostExposure = 0.05f, Temperature = -15f, Saturation = -15f,
                Weather = MapWeather.Snow,
            };

            case 26: return new BattleMapTheme
            {
                Seed = 26,
                Landform = MapLandform.Swamp, Relief = 20f, Roughness = 0.4f,
                // 고블린이 초록이라 바닥까지 초록이면 무리가 묻힌다(플레이 테스트). 바닥은 진흙 갈색, 풀만 초록으로 둔다.
                Floor = P(ForestFloor, 0.55f, 0.47f, 0.34f), Patch = P(LushGrass, 0.45f, 0.5f, 0.34f), PatchCoverage = 0.35f, PatchScale = 1.2f,
                Slope = P(RockA, 0.45f, 0.5f, 0.38f), High = P(Stones, 0.35f, 0.33f, 0.28f), HighFrom = -0.2f, HighBelow = true,
                Cover = new[] { GreenGrass, DryGrass, Debris }, CoverCoverage = 0.5f,
                Vegetation = new[] { DeadTrees, SmallPines, Ferns }, VegetationCount = 900,
                Rocks = new[] { Logs, SmallRocks }, RockCount = 60,
                Sky = SkyPreset.MorningEvening, SkyTint = C(0.4f, 0.45f, 0.35f), SkyExposure = 0.7f,
                SunColor = C(0.8f, 0.88f, 0.65f), SunIntensity = 1.4f, SunPitch = 30f, SunYaw = 60f, ShadowStrength = 0.5f,
                AmbientSky = C(0.53f, 0.62f, 0.49f), AmbientEquator = C(0.46f, 0.5f, 0.38f), AmbientGround = C(0.25f, 0.25f, 0.17f),
                FogColor = C(0.42f, 0.48f, 0.36f), FogDensity = 0.018f,
                PostExposure = 0.3f, Saturation = -20f, Contrast = 8f, ColorFilter = C(0.92f, 1f, 0.85f), Vignette = 0.35f,
                Weather = MapWeather.Spores, WeatherAmount = 1.2f,
                HasWater = true, WaterLevel = -0.35f, WaterColor = C(0.12f, 0.14f, 0.08f),
            };

            case 31: return new BattleMapTheme
            {
                Seed = 31,
                Landform = MapLandform.Crater, Relief = 50f, Roughness = 0.7f,
                Floor = P(RockB, 0.32f, 0.28f, 0.27f), Patch = P(Stones, 0.5f, 0.25f, 0.18f), PatchCoverage = 0.3f,
                Slope = P(RockA, 0.25f, 0.22f, 0.22f), High = P(Sand, 0.28f, 0.27f, 0.27f), HighFrom = 30f,
                Vegetation = new[] { DeadTrees }, VegetationCount = 150,
                Rocks = new[] { Boulders, FlatRocks, Cliffs }, RockCount = 50,
                Sky = SkyPreset.EveningTinted, SkyTint = C(0.6f, 0.3f, 0.25f), SkyExposure = 0.7f,
                SunColor = C(1f, 0.55f, 0.32f), SunIntensity = 1.5f, SunPitch = 35f, SunYaw = 250f,
                AmbientSky = C(0.55f, 0.25f, 0.15f), AmbientEquator = C(0.4f, 0.18f, 0.1f), AmbientGround = C(0.2f, 0.07f, 0.04f),
                FogColor = C(0.35f, 0.14f, 0.08f), FogDensity = 0.012f,
                Contrast = 18f, Temperature = 20f, Saturation = 10f, ColorFilter = C(1f, 0.9f, 0.85f), Vignette = 0.35f,
                Weather = MapWeather.Embers,
            };

            case 36: return new BattleMapTheme
            {
                Seed = 36,
                Landform = MapLandform.Mesas, Relief = 60f, Roughness = 0.5f,
                Floor = P(Sand, 1f, 0.72f, 0.55f), Patch = P(DriedGrass, 0.95f, 0.75f, 0.55f), PatchCoverage = 0.3f, PatchScale = 1.5f,
                Slope = P(RockB, 0.95f, 0.58f, 0.42f), High = P(RockB, 0.9f, 0.6f, 0.45f), HighFrom = 25f,
                Cover = new[] { DryGrass }, CoverCoverage = 0.2f,
                Vegetation = new[] { Bushes, DeadShrubs }, VegetationCount = 400,
                Rocks = new[] { FlatRocks, SmallRocks, Boulders }, RockCount = 40,
                Sky = SkyPreset.OasisSunset,
                SunColor = C(1f, 0.62f, 0.42f), SunIntensity = 1.8f, SunPitch = 12f, SunYaw = 90f,
                AmbientSky = C(0.55f, 0.42f, 0.55f), AmbientEquator = C(0.7f, 0.45f, 0.35f), AmbientGround = C(0.3f, 0.18f, 0.14f),
                FogColor = C(0.85f, 0.55f, 0.45f), FogDensity = 0.007f,
                Temperature = 15f, Saturation = 12f, Contrast = 8f, ColorFilter = C(1f, 0.93f, 0.9f),
            };

            case 41: return new BattleMapTheme
            {
                Seed = 41,
                Landform = MapLandform.Hills, Relief = 35f, Roughness = 0.5f,
                Floor = P(Grass, 1.15f, 0.88f, 0.45f), Patch = P(ForestFloor, 1f, 0.5f, 0.28f), PatchCoverage = 0.4f,
                Slope = P(RockA, 0.8f, 0.72f, 0.6f), High = P(DriedGrass, 1f, 0.8f, 0.5f),
                Cover = new[] { DryGrass, Debris }, CoverCoverage = 0.5f,
                Vegetation = new[] { RedBushes, Bushes, SmallPines, DeadShrubs }, VegetationCount = 1400,
                Rocks = new[] { Boulders, Logs }, RockCount = 40,
                Sky = SkyPreset.EveningTinted,
                SunColor = C(1f, 0.78f, 0.5f), SunIntensity = 1.9f, SunPitch = 24f, SunYaw = 280f,
                AmbientSky = C(0.6f, 0.5f, 0.42f), AmbientEquator = C(0.6f, 0.45f, 0.3f), AmbientGround = C(0.3f, 0.2f, 0.12f),
                FogColor = C(0.85f, 0.65f, 0.42f), FogDensity = 0.009f,
                Temperature = 20f, Saturation = 15f, Contrast = 6f,
                Weather = MapWeather.Leaves,
            };

            case 46: return new BattleMapTheme
            {
                Seed = 46,
                Landform = MapLandform.Lakeside, Relief = 45f, Roughness = 0.5f,
                Floor = P(Grass, 0.95f, 1f, 0.9f), Patch = P(Stones, 0.9f, 0.9f, 0.88f), PatchCoverage = 0.25f,
                Slope = P(RockA, 0.75f, 0.78f, 0.8f), High = P(Sand, 0.85f, 0.8f, 0.7f), HighFrom = -0.8f, HighBelow = true,
                Cover = new[] { GreenGrass }, CoverCoverage = 0.45f,
                Vegetation = new[] { TallPines, SmallPines, Bushes }, VegetationCount = 1500,
                Rocks = new[] { Boulders, FlatRocks }, RockCount = 45,
                Sky = SkyPreset.Day,
                SunColor = C(1f, 0.97f, 0.9f), SunIntensity = 2.1f, SunPitch = 48f, SunYaw = 320f,
                AmbientSky = C(0.6f, 0.7f, 0.85f), AmbientEquator = C(0.5f, 0.55f, 0.55f), AmbientGround = C(0.25f, 0.27f, 0.22f),
                FogColor = C(0.7f, 0.8f, 0.88f), FogDensity = 0.006f,
                Saturation = 10f, Temperature = -4f, Contrast = 4f,
                HasWater = true, WaterLevel = -1.2f, WaterColor = C(0.08f, 0.2f, 0.26f),
            };

            case 51: return new BattleMapTheme
            {
                Seed = 51,
                Landform = MapLandform.Hills, Relief = 25f, Roughness = 0.9f,
                Floor = P(Stones, 0.58f, 0.58f, 0.58f), Patch = P(RockB, 0.5f, 0.5f, 0.52f), PatchCoverage = 0.35f,
                Slope = P(RockB, 0.5f, 0.5f, 0.5f), High = P(Stones, 0.55f, 0.55f, 0.55f),
                Cover = new[] { Debris, DryGrass }, CoverCoverage = 0.2f,
                Vegetation = new[] { DeadTrees, DeadShrubs }, VegetationCount = 1200,
                Rocks = new[] { Logs, FlatRocks, SmallRocks }, RockCount = 70,
                Sky = SkyPreset.MorningEvening, SkyTint = C(0.42f, 0.42f, 0.42f), SkyExposure = 0.75f,
                SunColor = C(0.85f, 0.84f, 0.8f), SunIntensity = 1f, SunPitch = 38f, SunYaw = 200f, ShadowStrength = 0.6f,
                AmbientSky = C(0.48f, 0.48f, 0.5f), AmbientEquator = C(0.42f, 0.41f, 0.4f), AmbientGround = C(0.22f, 0.21f, 0.2f),
                FogColor = C(0.5f, 0.49f, 0.48f), FogDensity = 0.013f,
                Saturation = -45f, Contrast = 12f, PostExposure = -0.1f, Vignette = 0.35f,
                Weather = MapWeather.Ash,
            };

            case 56: return new BattleMapTheme
            {
                Seed = 56,
                Landform = MapLandform.Glacier, Relief = 70f, Roughness = 0.4f,
                Floor = P(Snow, 0.85f, 0.93f, 1.05f), Patch = P(RockB, 0.55f, 0.68f, 0.8f), PatchCoverage = 0.25f, PatchScale = 1.5f,
                Slope = P(Snow, 0.62f, 0.78f, 0.95f), High = P(Snow, 0.95f, 1f, 1.08f), HighFrom = 30f,
                Rocks = new[] { Boulders, FlatRocks }, RockCount = 35,
                Sky = SkyPreset.Procedural, SkyTint = C(0.35f, 0.45f, 0.6f), SkyGround = C(0.5f, 0.55f, 0.62f), Atmosphere = 0.6f, SkyExposure = 0.7f, SunSize = 0.03f,
                SunColor = C(0.72f, 0.82f, 1f), SunIntensity = 1.2f, SunPitch = 14f, SunYaw = 210f,
                AmbientSky = C(0.5f, 0.6f, 0.78f), AmbientEquator = C(0.55f, 0.65f, 0.78f), AmbientGround = C(0.45f, 0.52f, 0.62f),
                FogColor = C(0.7f, 0.8f, 0.92f), FogDensity = 0.015f,
                Temperature = -30f, Saturation = -10f, Contrast = 8f,
                Weather = MapWeather.Blizzard,
            };

            case 61: return new BattleMapTheme
            {
                Seed = 61,
                Landform = MapLandform.Hills, Relief = 30f, Roughness = 0.5f,
                Floor = P(ForestFloor, 0.7f, 0.75f, 0.85f), Patch = P(LushGrass, 0.55f, 0.65f, 0.7f), PatchCoverage = 0.35f,
                Slope = P(RockA, 0.6f, 0.65f, 0.75f), High = P(ForestFloor, 0.7f, 0.75f, 0.85f),
                Cover = new[] { GreenGrass, ShortGrass, Debris }, CoverCoverage = 0.45f,
                Vegetation = new[] { TallPines, SmallPines, Ferns }, VegetationCount = 2800,
                Rocks = new[] { Boulders, Logs }, RockCount = 45,
                Sky = SkyPreset.Night, SkyTint = C(0.38f, 0.45f, 0.62f), SkyExposure = 1.1f,
                SunColor = C(0.55f, 0.65f, 1f), SunIntensity = 1.3f, SunPitch = 40f, SunYaw = 140f, ShadowStrength = 0.8f,
                AmbientSky = C(0.3f, 0.36f, 0.55f), AmbientEquator = C(0.2f, 0.24f, 0.38f), AmbientGround = C(0.08f, 0.09f, 0.13f),
                FogColor = C(0.08f, 0.11f, 0.2f), FogDensity = 0.015f,
                PostExposure = 0.6f, Temperature = -25f, Saturation = -10f, Contrast = 10f, Vignette = 0.4f,
                Weather = MapWeather.Fireflies,
            };

            case 66: return new BattleMapTheme
            {
                Seed = 66,
                Landform = MapLandform.Canyon, Relief = 24f, Roughness = 0.9f,
                Floor = P(Sand, 0.95f, 0.6f, 0.45f), Patch = P(RockB, 0.85f, 0.5f, 0.38f), PatchCoverage = 0.4f, PatchScale = 0.8f,
                Slope = P(RockA, 0.85f, 0.48f, 0.35f), High = P(Sand, 0.9f, 0.55f, 0.4f), HighFrom = 18f,
                Cover = new[] { DryGrass }, CoverCoverage = 0.15f,
                Vegetation = new[] { DeadShrubs }, VegetationCount = 200,
                Rocks = new[] { SmallRocks, FlatRocks }, RockCount = 55,
                Sky = SkyPreset.EveningClear, SkyTint = C(0.6f, 0.45f, 0.35f), SkyExposure = 1.1f,
                SunColor = C(1f, 0.72f, 0.52f), SunIntensity = 2f, SunPitch = 32f, SunYaw = 120f,
                AmbientSky = C(0.7f, 0.5f, 0.4f), AmbientEquator = C(0.65f, 0.42f, 0.3f), AmbientGround = C(0.4f, 0.22f, 0.14f),
                FogColor = C(0.78f, 0.48f, 0.32f), FogDensity = 0.016f,
                Temperature = 25f, Contrast = 10f, Saturation = 5f,
                Weather = MapWeather.Dust, WeatherAmount = 2.2f,
            };

            case 71: return new BattleMapTheme
            {
                Seed = 71,
                Landform = MapLandform.Cliffside, Relief = 170f, Depth = 140f, Roughness = 0.6f,
                Floor = P(RockB, 0.8f, 0.8f, 0.82f), Patch = P(Snow, 1f, 1f, 1.02f), PatchCoverage = 0.3f, PatchScale = 1.2f,
                Slope = P(RockA, 0.72f, 0.72f, 0.75f), High = P(Snow, 1f, 1f, 1.02f), HighFrom = 10f,
                Cover = new[] { DryGrass }, CoverCoverage = 0.15f,
                Vegetation = new[] { SmallPines }, VegetationCount = 120,
                Rocks = new[] { Boulders, FlatRocks }, RockCount = 40,
                Sky = SkyPreset.Clear, SkyExposure = 1.1f,
                SunColor = C(1f, 0.95f, 0.88f), SunIntensity = 2.3f, SunPitch = 20f, SunYaw = 60f,
                AmbientSky = C(0.62f, 0.72f, 0.9f), AmbientEquator = C(0.7f, 0.72f, 0.75f), AmbientGround = C(0.8f, 0.8f, 0.82f),
                FogColor = C(0.92f, 0.94f, 0.97f), FogDensity = 0.006f,
                PostExposure = 0.1f, Contrast = 5f, Temperature = -5f,
            };

            case 76: return new BattleMapTheme
            {
                Seed = 76,
                Landform = MapLandform.Mountains, Relief = 60f, Roughness = 0.8f,
                // 늪과 같은 이유로 바닥은 젖은 잿빛 돌밭, 풀은 마른 풀만.
                Floor = P(Stones, 0.55f, 0.56f, 0.58f), Patch = P(LushGrass, 0.42f, 0.48f, 0.44f), PatchCoverage = 0.3f,
                Slope = P(RockA, 0.38f, 0.4f, 0.43f), High = P(RockA, 0.38f, 0.4f, 0.43f),
                Cover = new[] { DryGrass }, CoverCoverage = 0.4f,
                Vegetation = new[] { SmallPines, DeadTrees, Bushes }, VegetationCount = 600,
                Rocks = new[] { Boulders, FlatRocks }, RockCount = 50,
                Sky = SkyPreset.Cloudy, SkyTint = C(0.25f, 0.27f, 0.3f), SkyExposure = 0.45f,
                SunColor = C(0.7f, 0.76f, 0.85f), SunIntensity = 1.2f, SunPitch = 60f, SunYaw = 30f, ShadowStrength = 0.4f,
                AmbientSky = C(0.42f, 0.48f, 0.56f), AmbientEquator = C(0.36f, 0.39f, 0.42f), AmbientGround = C(0.17f, 0.18f, 0.18f),
                FogColor = C(0.28f, 0.31f, 0.35f), FogDensity = 0.016f,
                Saturation = -35f, Contrast = 12f, PostExposure = 0.15f, Temperature = -10f, Vignette = 0.4f,
                Weather = MapWeather.Rain,
            };

            case 81: return new BattleMapTheme
            {
                Seed = 81,
                Landform = MapLandform.Dunes, Relief = 20f, Roughness = 0.5f,
                Floor = P(Sand, 0.6f, 0.55f, 0.66f), Patch = P(RockB, 0.45f, 0.42f, 0.54f), PatchCoverage = 0.3f,
                Slope = P(Sand, 0.4f, 0.37f, 0.47f), High = P(Sand, 0.45f, 0.42f, 0.5f),
                Cover = new[] { DryGrass }, CoverCoverage = 0.15f,
                Vegetation = new[] { DeadTrees }, VegetationCount = 150,
                Rocks = new[] { Boulders, SmallRocks }, RockCount = 40,
                Sky = SkyPreset.OasisSunset, SkyTint = C(0.45f, 0.4f, 0.55f), SkyExposure = 0.8f, SkyRotation = 180f,
                SunColor = C(0.85f, 0.55f, 0.9f), SunIntensity = 1.6f, SunPitch = 18f, SunYaw = 70f,
                AmbientSky = C(0.6f, 0.45f, 0.8f), AmbientEquator = C(0.65f, 0.45f, 0.65f), AmbientGround = C(0.22f, 0.15f, 0.26f),
                FogColor = C(0.38f, 0.26f, 0.45f), FogDensity = 0.014f,
                PostExposure = 0.35f, Temperature = -5f, Saturation = 5f, Contrast = 10f, ColorFilter = C(0.95f, 0.88f, 1f), Vignette = 0.35f,
                Weather = MapWeather.Motes, WeatherAmount = 0.8f,
            };

            case 86: return new BattleMapTheme
            {
                Seed = 86,
                Landform = MapLandform.Crater, Relief = 75f, Roughness = 0.9f,
                Floor = P(RockB, 0.32f, 0.28f, 0.28f), Patch = P(Stones, 0.45f, 0.16f, 0.13f), PatchCoverage = 0.35f,
                Slope = P(RockA, 0.2f, 0.17f, 0.17f), High = P(RockA, 0.25f, 0.15f, 0.14f), HighFrom = 45f,
                Cover = new[] { Debris }, CoverCoverage = 0.1f,
                Vegetation = new[] { DeadTrees }, VegetationCount = 250,
                Rocks = new[] { Boulders, Cliffs, FlatRocks }, RockCount = 55,
                Sky = SkyPreset.Night, SkyTint = C(0.6f, 0.18f, 0.15f), SkyExposure = 0.9f,
                SunColor = C(1f, 0.3f, 0.25f), SunIntensity = 1.3f, SunPitch = 40f, SunYaw = 320f,
                AmbientSky = C(0.5f, 0.16f, 0.14f), AmbientEquator = C(0.38f, 0.12f, 0.1f), AmbientGround = C(0.14f, 0.04f, 0.04f),
                FogColor = C(0.2f, 0.04f, 0.04f), FogDensity = 0.014f,
                Contrast = 20f, Saturation = -5f, PostExposure = 0.5f, ColorFilter = C(1f, 0.85f, 0.85f), Vignette = 0.45f,
                Weather = MapWeather.Ash,
            };

            case 91: return new BattleMapTheme
            {
                Seed = 91,
                Landform = MapLandform.Cliffside, Relief = 0f, Depth = 260f, Roughness = 0.8f,
                Floor = P(RockA, 0.5f, 0.52f, 0.62f), Patch = P(Stones, 0.45f, 0.48f, 0.6f), PatchCoverage = 0.35f,
                Slope = P(RockB, 0.3f, 0.32f, 0.4f), High = P(RockA, 0.4f, 0.42f, 0.5f),
                Vegetation = new[] { DeadTrees }, VegetationCount = 60,
                Rocks = new[] { Boulders, FlatRocks }, RockCount = 45,
                Sky = SkyPreset.Stars, SkyTint = C(0.45f, 0.5f, 0.7f), SkyExposure = 1.6f,
                SunColor = C(0.6f, 0.7f, 1f), SunIntensity = 0.9f, SunPitch = 55f, SunYaw = 100f,
                AmbientSky = C(0.31f, 0.36f, 0.59f), AmbientEquator = C(0.22f, 0.25f, 0.42f), AmbientGround = C(0.06f, 0.06f, 0.11f),
                FogColor = C(0.03f, 0.04f, 0.09f), FogDensity = 0.012f,
                PostExposure = 0.7f, Temperature = -20f, Contrast = 12f, Vignette = 0.45f,
                Weather = MapWeather.Motes, WeatherAmount = 0.7f, WeatherGlow = new Color(1.5f, 2.2f, 4f, 1f),
            };

            case 96: return new BattleMapTheme
            {
                Seed = 96,
                Landform = MapLandform.Cliffside, Relief = 120f, Depth = 180f, Roughness = 0.4f,
                Floor = P(Stones, 1f, 0.95f, 0.85f), Patch = P(Sand, 1.05f, 0.98f, 0.85f), PatchCoverage = 0.3f,
                Slope = P(RockA, 0.9f, 0.86f, 0.8f), High = P(Snow, 1f, 1f, 1f), HighFrom = 20f,
                Cover = new[] { DryGrass }, CoverCoverage = 0.2f,
                Rocks = new[] { FlatRocks, Boulders }, RockCount = 30,
                Sky = SkyPreset.EveningClear, SkyTint = C(0.55f, 0.5f, 0.42f), SkyExposure = 1.2f,
                SunColor = C(1f, 0.9f, 0.7f), SunIntensity = 2.6f, SunPitch = 30f, SunYaw = 270f,
                AmbientSky = C(0.75f, 0.7f, 0.6f), AmbientEquator = C(0.8f, 0.7f, 0.55f), AmbientGround = C(0.5f, 0.42f, 0.32f),
                FogColor = C(0.95f, 0.88f, 0.72f), FogDensity = 0.008f,
                PostExposure = 0.15f, Temperature = 15f, Saturation = 5f, Contrast = 5f, ColorFilter = C(1f, 0.97f, 0.9f), Vignette = 0.2f,
                Weather = MapWeather.Motes, WeatherAmount = 1.2f,
            };

            default: return null;
        }
    }

    private static Color C(float r, float g, float b) => new Color(r, g, b);

    private static GroundPaint P(GroundTexture texture, float r, float g, float b, float tileScale = 1f) =>
        new GroundPaint(texture, new Color(r, g, b), tileScale);
}
