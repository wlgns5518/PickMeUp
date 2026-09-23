using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Pool;
using UnityEngine.UI;

// 층 선택의 탑 — 1층이 맨 아래, 꼭대기 층이 맨 위다. 위로 굴리면 위층이, 아래로 굴리면 아래층이 보인다.
//
//                 /\          ← 100층 위의 지붕
//        ☁      /____\
//            ┌──────────┐     한 층 = 돌벽 한 칸 + 창 둘 + 층 번호 판.
//            │ ▢ [12층] ▢│    깬 층은 창에 불이 켜져 있고, 잠긴 층은 벽과 창이 어둡다.
//            ├──────────┤     지금 도전할 층은 창 불빛이 더 밝고, 파티 표시에서 고리가 퍼져 나간다.
//     ☁      │ ▣ [11층] ▣│  ▐ 탑 옆 하늘은 구간(다섯 층)마다 그 전장의 색. 구름은 탑보다 느리게 흘러 높이가 느껴진다.
//            └─┬──────┬─┘  ▐ 오른쪽 막대는 탑 전체와 지금 보는 자리. 누르거나 끌어서 건너뛴다.
//      ▁▁▁▁▁▁▁▁│  문  │▁▁▁▁  ← 1층 아래의 입구와 땅
//
// 글자는 층 번호뿐이다. 깼는지·잠겼는지·어디서 싸우는지는 불빛과 색이 말한다.
//
// 100층을 다 만들어 두지 않는다. 화면에 들어온 층과 구간 하늘만 풀(ObjectPool)에서 꺼내 켜고, 화면 밖으로 나간 것은
// 끄고 풀에 돌려준다 — 몇 층을 오르내려도 살아 있는 층은 화면 한 장 분량(10개 안팎)뿐이고, 굴리는 동안 새로 만드는 것이 없다.
// 지붕과 입구는 하나씩뿐이라 풀 대신 보일 때만 켠다.
//
// 스크롤 창에 붙어 있어서 층 선택 화면이 닫히면(화면 루트가 꺼지면) LateUpdate도 같이 멈춘다.
// 화면(FacilityWindow)에 Update를 두지 않는 규칙은 그대로다.
[DisallowMultipleComponent]
public class FloorTowerView : MonoBehaviour
{
    public const float RowHeight = 120f;
    // 창 밖이라도 이만큼 안쪽이면 미리 켜 둔다 — 빠르게 굴릴 때 가장자리에 빈칸이 비치지 않게.
    private const float Preload = RowHeight;

    // ── 탑 몸통 ──
    private const float TowerWidth = 540f;
    // 오른쪽 막대 자리만큼 탑을 왼쪽으로 민다(창 가운데 기준).
    private const float TowerX = -35f;
    // 벽 무늬 한 장이 한 층 높이에 딱 들어가게 — 층과 층 사이에서 돌 줄눈이 끊기지 않는다.
    private const float WallTileWidth = RowHeight * 2f;
    private const float ShadeWidth = TowerWidth * 0.36f;
    private const float LedgeHeight = 12f;
    private const float LedgeOverhang = 12f;

    // ── 한 층 안 ──
    private const float WindowOffset = 168f;
    private const float WindowWidth = 64f;
    private const float WindowHeight = 88f;
    private const float WindowY = RowHeight * 0.46f;
    private const float GlowSize = 150f;
    private const float PlaqueWidth = 150f;
    private const float PlaqueHeight = 52f;
    private const float BadgeSize = 46f;
    private const float BadgeX = -PlaqueWidth * 0.5f - 6f;
    private const float PulseSize = 64f;

    // ── 위아래 ──
    private const float RoofWidth = TowerWidth * 1.28f;
    private const float RoofSink = 10f;       // 지붕 아래 변을 꼭대기 층 벽에 이만큼 묻는다
    private const float SkyAboveRoof = 70f;
    private const float BaseWallHeight = RowHeight * 2f;
    private const float BaseWidth = TowerWidth + 32f;
    private const float GroundHeight = 96f;
    private const float DoorHeight = 206f;

    // ── 하늘 ──
    private const int CloudsPerStage = 2;
    private const float CloudParallax = 0.22f;

    // ── 오른쪽 막대·버튼 ──
    private const float GaugeWidth = 18f;
    private const float GaugeRight = 33f;
    private const float GaugeInsetY = UiTheme.Space5;
    private const float GaugeMarkerSize = 22f;
    private const float JumpWidth = 150f;
    private const float JumpMargin = UiTheme.Space4;

    // 열 때 몇 층 아래에서 올라오며 멈추는지. 짧게 — 매번 기다리게 하면 거슬린다.
    private const float RiseFloors = 3f;
    private const float RiseSmoothTime = 0.3f;
    private const float GlideSmoothTime = 0.18f;
    private const float PulsePeriod = 1.8f;

    // 돌·청동·불빛 색. 탑은 어느 구간이든 같은 돌이다 — 구간 색은 하늘이 맡는다.
    private static readonly Color StoneFallback = new Color32(0x6E, 0x68, 0x61, 0xFF);
    private static readonly Color LedgeColor = new Color32(0x35, 0x31, 0x2D, 0xFF);
    private static readonly Color LedgeLight = new Color32(0x9A, 0x92, 0x86, 0xFF);
    private static readonly Color LockedWall = new Color(0.4f, 0.42f, 0.52f, 1f);
    private static readonly Color LockedWindow = new Color(0.2f, 0.21f, 0.27f, 1f);
    private static readonly Color WindowFallback = new Color32(0xF2, 0xB8, 0x5C, 0xFF);
    private static readonly Color Candle = new Color32(0xFF, 0xB0, 0x48, 0xFF);
    private static readonly Color PlaqueFill = new Color32(0x24, 0x20, 0x1C, 0xFF);
    private static readonly Color Bronze = new Color32(0xB0, 0x8D, 0x57, 0xFF);
    private static readonly Color PlaqueText = new Color32(0xF3, 0xE6, 0xC8, 0xFF);
    private static readonly Color SkyBase = new Color32(0x0C, 0x11, 0x1D, 0xFF);
    // 탑 아래는 저녁 하늘, 꼭대기는 밤하늘. 구간 색은 그 위에 조금만 얹는다.
    private static readonly Color SkyLow = new Color32(0x44, 0x5F, 0x80, 0xFF);
    private static readonly Color SkyHigh = new Color32(0x0A, 0x0D, 0x22, 0xFF);
    private static readonly Color Horizon = new Color32(0x9C, 0xAE, 0xC2, 0xFF);
    private const float StageTintInSky = 0.3f;
    private const float StarFullAltitude = 0.75f; // 탑 높이의 이 비율부터 별이 다 보인다
    private static readonly Color GroundTop = new Color32(0x4A, 0x6E, 0x3A, 0xFF);
    private static readonly Color GroundBottom = new Color32(0x22, 0x30, 0x1C, 0xFF);

