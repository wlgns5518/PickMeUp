using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 메인 씬에서 들어갈 층을 고르는 창.
//
// 층은 자동으로 넘어가지 않는다. 여기서 직접 고른 뒤 전투 씬으로 들어가고,
// 전투가 끝나면 다시 이 화면으로 돌아온다.
//
// 예전에는 화면 왼쪽에 목록이 늘 떠 있었다. 지금은 파티 편성 창(DeckBuildUI)에서 "출전하기"를
// 눌러야 열리는 팝업이라 기본은 닫힌 상태다. 여는 쪽에서 Show()나 Toggle()을 부른다.
// 마을에서의 흐름은 시공의 틈 → 편성 → 출전 → 여기다.
//
// BattleHud와 같은 방식으로 캔버스부터 코드에서 만든다.
// 이쪽은 클릭을 받아야 하므로 BattleHud와 달리 GraphicRaycaster를 붙인다.
[DisallowMultipleComponent]
public class FloorSelectUI : FacilityWindow
{
    [Header("Scene")]
    [Tooltip("모든 층이 함께 쓰는 전투 씬. 난이도는 고른 층 번호로 조정된다. Build Settings에 등록돼 있어야 한다.")]
    [SerializeField] private string battleSceneName = "Floor1~9";

    [Header("Layout")]
    [Tooltip("화면에 늘어놓을 층 버튼 개수. 모든 층이 전투 씬 하나를 함께 쓰므로 난이도 단계 수와 같다.")]
    [SerializeField] private int visibleFloorCount = 9;
    [SerializeField] private Vector2 panelPadding = new Vector2(30f, 26f);
    [Tooltip("층 버튼을 몇 줄로 늘어놓을지. 아홉 층이면 3이 정사각형에 가깝다.")]
    [SerializeField, Min(1)] private int floorColumns = 3;

    [Header("Open State")]
    [Tooltip("문을 누르지 않아도 처음부터 열려 있게 하려면 켠다.")]
    [SerializeField] private bool openOnStart;

    [Header("Warning Banner")]
    [Tooltip("경고를 띄울 장식 배너(Assets/Image/UI.png). 비워두면 금색 테두리에 검은 판으로 그린다.")]
    [SerializeField] private Sprite bannerSprite;
    [SerializeField] private float bannerWidth = 900f;

    // 편성이 비어 있으면 스포너가 인스펙터에 박아둔 명단으로 대신 싸운다. 그건 전투 씬을 직접
    // 재생할 때의 대비책이고, 플레이어가 문으로 들어갈 때는 자기가 고른 파티로만 들어가야 한다.
    // 파티가 셋이라 어느 파티가 비었는지 짚어줘야 한다.
    private const string EmptyPartyMessageFormat = "{0}파티에 출전할 영웅이 없습니다.\n먼저 영웅을 편성해주세요.";

    // 잠긴 층 칸. 열린 층(PortraitFrame)보다 한 단계 어둡게 눌러 둔다.
    private static readonly Color LockedFrame = new Color(0.11f, 0.10f, 0.13f, 0.95f);

    private const float ButtonWidth = 360f;
    private const float ButtonHeight = 150f;
    private const float ButtonSpacing = 20f;
    private const float TitleHeight = 72f;
    private const float CloseHeight = 60f;

    private AnnouncementBanner warningBanner;
    private readonly List<Button> floorButtons = new List<Button>();
    private readonly List<TMP_Text> floorLabels = new List<TMP_Text>();

    protected override string CanvasName => "FloorSelectCanvas";

    private void Awake()
    {
        EnsureBuilt();
        SetOpen(openOnStart);
    }

    private void OnEnable()
    {
        EnsureBuilt();
        RefreshButtons();
    }

    // 전투에서 돌아왔을 때 해금 상태가 바뀌었을 수 있으므로 다시 그린다.
    private void Start()
    {
        RefreshButtons();
    }

    protected override void TickWindow(float deltaTime)
    {
        warningBanner?.Tick(deltaTime);
    }

    // 문을 눌렀을 때 불린다. 열기 직전에 해금 상태를 다시 읽는다.
    public override void Show()
    {
        EnsureBuilt();
        RefreshButtons();
        SetOpen(true);
    }

