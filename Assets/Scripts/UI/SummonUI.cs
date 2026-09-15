using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

// 마을 소환소에서 여는 창.
//
// 소환소(FacilityGate)를 누르면 열린다. 무료 소환과 유료 소환 중 하나를 고르고 1회나 10회를 뽑는다.
// 확률은 SummonTable이 들고 있고 실제 소환은 CardSpawner가 한다 — 이 파일은 고르고 보여주기만 한다.
// 그래서 확률을 고치려면 SummonTable만 고치면 되고, 화면에 적히는 퍼센트도 따라 바뀐다.
//
// 뽑기를 누르면 창은 스스로 접힌다. 창이 떠 있으면 배경막이 카드를 가려서 무엇이 나왔는지 볼 수 없다.
// 대신 화면 위에 결과 띠가 남아 진행 상황과 결과를 알려주고, 여기서 카드를 치우고 나간다 —
// 카드는 씬에 상설로 있는 캔버스에 붙기 때문에 치우는 버튼이 없으면 마을로 돌아갈 방법이 없다.
//
// FloorSelectUI와 같은 방식으로 캔버스부터 코드에서 만든다. 클릭을 받아야 하므로 GraphicRaycaster를 붙인다.
[DisallowMultipleComponent]
public class SummonUI : FacilityWindow
{
    [Header("Spawner")]
    [Tooltip("뽑기를 실제로 돌리는 곳. 비워두면 씬에서 찾는다.")]
    [SerializeField] private CardSpawner cardSpawner;

    [Header("Layout")]
    [SerializeField] private Vector2 panelPadding = new Vector2(36f, 30f);

    [Header("Open State")]
    [Tooltip("제단을 누르지 않아도 처음부터 열려 있게 하려면 켠다.")]
    [SerializeField] private bool openOnStart;

    [Header("Warning Banner")]
    [Tooltip("경고 배너가 넘지 않을 가로 길이. 모양은 킷의 장식 메시지 박스다.")]
    [SerializeField] private float bannerWidth = 900f;

    private const float PanelWidth = 820f;
    private const float TitleHeight = 60f;
    private const float TabHeight = 72f;
    private const float RateHeaderHeight = 38f;
    private const float RateRowHeight = 50f;
    private const float DrawHeight = 96f;
    private const float HintHeight = 36f;
    private const float Gap = 14f;
    private const float TitleGap = 22f;

    // 확률표 아래에 통째로 붙어 다니는 부분(뽑기 버튼 + 안내)의 높이.
    private const float LowerHeight = DrawHeight + Gap + HintHeight;

    // 결과 띠. 화면 아래는 파티 편성 버튼이 쓰고 있어 위쪽에 붙인다.
    private const float BarWidth = 1040f;
    private const float BarHeight = 108f;
    private const float BarPadding = 22f;
    private const float BarTopMargin = 32f;
    private const float BarButtonWidth = 190f;
    private const float BarButtonHeight = 66f;

    // 두 확률표 중 긴 쪽(유료 6단계)에 맞춰 줄을 만들어 두고, 무료일 때는 남는 줄을 끈다.
    // 탭을 바꿀 때마다 줄을 새로 만들면 방금 누른 버튼 아래에서 오브젝트가 사라진다.
    private static readonly int RateRowCount =
        Mathf.Max(SummonTable.MaxStars(SummonKind.Free), SummonTable.MaxStars(SummonKind.Paid));

    private static readonly Color RareText = new Color(0.55f, 0.75f, 1.00f);   // 3성
    private static readonly Color EpicText = new Color(0.80f, 0.55f, 1.00f);   // 4성

    private static readonly StringBuilder Builder = new StringBuilder(96);

    private AnnouncementBanner warningBanner;

    private RectTransform panelRect;
    private RectTransform lowerSection;
    private float contentWidth;
    private float tableTop;

    private readonly List<NeonButton> tabs = new List<NeonButton>();
    private readonly List<NeonButton> drawButtons = new List<NeonButton>();
    private readonly List<RectTransform> rateRows = new List<RectTransform>();
    private readonly List<TMP_Text> rateGradeLabels = new List<TMP_Text>();
    private readonly List<TMP_Text> ratePercentLabels = new List<TMP_Text>();

    private GameObject resultBar;
    private TMP_Text resultText;
    private readonly List<NeonButton> barButtons = new List<NeonButton>();

    private SummonKind selected = SummonKind.Free;
    private bool summoning;

    protected override string CanvasName => "SummonCanvas";
    // 층 선택 창(95)보다 위.
    protected override int SortingOrder => 96;