    // 줄 자리 계산. 층이 몇 개든, 창 높이가 얼마든 같은 식이다(FloorTowerLayoutTests가 이것만 따로 본다).
    // 스크롤 값은 ScrollRect 내용물이 위로 올라간 거리다 — 0이면 꼭대기가 보이고, 클수록 아래층이 보인다.
    // 위 여백에는 지붕이, 아래 여백에는 입구와 땅이 들어간다.
    public readonly struct TowerLayout
    {
        public readonly int FirstFloor;
        public readonly int LastFloor;
        public readonly float RowHeight;
        public readonly float TopPadding;
        public readonly float BottomPadding;

        public TowerLayout(int firstFloor, int lastFloor, float rowHeight, float topPadding, float bottomPadding)
        {
            FirstFloor = firstFloor;
            LastFloor = lastFloor;
            RowHeight = rowHeight;
            TopPadding = topPadding;
            BottomPadding = bottomPadding;
        }

        public int FloorCount => LastFloor - FirstFloor + 1;
        public float TowerHeight => FloorCount * RowHeight;
        public float ContentHeight => TopPadding + TowerHeight + BottomPadding;

        // 목록 맨 위에서 그 층 줄의 윗변까지. 위층일수록 작다.
        public float TopOf(int floor) => TopPadding + (LastFloor - floor) * RowHeight;

        // 1층 줄의 아랫변(입구가 시작되는 곳).
        public float GroundFloorBottom => TopPadding + TowerHeight;

        public float MaxScroll(float viewHeight) => Mathf.Max(0f, ContentHeight - viewHeight);

        // 그 층이 창 가운데 오는 스크롤 값. 탑 끝에서는 끝을 넘지 않는다.
        public float ScrollToCenter(int floor, float viewHeight) =>
            Mathf.Clamp(TopOf(floor) + RowHeight * 0.5f - viewHeight * 0.5f, 0f, MaxScroll(viewHeight));

        // 스크롤 값 scroll에서 창에 걸치는 층(margin만큼 창 밖까지 친다). 하나도 없으면 low > high.
        public void VisibleRange(float scroll, float viewHeight, float margin, out int low, out int high)
        {
            float top = scroll - margin - TopPadding;
            float bottom = scroll + viewHeight + margin - TopPadding;
            int firstIndex = Mathf.Max(0, Mathf.FloorToInt(top / RowHeight));
            int lastIndex = Mathf.Min(FloorCount - 1, Mathf.CeilToInt(bottom / RowHeight) - 1);
            high = LastFloor - firstIndex;
            low = LastFloor - lastIndex;
        }

        // 목록 맨 위에서 잰 높이 y가 탑의 어디쯤인지 — 0이 1층 바닥, 1이 꼭대기 층 천장.
        public float RatioAt(float y) => Mathf.Clamp01((TopPadding + TowerHeight - y) / TowerHeight);

        // RatioAt의 반대.
        public float YAt(float ratio) => TopPadding + TowerHeight * (1f - Mathf.Clamp01(ratio));
    }

    // 풀에서 돌려 쓰는 한 층. 빌려 갈 때마다 Floor를 새로 받고 다시 칠한다.
    private sealed class FloorRow
    {
        public int Floor;
        public RectTransform Root;
        public Image Wall;
        public Image Highlight;
        public Image[] Glows;
        public Image[] Windows;
        public UiKit.Surface Plaque;
        public TMP_Text Number;
        public Image Lock;
        public Image Ring;
        public Image Badge;
    }

    // 풀에서 돌려 쓰는 구간 하늘 하나(다섯 층 높이, 맨 위·맨 아래 구간은 여백까지).
    private sealed class StageSky
    {
        public int Stage;
        public RectTransform Root;
        public UiGradient Fill;
        public Image Stars;
        // 구름은 하늘과 따로 한 층 위에 둔다 — 구간 경계를 넘어 흘러간 구름을 위 구간의 하늘이 덮어 반듯하게 잘리지 않게.
        public RectTransform CloudRoot;
        public Image[] Clouds;
        public float[] CloudTops;
        public float Top;
        public float Height;
    }

    private ScrollRect scroll;
    private RectTransform viewport;
    private RectTransform content;
    private RectTransform skyLayer;
    private RectTransform cloudLayer;
    private RectTransform structureLayer;
    private RectTransform rowLayer;
    private TowerLayout layout;
    private UiIconLibrary art;

    private ObjectPool<FloorRow> rowPool;
    private ObjectPool<StageSky> skyPool;
    // 지금 켜져 있는 것. 열쇠는 층 번호 / 구간 번호.
    private readonly Dictionary<int, FloorRow> rows = new Dictionary<int, FloorRow>();
    private readonly Dictionary<int, StageSky> skies = new Dictionary<int, StageSky>();
    private readonly List<int> leaving = new List<int>();

    private RectTransform roof;
    private float roofHeight;
    private RectTransform towerBase;