    public override void Hide()
    {
        SetOpen(false);
    }

    protected override void BuildWindow()
    {
        floorButtons.Clear();
        floorLabels.Clear();
        BuildCanvas();
        BuildPopup();

        // 경고 배너는 팝업 밖(캔버스 직속)에 둔다. 창이 닫혀도 같은 자리에 뜬다.
        warningBanner = AnnouncementBanner.Create(canvasRect, resolvedFont, bannerSprite, null, bannerWidth);
    }

    private void BuildCanvas()
    {
        var canvasGo = new GameObject("FloorSelectCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.layer = LayerMask.NameToLayer("UI");

        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 편성 카드(91)보다 위. 층을 고르는 동안에는 이 창이 가장 앞에 있어야 한다.
        canvas.sortingOrder = 95;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasRect = (RectTransform)canvasGo.transform;
    }

    private void BuildPopup()
    {
        RectTransform popup = HudFactory.CreateGroup(canvasRect, "Popup");
        Stretch(popup);
        popupRoot = popup.gameObject;

        // 배경막과 창은 형제로 둔다. 창을 자식으로 넣으면 창 안을 누른 클릭이
        // 배경막까지 거슬러 올라가 창이 곧바로 닫힌다.
        BuildBackdrop(popup);
        BuildPanel(popup);
    }

    private void BuildBackdrop(RectTransform popup)
    {
        Image backdrop = HudFactory.CreateImage(popup, "Backdrop", BattleHudPalette.PanelBackdrop);
        // 창 밖을 누르면 닫는다. 열려 있는 동안 뒤쪽 세계로 새는 클릭도 여기서 막힌다.
        backdrop.raycastTarget = true;
        Stretch(backdrop.rectTransform);

        var button = backdrop.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(Hide);
    }

    private void BuildPanel(RectTransform popup)
    {
        int count = Mathf.Max(1, visibleFloorCount);
        // 한 줄로 세우면 아홉 층이 세로로 길게 늘어서 창이 가늘고 길어진다. 격자로 깔아 화면을 채운다.
        int columns = Mathf.Clamp(floorColumns, 1, count);
        int rows = Mathf.CeilToInt(count / (float)columns);

        float listWidth = columns * ButtonWidth + (columns - 1) * ButtonSpacing;
        float panelWidth = listWidth + panelPadding.x * 2f;
        // 닫기 버튼이 제목줄로 올라갔으므로 목록 아래에 따로 자리를 남기지 않는다.
        float panelHeight = TitleHeight + rows * (ButtonHeight + ButtonSpacing) + panelPadding.y * 2f;

        Image panel = HudFactory.CreateImage(popup, "Panel", BattleHudPalette.PanelBody);
        // 창 안을 누른 클릭이 배경막으로 내려가지 않도록 여기서 받아 둔다.
        panel.raycastTarget = true;
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);
        panelRect.anchoredPosition = Vector2.zero;

        TMP_Text title = HudFactory.CreateText(panelRect, "Title", resolvedFont, 42f, BattleHudPalette.PanelText);
        title.alignment = TextAlignmentOptions.Left;
        SetTopLeft(title.rectTransform, new Vector2(listWidth, TitleHeight), new Vector2(panelPadding.x, -panelPadding.y));
        title.text = "층 선택";

        for (int i = 0; i < count; i++)
        {
            int floor = FloorProgress.FirstFloor + i;
            int column = i % columns;
            int row = i / columns;
            float x = panelPadding.x + column * (ButtonWidth + ButtonSpacing);
            float y = -(panelPadding.y + TitleHeight + row * (ButtonHeight + ButtonSpacing));

            Image background = HudFactory.CreateImage(panelRect, "Floor_" + floor, BattleHudPalette.PortraitFrame);
            background.raycastTarget = true;
            SetTopLeft(background.rectTransform, new Vector2(ButtonWidth, ButtonHeight), new Vector2(x, y));

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            // 잠긴 층도 칸은 보여야 한다. 기본 비활성 색이 반투명이라 그대로 두면 판이 통째로
            // 사라진 것처럼 보인다. 흐리게 만드는 일은 RefreshButtons가 색으로 직접 한다.
            ColorBlock colors = button.colors;
            colors.disabledColor = Color.white;
            button.colors = colors;

            TMP_Text label = HudFactory.CreateText(background.rectTransform, "Label", resolvedFont, 34f, BattleHudPalette.PanelText);
            Stretch(label.rectTransform);

            // 클로저가 반복 변수를 붙잡지 않도록 지역 변수에 복사해 넘긴다.
            int captured = floor;
            button.onClick.AddListener(() => EnterFloor(captured));

            floorButtons.Add(button);
            floorLabels.Add(label);
        }

        // 닫기는 글자 대신 X 하나. 목록 아래가 아니라 제목줄 오른쪽 끝에 정사각형으로 둔다.
        Image closeBackground = HudFactory.CreateImage(panelRect, "Close", BattleHudPalette.PanelBackdrop);
        closeBackground.raycastTarget = true;
        SetTopLeft(closeBackground.rectTransform, new Vector2(CloseHeight, CloseHeight),
            new Vector2(panelPadding.x + listWidth - CloseHeight, -panelPadding.y));

        var closeButton = closeBackground.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeBackground;
        closeButton.onClick.AddListener(Hide);

        TMP_Text closeLabel = HudFactory.CreateText(closeBackground.rectTransform, "Label", resolvedFont, 30f, BattleHudPalette.PanelText);
        Stretch(closeLabel.rectTransform);
        // 곱셈 기호(U+2715 등)는 NotoSansKR 아틀라스에 없어 네모로 그려진다. 알파벳 X를 쓴다.
        closeLabel.text = "X";
    }