    private void Awake()
    {
        if (cardSpawner == null) cardSpawner = FindAnyObjectByType<CardSpawner>(FindObjectsInactive.Include);

        EnsureBuilt();
        SetOpen(openOnStart);
    }

    public override void Show()
    {
        EnsureBuilt();
        SetOpen(true);
    }

    public override void Hide()
    {
        SetOpen(false);
    }

    // ---- 소환 -----------------------------------------------------------

    private void Draw(int count)
    {
        if (summoning) return;

        if (cardSpawner == null)
        {
            warningBanner?.Show("카드 소환기(CardSpawner)를 찾지 못했습니다.");
            return;
        }

        // 소환기는 자기 오브젝트에서 카드마다 코루틴을 돌린다. 꺼져 있으면 조용히 아무 일도 안 일어난다.
        if (!cardSpawner.isActiveAndEnabled)
        {
            warningBanner?.Show("카드 소환기가 꺼져 있습니다.\n씬에서 CardSpawner를 켜주세요.");
            return;
        }

        // 지난 결과가 남아 있으면 이번에 무엇이 나왔는지 알 수 없다.
        cardSpawner.ClearCards();

        // 창이 떠 있으면 배경막이 카드를 가린다. 뽑는 동안은 접어 둔다.
        Hide();
        StartCoroutine(SummonRoutine(count));
    }

    private IEnumerator SummonRoutine(int count)
    {
        summoning = true;
        RefreshInteractable();
        ShowBar(SummonTable.Korean(selected) + " " + count + "회 소환 중...", BattleHudPalette.TextPrimary);

        // 등급별로 몇 장 나왔는지. 0번 칸은 쓰지 않고 별 수를 그대로 색인으로 쓴다.
        var counts = new int[RateRowCount + 1];
        yield return cardSpawner.SummonBatch(selected, count, (card, stars) =>
        {
            if (stars >= 1 && stars < counts.Length) counts[stars]++;
        });

        int best = BestStars(counts);
        ShowBar(Summary(counts), best > 0 ? GradeColor(best) : BattleHudPalette.Dying);

        summoning = false;
        RefreshInteractable();
    }

    // 결과를 확인하고 마을로 돌아간다. 카드를 치우는 유일한 통로다.
    private void Confirm()
    {
        if (summoning) return;

        cardSpawner?.ClearCards();
        HideBar();
        Hide();
    }

    // 카드만 치우고 창을 다시 연다.
    private void SummonAgain()
    {
        if (summoning) return;

        cardSpawner?.ClearCards();
        HideBar();
        Show();
    }

    private static int BestStars(int[] counts)
    {
        for (int stars = counts.Length - 1; stars >= 1; stars--)
            if (counts[stars] > 0) return stars;
        return 0;
    }

    private static string Summary(int[] counts)
    {
        Builder.Clear();
        for (int stars = counts.Length - 1; stars >= 1; stars--)
        {
            if (counts[stars] <= 0) continue;
            if (Builder.Length > 0) Builder.Append("  /  ");
            Builder.Append(stars).Append("성 ").Append(counts[stars]).Append("장");
        }
        // 소환기가 준비되지 않았거나 이름을 받아오다 끊기면 한 장도 나오지 않는다.
        return Builder.Length > 0 ? Builder.ToString() : "소환된 영웅이 없습니다.";
    }

    // ---- 만들기 ---------------------------------------------------------

    protected override void BuildWindow()
    {
        tabs.Clear();
        drawButtons.Clear();
        rateRows.Clear();
        rateGradeLabels.Clear();
        ratePercentLabels.Clear();
        barButtons.Clear();

        BuildCanvas();
        BuildPopup();

        // 결과 띠는 창보다 뒤에 만든다. 뽑는 도중에 제단을 다시 눌러 창이 열려도 띠가 위에 남는다.
        BuildResultBar();

        // 경고 배너는 팝업 밖(캔버스 직속)에 둔다. 창이 닫혀도 같은 자리에 뜬다.
        warningBanner = AnnouncementBanner.Create(canvasRect, resolvedFont, null, bannerWidth);

        RefreshRates();
        RefreshInteractable();
    }

    private void BuildPopup()
    {
        BuildPanel(BuildPopupRoot());
    }

    private void BuildPanel(RectTransform popup)
    {
        contentWidth = PanelWidth - panelPadding.x * 2f;

        panelRect = HudFactory.CreatePanel(popup, "Panel").rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        // 창 높이는 확률표의 줄 수에 따라 달라진다. RefreshRates가 정한다.

        float y = panelPadding.y;

        BuildTitleBar(panelRect, "소환소", panelPadding.x, y, contentWidth, TitleHeight);
        y += TitleHeight + TitleGap;

        BuildTabs(y);
        y += TabHeight + Gap;

        tableTop = y;
        BuildRateTable(tableTop);

        // 확률표의 줄 수가 소환 종류마다 다르다. 아래쪽은 통째로 오르내리므로 한 덩어리로 묶어 둔다.
        lowerSection = HudFactory.CreateGroup(panelRect, "Lower");
        BuildLowerSection(lowerSection);
    }

