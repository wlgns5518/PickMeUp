using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 층 선택 — 파티 편성에서 "출전하기"를 누르면 열린다. 도전할 층을 고르고, 보상과 파티를 확인한 뒤 출전한다.
//
//   ┌ ‹ 층 선택 ──────────────────────────────────────────────┐
//   │ [‹]  11 ~ 20층  [›]                 ┌ 선택한 층 ─────────┐│
//   │ [11층][12층][13층][14층][15층]       │ 12층  도전          ││
//   │ [16층][17층][18층][19층][20층]       │ 보상 · 전장         ││
//   │                                      │ 출전 파티 [칸]x5    ││
//   │                                      │ [ 12층 출전 ]       ││
//
// 층은 자동으로 넘어가지 않는다. 여기서 직접 고른 뒤 전투 씬으로 들어가고, 전투가 끝나면 마을로 돌아온다.
// 흐름은 시공의 틈 → 파티 편성 → 출전하기 → 여기. 뒤로가기는 마을이 아니라 파티 편성으로 돌아간다.
//
// 고르는 것(층 칸)과 실행(출전)을 나눴다. 층을 누르면 오른쪽에 그 층의 보상과 출전 파티가 보이고, 출전 버튼을
// 눌러야 들어간다 — 층 칸을 누르는 순간 씬이 넘어가면 무엇을 얻는 층인지 볼 틈이 없다.
[DisallowMultipleComponent]
public class FloorSelectUI : UiScreen
{
    [Header("Layout")]
    [Tooltip("한 쪽에 늘어놓을 층 칸 개수. 층이 100개라 한 화면에 다 깔지 않고 쪽으로 넘긴다.")]
    [SerializeField, Min(1)] private int floorsPerPage = 10;
    [Tooltip("한 쪽의 층 칸을 몇 칸씩 늘어놓을지. 10층에 5칸이면 두 줄이다.")]
    [SerializeField, Min(1)] private int pageColumns = 5;

    [Header("Open State")]
    [Tooltip("문을 누르지 않아도 처음부터 열려 있게 하려면 켠다.")]
    [SerializeField] private bool openOnStart;

    [Header("Back")]
    [Tooltip("뒤로가기로 돌아갈 파티 편성 화면. 비워두면 씬에서 찾는다.")]
    [SerializeField] private DeckBuildUI deckBuild;

    private const float LeftWidth = 1040f;
    private const float PagerHeight = 64f;
    private const float CardHeight = 170f;
    private const float CardGap = 16f;
    private const float PartySlotSize = UiTheme.SlotSmall;

    private class FloorCard
    {
        public Button Button;
        public Image Fill;
        public Image Border;
        public Image Ring;
        public TMP_Text Number;
        public TMP_Text Status;
        public Image Lock;
    }

    private readonly List<FloorCard> cards = new List<FloorCard>();
    private TMP_Text pageLabel;
    private UiButton previousPage;
    private UiButton nextPage;

    private TMP_Text detailFloor;
    private TMP_Text detailStatus;
    private TMP_Text detailInfo;
    private TMP_Text partyLabel;
    private readonly List<UiSlot> partySlots = new List<UiSlot>();
    private UiButton enterButton;

    // 지금 보고 있는 쪽(0부터)과 고른 층.
    private int page;
    private int selectedFloor = FloorProgress.FirstFloor;

    private int PageCount => Mathf.CeilToInt((FloorProgress.LastFloor - FloorProgress.FirstFloor + 1) / (float)floorsPerPage);

    // 이 쪽의 slot번째 칸이 가리키는 층.
    private int FloorAt(int slot) => FloorProgress.FirstFloor + page * floorsPerPage + slot;

    protected override string CanvasName => "FloorSelectCanvas";
    // 편성 화면(91)보다 위.
    protected override int SortingOrder => 95;
    protected override string Title => "층 선택";
    protected override string Subtitle => "도전할 층을 고르고 출전합니다. 이긴 층의 다음 층이 열립니다.";
    protected override Currency[] HeaderCurrencies => new Currency[0];

    private void Awake()
    {
        EnsureBuilt();
        SetOpen(openOnStart);
    }