    private void RefreshButtons()
    {
        for (int i = 0; i < floorButtons.Count; i++)
        {
            int floor = FloorProgress.FirstFloor + i;
            bool unlocked = FloorProgress.IsUnlocked(floor);
            bool cleared = floor <= FloorProgress.HighestCleared;

            floorButtons[i].interactable = unlocked;

            // 잠긴 층은 칸을 어둡게 눌러 표시한다. 버튼의 비활성 색에 맡기면 반투명이라 칸이 사라진다.
            var frame = floorButtons[i].targetGraphic as Image;
            if (frame != null) frame.color = unlocked ? BattleHudPalette.PortraitFrame : LockedFrame;

            TMP_Text label = floorLabels[i];
            // 체크표시(U+2713)를 쓰면 NotoSansKR에 글리프가 없어 매번 경고를 뱉고 □로 그려진다.
            // 폰트가 Static 아틀라스라 동적으로 추가할 수도 없으니, 잠김 표기와 같은 한국어로 맞춘다.
            if (!unlocked) label.text = floor + "층  (잠김)";
            else if (cleared) label.text = floor + "층  (클리어)";
            else label.text = floor + "층";

            label.color = unlocked ? BattleHudPalette.PanelText : BattleHudPalette.Dying;
        }
    }

    private void EnterFloor(int floor)
    {
        // 편성이 비어 있으면 들여보내지 않는다. 층을 고른 뒤에 막으면 선택만 바뀐 채 남으므로
        // FloorProgress.TrySelect보다 먼저 확인한다.
        if (PartyDeck.Count == 0)
        {
            warningBanner?.Show(string.Format(EmptyPartyMessageFormat, PartyDeck.ActiveIndex + 1));
            return;
        }

        if (!FloorProgress.TrySelect(floor))
        {
            Debug.LogWarning($"[FloorSelectUI] 아직 열리지 않은 층입니다: {floor}층");
            return;
        }

        if (string.IsNullOrEmpty(battleSceneName))
        {
            Debug.LogError("[FloorSelectUI] 전투 씬 이름이 비어 있습니다.");
            return;
        }

        // 모든 층이 같은 씬을 쓴다. 고른 층은 FloorProgress.SelectedFloor로 전달되고,
        // 스포너가 그 값으로 적 수와 능력치를 키운다.
        string sceneName = battleSceneName;
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[FloorSelectUI] 씬 '{sceneName}'을 불러올 수 없습니다. Build Settings에 등록됐는지 확인하세요.");
            return;
        }

        SceneManager.LoadScene(sceneName);
    }

    private static void SetTopLeft(RectTransform rect, Vector2 size, Vector2 offset)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = offset;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