    private void BuildTabs(float y)
    {
        float tabWidth = (contentWidth - Gap) * 0.5f;

        for (int i = 0; i < 2; i++)
        {
            // 클로저가 반복 변수를 붙잡지 않도록 지역 변수에 복사해 넘긴다.
            var kind = (SummonKind)i;

            NeonButton tab = HudFactory.CreateButton(panelRect, "Tab_" + kind, NeonButtonStyle.Ghost,
                resolvedFont, SummonTable.Korean(kind), 32f, () => SelectKind(kind));
            HudFactory.SetTopLeft(tab.Rect, new Vector2(tabWidth, TabHeight),
                new Vector2(panelPadding.x + i * (tabWidth + Gap), -y));

            tabs.Add(tab);
        }
    }

    private void BuildRateTable(float y)
    {
        TMP_Text header = HudFactory.CreateText(panelRect, "RateHeader", resolvedFont, 26f, BattleHudPalette.TextMuted);
        header.alignment = TextAlignmentOptions.Left;
        HudFactory.SetTopLeft(header.rectTransform, new Vector2(contentWidth, RateHeaderHeight), new Vector2(panelPadding.x, -y));
        header.text = "등급별 확률";

        for (int i = 0; i < RateRowCount; i++)
        {
            float rowY = y + RateHeaderHeight + i * RateRowHeight;

            RectTransform row = HudFactory.CreateGroup(panelRect, "Rate_" + (i + 1));
            HudFactory.SetTopLeft(row, new Vector2(contentWidth, RateRowHeight), new Vector2(panelPadding.x, -rowY));

            TMP_Text grade = HudFactory.CreateText(row, "Grade", resolvedFont, 29f, BattleHudPalette.TextPrimary);
            grade.alignment = TextAlignmentOptions.Left;
            HudFactory.Stretch(grade.rectTransform);

            TMP_Text percent = HudFactory.CreateText(row, "Percent", resolvedFont, 29f, BattleHudPalette.TextPrimary);
            percent.alignment = TextAlignmentOptions.Right;
            HudFactory.Stretch(percent.rectTransform);

            rateRows.Add(row);
            rateGradeLabels.Add(grade);
            ratePercentLabels.Add(percent);
        }
    }

    // 뽑기 버튼과 그 아래 안내. 자리는 묶음 안에서의 상대 위치라 묶음만 옮기면 통째로 따라온다.
    private void BuildLowerSection(RectTransform lower)
    {
        BuildDrawButtons(lower);

        TMP_Text hint = HudFactory.CreateText(lower, "Hint", resolvedFont, 24f, BattleHudPalette.TextMuted);
        HudFactory.SetTopLeft(hint.rectTransform, new Vector2(contentWidth, HintHeight), new Vector2(0f, -(DrawHeight + Gap)));
        hint.text = "누르면 창이 닫히고 뽑은 영웅 카드가 화면에 나타납니다.";
    }

    private void BuildDrawButtons(RectTransform lower)
    {
        float buttonWidth = (contentWidth - Gap) * 0.5f;
        int[] counts = { 1, 10 };

        for (int i = 0; i < counts.Length; i++)
        {
            // 클로저가 반복 변수를 붙잡지 않도록 지역 변수에 복사해 넘긴다.
            int count = counts[i];

            // 10회가 이 창의 주된 행동이라 밝은 판으로, 1회는 보조 버튼으로 둔다.
            NeonButton button = HudFactory.CreateButton(lower, "Draw_" + count,
                count > 1 ? NeonButtonStyle.Primary : NeonButtonStyle.Secondary,
                resolvedFont, count + "회 소환", 36f, () => Draw(count));
            HudFactory.SetTopLeft(button.Rect, new Vector2(buttonWidth, DrawHeight), new Vector2(i * (buttonWidth + Gap), 0f));

            drawButtons.Add(button);
        }
    }

    // ---- 결과 띠 --------------------------------------------------------

