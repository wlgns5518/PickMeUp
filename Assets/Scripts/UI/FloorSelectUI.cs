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
    [Header("Layout")]
    [Tooltip("한 쪽에 늘어놓을 층 버튼 개수. 층이 100개라 한 화면에 다 깔지 않고 쪽으로 넘긴다.")]
    [SerializeField, Min(1)] private int floorsPerPage = 10;
    [Tooltip("한 쪽의 층 버튼을 몇 칸씩 늘어놓을지. 10층에 5칸이면 두 줄이다.")]
    [SerializeField, Min(1)] private int pageColumns = 5;
    [SerializeField] private Vector2 panelPadding = new Vector2(30f, 26f);

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

    private const float ButtonWidth = 250f;
    private const float ButtonHeight = 110f;
    private const float ButtonSpacing = 20f;
    private const float TitleHeight = 60f;
    private const float TitleGap = 24f;
    private const float PagerGap = 24f;
    private const float PagerHeight = 70f;
    private const float PagerButtonWidth = 160f;

    private AnnouncementBanner warningBanner;
    private readonly List<NeonButton> floorButtons = new List<NeonButton>();
    private NeonButton previousPageButton;
    private NeonButton nextPageButton;
    private TMP_Text pageLabel;

    // 지금 보고 있는 쪽(0부터).
    private int page;

    private int PageCount => Mathf.CeilToInt((FloorProgress.LastFloor - FloorProgress.FirstFloor + 1) / (float)floorsPerPage);

    // 이 쪽의 slot번째 버튼이 가리키는 층.
    private int FloorAt(int slot) => FloorProgress.FirstFloor + page * floorsPerPage + slot;

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
        // 열 때는 지금 도전할 층(열린 층 중 가장 높은 층)이 있는 쪽을 펼친다.
        // 1층 쪽부터 보여 주면 60층까지 온 플레이어가 매번 다섯 쪽을 넘겨야 한다.
        page = Mathf.Clamp((FloorProgress.HighestUnlocked - FloorProgress.FirstFloor) / floorsPerPage, 0, PageCount - 1);
        RefreshButtons();
        SetOpen(true);
    }

    private void TurnPage(int delta)
    {
        page = Mathf.Clamp(page + delta, 0, PageCount - 1);
        RefreshButtons();
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
        int count = Mathf.Max(1, floorsPerPage);
        // 한 줄로 세우면 창이 가늘고 길어진다. 격자로 깔아 화면을 채운다.
        int columns = Mathf.Clamp(pageColumns, 1, count);
        int rows = Mathf.CeilToInt(count / (float)columns);

        float listWidth = columns * ButtonWidth + (columns - 1) * ButtonSpacing;
        float listHeight = rows * ButtonHeight + (rows - 1) * ButtonSpacing;
        float panelWidth = listWidth + panelPadding.x * 2f;
        // 닫기 버튼이 제목줄에 있으므로 목록 아래에는 쪽 넘김 줄만 둔다.
        float panelHeight = TitleHeight + TitleGap + listHeight + PagerGap + PagerHeight + panelPadding.y * 2f;

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
            int column = i % columns;
            int row = i / columns;
            float x = panelPadding.x + column * (ButtonWidth + ButtonSpacing);
            float y = panelPadding.y + TitleHeight + TitleGap + row * (ButtonHeight + ButtonSpacing);

            // 버튼은 쪽마다 새로 만들지 않고 자리(slot)로 재사용한다. 누른 순간의 쪽으로 층을 푼다.
            // 클로저가 반복 변수를 붙잡지 않도록 지역 변수에 복사해 넘긴다.
            int slot = i;
            // 잠긴 층은 누를 수 없게만 해 두면 NeonButton이 비활성 판(btn_disabled)과 흐린 글자로 바꾼다.
            NeonButton button = HudFactory.CreateButton(panelRect, "FloorSlot_" + slot, NeonButtonStyle.Secondary,
                resolvedFont, string.Empty, 30f, () => EnterFloor(FloorAt(slot)));
            HudFactory.SetTopLeft(button.Rect, new Vector2(ButtonWidth, ButtonHeight), new Vector2(x, -y));

            floorButtons.Add(button);
        }

        // 쪽 넘김 줄. 화살표 기호는 NotoSansKR 아틀라스에 없어 네모로 그려지므로 한국어로 쓴다.
        float pagerY = panelPadding.y + TitleHeight + TitleGap + listHeight + PagerGap;

        previousPageButton = HudFactory.CreateButton(panelRect, "PreviousPage", NeonButtonStyle.Secondary,
            resolvedFont, "이전", 28f, () => TurnPage(-1));
        HudFactory.SetTopLeft(previousPageButton.Rect, new Vector2(PagerButtonWidth, PagerHeight),
            new Vector2(panelPadding.x, -pagerY));

        nextPageButton = HudFactory.CreateButton(panelRect, "NextPage", NeonButtonStyle.Secondary,
            resolvedFont, "다음", 28f, () => TurnPage(1));
        HudFactory.SetTopLeft(nextPageButton.Rect, new Vector2(PagerButtonWidth, PagerHeight),
            new Vector2(panelPadding.x + listWidth - PagerButtonWidth, -pagerY));

        pageLabel = HudFactory.CreateText(panelRect, "PageLabel", resolvedFont, 28f, BattleHudPalette.TextPrimary);
        HudFactory.SetTopLeft(pageLabel.rectTransform, new Vector2(listWidth - PagerButtonWidth * 2f, PagerHeight),
            new Vector2(panelPadding.x + PagerButtonWidth, -pagerY));
    }

    private void RefreshButtons()
    {
        page = Mathf.Clamp(page, 0, Mathf.Max(0, PageCount - 1));

        for (int i = 0; i < floorButtons.Count; i++)
        {
            int floor = FloorAt(i);
            NeonButton button = floorButtons[i];

            // 마지막 쪽이 덜 찼으면 남는 자리는 감춘다.
            bool exists = floor <= FloorProgress.LastFloor;
            button.gameObject.SetActive(exists);
            if (!exists) continue;

            bool unlocked = FloorProgress.IsUnlocked(floor);
            bool cleared = floor <= FloorProgress.HighestCleared;

            button.interactable = unlocked;
            // 열렸지만 아직 깨지 않은 층(지금 도전할 층)만 밝은 판으로 띄워 어디로 가야 할지 보이게 한다.
            button.SetStyle(unlocked && !cleared ? NeonButtonStyle.Primary : NeonButtonStyle.Secondary);

            // 체크표시(U+2713)를 쓰면 NotoSansKR에 글리프가 없어 매번 경고를 뱉고 □로 그려진다.
            // 폰트가 Static 아틀라스라 동적으로 추가할 수도 없으니, 잠김 표기와 같은 한국어로 맞춘다.
            TMP_Text label = button.Label;
            // 버튼 폭이 좁아져(쪽마다 다섯 칸) 상태 표기는 아랫줄로 내린다. "100층  (클리어)"는 한 줄에 안 들어간다.
            if (!unlocked) label.text = floor + "층\n<size=22>(잠김)</size>";
            else if (cleared) label.text = floor + "층\n<size=22>(클리어)</size>";
            else label.text = floor + "층";
        }

        int first = FloorAt(0);
        int last = Mathf.Min(FloorAt(floorButtons.Count - 1), FloorProgress.LastFloor);
        if (pageLabel != null) pageLabel.text = first + " ~ " + last + "층";
        if (previousPageButton != null) previousPageButton.interactable = page > 0;
        if (nextPageButton != null) nextPageButton.interactable = page < PageCount - 1;
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

        // 다섯 층마다 전장 맵이 바뀐다(FloorProgress.BattleSceneName). 같은 구간의 층끼리는 씬이 같고,
        // 고른 층은 FloorProgress.SelectedFloor로 전달돼 스포너가 그 값으로 적 수와 능력치를 키운다.
        string sceneName = FloorProgress.BattleSceneName(floor);
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[FloorSelectUI] 씬 '{sceneName}'을 불러올 수 없습니다. Build Settings에 등록됐는지 확인하세요.");
            return;
        }

        SceneManager.LoadScene(sceneName);
    }

}
