using UnityEngine;

// 전투 맵 한 장의 분위기. BattleMapBuilder가 이 값대로 지형·바닥·소품·하늘·빛·날씨를 깔아 씬을 만든다.
// 값은 BattleMapThemes에 구간마다 한 줄씩 있다.
//
// 싸우는 바닥은 어느 맵이든 평평하다(BattleMapBuilder 머리 주석). 그리고 전투 카메라는 36도로 내려다보며
// 발밑 55m 안쪽만 비춘다 — 하늘과 먼 산은 거의 화면에 들어오지 않는다. 그래서 전투 중 분위기를 실제로
// 만드는 것은 바닥 질감(Floor·Patch)과 풀(Cover), 빛과 안개의 색, 화면 색(Grade), 날씨다.
// 지형(Landform)과 소품은 화면 위쪽 가장자리를 채우고, 캐릭터가 전장 끝으로 밀려났을 때 드러난다.
//
// 맵 이름은 여기 없다 — 층 선택 화면도 같은 이름을 쓰므로 런타임 표(FloorStages)에 둔다.
public sealed class BattleMapTheme
{
    public int Seed;

    // ── 지형 ──
    public MapLandform Landform;
    // 둘레 지형이 솟는 높이(m). 절벽 지형(Cliffside)에서는 먼 봉우리의 높이다.
    public float Relief = 30f;
    // 절벽 지형(Cliffside)에서 전장 밖이 꺼지는 깊이(m).
    public float Depth;
    // 잔기복의 세기(0~1).
    public float Roughness = 0.5f;

    // ── 바닥 네 겹 ──
    // Floor: 전장 바닥, Patch: 그 위의 얼룩, Slope: 가파른 비탈, High: 높은(또는 낮은) 곳.
    public GroundPaint Floor;
    public GroundPaint Patch;
    public GroundPaint Slope;
    public GroundPaint High;
    // 바닥 중 얼룩이 덮는 비율(0~1).
    public float PatchCoverage = 0.35f;
    // 얼룩 한 덩이의 크기 배율. 크면 넓게 번진다.
    public float PatchScale = 1f;
    // 이 높이(m)부터 High 겹이 덮는다. HighBelow면 이 높이보다 낮은 곳을 덮는다(물가의 진흙·모래).
    public float HighFrom = float.PositiveInfinity;
    public bool HighBelow;

    // ── 발밑 풀(지형 디테일) ──
    // 전장 안에도 깔린다. NavMesh에 잡히지 않고 적도 그냥 헤치고 지나가므로 싸움을 막지 않는다.
    public GroundCover[] Cover = System.Array.Empty<GroundCover>();
    // 풀이 나는 자리를 가르는 얼룩의 비율(0~1). 1이면 고르게 덮는다.
    public float CoverCoverage = 0.5f;

    // ── 둘레 소품 ──
    // 나무·덤불(지형 나무 인스턴스). 전장 밖에만 선다.
    public PropGroup[] Vegetation = System.Array.Empty<PropGroup>();
    public int VegetationCount;
    // 바위·절벽·통나무(게임오브젝트). 전장 가장자리 띠와 비탈에 흩는다.
    public PropGroup[] Rocks = System.Array.Empty<PropGroup>();
    public int RockCount;
    public float RockScale = 1f;

    // ── 하늘 ──
    // 절차적 하늘은 푸른 계열에만 쓴다. _SkyTint는 산란 파장을 비트는 값이라 붉게 주면 지평선이 초록으로 뜬다.
    // 붉은 하늘은 노을 큐브맵에 틴트를 준다.
    public SkyPreset Sky;
    // 큐브맵이면 _Tint(회색 0.5가 원본), 절차적 하늘이면 _SkyTint.
    public Color SkyTint = new Color(0.5f, 0.5f, 0.5f);
    public float SkyExposure = 1f;
    public float SkyRotation;
    // 절차적 하늘(SkyPreset.Procedural)에만 쓴다.
    public Color SkyGround = new Color(0.37f, 0.35f, 0.34f);
    public float Atmosphere = 1f;
    public float SunSize = 0.04f;

    // ── 빛 ──
    public Color SunColor = Color.white;
    public float SunIntensity = 2f;
    public float SunPitch = 50f;
    public float SunYaw = 330f;
    public float ShadowStrength = 1f;
    // 주변광은 세 색(하늘·지평선·땅)으로 준다. 하늘 큐브맵에서 굽지 않아도 되고 색을 손으로 쥘 수 있다.
    public Color AmbientSky = new Color(0.55f, 0.6f, 0.7f);
    public Color AmbientEquator = new Color(0.45f, 0.45f, 0.45f);
    public Color AmbientGround = new Color(0.25f, 0.23f, 0.2f);
    public Color FogColor = new Color(0.6f, 0.65f, 0.7f);
    // 지수제곱 안개. 0.01이면 55m 앞 땅이 28% 흐려진다.
    public float FogDensity = 0.008f;