    // 지금 도전할 층의 파티 표시에서 퍼지는 고리. 탑에서 매 프레임 바뀌는 것은 이것뿐이라 제 캔버스를 따로 준다 —
    // 층마다 두면 고리가 번질 때마다 탑 전체(층 열 개, 그래픽 수백 개)가 매 프레임 다시 묶였다.
    private RectTransform pulse;
    private Image pulseRing;

    private RectTransform gaugeInner;
    private readonly List<Image> gaugeSegments = new List<Image>();
    private RectTransform gaugeWindow;
    private RectTransform gaugeMarker;

    private UiButton jumpButton;
    private RectTransform jumpArrow;

    private Action<int> floorClicked;
    private int selectedFloor = FloorProgress.FirstFloor;

    private bool gliding;
    private float glideTarget;
    private float glideVelocity;
    private float glideSmoothTime;
    private float lastViewHeight = -1f;

    public RectTransform Panel { get; private set; }

    // 풀 상태. 플레이 테스트에서 "보이는 만큼만 켜져 있는가"를 잴 때 쓴다.
    public int ActiveRowCount => rows.Count;
    public int ActiveSkyCount => skies.Count;
    public int CreatedRowCount => rowPool != null ? rowPool.CountAll : 0;
    public int PooledRowCount => rowPool != null ? rowPool.CountInactive : 0;

    // 탑 판을 만든다. 자리는 돌려받은 Panel에 잡는다. 층을 누르면 그 층 번호를 onFloorClicked로 넘긴다.
    public static FloorTowerView Create(RectTransform parent, Action<int> onFloorClicked)
    {
        UiKit.Surface panel = UiKit.Panel(parent, "Tower", SkyBase, UiTheme.RadiusL, UiTheme.Border);
        // 탑을 굴리면 오른쪽 막대의 보는 창과 되돌아가기 버튼도 따라 바뀐다. 판째로 캔버스를 떼어
        // 굴리는 동안 옆의 상세 칸·머리줄은 다시 묶이지 않게 한다(스크롤 내용은 ScrollArea가 한 번 더 뗀다).
        UiKit.SplitCanvas(panel.Rect, true);
        RectTransform list = UiKit.ScrollArea(panel.Rect, "Viewport", out ScrollRect scrollRect);
        UiKit.Fill((RectTransform)scrollRect.transform, 2f);

        var view = scrollRect.gameObject.AddComponent<FloorTowerView>();
        view.Build(panel.Rect, scrollRect, list, onFloorClicked);
        return view;
    }

    // ---- 짓기 ---------------------------------------------------------------

    private void Build(RectTransform panel, ScrollRect scrollRect, RectTransform list, Action<int> onFloorClicked)
    {
        Panel = panel;
        scroll = scrollRect;
        viewport = scrollRect.viewport;
        content = list;
        floorClicked = onFloorClicked;
        art = UiIconLibrary.Current;

        // 지붕 그림의 비율로 위 여백을 정한다. 그림이 없으면 꼭대기 위에 하늘만 조금.
        Sprite roofSprite = art != null ? art.towerRoof : null;
        roofHeight = roofSprite != null ? RoofWidth * roofSprite.rect.height / roofSprite.rect.width : 0f;
        float topPadding = roofHeight - RoofSink + SkyAboveRoof;
        float bottomPadding = BaseWallHeight + GroundHeight;
        layout = new TowerLayout(FloorProgress.FirstFloor, FloorProgress.LastFloor, RowHeight, topPadding, bottomPadding);

        // 네모로 자르면(RectMask2D) 하늘이 둥근 판 모서리 밖으로 삐져나온다. 둥근 가림막으로 바꾼다.
        DestroyImmediate(viewport.GetComponent<RectMask2D>());
        Image hit = viewport.GetComponent<Image>();
        hit.sprite = UiSprites.Rounded(UiTheme.RadiusL - 2);
        hit.type = Image.Type.Sliced;
        hit.color = Color.white;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        // 탑 끝에서 살짝 늘어났다 돌아온다. 한 층이 크니 휠 한 번에 반 층 넘게 움직이게.
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.elasticity = 0.12f;
        scroll.scrollSensitivity = RowHeight * 0.6f;
        scroll.onValueChanged.AddListener(_ => UpdateVisible());
        UiKit.SetContentHeight(content, layout.ContentHeight);

        // 뒤에서부터 하늘 → 구름 → 지붕·입구 → 층.
        skyLayer = UiKit.Node(content, "Sky");
        UiKit.Fill(skyLayer);
        cloudLayer = UiKit.Node(content, "Clouds");
        UiKit.Fill(cloudLayer);
        structureLayer = UiKit.Node(content, "Structure");
        UiKit.Fill(structureLayer);
        rowLayer = UiKit.Node(content, "Floors");
        UiKit.Fill(rowLayer);

        // 층들 위에 고리 하나. 누를 것이 아니라 레이캐스터는 두지 않는다.
        pulse = UiKit.Node(content, "Pulse");
        UiKit.SplitCanvas(pulse, false);
        pulseRing = UiKit.Line(pulse, "Ring", UiTheme.Primary, Mathf.RoundToInt(PulseSize * 0.5f), 3);
        pulse.gameObject.SetActive(false);

        // 끌거나 휠을 굴리면 자동으로 흘러가던 것을 멈추고 손을 따른다.
        var trigger = viewport.gameObject.AddComponent<EventTrigger>();
        AddTrigger(trigger, EventTriggerType.BeginDrag, _ => gliding = false);
        AddTrigger(trigger, EventTriggerType.Scroll, _ => gliding = false);

        rowPool = new ObjectPool<FloorRow>(CreateRow,
            row => row.Root.gameObject.SetActive(true),
            row => row.Root.gameObject.SetActive(false),
            row => { if (row.Root != null) Destroy(row.Root.gameObject); },
            collectionCheck: true, defaultCapacity: 12, maxSize: 48);
        skyPool = new ObjectPool<StageSky>(CreateSky,
            sky => ShowSky(sky, true),
            sky => ShowSky(sky, false),
            sky =>
            {
                if (sky.Root != null) Destroy(sky.Root.gameObject);
                if (sky.CloudRoot != null) Destroy(sky.CloudRoot.gameObject);
            },
            collectionCheck: true, defaultCapacity: 4, maxSize: 16);

        BuildRoof(roofSprite);
        BuildBase();
        BuildGauge(panel);
        BuildJumpButton(panel);
    }

