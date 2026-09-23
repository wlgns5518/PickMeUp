using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// 층 선택 — 시공의 틈에서 메인 던전에 들어가면 열린다. 도전할 층을 고르고, 보상과 파티를 확인한 뒤 출전한다.
//
//   ┌ ‹ 층 선택 ──────────────────────────────────────────────────┐
//   │ ┌ 탑 ───────────────────┬─┐  ┌ 선택한 층 ─────────────────┐│
//   │ │  ☁   ▢ [ 8층 ] ▢      │▒│  │ 7층                 도전    ││
//   │ │      ▣ [ 7층 ] ▣  ☁   │▒│  │ 클리어 보상 · 제작 재료     ││
//   │ │      ▣ [ 6층 ] ▣      │█│  │ 출전 파티     [칸][칸][칸]… ││  ← 칸 줄은 판 가운데
//   │ │ ▁▁▁▁▁▁  [ 문 ]  ▁▁▁▁▁ │█│  │      [ 파티 변경 ]          ││
//   │ └───────────────────────┴─┘  │ [ 7층 출전 ]                ││
//
// 층은 탑처럼 1층이 아래, 위로 쌓인다(FloorTowerView). 위로 굴리면 위층, 아래로 굴리면 아래층이 보인다.
// 층은 자동으로 넘어가지 않는다. 여기서 직접 고른 뒤 전투 씬으로 들어가고, 전투가 끝나면 마을로 돌아온다.
// 흐름은 시공의 틈 → 메인 던전 → 여기. 편성은 마을 훈련소에서 미리 해 두고, 여기서 바꾸려면 "파티 변경"을 누른다.
//
// 고르는 것(층 줄)과 실행(출전)을 나눴다. 층을 누르면 오른쪽에 그 층의 보상과 출전 파티가 보이고, 출전 버튼을
// 눌러야 들어간다 — 층 줄을 누르는 순간 씬이 넘어가면 무엇을 얻는 층인지 볼 틈이 없다.
[DisallowMultipleComponent]
public class FloorSelectUI : UiScreen
{
    [Header("Open State")]
    [Tooltip("문을 누르지 않아도 처음부터 열려 있게 하려면 켠다.")]
    [SerializeField] private bool openOnStart;

    [Header("Back")]
    [Tooltip("뒤로가기로 돌아갈 던전 선택 화면. 비워두면 씬에서 찾는다.")]
    [SerializeField] private DungeonSelectUI dungeonSelect;

    [Header("Party")]
    [Tooltip("\"파티 변경\"으로 여는 파티 편성 화면. 비워두면 씬에서 찾는다.")]
    [SerializeField] private DeckBuildUI deckBuild;

    private const float LeftWidth = 1040f;
    // 보상 두 줄(골드·재료)이 들어가는 칸.
    private const float InfoTop = 164f;
    private const float InfoHeight = 128f;
    // 출전 파티 칸은 판이 허락하는 만큼 크게, 판 가운데에.
    private const float PartySlotMax = UiTheme.SlotMedium;
    private const float PartySlotGap = UiTheme.Space3;
    private const float ChangePartyWidth = 200f;

    private FloorTowerView tower;

    private TMP_Text detailFloor;
    private TMP_Text detailStatus;
    private TMP_Text detailInfo;
    private TMP_Text partyLabel;
    private readonly List<UiSlot> partySlots = new List<UiSlot>();
    private UiButton enterButton;

    private int selectedFloor = FloorProgress.FirstFloor;