    // ── 화면 색(볼륨의 Color Adjustments / White Balance / Vignette) ──
    public float PostExposure;
    public float Contrast;
    public float Saturation;
    public float Temperature;
    public Color ColorFilter = Color.white;
    public float Vignette = 0.25f;

    // ── 날씨와 물 ──
    public MapWeather Weather;
    // 입자 양의 배율.
    public float WeatherAmount = 1f;
    // 입자 재질 색(HDR). 비워 두면(알파 0) 날씨마다 정해 둔 색을 쓴다.
    public Color WeatherGlow = Color.clear;
    public bool HasWater;
    public float WaterLevel;
    public Color WaterColor = new Color(0.1f, 0.15f, 0.15f);
}

public enum MapLandform
{
    Hills,      // 완만한 구릉
    Mountains,  // 멀어질수록 솟는 뾰족한 산
    Canyon,     // 전장을 계단 절벽이 둘러싸고 두 갈래 틈으로만 트인 협곡
    Dunes,      // 바람결을 따라 늘어선 모래 언덕
    Mesas,      // 낮은 황야 위로 드문드문 솟은 탁상 바위
    Crater,     // 전장을 둥근 능선이 에워싼 분화구
    Swamp,      // 물에 잠긴 땅과 드문드문 솟은 둔덕
    Lakeside,   // 한쪽이 호수로 꺼지는 물가
    Glacier,    // 둥근 얼음 능선과 갈라진 틈
    Cliffside,  // 전장만 높이 남고 둘레가 절벽 아래로 꺼지는 고지
}

public enum GroundTexture
{
    Grass,       // 짧은 풀밭
    RockA,       // 거친 바위
    RockB,       // 갈라진 바위
    Sand,
    Snow,
    DriedGrass,  // 마른 풀
    LushGrass,   // 우거진 풀
    Stones,      // 자갈이 흩어진 흙
    ForestFloor, // 솔잎·잔가지가 덮인 흙
}

// 지형 레이어 한 겹. 색은 텍스처에 곱해진다(TerrainLayer.diffuseRemapMax).
public readonly struct GroundPaint
{
    public readonly GroundTexture Texture;
    public readonly Color Tint;
    public readonly float TileScale;

    public GroundPaint(GroundTexture texture, Color tint, float tileScale = 1f)
    {
        Texture = texture;
        Tint = tint;
        TileScale = tileScale;
    }
}

// 발밑 풀. 지형 디테일 메시로 깐다 — 루트에 메시 하나만 달린 프리팹만 쓸 수 있어서 팩에서 고를 수 있는 것이 이 넷뿐이다.
public enum GroundCover
{
    GreenGrass,  // 무릎 높이 푸른 풀
    DryGrass,    // 마른 풀 포기
    ShortGrass,  // 낮은 풀 무더기
    Debris,      // 잔가지
}

public enum PropGroup
{
    // 지형 나무로 심는 것
    TallPines,
    SmallPines,
    DeadTrees,
    Bushes,
    RedBushes,
    Ferns,
    DeadShrubs,

    // 게임오브젝트로 놓는 것
    Boulders,
    FlatRocks,
    SmallRocks,
    Cliffs,
    Logs,
}

public enum SkyPreset
{
    Procedural,
    Day,            // 뭉게구름 낀 밝은 낮
    EveningTinted,  // 주황빛 구름 저녁
    MorningEvening, // 흐린 잿빛 저녁
    Night,          // 달 뜬 흐린 밤
    OasisSunset,    // 보랏빛 노을
    Cloudy,         // 구름 많은 파란 하늘
    EveningClear,   // 해 낮게 걸린 황혼
    Clear,          // 청명한 파란 하늘
    Stars,          // 별밭
}

public enum MapWeather
{
    None,
    Dust,       // 옆으로 흐르는 흙먼지
    Snow,
    Blizzard,   // 비스듬히 몰아치는 눈보라
    Ash,        // 천천히 내려앉는 재
    Embers,     // 떠오르는 불티
    Rain,
    Spores,     // 떠다니는 포자
    Fireflies,
    Leaves,     // 흩날리는 낙엽
    Motes,      // 떠오르는 빛 알갱이
}
