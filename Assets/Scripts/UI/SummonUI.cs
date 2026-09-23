using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 캐릭터 소환소 — 영웅을 소환하고, 소환 확률을 확인한다.
//
//   ┌ ‹ 캐릭터 소환소 ─────────────────────────── [골드] [젬] ┐
//   │ [일반 소환 ] ┌──────────── 배너 그림 ────────────┐    │
//   │ [고급 소환 ] │ 이름 · 테마 · 최고 등급            │    │
//   │              └───────────────────────────────────┘    │
//   │              [소환 확률]        [1회 소환] [10회 소환] │
//
// 확률은 메인 화면에 다 적지 않는다. "소환 확률" 버튼이 늘 같은 자리(실행 버튼 줄 왼쪽)에 있고,
// 누르면 등급별 확률표 팝업이 뜬다 — 숨겨 둔 정보처럼 보이지 않게, 그러나 화면을 차지하지 않게.
//
// 확률은 SummonTable, 실제 소환은 CardSpawner, 값(일반은 골드, 고급은 젬)은 GameEconomy가 들고 있다 — 이 파일은 고르고 보여 주기만 한다.
// 소환을 누르면 화면을 접고 카드가 마을 화면에 펼쳐진다. 화면 위에 결과 띠가 남아 결과를 알리고,
// 거기서 카드를 치우거나(확인) 곧바로 다시 소환한다.
[DisallowMultipleComponent]
public class SummonUI : UiScreen
{
    [Header("Spawner")]
    [Tooltip("뽑기를 실제로 돌리는 곳. 비워두면 씬에서 찾는다.")]
    [SerializeField] private CardSpawner cardSpawner;

    [Header("Open State")]
    [Tooltip("제단을 누르지 않아도 처음부터 열려 있게 하려면 켠다.")]
    [SerializeField] private bool openOnStart;

    private const float BannerListWidth = 360f;
    private const float BannerCardHeight = 176f;
    private const float ActionRowHeight = UiTheme.ButtonLarge;
    private const float SingleWidth = 340f;
    private const float TenWidth = 420f;

    private static readonly SummonKind[] Banners = { SummonKind.Normal, SummonKind.Paid };

    private class BannerCard
    {
        public SummonKind Kind;
        public Image Ring;
        public TMP_Text Cost;
    }

    private readonly List<BannerCard> bannerCards = new List<BannerCard>();
    private Image bannerArt;
    private TMP_Text bannerName;
    private TMP_Text bannerTheme;
    private TMP_Text bannerInfo;
    private UiButton singleButton;
    private UiButton tenButton;

    private UiPopup ratePopup;
    private UiTabs rateTabs;
    private readonly List<RectTransform> rateRows = new List<RectTransform>();
    private TMP_Text rateSummary;

    private RectTransform resultBar;
    private TMP_Text resultText;
    private UiButton againButton;
    private UiButton confirmButton;

    private SummonKind selected = SummonKind.Paid;
    private bool summoning;

    private static readonly StringBuilder Builder = new StringBuilder(96);

    protected override string CanvasName => "SummonCanvas";
    // 층 선택 창(95)보다 위.
    protected override int SortingOrder => 96;
    protected override string Title => "캐릭터 소환소";
    protected override Currency[] HeaderCurrencies => new[] { Currency.Gold, Currency.Gem };

    private void Awake()
    {
        if (cardSpawner == null) cardSpawner = FindAnyObjectByType<CardSpawner>(FindObjectsInactive.Include);

        EnsureBuilt();
        SetOpen(openOnStart);
    }

    private void OnEnable() => PlayerAccount.Changed += RefreshCosts;
    private void OnDisable() => PlayerAccount.Changed -= RefreshCosts;

    public override void Show()
    {
        EnsureBuilt();
        RefreshBanner();
        SetOpen(true);
    }

    public override void Hide()
    {
        if (ratePopup != null) ratePopup.Hide();
        SetOpen(false);
    }

    // ---- 짓기 ---------------------------------------------------------------

    protected override void BuildContent(RectTransform root, Vector2 size)
    {
        bannerCards.Clear();

        BuildBannerList(root, size);

        // 배너는 남는 폭과 높이를 모두 쓴다(화면 비율이 달라도 실행 버튼 줄은 늘 아래에 붙는다).
        float mainX = BannerListWidth + UiTheme.ColumnGap;
        BuildBannerView(root, mainX, size.x - mainX);
        BuildActionRow(root, mainX);
    }