    protected override string CanvasName => "FloorSelectCanvas";
    // 편성 화면(91)보다 위.
    protected override int SortingOrder => 95;
    protected override string Title => "메인 던전 — 층 선택";
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
        // 열 때는 지금 도전할 층(열린 층 중 가장 높은 층)을 고르고, 탑을 몇 층 아래에서부터 그 층까지 올려 보인다.
        selectedFloor = FloorProgress.HighestUnlocked;
        Refresh();
        SetOpen(true);
        tower.ShowFloor(selectedFloor, rise: true);
    }

    public override void Hide()
    {
        SetOpen(false);
    }

    // 뒤로가기는 앞 단계(던전 선택)로.
    protected override void OnBack()
    {
        Hide();
        if (dungeonSelect == null) dungeonSelect = FindAnyObjectByType<DungeonSelectUI>(FindObjectsInactive.Include);
        if (dungeonSelect != null) dungeonSelect.Show();
    }

    // 여기서 파티를 다시 짜고 싶을 때. 편성을 마치고 뒤로가기를 누르면 이 화면으로 돌아온다.
    private void ChangeParty()
    {
        if (deckBuild == null) deckBuild = FindAnyObjectByType<DeckBuildUI>(FindObjectsInactive.Include);
        if (deckBuild == null)
        {
            toast.Show("파티 편성 화면을 찾지 못했습니다.", UiToastKind.Danger);
            return;
        }

        Hide();
        deckBuild.ShowFrom(this);
    }

    private void HandleDataChanged()
    {
        if (IsOpen) Refresh();
    }

    // ---- 짓기 ---------------------------------------------------------------

    protected override void BuildContent(RectTransform root, Vector2 size)
    {
        partySlots.Clear();

        tower = FloorTowerView.Create(root, SelectFloor);
        UiKit.Column(tower.Panel, 0f, LeftWidth);

        BuildDetail(root, size);
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
        UiKit.TopStretch(info.Rect, InfoTop, InfoHeight, pad, pad);
        detailInfo = UiKit.Wrap(UiKit.Text(info.Rect, "Text", string.Empty, UiTheme.FontBody, UiTheme.TextPrimary));
        detailInfo.alignment = TextAlignmentOptions.MidlineLeft;
        detailInfo.lineSpacing = 12f;
        UiKit.Fill(detailInfo.rectTransform, pad, UiTheme.Space3, pad, UiTheme.Space3);

        // 출전 파티 — 누구를 데리고 들어가는지 출전 직전에 한 번 더 보여 준다.
        float partyTop = InfoTop + InfoHeight + UiTheme.Space6;
        TMP_Text partyTitle = UiKit.SectionTitle(panel.Rect, "PartyTitle", "출전 파티");
        UiKit.TopStretch(partyTitle.rectTransform, partyTop, 48f, pad, 260f);
        partyLabel = UiKit.Text(panel.Rect, "PartyLabel", string.Empty, UiTheme.FontLabel, UiTheme.TextSecondary, TextAlignmentOptions.Right);
        UiKit.TopStretch(partyLabel.rectTransform, partyTop, 48f, 260f, pad);

        // 칸 줄은 판 가운데를 기준으로 놓는다 — 화면 비율이 바뀌어 판 폭이 달라져도 가운데에 남는다.
        int capacity = Mathf.Max(1, PartyDeck.Capacity);
        float slotSize = Mathf.Min(PartySlotMax, (width - pad * 2f - (capacity - 1) * PartySlotGap) / capacity);
        float rowWidth = capacity * slotSize + (capacity - 1) * PartySlotGap;
        float slotTop = partyTop + 60f;
        for (int i = 0; i < capacity; i++)
        {
            UiSlot slot = UiSlot.Create(panel.Rect, "Party_" + i, slotSize);
            float offsetX = -rowWidth * 0.5f + slotSize * 0.5f + i * (slotSize + PartySlotGap);
            UiKit.TopCenter(slot.Rect, offsetX, slotTop, slotSize, slotSize);
            slot.SetInteractable(false);
            partySlots.Add(slot);
        }

        UiButton change = UiButton.Create(panel.Rect, "ChangeParty", "파티 변경", UiButtonStyle.Secondary, UiButtonSize.Small, ChangeParty);
        UiKit.TopCenter(change.Rect, 0f, slotTop + slotSize + UiTheme.Space5, ChangePartyWidth, UiTheme.ButtonSmall);

        enterButton = UiButton.Create(panel.Rect, "Enter", "출전", UiButtonStyle.Primary, UiButtonSize.Large, EnterSelected);
        UiKit.BottomStretch(enterButton.Rect, pad, UiTheme.ButtonLarge, pad, pad);
    }

    protected override void BuildOverlays() => Refresh();

    // ---- 고르기와 출전 ---------------------------------------------------------

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
        if (tower == null || enterButton == null) return;

        tower.Refresh(selectedFloor);
        RefreshDetail();
    }

    private void RefreshDetail()
    {
        int floor = selectedFloor;
        bool cleared = floor <= FloorProgress.HighestCleared;

        detailFloor.text = floor + "층";
        detailStatus.text = cleared ? "클리어" : "도전";
        detailStatus.color = cleared ? UiTheme.Success : UiTheme.Primary;

        // 나올 수 있는 재료 등급을 범위로 — "E ~ D등급". 위 등급은 드물게 나오지만 무엇이 나오는지는 바로 보인다.
        EquipmentGrade low = MaterialDrops.BaseGrade(floor);
        EquipmentGrade high = MaterialDrops.HighestGrade(floor);
        string grades = high > low ? $"{PaintGrade(low)} ~ {PaintGrade(high)}등급" : PaintGrade(low) + "등급";
        detailInfo.text =
            $"클리어 보상  골드 {UiTheme.Paint(UiKit.Amount(GameEconomy.FloorClearGold(floor)), UiTheme.Primary)}\n" +
            $"제작 재료  {grades}";

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

    private static string PaintGrade(EquipmentGrade grade) =>
        UiTheme.Paint(EquipmentGradeNames.NameOf(grade), UiTheme.GradeColor(grade));
}