    private void OnEnable() => PartyDeck.Changed += HandleDataChanged;
    private void OnDisable() => PartyDeck.Changed -= HandleDataChanged;

    // 전투에서 돌아왔을 때 해금 상태가 바뀌었을 수 있다. 열 때마다 다시 읽는다(Show).
    public override void Show()
    {
        EnsureBuilt();
        // 열 때는 지금 도전할 층(열린 층 중 가장 높은 층)을 고르고 그 층이 있는 쪽을 펼친다.
        // 1층 쪽부터 보여 주면 60층까지 온 플레이어가 매번 다섯 쪽을 넘겨야 한다.
        selectedFloor = FloorProgress.HighestUnlocked;
        page = Mathf.Clamp((selectedFloor - FloorProgress.FirstFloor) / floorsPerPage, 0, PageCount - 1);
        Refresh();
        SetOpen(true);
    }

    public override void Hide()
    {
        SetOpen(false);
    }

    // 뒤로가기는 앞 단계(파티 편성)로.
    protected override void OnBack()
    {
        Hide();
        if (deckBuild == null) deckBuild = FindAnyObjectByType<DeckBuildUI>(FindObjectsInactive.Include);
        if (deckBuild != null) deckBuild.Show();
    }

    private void HandleDataChanged()
    {
        if (IsOpen) Refresh();
    }

    // ---- 짓기 ---------------------------------------------------------------

    protected override void BuildContent(RectTransform root, Vector2 size)
    {
        cards.Clear();
        partySlots.Clear();

        BuildPager(root);
        BuildGrid(root);
        BuildDetail(root, size);
    }

    private void BuildPager(RectTransform root)
    {
        previousPage = UiButton.CreateIcon(root, "PreviousPage", UiSprites.Icon(UiSprites.Glyph.ChevronLeft), true, 56f,
            UiButtonStyle.Secondary, () => TurnPage(-1));
        UiKit.TopLeft(previousPage.Rect, 0f, (PagerHeight - 56f) * 0.5f, 56f, 56f);

        pageLabel = UiKit.Text(root, "PageLabel", string.Empty, UiTheme.FontTitle, UiTheme.TextPrimary, TextAlignmentOptions.Center);
        UiKit.TopLeft(pageLabel.rectTransform, 72f, 0f, 280f, PagerHeight);

        nextPage = UiButton.CreateIcon(root, "NextPage", UiSprites.Icon(UiSprites.Glyph.ChevronRight), true, 56f,
            UiButtonStyle.Secondary, () => TurnPage(1));
        UiKit.TopLeft(nextPage.Rect, 368f, (PagerHeight - 56f) * 0.5f, 56f, 56f);

        // 칸 색이 무엇을 뜻하는지. 칸마다 글자로도 적지만, 한눈에 훑을 때는 색이 먼저 읽힌다.
        TMP_Text legend = UiKit.Text(root, "Legend",
            $"{UiTheme.Paint("●", UiTheme.Primary)} 도전   {UiTheme.Paint("●", UiTheme.Success)} 클리어   {UiTheme.Paint("●", UiTheme.TextMuted)} 잠김",
            UiTheme.FontLabel, UiTheme.TextSecondary, TextAlignmentOptions.Right);
        UiKit.TopLeft(legend.rectTransform, LeftWidth - 460f, 0f, 460f, PagerHeight);
    }

