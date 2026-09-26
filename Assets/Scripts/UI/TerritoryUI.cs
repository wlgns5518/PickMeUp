using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Kind = VillageBlockout.Kind;

// 영지 관리 — 마을 시설을 한 화면에서 보고 젬으로 업그레이드한다(2026-09-26 사용자 결정).
// 마을 왼쪽 아래 영지 버튼(TerritoryHud)을 누르면 열린다.
//
//   ┌ ‹ 영지 관리 ───────────────────────────────────── [젬 +] ┐
//   │ ┌ 소환소      Lv.2 ┐ ┌ 합성소      Lv.1 ┐ ┌ 장비제작소 Lv.1 ┐ │
//   │ │ ✓ Lv.1 1회 소환   │ │ ✓ Lv.1 스킬 2개   │ │ …               │ │
//   │ │ ✓ Lv.2 10회 소환  │ │ ▸ Lv.2 스킬 3개   │ │                 │ │
//   │ │ ▸ Lv.3 …          │ │ 🔒 Lv.3 스킬 4개  │ │                 │ │
//   │ │ [업그레이드 1,500]│ │ [업그레이드 500]  │ │                 │ │
//   │ └───────────────────┘ └───────────────────┘ └─────────────────┘ │
//   │ (무기창고 · 훈련소 · 시공의 틈)                                  │
//
// 레벨마다 무엇이 열리는지는 FacilityUnlocks, 레벨과 값은 FacilityLevels·GameEconomy가 들고 있다 — 이 화면은 묻고 그리기만 한다.
// 레벨이 오르면 마을 건물도 그 자리에서 커진다(VillageBlockout이 FacilityLevels.Changed를 듣는다).
[DisallowMultipleComponent]
public class TerritoryUI : UiScreen
{
    private const int Columns = 3;
    private const float CardGap = UiTheme.ColumnGap;
    private const float RowHeight = 64f;
    private const float RowGap = UiTheme.Space2;
    private const float RowsTop = 92f;
    private const float GlyphSize = 30f;

    private class LevelRow
    {
        public Image Fill;
        public Image Border;
        public Image Glyph;
        public TMP_Text Level;
        public TMP_Text Feature;
    }

    private class FacilityCard
    {
        public Kind Kind;
        public UiKit.Surface Surface;
        public TMP_Text Name;
        public TMP_Text Level;
        public readonly List<LevelRow> Rows = new List<LevelRow>();
        public UiButton Upgrade;
    }

    private readonly List<FacilityCard> cards = new List<FacilityCard>();

    protected override string CanvasName => "TerritoryCanvas";
    // 마을 상단바(90)를 덮는다. 시설 화면끼리는 함께 열리지 않는다.
    protected override int SortingOrder => 92;
    protected override string Title => "영지 관리";
    protected override Currency[] HeaderCurrencies => new[] { GameEconomy.FacilityUpgradeCurrency };

    private void Awake()
    {
        EnsureBuilt();
        SetOpen(false);
    }

    private void OnEnable() => PlayerAccount.Changed += Refresh;
    private void OnDisable() => PlayerAccount.Changed -= Refresh;

    public override void Show()
    {
        EnsureBuilt();
        Refresh();
        SetOpen(true);
    }

    public override void Hide()
    {
        SetOpen(false);
    }

    // ---- 짓기 ---------------------------------------------------------------

    protected override void BuildContent(RectTransform root, Vector2 size)
    {
        cards.Clear();

        Kind[] kinds = FacilityUnlocks.Upgradeable;
        int rows = Mathf.CeilToInt(kinds.Length / (float)Columns);
        float width = (size.x - CardGap * (Columns - 1)) / Columns;
        float height = (size.y - CardGap * (rows - 1)) / rows;

        for (int i = 0; i < kinds.Length; i++)
        {
            float x = (i % Columns) * (width + CardGap);
            float y = (i / Columns) * (height + CardGap);
            cards.Add(BuildCard(root, kinds[i], x, y, width, height));
        }
    }

    private FacilityCard BuildCard(RectTransform root, Kind kind, float x, float y, float width, float height)
    {
        var card = new FacilityCard { Kind = kind };
        card.Surface = UiKit.Panel(root, "Facility_" + kind, UiTheme.SurfaceRaised, UiTheme.RadiusL, UiTheme.Border, 24);
        UiKit.TopLeft(card.Surface.Rect, x, y, width, height);

        RectTransform panel = card.Surface.Rect;
        float pad = UiTheme.Space5;

        // 레벨은 이름 줄 오른쪽 끝. 말줄임은 자리를 잡은 뒤에 건다(UiKit.Ellipsis).
        const float levelWidth = 120f;
        card.Level = UiKit.Text(panel, "Level", string.Empty, UiTheme.FontTitle, UiTheme.Primary, TextAlignmentOptions.Right);
        UiKit.TopRight(card.Level.rectTransform, pad, UiTheme.Space5, levelWidth, 52f);

        card.Name = UiKit.Text(panel, "Name", FacilityUnlocks.Name(kind), UiTheme.FontTitle, UiTheme.TextPrimary);
        UiKit.TopStretch(card.Name.rectTransform, UiTheme.Space5, 52f, pad, pad + levelWidth);
        UiKit.Ellipsis(card.Name);

        for (int level = 1; level <= FacilityLevels.MaxLevel; level++)
            card.Rows.Add(BuildRow(panel, level, RowsTop + (level - 1) * (RowHeight + RowGap), pad));

        card.Upgrade = UiButton.Create(panel, "Upgrade", "업그레이드", UiButtonStyle.Primary, UiButtonSize.Medium, () => AskUpgrade(kind));
        UiKit.BottomStretch(card.Upgrade.Rect, pad, UiTheme.ButtonMedium, pad, pad);
        return card;
    }