    private void BuildResultBar()
    {
        // 띠 위를 누른 클릭이 뒤쪽 세계로 새지 않게 판이 클릭을 받아 둔다.
        RectTransform barRect = HudFactory.CreateHudStrip(canvasRect, "ResultBar").rectTransform;
        barRect.anchorMin = new Vector2(0.5f, 1f);
        barRect.anchorMax = new Vector2(0.5f, 1f);
        barRect.pivot = new Vector2(0.5f, 1f);
        barRect.sizeDelta = new Vector2(BarWidth, BarHeight);
        barRect.anchoredPosition = new Vector2(0f, -BarTopMargin);
        resultBar = barRect.gameObject;

        float buttonsWidth = BarButtonWidth * 2f + Gap;
        float textWidth = BarWidth - BarPadding * 2f - buttonsWidth - Gap;

        resultText = HudFactory.CreateText(barRect, "Result", resolvedFont, 30f, BattleHudPalette.TextPrimary);
        resultText.alignment = TextAlignmentOptions.Left;
        SetLeftMiddle(resultText.rectTransform, new Vector2(textWidth, BarHeight), BarPadding);

        float againX = BarWidth - BarPadding - buttonsWidth;
        barButtons.Add(BuildBarButton(barRect, "다시 소환", againX, NeonButtonStyle.Secondary, SummonAgain));
        barButtons.Add(BuildBarButton(barRect, "확인", againX + BarButtonWidth + Gap, NeonButtonStyle.Primary, Confirm));

        resultBar.SetActive(false);
    }

    private NeonButton BuildBarButton(RectTransform barRect, string text, float x, NeonButtonStyle style, UnityEngine.Events.UnityAction onClick)
    {
        NeonButton button = HudFactory.CreateButton(barRect, "Bar_" + text, style, resolvedFont, text, 28f, onClick);
        SetLeftMiddle(button.Rect, new Vector2(BarButtonWidth, BarButtonHeight), x);
        return button;
    }

    private void ShowBar(string message, Color color)
    {
        if (resultBar == null) return;

        resultBar.SetActive(true);
        resultText.text = message;
        resultText.color = color;
    }

    private void HideBar()
    {
        if (resultBar != null) resultBar.SetActive(false);
    }

    // ---- 갱신 -----------------------------------------------------------

    private void SelectKind(SummonKind kind)
    {
        // 뽑는 중에 종류를 바꾸면 진행 중인 소환이 어느 확률로 나온 것인지 알 수 없어진다.
        if (summoning) return;

        selected = kind;
        RefreshRates();
    }

    private void RefreshRates()
    {
        int shown = SummonTable.MaxStars(selected);

        for (int i = 0; i < rateRows.Count; i++)
        {
            int stars = i + 1;
            bool used = stars <= shown;
            rateRows[i].gameObject.SetActive(used);
            if (!used) continue;

            rateGradeLabels[i].text = stars + "성";
            rateGradeLabels[i].color = GradeColor(stars);
            ratePercentLabels[i].text = SummonTable.PercentText(selected, stars);
            ratePercentLabels[i].color = GradeColor(stars);
        }

        for (int i = 0; i < tabs.Count; i++)
            tabs[i].SetStyle((SummonKind)i == selected ? NeonButtonStyle.Primary : NeonButtonStyle.Ghost);

        LayoutPanel(shown);
    }

    // 무료 소환은 두 줄, 유료 소환은 여섯 줄이다. 창을 늘 여섯 줄 높이로 두면 무료일 때
    // 확률표와 버튼 사이가 통째로 비어 보인다. 줄 수에 맞춰 아래쪽과 창 높이를 함께 옮긴다.
    private void LayoutPanel(int shownRows)
    {
        if (panelRect == null || lowerSection == null) return;

        float lowerY = tableTop + RateHeaderHeight + shownRows * RateRowHeight + Gap;

        HudFactory.SetTopLeft(lowerSection, new Vector2(contentWidth, LowerHeight), new Vector2(panelPadding.x, -lowerY));
        panelRect.sizeDelta = new Vector2(PanelWidth, lowerY + LowerHeight + panelPadding.y);
    }

    private void RefreshInteractable()
    {
        for (int i = 0; i < drawButtons.Count; i++) drawButtons[i].interactable = !summoning;
        // 뽑는 중에 카드를 치우면 남은 생성이 빈 자리에 값을 쓰게 된다. 끝날 때까지 잠가 둔다.
        for (int i = 0; i < barButtons.Count; i++) barButtons[i].interactable = !summoning;
    }

    private static Color GradeColor(int stars)
    {
        if (stars >= 5) return BattleHudPalette.Mvp;
        if (stars == 4) return EpicText;
        if (stars == 3) return RareText;
        return BattleHudPalette.TextPrimary;
    }

    // ---- 자리 잡기 ------------------------------------------------------

    // 왼쪽 끝에서 x만큼 떨어진 자리에 세로로는 가운데. 한 줄로 늘어놓는 결과 띠에 쓴다.
    private static void SetLeftMiddle(RectTransform rect, Vector2 size, float x)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(x, 0f);
    }

}