    private void BuildGrid(RectTransform root)
    {
        int count = Mathf.Max(1, floorsPerPage);
        int columns = Mathf.Clamp(pageColumns, 1, count);
        int rows = Mathf.CeilToInt(count / (float)columns);
        float pad = UiTheme.Space5;
        float cardWidth = (LeftWidth - pad * 2f - (columns - 1) * CardGap) / columns;
        float gridHeight = pad * 2f + rows * CardHeight + (rows - 1) * CardGap;

        float top = PagerHeight + UiTheme.Space4;
        UiKit.Surface panel = UiKit.Panel(root, "Floors", UiTheme.Surface, UiTheme.RadiusL, UiTheme.Border);
        UiKit.TopLeft(panel.Rect, 0f, top, LeftWidth, gridHeight);

        for (int i = 0; i < count; i++)
        {
            int slot = i;
            float x = pad + (i % columns) * (cardWidth + CardGap);
            float y = pad + (i / columns) * (CardHeight + CardGap);
            cards.Add(BuildCard(panel.Rect, "Floor_" + i, x, y, cardWidth, () => SelectFloor(FloorAt(slot))));
        }

        TMP_Text note = UiKit.Wrap(UiKit.Text(root, "Note",
            "· 다섯 층마다 전장이 바뀝니다. 층이 높을수록 적이 많고 강해지며, 보상도 커집니다.\n" +
            "· 이긴 판에서만 골드와 제작 재료를 얻습니다. 재료 등급은 층 구간이 정합니다.",
            UiTheme.FontLabel, UiTheme.TextSecondary));
        note.alignment = TextAlignmentOptions.TopLeft;
        note.lineSpacing = 8f;
        UiKit.TopLeft(note.rectTransform, 4f, top + gridHeight + UiTheme.Space5, LeftWidth, 90f);
    }

    private FloorCard BuildCard(RectTransform parent, string name, float x, float y, float width,
        UnityEngine.Events.UnityAction onClick)
    {
        UiKit.Surface surface = UiKit.Panel(parent, name, UiTheme.SurfaceRaised, UiTheme.RadiusM, UiTheme.Border);
        UiKit.TopLeft(surface.Rect, x, y, width, CardHeight);
        surface.Fill.raycastTarget = true;
        surface.Border.sprite = UiSprites.Outline(UiTheme.RadiusM, 3);

        var card = new FloorCard { Fill = surface.Fill, Border = surface.Border };

        card.Number = UiKit.Text(surface.Rect, "Number", string.Empty, UiTheme.FontDisplay, UiTheme.TextPrimary, TextAlignmentOptions.Center);
        UiKit.TopStretch(card.Number.rectTransform, 28f, 70f);

        card.Status = UiKit.Text(surface.Rect, "Status", string.Empty, UiTheme.FontLabel, UiTheme.TextSecondary, TextAlignmentOptions.Center);
        UiKit.TopStretch(card.Status.rectTransform, 104f, 34f);

        card.Lock = UiKit.Glyph(surface.Rect, "Lock", UiSprites.Glyph.Lock, UiTheme.TextMuted);
        UiKit.TopRight(card.Lock.rectTransform, 12f, 12f, 30f, 30f);

        card.Ring = UiKit.Line(surface.Rect, "Selection", UiTheme.Selection, UiTheme.RadiusM + 5, (int)UiTheme.SelectionWidth);
        UiKit.Fill(card.Ring.rectTransform, -5f);

        card.Button = surface.Rect.gameObject.AddComponent<Button>();
        card.Button.targetGraphic = surface.Fill;
        card.Button.transition = Selectable.Transition.None;
        card.Button.onClick.AddListener(onClick);
        return card;
    }

