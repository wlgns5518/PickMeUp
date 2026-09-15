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
    [Tooltip("경고 배너가 넘지 않을 가로 길이. 모양은 킷의 장식 메시지 박스다.")]
    [SerializeField] private float bannerWidth = 900f;

    // 편성이 비어 있으면 스포너가 인스펙터에 박아둔 명단으로 대신 싸운다. 그건 전투 씬을 직접
    // 재생할 때의 대비책이고, 플레이어가 문으로 들어갈 때는 자기가 고른 파티로만 들어가야 한다.
    // 파티가 셋이라 어느 파티가 비었는지 짚어줘야 한다.
    private const string EmptyPartyMessageFormat = "{0}파티에 출전할 영웅이 없습니다.\n먼저 영웅을 편성해주세요.";

    private const float ButtonWidth = 360f;
    private const float ButtonHeight = 132f;
    private const float ButtonSpacing = 20f;
    private const float TitleHeight = 60f;
    private const float TitleGap = 24f;

    private AnnouncementBanner warningBanner;
    private readonly List<NeonButton> floorButtons = new List<NeonButton>();

    protected override string CanvasName => "FloorSelectCanvas";
    // 편성 카드(91)보다 위. 층을 고르는 동안에는 이 창이 가장 앞에 있어야 한다.
    protected override int SortingOrder => 95;

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
        BuildCanvas();
        BuildPopup();

        // 경고 배너는 팝업 밖(캔버스 직속)에 둔다. 창이 닫혀도 같은 자리에 뜬다.
        warningBanner = AnnouncementBanner.Create(canvasRect, resolvedFont, null, bannerWidth);
    }

    private void BuildPopup()
    {
        BuildPanel(BuildPopupRoot());
    }

    private void BuildPanel(RectTransform popup)
    {
        int count = Mathf.Max(1, visibleFloorCount);
        // 한 줄로 세우면 아홉 층이 세로로 길게 늘어서 창이 가늘고 길어진다. 격자로 깔아 화면을 채운다.
        int columns = Mathf.Clamp(floorColumns, 1, count);
        int rows = Mathf.CeilToInt(count / (float)columns);

        float listWidth = columns * ButtonWidth + (columns - 1) * ButtonSpacing;
        float panelWidth = listWidth + panelPadding.x * 2f;
        // 닫기 버튼이 제목줄에 있으므로 목록 아래에 따로 자리를 남기지 않는다.
        float panelHeight = TitleHeight + TitleGap + rows * ButtonHeight + (rows - 1) * ButtonSpacing + panelPadding.y * 2f;

        Image panel = HudFactory.CreatePanel(popup, "Panel");
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);
        panelRect.anchoredPosition = Vector2.zero;

        BuildTitleBar(panelRect, "층 선택", panelPadding.x, panelPadding.y, listWidth, TitleHeight);

        for (int i = 0; i < count; i++)
        {
            int floor = FloorProgress.FirstFloor + i;
            int column = i % columns;
            int row = i / columns;
            float x = panelPadding.x + column * (ButtonWidth + ButtonSpacing);
            float y = panelPadding.y + TitleHeight + TitleGap + row * (ButtonHeight + ButtonSpacing);

            // 클로저가 반복 변수를 붙잡지 않도록 지역 변수에 복사해 넘긴다.
            int captured = floor;
            // 잠긴 층은 누를 수 없게만 해 두면 NeonButton이 비활성 판(btn_disabled)과 흐린 글자로 바꾼다.
            NeonButton button = HudFactory.CreateButton(panelRect, "Floor_" + floor, NeonButtonStyle.Secondary,
                resolvedFont, string.Empty, 34f, () => EnterFloor(captured));
            HudFactory.SetTopLeft(button.Rect, new Vector2(ButtonWidth, ButtonHeight), new Vector2(x, -y));

            floorButtons.Add(button);
        }
    }

    private void RefreshButtons()
    {
        for (int i = 0; i < floorButtons.Count; i++)
        {
            int floor = FloorProgress.FirstFloor + i;
            bool unlocked = FloorProgress.IsUnlocked(floor);
            bool cleared = floor <= FloorProgress.HighestCleared;

            NeonButton button = floorButtons[i];
            button.interactable = unlocked;
            // 열렸지만 아직 깨지 않은 층(지금 도전할 층)만 밝은 판으로 띄워 어디로 가야 할지 보이게 한다.
            button.SetStyle(unlocked && !cleared ? NeonButtonStyle.Primary : NeonButtonStyle.Secondary);

            // 체크표시(U+2713)를 쓰면 NotoSansKR에 글리프가 없어 매번 경고를 뱉고 □로 그려진다.
            // 폰트가 Static 아틀라스라 동적으로 추가할 수도 없으니, 잠김 표기와 같은 한국어로 맞춘다.
            TMP_Text label = button.Label;
            if (!unlocked) label.text = floor + "층  (잠김)";
            else if (cleared) label.text = floor + "층  <size=24>(클리어)</size>";
            else label.text = floor + "층";
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

}