    private static void AddTrigger(EventTrigger trigger, EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(action);
        trigger.triggers.Add(entry);
    }

    // 탑 가운데 기준으로, 줄 윗변에서 top만큼 내려온 곳에 가운데를 둔다.
    private static void PlaceOnTower(RectTransform rect, float x, float centerY, float width, float height) =>
        UiKit.Place(rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(TowerX + x, -centerY), new Vector2(width, height));

    // 돌벽 한 장. 무늬 그림이 있으면 한 층 높이에 무늬 한 장이 들어가게 깐다.
    private Image CreateWall(RectTransform parent, string name, float width, float height, float top)
    {
        Sprite sprite = art != null ? art.towerWall : null;
        Image wall = UiKit.Image(parent, name, sprite, sprite != null ? Color.white : StoneFallback, false);
        if (sprite != null)
        {
            wall.type = Image.Type.Tiled;
            wall.pixelsPerUnitMultiplier = sprite.rect.width / WallTileWidth;
        }
        UiKit.Place(wall.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(TowerX, -top), new Vector2(width, height));

        // 양쪽 끝을 어둡게 눌러 둥근 기둥처럼 보이게 한다(빛은 왼쪽 위에서).
        Image left = UiKit.Image(wall.rectTransform, "ShadeLeft", null, Color.white, false);
        UiKit.Fill(left.rectTransform, 0f, 0f, width - ShadeWidth, 0f);
        left.gameObject.AddComponent<UiGradient>().SetHorizontal(new Color(0f, 0f, 0f, 0.45f), new Color(0f, 0f, 0f, 0f));
        Image right = UiKit.Image(wall.rectTransform, "ShadeRight", null, Color.white, false);
        UiKit.Fill(right.rectTransform, width - ShadeWidth, 0f, 0f, 0f);
        right.gameObject.AddComponent<UiGradient>().SetHorizontal(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.6f));
        return wall;
    }

    // 층과 층 사이의 돌 띠. 벽보다 조금 튀어나와 층이 한 칸씩 쌓인 것처럼 보이게 한다.
    private static void CreateLedge(RectTransform parent, float width, float top)
    {
        Image ledge = UiKit.Image(parent, "Ledge", null, LedgeColor, false);
        UiKit.Place(ledge.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(TowerX, -top),
            new Vector2(width + LedgeOverhang * 2f, LedgeHeight));
        Image light = UiKit.Image(ledge.rectTransform, "Light", null, LedgeLight, false);
        UiKit.TopStretch(light.rectTransform, 0f, 2f);
    }

    private FloorRow CreateRow()
    {
        var row = new FloorRow();
        row.Root = UiKit.Node(rowLayer, "Floor");

        row.Wall = CreateWall(row.Root, "Wall", TowerWidth, RowHeight, 0f);
        row.Wall.raycastTarget = true;

        // 고른 층은 벽이 조금 밝아진다. 무엇을 골랐는지는 번호 판 테두리가 말한다.
        row.Highlight = UiKit.Image(row.Wall.rectTransform, "Highlight", null, new Color(1f, 1f, 1f, 0.08f), false);

        CreateLedge(row.Root, TowerWidth, RowHeight - LedgeHeight);

        row.Glows = new Image[2];
        row.Windows = new Image[2];
        Sprite windowSprite = art != null ? art.towerWindow : null;
        Sprite glowSprite = UiSprites.Shadow(8, 48);
        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? -WindowOffset : WindowOffset;
            row.Glows[i] = UiKit.Image(row.Root, "Glow_" + i, glowSprite, Candle, false);
            PlaceOnTower(row.Glows[i].rectTransform, x, WindowY, GlowSize, GlowSize);

            row.Windows[i] = windowSprite != null
                ? UiKit.Image(row.Root, "Window_" + i, windowSprite, Color.white)
                : UiKit.Rounded(row.Root, "Window_" + i, WindowFallback, 12);
            PlaceOnTower(row.Windows[i].rectTransform, x, WindowY, WindowWidth, WindowHeight);
        }

        // 층 번호 판. 탑에 적힌 글자는 이것뿐이다.
        row.Plaque = UiKit.Panel(row.Root, "Plaque", PlaqueFill, 10, Bronze, 10);
        PlaceOnTower(row.Plaque.Rect, 0f, WindowY, PlaqueWidth, PlaqueHeight);
        row.Number = UiKit.Text(row.Plaque.Rect, "Number", string.Empty, UiTheme.FontHeading, PlaqueText, TextAlignmentOptions.Center);
        row.Lock = UiKit.Glyph(row.Plaque.Rect, "Lock", UiSprites.Glyph.Lock, UiTheme.TextMuted);
        UiKit.RightMiddle(row.Lock.rectTransform, 10f, 18f, 18f);
        row.Ring = UiKit.Line(row.Plaque.Rect, "Selection", UiTheme.Selection, 15, (int)UiTheme.SelectionWidth);
        UiKit.Fill(row.Ring.rectTransform, -5f);

        // 지금 도전할 층의 파티 표시. 번호 판 왼쪽 끝에 걸친다(퍼지는 고리는 pulse가 따로 그린다).
        row.Badge = UiKit.Rounded(row.Root, "Badge", UiTheme.Primary, Mathf.RoundToInt(BadgeSize * 0.5f));
        PlaceOnTower(row.Badge.rectTransform, BadgeX, WindowY, BadgeSize, BadgeSize);
        Image party = UiKit.Glyph(row.Badge.rectTransform, "Party", UiSprites.Glyph.Party, UiTheme.OnPrimary);
        UiKit.Fill(party.rectTransform, BadgeSize * 0.16f);

        var button = row.Wall.gameObject.AddComponent<Button>();
        button.targetGraphic = row.Wall;
        button.transition = Selectable.Transition.None;
        // 층은 돌려 쓰므로 만들 때의 층이 아니라 누르는 순간의 층을 넘긴다.
        button.onClick.AddListener(() => floorClicked?.Invoke(row.Floor));
        return row;
    }

    private StageSky CreateSky()
    {
        var sky = new StageSky();
        sky.Root = UiKit.Node(skyLayer, "Stage");

        Image fill = UiKit.Image(sky.Root, "Fill", null, Color.white, false);
        sky.Fill = fill.gameObject.AddComponent<UiGradient>();

        // 높이 올라갈수록 별이 돋는다. 무늬 한 장을 되풀이해 깐다.
        sky.Stars = UiKit.Image(sky.Root, "Stars", UiSprites.StarField(), Color.white, false);
        sky.Stars.type = Image.Type.Tiled;

        sky.CloudRoot = UiKit.Node(cloudLayer, "Stage");
        Sprite cloud = art != null ? art.cloud : null;
        sky.Clouds = new Image[cloud != null ? CloudsPerStage : 0];
        sky.CloudTops = new float[sky.Clouds.Length];
        for (int i = 0; i < sky.Clouds.Length; i++)
            sky.Clouds[i] = UiKit.Image(sky.CloudRoot, "Cloud_" + i, cloud, Color.white);
        return sky;
    }

    private static void ShowSky(StageSky sky, bool show)
    {
        sky.Root.gameObject.SetActive(show);
        sky.CloudRoot.gameObject.SetActive(show);
    }

    private void BuildRoof(Sprite sprite)
    {
        if (sprite == null) return;

        Image image = UiKit.Image(structureLayer, "Roof", sprite, Color.white);
        roof = image.rectTransform;
        float bottom = layout.TopOf(layout.LastFloor) + RoofSink;
        UiKit.Place(roof, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(TowerX, -bottom), new Vector2(RoofWidth, roofHeight));
    }

    // 1층 아래 — 조금 넓은 기단 벽에 문, 그 아래 땅.
    private void BuildBase()
    {
        float top = layout.GroundFloorBottom;
        towerBase = UiKit.Node(structureLayer, "Base");
        UiKit.TopStretch(towerBase, 0f, layout.ContentHeight);

        Image ground = UiKit.Image(towerBase, "Ground", null, Color.white, false);
        UiKit.TopStretch(ground.rectTransform, top + BaseWallHeight, GroundHeight);
        ground.gameObject.AddComponent<UiGradient>().Set(GroundTop, GroundBottom);

        // 기단은 층 벽보다 한 톤 어둡게 — 탑을 받치는 무거운 돌.
        Image wall = CreateWall(towerBase, "Wall", BaseWidth, BaseWallHeight, top);
        Color foundation = new Color(0.82f, 0.8f, 0.78f, 1f);
        wall.color = wall.sprite != null ? foundation : StoneFallback * foundation;
        CreateLedge(towerBase, BaseWidth, top - LedgeHeight * 0.5f);

        Sprite door = art != null ? art.towerDoor : null;
        if (door != null)
        {
            float width = DoorHeight * door.rect.width / door.rect.height;
            Image image = UiKit.Image(towerBase, "Door", door, Color.white);
            UiKit.Place(image.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f),
                new Vector2(TowerX, -(top + BaseWallHeight + 6f)), new Vector2(width, DoorHeight));
        }
    }

    private void BuildGauge(RectTransform panel)
    {
        RectTransform gauge = UiKit.Node(panel, "Gauge");
        gauge.anchorMin = new Vector2(1f, 0f);
        gauge.anchorMax = new Vector2(1f, 1f);
        gauge.pivot = new Vector2(1f, 0.5f);
        gauge.offsetMin = new Vector2(-GaugeRight - GaugeWidth, GaugeInsetY);
        gauge.offsetMax = new Vector2(-GaugeRight, -GaugeInsetY);

        Image track = UiKit.Rounded(gauge, "Track", UiTheme.Surface, Mathf.RoundToInt(GaugeWidth * 0.5f));
        track.raycastTarget = true;
        var trigger = track.gameObject.AddComponent<EventTrigger>();
        AddTrigger(trigger, EventTriggerType.PointerDown, JumpToGauge);
        AddTrigger(trigger, EventTriggerType.Drag, JumpToGauge);

        // 둥근 막대 끝에 네모 조각이 삐져나오지 않게 안쪽에 칸을 깐다.
        gaugeInner = UiKit.Node(gauge, "Stages");
        UiKit.Fill(gaugeInner, 4f, 6f, 4f, 6f);

        int stages = FloorStages.Count;
        float floors = layout.FloorCount;
        for (int stage = 0; stage < stages; stage++)
        {
            Image segment = UiKit.Image(gaugeInner, "Stage_" + stage, null, Color.white, false);
            RectTransform rect = segment.rectTransform;
            rect.anchorMin = new Vector2(0f, (FloorStages.FirstFloorOf(stage) - layout.FirstFloor) / floors);
            rect.anchorMax = new Vector2(1f, (FloorStages.LastFloorOf(stage) - layout.FirstFloor + 1) / floors);
            rect.offsetMin = new Vector2(0f, 1f);
            rect.offsetMax = new Vector2(0f, -1f);
            gaugeSegments.Add(segment);
        }

        gaugeWindow = UiKit.Line(gaugeInner, "View", UiTheme.TextPrimary, 6, 2).rectTransform;
        gaugeMarker = UiKit.Rounded(gaugeInner, "Current", UiTheme.Primary, Mathf.RoundToInt(GaugeMarkerSize * 0.5f)).rectTransform;
        UiKit.Image(gaugeMarker, "Ring", UiSprites.Outline(Mathf.RoundToInt(GaugeMarkerSize * 0.5f), 3), UiTheme.OnPrimary, false)
            .type = Image.Type.Sliced;
    }

    private void BuildJumpButton(RectTransform panel)
    {
        jumpButton = UiButton.Create(panel, "ToCurrent", string.Empty, UiButtonStyle.Secondary, UiButtonSize.Small,
            () => GlideTo(layout.ScrollToCenter(FloorProgress.HighestUnlocked, ViewHeight()), GlideSmoothTime));
        UiKit.Fill(jumpButton.Label.rectTransform, 34f, 0f, 12f, 0f);

        Image arrow = UiKit.Glyph(jumpButton.Rect, "Arrow", UiSprites.Glyph.ChevronDown, UiTheme.TextPrimary);
        jumpArrow = arrow.rectTransform;
        UiKit.LeftMiddle(jumpArrow, 12f, 28f, 28f);
        jumpArrow.pivot = new Vector2(0.5f, 0.5f);
        jumpArrow.anchoredPosition = new Vector2(26f, 0f);
        jumpButton.gameObject.SetActive(false);
    }

    // ---- 바깥에서 부르는 것 ----------------------------------------------------

    // 고른 층이나 진행도가 바뀌었다. 켜져 있는 층과 하늘만 다시 칠한다.
    public void Refresh(int selected)
    {
        selectedFloor = selected;
        foreach (KeyValuePair<int, FloorRow> pair in rows) BindRow(pair.Value);
        foreach (KeyValuePair<int, StageSky> pair in skies) BindSky(pair.Value);
        RefreshGauge();
        UpdateVisible();
    }

    // 그 층을 창 가운데로. rise면 몇 층 아래에서 시작해 올라오며 멈춘다 — 탑을 올라와 거기 선 느낌.
    public void ShowFloor(int floor, bool rise)
    {
        float view = ViewHeight();
        float target = layout.ScrollToCenter(floor, view);
        float start = rise ? Mathf.Min(target + RiseFloors * RowHeight, layout.MaxScroll(view)) : target;

        scroll.StopMovement();
        SetScroll(start);
        if (rise && start > target) GlideTo(target, RiseSmoothTime);
        else gliding = false;
    }

    // ---- 보이는 것만 켜기 ------------------------------------------------------

    private void UpdateVisible()
    {
        if (rowPool == null) return;

        float scrollY = content.anchoredPosition.y;
        float view = ViewHeight();
        layout.VisibleRange(scrollY, view, Preload, out int low, out int high);

        // 창 밖으로 나간 층은 끄고 풀에 돌려준다.
        leaving.Clear();
        foreach (KeyValuePair<int, FloorRow> pair in rows)
            if (pair.Key < low || pair.Key > high) leaving.Add(pair.Key);
        for (int i = 0; i < leaving.Count; i++)
        {
            rowPool.Release(rows[leaving[i]]);
            rows.Remove(leaving[i]);
        }

        // 창에 들어온 층은 풀에서 꺼내 칠한다.
        for (int floor = low; floor <= high; floor++)
        {
            if (rows.ContainsKey(floor)) continue;
            FloorRow row = rowPool.Get();
            row.Floor = floor;
            BindRow(row);
            rows.Add(floor, row);
        }

        // 하늘도 같은 식으로. 보이는 층이 걸친 구간만 — 층이 하나도 안 보여도(지붕·입구만 보일 때) 맨 끝 구간 하늘은 편다.
        int stageLow, stageHigh;
        if (low <= high)
        {
            stageLow = FloorStages.IndexOf(low);
            stageHigh = FloorStages.IndexOf(high);
        }
        else
        {
            stageLow = stageHigh = scrollY < layout.TopPadding ? FloorStages.Count - 1 : 0;
        }
        leaving.Clear();
        foreach (KeyValuePair<int, StageSky> pair in skies)
            if (pair.Key < stageLow || pair.Key > stageHigh) leaving.Add(pair.Key);
        for (int i = 0; i < leaving.Count; i++)
        {
            skyPool.Release(skies[leaving[i]]);
            skies.Remove(leaving[i]);
        }
        for (int stage = stageLow; stage <= stageHigh; stage++)
        {
            if (skies.ContainsKey(stage)) continue;
            StageSky sky = skyPool.Get();
            sky.Stage = stage;
            BindSky(sky);
            skies.Add(stage, sky);
        }

        // 지붕과 입구는 하나씩이라 풀 대신 보일 때만 켠다.
        if (roof != null) SetActive(roof, scrollY < layout.TopOf(layout.LastFloor) + Preload);
        SetActive(towerBase, scrollY + view > layout.GroundFloorBottom - Preload);

        // 고리는 도전할 층이 창에 있을 때만(다 깬 탑에는 도전할 층이 없다).
        int current = FloorProgress.HighestUnlocked;
        bool pulsing = rows.ContainsKey(current) && current > FloorProgress.HighestCleared;
        SetActive(pulse, pulsing);
        if (pulsing)
            UiKit.Place(pulse, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(TowerX + BadgeX, -(layout.TopOf(current) + WindowY)), new Vector2(PulseSize, PulseSize));

        DriftClouds(scrollY, view);
        UpdateGaugeWindow(scrollY, view);
        UpdateJumpButton(scrollY, view);
    }

    private static void SetActive(Component component, bool active)
    {
        if (component.gameObject.activeSelf != active) component.gameObject.SetActive(active);
    }

    private void BindRow(FloorRow row)
    {
        int floor = row.Floor;
        UiKit.TopStretch(row.Root, layout.TopOf(floor), RowHeight);

        bool unlocked = FloorProgress.IsUnlocked(floor);
        bool cleared = floor <= FloorProgress.HighestCleared;
        bool isCurrent = unlocked && !cleared;
        bool selected = floor == selectedFloor;

        // 올라온 층은 밝은 돌, 아직 못 간 층은 그늘진 돌.
        Color wall = art != null && art.towerWall != null ? Color.white : StoneFallback;
        row.Wall.color = unlocked ? wall : wall * LockedWall;
        row.Highlight.enabled = selected;

        // 깬 층은 창에 불이 켜져 있다. 잠긴 층은 불이 꺼져 있다.
        Color lit = art != null && art.towerWindow != null ? Color.white : WindowFallback;
        for (int i = 0; i < row.Windows.Length; i++)
        {
            row.Windows[i].color = unlocked ? lit : lit * LockedWindow;
            // 지금 도전할 층은 불빛이 더 크고 밝다(깜빡이지 않는다 — 움직이는 것은 파티 표시의 고리 하나로 충분하다).
            row.Glows[i].enabled = unlocked;
            row.Glows[i].color = UiTheme.WithAlpha(Candle, isCurrent ? 0.8f : 0.35f);
            row.Glows[i].rectTransform.localScale = Vector3.one * (isCurrent ? 1.12f : 1f);
        }

        row.Number.text = floor + "층";
        row.Number.color = unlocked ? PlaqueText : UiTheme.TextMuted;
        row.Plaque.Border.color = isCurrent ? UiTheme.Primary : unlocked ? Bronze : Bronze * LockedWall;
        row.Lock.enabled = !unlocked;
        UiKit.Fill(row.Number.rectTransform, 0f, 0f, unlocked ? 0f : 18f, 0f);
        row.Ring.enabled = selected;

        SetActive(row.Badge, isCurrent);
    }

    private void BindSky(StageSky sky)
    {
        int stage = sky.Stage;
        int first = FloorStages.FirstFloorOf(stage);
        int last = FloorStages.LastFloorOf(stage);

        // 맨 위 구간은 지붕 위 하늘까지, 맨 아래 구간은 땅까지 편다.
        float top = stage == FloorStages.Count - 1 ? 0f : layout.TopOf(last);
        float bottom = stage == 0 ? layout.ContentHeight : layout.TopOf(first) + RowHeight;
        sky.Top = top;
        sky.Height = bottom - top;
        UiKit.TopStretch(sky.Root, top, sky.Height);
        UiKit.TopStretch(sky.CloudRoot, top, sky.Height);

        // 구간 경계에서 이웃 구간 색과 반씩 섞어 하늘이 층층이 끊기지 않고 이어지게 한다.
        Color own = SkyColorOf(stage);
        Color above = stage < FloorStages.Count - 1 ? Color.Lerp(own, SkyColorOf(stage + 1), 0.5f) : Color.Lerp(own, SkyHigh, 0.5f);
        Color below = stage > 0 ? Color.Lerp(own, SkyColorOf(stage - 1), 0.5f) : Color.Lerp(own, Horizon, 0.35f);
        sky.Fill.Set(above, below);

        bool reached = first <= FloorProgress.HighestUnlocked;
        float altitude = stage / (float)Mathf.Max(1, FloorStages.Count - 1);
        float starAlpha = Mathf.Clamp01((altitude - 0.2f) / (StarFullAltitude - 0.2f));
        sky.Stars.enabled = starAlpha > 0f;
        sky.Stars.color = new Color(1f, 1f, 1f, starAlpha * starAlpha * (reached ? 0.9f : 0.55f));

        // 구름 자리는 구간마다 정해져 있다 — 굴릴 때마다 바뀌면 안 된다.
        float viewWidth = Mathf.Max(1f, viewport.rect.width);
        float towerLeft = viewWidth * 0.5f + TowerX - TowerWidth * 0.5f;
        float towerRight = towerLeft + TowerWidth;
        float skyRight = viewWidth - GaugeRight - GaugeWidth - 12f;
        float bandHeight = (last - first + 1) * RowHeight;
        for (int i = 0; i < sky.Clouds.Length; i++)
        {
            Image cloud = sky.Clouds[i];
            float size = Mathf.Lerp(190f, 290f, Hash(stage, i * 3 + 1));
            float height = size * cloud.sprite.rect.height / cloud.sprite.rect.width;
            bool left = (stage + i) % 2 == 0;
            float x = left
                ? Mathf.Lerp(towerLeft * 0.25f, towerLeft * 0.65f, Hash(stage, i * 3 + 2))
                : Mathf.Lerp(towerRight + (skyRight - towerRight) * 0.35f, skyRight - 20f, Hash(stage, i * 3 + 2));
            // 구간 첫 층 윗변에서 잰 구름 가운데 높이.
            sky.CloudTops[i] = (layout.TopOf(last) - top) + bandHeight * Mathf.Lerp(0.15f, 0.85f, (i + Hash(stage, i * 3 + 3)) / CloudsPerStage);
            UiKit.Place(cloud.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(x, -sky.CloudTops[i]),
                new Vector2(size, height));
            cloud.color = new Color(1f, 1f, 1f, (reached ? 0.8f : 0.45f) * Mathf.Lerp(0.6f, 1f, Hash(stage, i * 3 + 4)));
        }
    }

    // 구간 하늘색 — 탑 아래의 저녁 하늘에서 꼭대기의 밤하늘로, 거기에 그 전장의 색을 조금. 아직 못 간 구간은 어둡다.
    private static Color SkyColorOf(int stage)
    {
        float altitude = stage / (float)Mathf.Max(1, FloorStages.Count - 1);
        Color sky = Color.Lerp(Color.Lerp(SkyLow, SkyHigh, altitude), FloorStages.ColorOfStage(stage), StageTintInSky);
        bool reached = FloorStages.FirstFloorOf(stage) <= FloorProgress.HighestUnlocked;
        return reached ? sky : Color.Lerp(sky, SkyBase, 0.45f);
    }

    // 구름은 탑보다 느리게 흘러간다 — 멀리 있는 것이 천천히 움직여야 높이와 거리가 느껴진다.
    private void DriftClouds(float scrollY, float view)
    {
        float viewCenter = scrollY + view * 0.5f;
        foreach (KeyValuePair<int, StageSky> pair in skies)
        {
            StageSky sky = pair.Value;
            float drift = (viewCenter - (sky.Top + sky.Height * 0.5f)) * CloudParallax;
            for (int i = 0; i < sky.Clouds.Length; i++)
            {
                RectTransform rect = sky.Clouds[i].rectTransform;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -(sky.CloudTops[i] + drift));
            }
        }
    }

    // 0~1. 같은 구간·같은 번호면 늘 같은 값.
    private static float Hash(int stage, int salt)
    {
        unchecked
        {
            uint h = (uint)(stage + 1) * 2654435761u ^ (uint)(salt + 7) * 2246822519u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (h & 0xFFFF) / 65535f;
        }
    }

    // ---- 오른쪽 막대 ---------------------------------------------------------

    private void RefreshGauge()
    {
        int current = FloorProgress.HighestUnlocked;
        for (int stage = 0; stage < gaugeSegments.Count; stage++)
        {
            Color tint = FloorStages.ColorOfStage(stage);
            bool reached = FloorStages.FirstFloorOf(stage) <= current;
            gaugeSegments[stage].color = reached ? tint : Color.Lerp(UiTheme.SurfaceSunken, tint, 0.3f);
        }

        float ratio = (current - layout.FirstFloor + 0.5f) / layout.FloorCount;
        UiKit.Place(gaugeMarker, new Vector2(0.5f, ratio), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(GaugeMarkerSize, GaugeMarkerSize));
    }

    private void UpdateGaugeWindow(float scrollY, float view)
    {
        float top = layout.RatioAt(scrollY);
        float bottom = layout.RatioAt(scrollY + view);
        gaugeWindow.anchorMin = new Vector2(0f, bottom);
        gaugeWindow.anchorMax = new Vector2(1f, top);
        gaugeWindow.offsetMin = new Vector2(-6f, -2f);
        gaugeWindow.offsetMax = new Vector2(6f, 2f);
    }

    // 막대를 누르거나 끌면 그 높이의 층으로 건너뛴다.
    private void JumpToGauge(BaseEventData data)
    {
        if (!(data is PointerEventData pointer)) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(gaugeInner, pointer.position, pointer.pressEventCamera,
                out Vector2 local)) return;

        Rect rect = gaugeInner.rect;
        float ratio = (local.y - rect.yMin) / Mathf.Max(1f, rect.height);
        float view = ViewHeight();
        float target = Mathf.Clamp(layout.YAt(ratio) - view * 0.5f, 0f, layout.MaxScroll(view));
        GlideTo(target, GlideSmoothTime * 0.5f);
    }

    // ---- 지금 도전할 층으로 ----------------------------------------------------

    // 도전할 층이 창 밖에 있으면 그쪽 가장자리에 "▲ 12층" 버튼을 띄운다.
    private void UpdateJumpButton(float scrollY, float view)
    {
        int current = FloorProgress.HighestUnlocked;
        float rowTop = layout.TopOf(current);
        bool above = rowTop + RowHeight <= scrollY;
        bool below = rowTop >= scrollY + view;
        bool show = above || below;

        if (jumpButton.gameObject.activeSelf != show) jumpButton.gameObject.SetActive(show);
        if (!show) return;

        // 탑 오른쪽 하늘에 띄운다 — 탑 위에 두면 층 번호 판을 가린다.
        float right = GaugeRight + GaugeWidth + JumpMargin;
        if (above)
            UiKit.Place(jumpButton.Rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-right, -JumpMargin),
                new Vector2(JumpWidth, UiTheme.ButtonSmall));
        else
            UiKit.Place(jumpButton.Rect, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-right, JumpMargin),
                new Vector2(JumpWidth, UiTheme.ButtonSmall));

        jumpArrow.localEulerAngles = new Vector3(0f, 0f, above ? 180f : 0f);
        jumpButton.SetLabel(current + "층");
    }

    // ---- 움직임 ---------------------------------------------------------------

    private void GlideTo(float target, float smoothTime)
    {
        glideTarget = target;
        glideSmoothTime = smoothTime;
        glideVelocity = 0f;
        gliding = true;
        scroll.StopMovement();
    }

    private void SetScroll(float y)
    {
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, y);
        UpdateVisible();
    }

    private float ViewHeight()
    {
        float height = viewport.rect.height;
        if (height >= 1f) return height;

        // 막 지은 캔버스는 한 번 갱신해야 크기가 잡힌다.
        Canvas.ForceUpdateCanvases();
        return Mathf.Max(1f, viewport.rect.height);
    }

    private void LateUpdate()
    {
        // 창 크기가 바뀌면(해상도·비율) 보이는 층 수도 바뀐다.
        float view = viewport.rect.height;
        if (!Mathf.Approximately(view, lastViewHeight))
        {
            lastViewHeight = view;
            UpdateVisible();
        }

        if (gliding)
        {
            float y = Mathf.SmoothDamp(content.anchoredPosition.y, glideTarget, ref glideVelocity, glideSmoothTime,
                Mathf.Infinity, Time.unscaledDeltaTime);
            if (Mathf.Abs(glideTarget - y) < 0.5f)
            {
                y = glideTarget;
                gliding = false;
            }
            scroll.StopMovement();
            SetScroll(y);
        }

        // 지금 도전할 층의 파티 표시에서 고리가 퍼져 나간다. 제 캔버스라 고리 하나만 다시 묶인다.
        // 옅어지는 것은 Image.color가 아니라 CanvasRenderer 알파로 — color를 바꾸면 매 프레임 메시를 새로 짓는다.
        if (pulse.gameObject.activeSelf)
        {
            float phase = Mathf.Repeat(Time.unscaledTime, PulsePeriod) / PulsePeriod;
            pulse.localScale = Vector3.one * (0.8f + 0.5f * phase);
            pulseRing.canvasRenderer.SetAlpha(0.85f * (1f - phase));
        }
    }
}
