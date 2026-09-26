using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 시공의 틈 — 어느 던전으로 들어갈지 고른다. 마을의 시공의 틈(FacilityGate)을 누르면 열린다.
//
//   ┌ ‹ 시공의 틈 ─────────────────────────────────────────────────┐
//   │ ┌ 메인 던전 ──┐ ┌ 요일 던전 ──┐ ┌ 탐험 던전 ──┐              │
//   │ │ 최고 12층    │ │  [자물쇠]    │ │  [자물쇠]    │              │
//   │ │ 다음 13층    │ │ 10층 클리어  │ │ 20층 클리어  │              │
//   │ │ [ 입장 ]     │ │ [ 잠김 ]     │ │ [ 잠김 ]     │              │
//
// 해금 조건은 DungeonCatalog가 들고 있고, 그 조건은 메인 던전 진행도(FloorProgress)와 시공의 틈 레벨을 본다 —
// 이 화면은 지금 열렸는지 묻고 그리기만 한다.
//
// 메인 던전으로 들어가면 지금 짜 둔 파티 그대로 층 선택(FloorSelectUI)으로 간다 — 편성은 마을의 훈련소에서 한다.
// 요일·탐험 던전은 자리와 해금만 있고 안에 들어갈 내용은 아직 없다 — 열린 뒤에 누르면 준비 중이라고 알린다.
[DisallowMultipleComponent]
public class DungeonSelectUI : UiScreen
{
    [Header("Main Dungeon")]
    [Tooltip("메인 던전으로 들어갈 때 여는 층 선택 화면. 비워두면 씬에서 찾는다.")]
    [SerializeField] private FloorSelectUI floorSelect;

    [Header("Open State")]
    [Tooltip("시공의 틈을 누르지 않아도 처음부터 열려 있게 하려면 켠다.")]
    [SerializeField] private bool openOnStart;

    private const float CardGap = UiTheme.ColumnGap;
    private const float LockGlyphSize = 96f;

    private class DungeonCard
    {
        public DungeonKind Kind;
        public UiKit.Surface Surface;
        public TMP_Text Name;
        public TMP_Text Summary;
        public TMP_Text State;
        public Image Lock;
        public TMP_Text Condition;
        public UiButton Enter;
    }

    private readonly List<DungeonCard> cards = new List<DungeonCard>();

    protected override string CanvasName => "DungeonSelectCanvas";
    // 마을 상단바(90)를 덮는다. 시설 화면끼리는 함께 열리지 않는다 — 다음 단계로 넘어갈 때 이 화면은 닫는다.
    protected override int SortingOrder => 92;
    protected override string Title => "시공의 틈";
    protected override Currency[] HeaderCurrencies => new Currency[0];

    private void Awake()
    {
        EnsureBuilt();
        SetOpen(openOnStart);
    }

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