    private void BuildDetail(RectTransform root, Vector2 size)
    {
        float x = LeftWidth + UiTheme.ColumnGap;
        float width = size.x - x;
        float pad = UiTheme.Space5;

        UiKit.Surface panel = UiKit.Panel(root, "Detail", UiTheme.SurfaceRaised, UiTheme.RadiusL, UiTheme.BorderStrong, 24);
        UiKit.Fill(panel.Rect, x, 0f, 0f, 0f);

        TMP_Text title = UiKit.SectionTitle(panel.Rect, "Title", "선택한 층");
        UiKit.TopStretch(title.rectTransform, 0f, 60f, pad, pad);

        detailFloor = UiKit.Text(panel.Rect, "Floor", string.Empty, UiTheme.FontDisplay + 24f, UiTheme.TextPrimary);
        UiKit.TopStretch(detailFloor.rectTransform, 60f, 90f, pad, 200f);
        detailStatus = UiKit.Text(panel.Rect, "Status", string.Empty, UiTheme.FontHeading, UiTheme.Primary, TextAlignmentOptions.Right);
        UiKit.TopStretch(detailStatus.rectTransform, 60f, 90f, 200f, pad);

        UiKit.Surface info = UiKit.Panel(panel.Rect, "Info", UiTheme.SurfaceSunken, UiTheme.RadiusM);
        UiKit.TopStretch(info.Rect, 164f, 176f, pad, pad);
        detailInfo = UiKit.Wrap(UiKit.Text(info.Rect, "Text", string.Empty, UiTheme.FontBody, UiTheme.TextPrimary));
        detailInfo.alignment = TextAlignmentOptions.MidlineLeft;
        detailInfo.lineSpacing = 12f;
        UiKit.Fill(detailInfo.rectTransform, pad, UiTheme.Space3, pad, UiTheme.Space3);

        // 출전 파티 — 누구를 데리고 들어가는지 출전 직전에 한 번 더 보여 준다.
        float partyTop = 164f + 176f + UiTheme.Space5;
        TMP_Text partyTitle = UiKit.SectionTitle(panel.Rect, "PartyTitle", "출전 파티");
        UiKit.TopStretch(partyTitle.rectTransform, partyTop, 48f, pad, 260f);
        partyLabel = UiKit.Text(panel.Rect, "PartyLabel", string.Empty, UiTheme.FontLabel, UiTheme.TextSecondary, TextAlignmentOptions.Right);
        UiKit.TopStretch(partyLabel.rectTransform, partyTop, 48f, 260f, pad);

        int capacity = Mathf.Max(1, PartyDeck.Capacity);
        float step = Mathf.Min(PartySlotSize + UiTheme.Space3, (width - pad * 2f - PartySlotSize) / Mathf.Max(1, capacity - 1));
        for (int i = 0; i < capacity; i++)
        {
            UiSlot slot = UiSlot.Create(panel.Rect, "Party_" + i, PartySlotSize);
            UiKit.TopLeft(slot.Rect, pad + i * step, partyTop + 56f, PartySlotSize, PartySlotSize);
            slot.SetInteractable(false);
            partySlots.Add(slot);
        }

        UiButton change = UiButton.Create(panel.Rect, "ChangeParty", "파티 변경", UiButtonStyle.Secondary, UiButtonSize.Small, OnBack);
        UiKit.TopRight(change.Rect, pad, partyTop + 56f + PartySlotSize + UiTheme.Space3, 180f, UiTheme.ButtonSmall);

        enterButton = UiButton.Create(panel.Rect, "Enter", "출전", UiButtonStyle.Primary, UiButtonSize.Large, EnterSelected);
        UiKit.BottomStretch(enterButton.Rect, pad, UiTheme.ButtonLarge, pad, pad);
    }

    protected override void BuildOverlays() => Refresh();

    // ---- 고르기와 출전 ---------------------------------------------------------

    private void TurnPage(int delta)
    {
        page = Mathf.Clamp(page + delta, 0, PageCount - 1);
        Refresh();
    }

    private void SelectFloor(int floor)
    {
        if (!FloorProgress.IsUnlocked(floor))
        {
            toast.Show($"{floor}층은 아직 잠겨 있습니다. {floor - 1}층을 먼저 이기세요.", UiToastKind.Info);
            return;
        }
        selectedFloor = floor;
        Refresh();
    }

    private void EnterSelected()
    {
        int floor = selectedFloor;

        // 편성이 비어 있으면 들여보내지 않는다. 비어 있으면 스포너가 씬에 박아 둔 명단으로 대신 싸우는데, 그건 전투 씬을
        // 직접 재생할 때의 대비책이다. 층 선택보다 먼저 확인해야 선택만 바뀐 채 남지 않는다.
        if (PartyDeck.Count == 0)
        {
            toast.Show($"{PartyDeck.ActiveIndex + 1}파티에 출전할 영웅이 없습니다. 먼저 영웅을 편성해 주세요.", UiToastKind.Warning);
            return;
        }

        if (!FloorProgress.TrySelect(floor))
        {
            toast.Show($"{floor}층은 아직 잠겨 있습니다.", UiToastKind.Warning);
            return;
        }

        // 다섯 층마다 전장 맵이 바뀐다(FloorProgress.BattleSceneName). 고른 층은 FloorProgress.SelectedFloor로 전달돼
        // 스포너가 그 값으로 적 수와 능력치를 키운다.
        string sceneName = FloorProgress.BattleSceneName(floor);
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[FloorSelectUI] 씬 '{sceneName}'을 불러올 수 없습니다. Build Settings에 등록됐는지 확인하세요.");
            toast.Show("전장을 불러올 수 없습니다.", UiToastKind.Danger);
            return;
        }