    private void BuildBannerList(RectTransform root, Vector2 size)
    {
        TMP_Text title = UiKit.SectionTitle(root, "BannerTitle", "소환 배너");
        UiKit.TopLeft(title.rectTransform, 4f, 0f, BannerListWidth, 44f);

        for (int i = 0; i < Banners.Length; i++)
        {
            SummonKind kind = Banners[i];
            float y = 56f + i * (BannerCardHeight + UiTheme.Space4);

            UiKit.Surface card = UiKit.Panel(root, "Banner_" + kind, UiTheme.SurfaceRaised, UiTheme.RadiusL, UiTheme.Border);
            UiKit.TopLeft(card.Rect, 0f, y, BannerListWidth, BannerCardHeight);
            card.Fill.raycastTarget = true;

            // 배너 그림을 작게. 둥근 판 안에서 잘리도록 가림막을 둔다.
            RectTransform mask = UiKit.Node(card.Rect, "Art");
            UiKit.Fill(mask, 3f);
            mask.gameObject.AddComponent<RectMask2D>();
            Image art = UiKit.Image(mask, "Image", UiIconLibrary.Banner(kind), Color.white, false);
            Image shade = UiKit.Image(mask, "Shade", null, Color.white, false);
            shade.gameObject.AddComponent<UiGradient>().Set(new Color(0f, 0f, 0f, 0f), new Color(0.02f, 0.03f, 0.05f, 0.92f));
            art.enabled = art.sprite != null;

            TMP_Text name = UiKit.Text(card.Rect, "Name", BannerName(kind), UiTheme.FontHeading, UiTheme.TextPrimary);
            UiKit.BottomLeft(name.rectTransform, UiTheme.Space4, 44f, BannerListWidth - 32f, 40f);
            TMP_Text cost = UiKit.Text(card.Rect, "Cost", string.Empty, UiTheme.FontLabel, UiTheme.TextSecondary);
            UiKit.BottomLeft(cost.rectTransform, UiTheme.Space4, 12f, BannerListWidth - 32f, 32f);

            Image ring = UiKit.Line(card.Rect, "Selection", UiTheme.Selection, UiTheme.RadiusL + 4, (int)UiTheme.SelectionWidth);
            UiKit.Fill(ring.rectTransform, -4f);

            var button = card.Rect.gameObject.AddComponent<Button>();
            button.targetGraphic = card.Fill;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => SelectBanner(kind));

            bannerCards.Add(new BannerCard { Kind = kind, Ring = ring, Cost = cost });
        }
    }

    private void BuildBannerView(RectTransform root, float x, float width)
    {
        UiKit.Surface view = UiKit.Panel(root, "BannerView", UiTheme.SurfaceSunken, UiTheme.RadiusL, UiTheme.BorderStrong, 24);
        UiKit.Fill(view.Rect, x, 0f, 0f, ActionRowHeight + UiTheme.Space5);

        RectTransform mask = UiKit.Node(view.Rect, "Art");
        UiKit.Fill(mask, 3f);
        mask.gameObject.AddComponent<RectMask2D>();
        bannerArt = UiKit.Image(mask, "Image", null, Color.white, false);
        // 판 비율이 2:1이 아니어도 그림을 찌그러뜨리지 않고 넘치는 쪽을 잘라 꽉 채운다.
        var fitter = bannerArt.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = 2f;

        // 왼쪽을 어둡게 눌러 글자가 그림 위에서도 읽히게 한다(배너 그림은 왼쪽을 비워 두고 그렸다).
        Image shade = UiKit.Image(mask, "Shade", null, Color.white, false);
        shade.rectTransform.anchorMax = new Vector2(0.66f, 1f);
        shade.gameObject.AddComponent<UiGradient>().SetHorizontal(
            new Color(0.03f, 0.04f, 0.07f, 0.94f), new Color(0.03f, 0.04f, 0.07f, 0f));

        float textX = UiTheme.Space6 + 8f;
        float textWidth = width * 0.5f;

        bannerTheme = UiKit.Text(view.Rect, "Theme", string.Empty, UiTheme.FontLabel, UiTheme.Primary);
        UiKit.TopLeft(bannerTheme.rectTransform, textX, UiTheme.Space6 + 4f, textWidth, 32f);

        bannerName = UiKit.Text(view.Rect, "Name", string.Empty, UiTheme.FontDisplay + 14f, UiTheme.TextPrimary);
        UiKit.TopLeft(bannerName.rectTransform, textX, UiTheme.Space6 + 40f, textWidth, 84f);

        bannerInfo = UiKit.Wrap(UiKit.Text(view.Rect, "Info", string.Empty, UiTheme.FontBody, UiTheme.TextSecondary));
        bannerInfo.alignment = TextAlignmentOptions.TopLeft;
        bannerInfo.lineSpacing = 10f;
        UiKit.TopLeft(bannerInfo.rectTransform, textX, UiTheme.Space6 + 140f, textWidth, 360f);
    }

    private void BuildActionRow(RectTransform root, float x)
    {
        // 확률은 늘 여기 있다. 실행 버튼과 같은 줄, 누구나 보는 자리.
        UiButton rates = UiButton.Create(root, "Rates", "소환 확률", UiButtonStyle.Secondary, UiButtonSize.Medium, OpenRates);
        UiKit.BottomLeft(rates.Rect, x, (ActionRowHeight - UiTheme.ButtonMedium) * 0.5f, 240f, UiTheme.ButtonMedium);
        Image info = UiKit.Glyph(rates.Rect, "Info", UiSprites.Glyph.Info, UiTheme.TextSecondary);
        UiKit.LeftMiddle(info.rectTransform, 22f, 30f, 30f);
        rates.Label.margin = new Vector4(34f, 0f, 0f, 0f);

        tenButton = UiButton.Create(root, "Draw10", "10회 소환", UiButtonStyle.Primary, UiButtonSize.Large, () => Draw(10));
        UiKit.BottomRight(tenButton.Rect, 0f, 0f, TenWidth, ActionRowHeight);

        singleButton = UiButton.Create(root, "Draw1", "1회 소환", UiButtonStyle.Secondary, UiButtonSize.Large, () => Draw(1));
        UiKit.BottomRight(singleButton.Rect, TenWidth + UiTheme.Space4, 0f, SingleWidth, ActionRowHeight);
    }

    protected override void BuildOverlays()
    {
        BuildRatePopup();
        BuildResultBar();
        RefreshBanner();
    }

    // ---- 확률 팝업 ------------------------------------------------------------

    private const float RateRowHeight = 60f;
    private static readonly float[] RateColumns = { 0f, 200f, 420f };

    private void BuildRatePopup()
    {
        rateRows.Clear();
        ratePopup = UiPopup.Create(overlay, "RatePopup", "소환 확률", new Vector2(1080f, 820f), false);

        string[] labels = new string[Banners.Length];
        for (int i = 0; i < Banners.Length; i++) labels[i] = BannerName(Banners[i]);
        rateTabs = UiTabs.Create(ratePopup.Body, "Tabs", labels);
        UiKit.TopStretch(rateTabs.Rect, 0f, UiTheme.TabHeight);
        rateTabs.Changed += _ => RefreshRates();

        float y = UiTheme.TabHeight + UiTheme.Space5;

        // 머리줄
        RectTransform header = UiKit.Node(ratePopup.Body, "Header");
        UiKit.TopStretch(header, y, 44f);
        UiKit.Rounded(header, "Fill", UiTheme.SurfaceSunken, UiTheme.RadiusS);
        string[] titles = { "등급", "등장 확률", "등장하는 영웅" };
        for (int c = 0; c < titles.Length; c++)
        {
            TMP_Text t = UiKit.Text(header, "Col_" + c, titles[c], UiTheme.FontLabel, UiTheme.TextSecondary);
            UiKit.Fill(t.rectTransform, RateColumns[c] + UiTheme.Space5, 0f, 0f, 0f);
        }
        y += 44f + UiTheme.Space2;

        int maxRows = Mathf.Max(SummonTable.MaxStars(SummonKind.Normal), SummonTable.MaxStars(SummonKind.Paid));
        for (int i = 0; i < maxRows; i++)
        {
            RectTransform row = UiKit.Node(ratePopup.Body, "Row_" + i);
            UiKit.TopStretch(row, y + i * RateRowHeight, RateRowHeight);
            UiKit.Rounded(row, "Fill", i % 2 == 0 ? UiTheme.SurfaceRaised : UiTheme.Surface, UiTheme.RadiusS);
            for (int c = 0; c < RateColumns.Length; c++)
            {
                TMP_Text t = UiKit.Text(row, "Col_" + c, string.Empty, c == 1 ? UiTheme.FontHeading : UiTheme.FontBody, UiTheme.TextPrimary);
                UiKit.Fill(t.rectTransform, RateColumns[c] + UiTheme.Space5, 0f, 0f, 0f);
            }
            rateRows.Add(row);
        }

        rateSummary = UiKit.Wrap(UiKit.Text(ratePopup.Body, "Note", string.Empty, UiTheme.FontLabel, UiTheme.TextSecondary));
        rateSummary.alignment = TextAlignmentOptions.BottomLeft;
        rateSummary.lineSpacing = 6f;
        UiKit.BottomStretch(rateSummary.rectTransform, 0f, 110f);
    }

    private void OpenRates()
    {
        rateTabs.Select(System.Array.IndexOf(Banners, selected), false);
        RefreshRates();
        ratePopup.Show();
    }

    private void RefreshRates()
    {
        SummonKind kind = Banners[Mathf.Clamp(rateTabs.Selected, 0, Banners.Length - 1)];
        int shown = SummonTable.MaxStars(kind);

        for (int i = 0; i < rateRows.Count; i++)
        {
            int stars = i + 1;
            bool used = stars <= shown;
            rateRows[i].gameObject.SetActive(used);
            if (!used) continue;

            Color color = UiTheme.StarColor(stars);
            SetCell(rateRows[i], 0, UiTheme.Paint(UiKit.Stars(stars), color) + "  " + stars + "성");
            SetCell(rateRows[i], 1, UiTheme.Paint(SummonTable.PercentText(kind, stars), color));
            SetCell(rateRows[i], 2, $"{stars}성 영웅");
        }

        // 영웅은 소환될 때마다 새로 태어나므로 영웅별 개별 확률이 없다. 그것만 짚어 준다.
        rateSummary.text =
            "영웅별 개별 확률은 없습니다.\n" +
            $"{UiKit.Stars(UiTheme.MaxTier)}은 소환으로 등장하지 않습니다.";
    }

    private static void SetCell(RectTransform row, int column, string text) =>
        row.Find("Col_" + column).GetComponent<TMP_Text>().text = text;

    // ---- 소환 ---------------------------------------------------------------

    private void SelectBanner(SummonKind kind)
    {
        if (summoning) return;
        selected = kind;
        RefreshBanner();
    }

    private void Draw(int count)
    {
        if (summoning) return;

        if (cardSpawner == null || !cardSpawner.isActiveAndEnabled)
        {
            toast.Show("소환기(CardSpawner)를 찾지 못했거나 꺼져 있습니다.", UiToastKind.Danger);
            return;
        }

        Currency currency = GameEconomy.SummonCurrency(selected);
        long cost = GameEconomy.SummonCost(selected, count);
        if (!PlayerAccount.TrySpend(currency, cost))
        {
            toast.Show($"{CurrencyName(currency)}{HeroLabel.SubjectParticle(CurrencyName(currency))} 부족합니다. ({UiKit.Amount(cost)} 필요)", UiToastKind.Warning);
            return;
        }

        // 지난 결과가 남아 있으면 이번에 무엇이 나왔는지 알 수 없다.
        cardSpawner.ClearCards();

        // 화면이 떠 있으면 카드가 가려진다. 뽑는 동안은 접어 둔다.
        Hide();
        StartCoroutine(SummonRoutine(selected, count, currency, cost));
    }

    private IEnumerator SummonRoutine(SummonKind kind, int count, Currency currency, long cost)
    {
        summoning = true;
        RefreshInteractable();
        ShowBar($"{BannerName(kind)} {count}회 소환 중...", UiTheme.TextPrimary);

        // 등급별로 몇 장 나왔는지. 0번 칸은 쓰지 않고 별 수를 그대로 색인으로 쓴다.
        var counts = new int[UiTheme.MaxTier + 1];
        int summoned = 0;
        yield return cardSpawner.SummonBatch(kind, count, (card, stars) =>
        {
            summoned++;
            if (stars >= 1 && stars < counts.Length) counts[stars]++;
        });

        // 한 장도 나오지 않았다면(소환기가 준비되지 않았거나 도중에 끊김) 낸 재화를 돌려준다.
        if (summoned == 0 && cost > 0) PlayerAccount.Add(currency, cost);

        int best = BestStars(counts);
        ShowBar(Summary(counts), best > 0 ? UiTheme.StarColor(best) : UiTheme.TextMuted);

        summoning = false;
        RefreshInteractable();
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
            if (Builder.Length > 0) Builder.Append("   ");
            Builder.Append(UiTheme.Paint(UiKit.Stars(stars), UiTheme.StarColor(stars))).Append(" x").Append(counts[stars]);
        }
        return Builder.Length > 0 ? "소환 결과   " + Builder : "소환된 영웅이 없습니다. 낸 재화는 돌려받았습니다.";
    }

    // ---- 결과 띠 ------------------------------------------------------------

    private void BuildResultBar()
    {
        // 화면(Screen) 밖, 캔버스 바로 아래에 둔다. 화면을 접어도 띠는 남아야 한다.
        UiKit.Surface bar = UiKit.Panel(canvasRect, "ResultBar", UiTheme.Surface, UiTheme.RadiusL, UiTheme.BorderStrong, 24);
        UiKit.TopCenter(bar.Rect, 0f, TopBarHud.ReservedHeight + UiTheme.Space4, 1100f, 112f);
        bar.Fill.raycastTarget = true;
        resultBar = bar.Rect;

        resultText = UiKit.Text(bar.Rect, "Result", string.Empty, UiTheme.FontHeading, UiTheme.TextPrimary);
        UiKit.Fill(resultText.rectTransform, UiTheme.Space6, 0f, 520f, 0f);

        confirmButton = UiButton.Create(bar.Rect, "Confirm", "확인", UiButtonStyle.Primary, UiButtonSize.Medium, Confirm);
        UiKit.RightMiddle(confirmButton.Rect, UiTheme.Space5, 200f, UiTheme.ButtonMedium);
        againButton = UiButton.Create(bar.Rect, "Again", "다시 소환", UiButtonStyle.Secondary, UiButtonSize.Medium, SummonAgain);
        UiKit.RightMiddle(againButton.Rect, UiTheme.Space5 + 200f + UiTheme.Space3, 220f, UiTheme.ButtonMedium);

        resultBar.gameObject.SetActive(false);
    }

    private void ShowBar(string message, Color color)
    {
        resultBar.gameObject.SetActive(true);
        resultBar.SetAsLastSibling();
        resultText.text = message;
        resultText.color = color;
    }

    // 결과를 확인하고 마을로 돌아간다. 카드를 치우는 유일한 통로다.
    private void Confirm()
    {
        if (summoning) return;
        cardSpawner?.ClearCards();
        resultBar.gameObject.SetActive(false);
    }

    // 카드만 치우고 화면을 다시 연다.
    private void SummonAgain()
    {
        if (summoning) return;
        cardSpawner?.ClearCards();
        resultBar.gameObject.SetActive(false);
        Show();
    }

    // ---- 갱신 ---------------------------------------------------------------

    private void RefreshBanner()
    {
        if (bannerName == null) return;

        Sprite art = UiIconLibrary.Banner(selected);
        bannerArt.sprite = art;
        bannerArt.enabled = art != null;
        bannerTheme.text = BannerTheme(selected);
        bannerName.text = BannerName(selected);
        bannerInfo.text = BannerInfo(selected);

        for (int i = 0; i < bannerCards.Count; i++)
            bannerCards[i].Ring.enabled = bannerCards[i].Kind == selected;

        RefreshCosts();
    }

    private void RefreshCosts()
    {
        if (singleButton == null) return;

        for (int i = 0; i < bannerCards.Count; i++)
        {
            long single = GameEconomy.SummonCost(bannerCards[i].Kind, 1);
            Currency currency = GameEconomy.SummonCurrency(bannerCards[i].Kind);
            bannerCards[i].Cost.text = $"1회 {CurrencyName(currency)} {UiKit.Amount(single)}";
        }

        ApplyCost(singleButton, 1);
        ApplyCost(tenButton, 10);
        RefreshInteractable();
    }

    private void ApplyCost(UiButton button, int count)
    {
        button.SetCost(GameEconomy.SummonCurrency(selected), GameEconomy.SummonCost(selected, count));
    }

    private void RefreshInteractable()
    {
        if (singleButton != null)
        {
            // 재화가 모자라도 버튼은 누를 수 있다 — 누르면 얼마가 모자란지 알려 준다. 모자란 액수는 이미 빨갛다.
            singleButton.interactable = !summoning;
            tenButton.interactable = !summoning;
        }
        if (againButton != null)
        {
            // 뽑는 중에 카드를 치우면 남은 생성이 빈 자리에 값을 쓰게 된다. 끝날 때까지 잠가 둔다.
            againButton.interactable = !summoning;
            confirmButton.interactable = !summoning;
        }
    }

    // ---- 배너 문구 ------------------------------------------------------------

    private static string BannerName(SummonKind kind) => kind == SummonKind.Paid ? "고급 소환" : "일반 소환";

    private static string BannerTheme(SummonKind kind) => kind == SummonKind.Paid ? "황금 관문" : "달빛 사원의 소환진";

    private static string BannerInfo(SummonKind kind)
    {
        int max = SummonTable.MaxStars(kind);
        string top = UiTheme.Paint(UiKit.Stars(max), UiTheme.StarColor(max));
        string topRate = SummonTable.PercentText(kind, max);
        return $"최고 {top} 등장  ·  확률 {topRate}";
    }

    private static string CurrencyName(Currency currency) => currency == Currency.Gem ? "젬" : "골드";
}