        float cardWidth = (size.x - CardGap * (DungeonCatalog.All.Length - 1)) / DungeonCatalog.All.Length;
        for (int i = 0; i < DungeonCatalog.All.Length; i++)
            cards.Add(BuildCard(root, DungeonCatalog.All[i], i * (cardWidth + CardGap), cardWidth, size.y));

    }

    // 칸은 내용 칸을 세로로 다 채운다. 적는 말이 몇 줄 안 되므로 가운데 칸의 내용은 위가 아니라 가운데에 둔다 —
    // 위로 몰아 두면 칸 아래가 통째로 빈 상자로 남는다.
    private DungeonCard BuildCard(RectTransform root, DungeonKind kind, float x, float width, float height)
    {
        var card = new DungeonCard { Kind = kind };
        card.Surface = UiKit.Panel(root, "Dungeon_" + kind, UiTheme.SurfaceRaised, UiTheme.RadiusL, UiTheme.Border, 24);
        UiKit.TopLeft(card.Surface.Rect, x, 0f, width, height);
        card.Surface.Border.sprite = UiSprites.Outline(UiTheme.RadiusL, 3);

        RectTransform panel = card.Surface.Rect;
        float pad = UiTheme.Space5;

        // 말줄임은 자리를 잡은 뒤에 건다. 칸이 줄 높이보다 낮은 채로 걸면 이름이 통째로 사라진다(UiKit.Ellipsis).
        card.Name = UiKit.Text(panel, "Name", DungeonCatalog.Korean(kind), UiTheme.FontDisplay, UiTheme.TextPrimary);
        UiKit.TopStretch(card.Name.rectTransform, UiTheme.Space5, 72f, pad, pad);
        UiKit.Ellipsis(card.Name);

        card.Summary = UiKit.Wrap(UiKit.Text(panel, "Summary", DungeonCatalog.Summary(kind), UiTheme.FontLabel, UiTheme.TextSecondary));
        card.Summary.alignment = TextAlignmentOptions.TopLeft;
        card.Summary.lineSpacing = 6f;
        UiKit.TopStretch(card.Summary.rectTransform, 104f, 72f, pad, pad);

        // 가운데 — 열려 있으면 진행 상황, 잠겨 있으면 자물쇠와 조건. 칸은 세로로 다 채우되 이 상자는 내용만큼만
        // 두고 남는 자리의 가운데에 놓는다. 상자까지 늘리면 칸 안이 빈 상자 하나로 보인다.
        const float ConditionHeight = 100f;   // 조건 두 줄(시공의 틈 레벨 · 층)까지
        float bodyHeight = UiTheme.Space5 * 2f + LockGlyphSize + UiTheme.Space4 + ConditionHeight;
        float free = height - 188f - UiTheme.ButtonLarge - pad * 2f;

        UiKit.Surface body = UiKit.Panel(panel, "Body", UiTheme.SurfaceSunken, UiTheme.RadiusM);
        UiKit.TopStretch(body.Rect, 188f + Mathf.Max(0f, (free - bodyHeight) * 0.5f), bodyHeight, pad, pad);

        float lockTop = UiTheme.Space5;

        card.Lock = UiKit.Glyph(body.Rect, "Lock", UiSprites.Glyph.Lock, UiTheme.TextMuted);
        UiKit.TopCenter(card.Lock.rectTransform, 0f, lockTop, LockGlyphSize, LockGlyphSize);

        card.Condition = UiKit.Wrap(UiKit.Text(body.Rect, "Condition", string.Empty, UiTheme.FontBody, UiTheme.Warning,
            TextAlignmentOptions.Top));
        card.Condition.lineSpacing = 8f;
        UiKit.TopStretch(card.Condition.rectTransform, lockTop + LockGlyphSize + UiTheme.Space4, ConditionHeight, pad, pad);

        card.State = UiKit.Wrap(UiKit.Text(body.Rect, "State", string.Empty, UiTheme.FontBody, UiTheme.TextPrimary));
        card.State.alignment = TextAlignmentOptions.Left;
        card.State.lineSpacing = 12f;
        UiKit.Fill(card.State.rectTransform, pad, UiTheme.Space5, pad, UiTheme.Space5);

        card.Enter = UiButton.Create(panel, "Enter", "입장", UiButtonStyle.Primary, UiButtonSize.Large, () => Enter(kind));
        UiKit.BottomStretch(card.Enter.Rect, pad, UiTheme.ButtonLarge, pad, pad);
        return card;
    }

    protected override void BuildOverlays() => Refresh();

    // ---- 들어가기 -------------------------------------------------------------

    private void Enter(DungeonKind kind)
    {
        if (!DungeonCatalog.IsUnlocked(kind))
        {
            toast.Show(DungeonCatalog.UnlockText(kind), UiToastKind.Warning);
            return;
        }

        if (kind != DungeonKind.Main)
        {
            // 자리와 해금만 있고 안에 들어갈 내용은 아직 없다.
            toast.Show($"{DungeonCatalog.Korean(kind)}은 아직 준비 중입니다.", UiToastKind.Info);
            return;
        }

        // 편성은 마을 훈련소에서 미리 해 둔다. 여기서는 지금 고른 파티 그대로 층 선택으로 들어간다.
        if (floorSelect == null) floorSelect = FindAnyObjectByType<FloorSelectUI>(FindObjectsInactive.Include);
        if (floorSelect == null)
        {
            toast.Show("층 선택 화면을 찾지 못했습니다.", UiToastKind.Danger);
            return;
        }

        Hide();
        floorSelect.Show();
    }

    // ---- 갱신 ---------------------------------------------------------------

    private void Refresh()
    {
        for (int i = 0; i < cards.Count; i++) Apply(cards[i]);
    }

    private void Apply(DungeonCard card)
    {
        bool unlocked = DungeonCatalog.IsUnlocked(card.Kind);
        bool ready = card.Kind == DungeonKind.Main;

        card.Name.color = unlocked ? UiTheme.TextPrimary : UiTheme.TextMuted;
        card.Surface.Fill.color = unlocked ? UiTheme.SurfaceRaised : UiTheme.Surface;
        // 지금 들어갈 수 있는 던전만 실행 색 테두리로 띄운다.
        card.Surface.Border.color = unlocked && ready ? UiTheme.Primary : UiTheme.Border;

        card.Lock.enabled = !unlocked;
        card.Condition.gameObject.SetActive(!unlocked);
        card.State.gameObject.SetActive(unlocked);

        if (!unlocked)
        {
            card.Condition.text = UiTheme.Paint(DungeonCatalog.UnlockText(card.Kind), UiTheme.Warning);
            card.Enter.SetLabel("잠김");
            card.Enter.SetStyle(UiButtonStyle.Ghost);
            card.Enter.interactable = false;
            return;
        }

        card.State.text = StateText(card.Kind);
        card.Enter.SetLabel(ready ? "입장" : "준비 중");
        card.Enter.SetStyle(ready ? UiButtonStyle.Primary : UiButtonStyle.Secondary);
        card.Enter.interactable = true;
    }

    private static string StateText(DungeonKind kind)
    {
        switch (kind)
        {
            case DungeonKind.Main:
                int cleared = FloorProgress.HighestCleared;
                string clearedText = cleared > 0 ? $"{cleared}층" : "없음";
                return
                    $"클리어  {UiTheme.Paint(clearedText, UiTheme.Success)}\n" +
                    $"다음  {UiTheme.Paint(FloorProgress.HighestUnlocked + "층", UiTheme.Primary)}  /  {FloorProgress.LastFloor}층";

            case DungeonKind.Daily:
                return
                    $"오늘  {Weekday()}\n" +
                    UiTheme.Paint("준비 중", UiTheme.TextMuted);

            default:
                return
                    "전투 · 보물 · 사건 · 회복 · 보스\n" +
                    UiTheme.Paint("준비 중", UiTheme.TextMuted);
        }
    }

    private static string Weekday()
    {
        switch (System.DateTime.Now.DayOfWeek)
        {
            case System.DayOfWeek.Monday:    return "월요일";
            case System.DayOfWeek.Tuesday:   return "화요일";
            case System.DayOfWeek.Wednesday: return "수요일";
            case System.DayOfWeek.Thursday:  return "목요일";
            case System.DayOfWeek.Friday:    return "금요일";
            case System.DayOfWeek.Saturday:  return "토요일";
            default:                         return "일요일";
        }
    }
}