    private static LevelRow BuildRow(RectTransform panel, int level, float top, float pad)
    {
        var row = new LevelRow();
        UiKit.Surface surface = UiKit.Panel(panel, "Lv" + level, UiTheme.SurfaceSunken, UiTheme.RadiusM, UiTheme.Border);
        UiKit.TopStretch(surface.Rect, top, RowHeight, pad, pad);
        row.Fill = surface.Fill;
        row.Border = surface.Border;

        row.Glyph = UiKit.Glyph(surface.Rect, "State", UiSprites.Glyph.Lock, UiTheme.TextMuted);
        UiKit.LeftMiddle(row.Glyph.rectTransform, UiTheme.Space4, GlyphSize, GlyphSize);

        const float levelX = UiTheme.Space4 + GlyphSize + UiTheme.Space3;
        const float levelWidth = 72f;
        row.Level = UiKit.Text(surface.Rect, "Level", "Lv." + level, UiTheme.FontLabel, UiTheme.TextSecondary);
        UiKit.LeftMiddle(row.Level.rectTransform, levelX, levelWidth, RowHeight);

        row.Feature = UiKit.Text(surface.Rect, "Feature", string.Empty, UiTheme.FontBody, UiTheme.TextPrimary);
        UiKit.Fill(row.Feature.rectTransform, levelX + levelWidth, 0f, UiTheme.Space4, 0f);
        UiKit.Ellipsis(row.Feature);
        return row;
    }

    protected override void BuildOverlays() => Refresh();

    // ---- 올리기 ---------------------------------------------------------------

    private void AskUpgrade(Kind kind)
    {
        if (FacilityLevels.IsMaxed(kind)) return;

        int next = FacilityLevels.Get(kind) + 1;
        long cost = FacilityLevels.NextCost(kind);
        Currency currency = GameEconomy.FacilityUpgradeCurrency;

        // 모자라면 묻지 않고 바로 알린다. 버튼의 값은 이미 빨갛다.
        if (PlayerAccount.Balance(currency) < cost)
        {
            toast.Show($"젬이 부족합니다. ({UiKit.Amount(cost)} 필요)", UiToastKind.Warning);
            return;
        }

        confirm.Ask("시설 업그레이드",
            $"{FacilityUnlocks.Name(kind)} {UiTheme.Paint("Lv." + next, UiTheme.Primary)}\n" +
            $"{FacilityUnlocks.Feature(kind, next)}\n" +
            $"젬 {UiKit.Amount(cost)}",
            "업그레이드", false, () => Upgrade(kind));
    }

    private void Upgrade(Kind kind)
    {
        if (!FacilityLevels.TryUpgrade(kind, out string reason))
        {
            toast.Show(reason, UiToastKind.Warning);
            return;
        }

        int level = FacilityLevels.Get(kind);
        toast.Show($"{FacilityUnlocks.Name(kind)} Lv.{level} · {FacilityUnlocks.Feature(kind, level)}", UiToastKind.Success);
        Refresh();
    }

    // ---- 갱신 ---------------------------------------------------------------

    private void Refresh()
    {
        for (int i = 0; i < cards.Count; i++) Apply(cards[i]);
    }

    private static void Apply(FacilityCard card)
    {
        int level = FacilityLevels.Get(card.Kind);
        bool maxed = level >= FacilityLevels.MaxLevel;

        card.Level.text = "Lv." + level;

        for (int i = 0; i < card.Rows.Count; i++)
        {
            int rowLevel = i + 1;
            LevelRow row = card.Rows[i];
            bool reached = rowLevel <= level;
            bool next = rowLevel == level + 1;

            row.Feature.text = FacilityUnlocks.Feature(card.Kind, rowLevel);
            // 연 것은 체크, 바로 다음 것은 실행 색으로 띄우고, 그 뒤는 자물쇠로 흐리게.
            row.Glyph.sprite = UiSprites.Icon(reached ? UiSprites.Glyph.Check : UiSprites.Glyph.Lock);
            row.Glyph.color = reached ? UiTheme.Success : next ? UiTheme.Primary : UiTheme.TextMuted;
            row.Feature.color = reached ? UiTheme.TextPrimary : next ? UiTheme.Primary : UiTheme.TextMuted;
            row.Level.color = reached ? UiTheme.TextSecondary : UiTheme.TextMuted;
            row.Fill.color = reached ? UiTheme.SurfaceSunken : UiTheme.Surface;
            row.Border.color = next ? UiTheme.Primary : UiTheme.Border;
        }

        if (maxed)
        {
            card.Upgrade.ClearCost();
            card.Upgrade.SetLabel("최대 레벨");
            card.Upgrade.SetStyle(UiButtonStyle.Ghost);
            card.Upgrade.interactable = false;
            return;
        }

        card.Upgrade.SetLabel("업그레이드");
        card.Upgrade.SetStyle(UiButtonStyle.Primary);
        card.Upgrade.SetCost(GameEconomy.FacilityUpgradeCurrency, FacilityLevels.NextCost(card.Kind));
        card.Upgrade.interactable = true;
    }
}