        SceneManager.LoadScene(sceneName);
    }

    // ---- 갱신 ---------------------------------------------------------------

    private void Refresh()
    {
        if (cards.Count == 0 || enterButton == null) return;

        page = Mathf.Clamp(page, 0, Mathf.Max(0, PageCount - 1));
        RefreshCards();
        RefreshDetail();
    }

    private void RefreshCards()
    {
        for (int i = 0; i < cards.Count; i++)
        {
            FloorCard card = cards[i];
            int floor = FloorAt(i);

            // 마지막 쪽이 덜 찼으면 남는 칸은 감춘다.
            bool exists = floor <= FloorProgress.LastFloor;
            card.Button.transform.gameObject.SetActive(exists);
            if (!exists) continue;

            bool unlocked = FloorProgress.IsUnlocked(floor);
            bool cleared = floor <= FloorProgress.HighestCleared;
            bool selected = floor == selectedFloor;

            card.Number.text = floor + "층";
            card.Number.color = unlocked ? UiTheme.TextPrimary : UiTheme.TextMuted;
            card.Status.text = !unlocked ? "잠김" : cleared ? "클리어" : "도전";
            card.Status.color = !unlocked ? UiTheme.TextMuted : cleared ? UiTheme.Success : UiTheme.Primary;
            card.Lock.enabled = !unlocked;

            card.Fill.color = !unlocked ? UiTheme.SurfaceSunken : selected ? UiTheme.SurfaceHover : UiTheme.SurfaceRaised;
            // 지금 도전할 층(열렸지만 아직 못 깬 층)은 실행 색 테두리로 — 어디로 가야 할지 보이게.
            card.Border.color = !unlocked ? UiTheme.Border : cleared ? UiTheme.WithAlpha(UiTheme.Success, 0.6f) : UiTheme.Primary;
            card.Ring.enabled = selected;
        }

        int first = FloorAt(0);
        int last = Mathf.Min(FloorAt(cards.Count - 1), FloorProgress.LastFloor);
        pageLabel.text = $"{first} ~ {last}층";
        previousPage.interactable = page > 0;
        nextPage.interactable = page < PageCount - 1;
    }

    private void RefreshDetail()
    {
        int floor = selectedFloor;
        bool cleared = floor <= FloorProgress.HighestCleared;

        detailFloor.text = floor + "층";
        detailStatus.text = cleared ? "클리어" : "도전";
        detailStatus.color = cleared ? UiTheme.Success : UiTheme.Primary;

        int stageFirst = FloorProgress.StageFirstFloor(floor);
        int stageLast = Mathf.Min(stageFirst + FloorProgress.FloorsPerStage - 1, FloorProgress.LastFloor);
        EquipmentGrade grade = MaterialDrops.BaseGrade(floor);
        string gradeText = UiTheme.Paint(EquipmentGradeNames.NameOf(grade) + "등급", UiTheme.GradeColor(grade));
        detailInfo.text =
            $"전장  {stageFirst} ~ {stageLast}층 구간\n" +
            $"클리어 보상  골드 {UiTheme.Paint(UiKit.Amount(GameEconomy.FloorClearGold(floor)), UiTheme.Primary)}\n" +
            $"제작 재료  {gradeText} 재료" +
            (grade < EquipmentGrade.S ? UiTheme.Paint("  (낮은 확률로 한 단계 위)", UiTheme.TextMuted) : string.Empty);

        IReadOnlyList<CharacterSO> members = PartyDeck.Members;
        for (int i = 0; i < partySlots.Count; i++)
        {
            if (i < members.Count) partySlots[i].SetContent(UiSlotContents.Hero(members[i]));
            else partySlots[i].SetEmpty(string.Empty);
        }
        partyLabel.text = $"{PartyDeck.ActiveIndex + 1}파티 · {members.Count} / {PartyDeck.Capacity}명";

        enterButton.SetLabel(floor + "층 출전");
        enterButton.interactable = members.Count > 0;
    }
}
