using System.Collections.Generic;
using TMPro;
using UnityEngine;

// 파티 편성 — 이번 전투에 내보낼 파티를 짜고 출전한다. 마을의 시공의 틈(FacilityGate)을 누르면 열린다.
//
//   ┌ [ 1파티 | 2파티 | 3파티 ] ───────────┐   ┌ 보유 영웅 ──────────── 8명 ┐
//   ┌ 출전 파티 ────────────────── 3 / 5 ┐   │ [ 등급순 | 레벨순 ]         │
//   │ [1] [2] [3] [ ] [ ]                 │   │ [칸][칸][칸][칸][칸]        │
//   └─────────────────────────────────────┘   │                             │
//   ┌ 파티 정보 ──────────────────────────┐   │                             │
//   └─────────────────────────────────────┘   │                             │
//   ┌ 1파티 · 3명 출전 ─────── [ 출전하기 ] ┐   └─────────────────────────────┘
//
// 합성소·장비창과 같은 자리 배치다: 오른쪽 목록에서 골라 왼쪽에 넣고, 왼쪽 아래에서 실행한다.
// 넣고 빼는 길은 두 가지 — 누르거나(목록의 영웅을 누르면 빈 자리에, 자리의 영웅을 누르면 뺀다), 끌거나(목록에서
// 자리로, 자리끼리 끌면 순서가 바뀌고, 자리에서 목록이나 허공으로 끌면 뺀다). 출전 순서가 전장의 배치 순서다.
//
// 한 영웅은 한 파티에만 들어간다(PartyDeck). 다른 파티 영웅은 목록에서 흐리게 "n파티"로 보이고, 누르면 이유를 알려 준다.
// 출전하기를 누르면 이 화면을 닫고 층 선택(FloorSelectUI)으로 넘어간다.
[DisallowMultipleComponent]
public class DeckBuildUI : UiScreen, ICardDragHost
{
    [Header("Roster")]
    [Tooltip("보유 캐릭터 전원의 명단. 실제 보유 목록은 런타임(OwnedRoster)이 들고, 이건 씬에 부트스트랩이 없을 때의 대비책이다.")]
    [SerializeField] private CharacterRosterSO roster;

    [Header("Deck")]
    [Tooltip("한 번에 출전할 수 있는 인원. 출전 슬롯 개수이기도 하다.")]
    [SerializeField, Min(1)] private int deckCapacity = 5;

    [Header("Depart")]
    [Tooltip("편성을 마치고 층을 고를 화면. 비워두면 씬에서 찾는다.")]
    [SerializeField] private FloorSelectUI floorSelect;

    private const float LeftWidth = 1040f;
    private const float PartySlotSize = 164f;
    private const float PartyPanelHeader = 60f;
    private const float ActionBarHeight = 136f;
    private const float DepartWidth = 380f;

    private static readonly string[] SortTabs = { "등급순", "레벨순" };

    private UiTabs partyTabs;
    private TMP_Text partyCondition;
    private readonly List<UiTile> partyTiles = new List<UiTile>();
    private TMP_Text infoStats;
    private TMP_Text actionTitle;
    private TMP_Text actionNote;
    private UiButton departButton;
    private UiPickerPanel picker;

    private RectTransform dragLayer;
    // 끌고 다니는 칸 하나. 드래그마다 주인만 바꿔 칠한다.
    private UiSlot dragGhost;

    private readonly List<CharacterSO> sorted = new List<CharacterSO>();

    protected override string CanvasName => "DeckBuildCanvas";
    protected override int SortingOrder => 91;
    protected override string Title => "파티 편성";
    protected override string Subtitle => "출전할 영웅을 골라 파티를 짜고 층을 골라 출전합니다. 출전 순서대로 전장에 배치됩니다.";
    protected override Currency[] HeaderCurrencies => new Currency[0];

    private void Awake()
    {
        // 로스터 에셋은 시작 명단이다. Seed는 이미 있는 캐릭터를 건너뛰므로 두 번 불려도 결과가 같고,
        // 여기서만 얹으므로 합성으로 사라진 시작 멤버가 되살아나지 않는다.
        if (roster != null) OwnedRoster.Seed(roster.Members);

        PartyDeck.SetCapacity(deckCapacity);
        EnsureBuilt();
        SetOpen(false);
    }

    private void OnEnable()
    {
        PartyDeck.Changed += HandleDataChanged;
        OwnedRoster.Changed += HandleDataChanged;
    }

    private void OnDisable()
    {
        PartyDeck.Changed -= HandleDataChanged;
        OwnedRoster.Changed -= HandleDataChanged;
    }

    // 전투에서 돌아오면 편성에 쓰러진 영웅이 남아 있을 수 있다.
    private void Start()
    {
        PartyDeck.PruneFallen();
    }

    public override void Show()
    {
        EnsureBuilt();
        Refresh();
        picker.Grid.ScrollToTop();
        SetOpen(true);
    }

    public override void Hide()
    {
        SetOpen(false);
    }

    private void HandleDataChanged()
    {
        if (IsOpen) Refresh();
    }

    // ---- 짓기 ---------------------------------------------------------------

    protected override void BuildContent(RectTransform root, Vector2 size)
    {
        partyTiles.Clear();

        string[] tabLabels = new string[PartyDeck.PartyCount];
        for (int i = 0; i < tabLabels.Length; i++) tabLabels[i] = (i + 1) + "파티";
        partyTabs = UiTabs.Create(root, "PartyTabs", tabLabels);
        UiKit.TopLeft(partyTabs.Rect, 0f, 0f, LeftWidth, UiTheme.TabHeight);
        partyTabs.Changed += PartyDeck.SetActive;

        float y = UiTheme.TabHeight + UiTheme.Space4;
        float partyHeight = PartyPanelHeader + UiTile.HeightOf(PartySlotSize) + UiTheme.Space5;
        BuildPartyPanel(root, y, partyHeight);
        y += partyHeight + UiTheme.Space4;

        BuildInfoPanel(root, y);
        BuildActionBar(root);

        float pickerX = LeftWidth + UiTheme.ColumnGap;
        picker = UiPickerPanel.Create(root, "Heroes", size.x - pickerX, "보유 영웅", SortTabs, UiTheme.SlotMedium);
        UiKit.Fill(picker.Rect, pickerX, 0f, 0f, 0f);
        picker.Tabs.Changed += _ => Refresh();
        picker.Grid.TileClicked += tile => ToggleHero(tile.Payload as CharacterSO);
        // 목록 위에 놓으면 파티에서 뺀다. 칸 위에 놓아도 드롭 이벤트가 여기까지 올라온다.
        picker.gameObject.AddComponent<CardDropTarget>().Bind(this, CardDragSource.RosterSlot);
    }

    private void BuildPartyPanel(RectTransform root, float y, float height)
    {
        UiKit.Surface panel = UiKit.Panel(root, "Party", UiTheme.Surface, UiTheme.RadiusL, UiTheme.Border);
        UiKit.TopLeft(panel.Rect, 0f, y, LeftWidth, height);

        TMP_Text title = UiKit.SectionTitle(panel.Rect, "Title", "출전 파티");
        UiKit.TopStretch(title.rectTransform, 0f, PartyPanelHeader, UiTheme.Space5, 300f);
        partyCondition = UiKit.Text(panel.Rect, "Condition", string.Empty, UiTheme.FontBody, UiTheme.TextSecondary,
            TextAlignmentOptions.Right);
        UiKit.TopStretch(partyCondition.rectTransform, 0f, PartyPanelHeader, 300f, UiTheme.Space5);

        int capacity = Mathf.Max(1, deckCapacity);
        float step = (LeftWidth - UiTheme.Space5 * 2f - PartySlotSize) / Mathf.Max(1, capacity - 1);
        step = Mathf.Min(step, PartySlotSize + UiTheme.Space6);
        float rowWidth = PartySlotSize + step * (capacity - 1);
        float startX = (LeftWidth - rowWidth) * 0.5f;

        for (int i = 0; i < capacity; i++)
        {
            int index = i;
            UiTile tile = UiTile.Create(panel.Rect, "Slot_" + i, PartySlotSize);
            UiKit.TopLeft(tile.Rect, startX + i * step, PartyPanelHeader, PartySlotSize, UiTile.HeightOf(PartySlotSize));

            // 누르면 뺀다. 끌어서 순서를 바꾸거나 목록으로 돌려보낼 수도 있다.
            tile.Slot.Clicked += () => PartyDeck.RemoveAt(index);
            tile.Slot.gameObject.AddComponent<CardDropTarget>().Bind(this, index);
            tile.Slot.gameObject.AddComponent<CardDragSource>().Bind(this, null, index);
            partyTiles.Add(tile);
        }
    }

    private void BuildInfoPanel(RectTransform root, float y)
    {
        UiKit.Surface panel = UiKit.Panel(root, "Info", UiTheme.Surface, UiTheme.RadiusL, UiTheme.Border);
        panel.Rect.anchorMin = new Vector2(0f, 0f);
        panel.Rect.anchorMax = new Vector2(0f, 1f);
        panel.Rect.pivot = new Vector2(0f, 1f);
        panel.Rect.offsetMin = new Vector2(0f, ActionBarHeight + UiTheme.Space4);
        panel.Rect.offsetMax = new Vector2(LeftWidth, -y);

        TMP_Text title = UiKit.SectionTitle(panel.Rect, "Title", "파티 정보");
        UiKit.TopStretch(title.rectTransform, 0f, PartyPanelHeader, UiTheme.Space5, UiTheme.Space5);

        infoStats = UiKit.Wrap(UiKit.Text(panel.Rect, "Stats", string.Empty, UiTheme.FontBody, UiTheme.TextPrimary));
        infoStats.alignment = TextAlignmentOptions.TopLeft;
        infoStats.lineSpacing = 10f;
        UiKit.Fill(infoStats.rectTransform, UiTheme.Space5, PartyPanelHeader, LeftWidth * 0.5f, UiTheme.Space4);

        TMP_Text guide = UiKit.Wrap(UiKit.Text(panel.Rect, "Guide",
            "· 오른쪽 목록의 영웅을 누르면 빈 자리에 들어가고, 자리의 영웅을 누르면 빠집니다.\n" +
            "· 끌어다 놓을 수도 있습니다. 자리끼리 끌면 출전 순서가 바뀝니다.\n" +
            "· 한 영웅은 한 파티에만 들어갑니다. 쓰러진 영웅은 출전할 수 없습니다.",
            UiTheme.FontLabel, UiTheme.TextSecondary));
        guide.alignment = TextAlignmentOptions.TopLeft;
        guide.lineSpacing = 8f;
        UiKit.Fill(guide.rectTransform, LeftWidth * 0.5f, PartyPanelHeader, UiTheme.Space5, UiTheme.Space4);
    }

    private void BuildActionBar(RectTransform root)
    {
        UiKit.Surface bar = UiKit.Panel(root, "ActionBar", UiTheme.Surface, UiTheme.RadiusL, UiTheme.Border);
        UiKit.BottomLeft(bar.Rect, 0f, 0f, LeftWidth, ActionBarHeight);

        float textWidth = LeftWidth - DepartWidth - UiTheme.Space5 * 3f;
        actionTitle = UiKit.Text(bar.Rect, "Title", string.Empty, UiTheme.FontHeading, UiTheme.TextPrimary);
        UiKit.TopLeft(actionTitle.rectTransform, UiTheme.Space5, 26f, textWidth, 44f);
        actionNote = UiKit.Text(bar.Rect, "Note", string.Empty, UiTheme.FontLabel, UiTheme.TextSecondary);
        UiKit.TopLeft(actionNote.rectTransform, UiTheme.Space5, 74f, textWidth, 34f);

        departButton = UiButton.Create(bar.Rect, "Depart", "출전하기", UiButtonStyle.Primary, UiButtonSize.Large, Depart);
        UiKit.RightMiddle(departButton.Rect, UiTheme.Space5, DepartWidth, UiTheme.ButtonLarge);
    }

    protected override void BuildOverlays()
    {
        // 끌고 다니는 칸이 화면 밖으로 나가도 잘리지 않도록, 그리고 팝업보다도 위에 오도록 맨 위에 따로 층을 둔다.
        dragLayer = UiKit.Node(overlay, "DragLayer");
        UiKit.Fill(dragLayer);
        var group = dragLayer.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        Refresh();
    }

    // ---- 넣고 빼기 ------------------------------------------------------------

    // 목록에서 누르면 활성 파티의 빈 자리에 넣는다. 이미 이 파티에 있으면 뺀다.
    private void ToggleHero(CharacterSO hero)
    {
        if (hero == null) return;

        if (PartyDeck.Contains(hero))
        {
            PartyDeck.Remove(hero);
            return;
        }

        if (WarnIfUnavailable(hero)) return;

        if (PartyDeck.IsFull)
        {
            toast.Show($"파티는 {PartyDeck.Capacity}명까지입니다. 자리의 영웅을 눌러 먼저 빼세요.", UiToastKind.Info);
            return;
        }

        PartyDeck.Add(hero);
    }

    // 넣을 수 없는 영웅이면 이유를 띄우고 true(= 이 조작은 취소).
    private bool WarnIfUnavailable(CharacterSO hero)
    {
        if (PartyRoster.IsFallen(hero))
        {
            toast.Show($"{HeroLabel.Name(hero)}{HeroLabel.TopicParticle(HeroLabel.Name(hero))} 쓰러져 출전할 수 없습니다.", UiToastKind.Warning);
            return true;
        }

        if (PartyDeck.IsInOtherParty(hero))
        {
            int party = PartyDeck.PartyIndexOf(hero) + 1;
            toast.Show($"이미 {party}파티에 편성된 영웅입니다. {party}파티에서 먼저 빼세요.", UiToastKind.Warning);
            return true;
        }
        return false;
    }

    // 편성을 마치고 층 선택으로 넘어간다. 두 화면이 겹쳐 떠 있으면 어느 쪽을 만지는지 알 수 없어 이 화면은 닫는다.
    private void Depart()
    {
        if (PartyDeck.Count == 0)
        {
            toast.Show($"{PartyDeck.ActiveIndex + 1}파티에 출전할 영웅이 없습니다. 먼저 영웅을 편성해 주세요.", UiToastKind.Warning);
            return;
        }

        if (floorSelect == null) floorSelect = FindAnyObjectByType<FloorSelectUI>(FindObjectsInactive.Include);
        if (floorSelect == null)
        {
            toast.Show("층 선택 화면을 찾지 못했습니다.", UiToastKind.Danger);
            return;
        }

        Hide();
        floorSelect.Show();
    }

    // ---- 드래그 앤 드롭 --------------------------------------------------------

    public RectTransform CreateDragGhost(CharacterSO character)
    {
        if (dragLayer == null || character == null) return null;

        // 화면을 다시 지으면(도메인 리로드) 옛 층과 함께 사라진다. 그때만 새로 만든다.
        if (dragGhost == null)
        {
            dragGhost = UiSlot.Create(dragLayer, "DragGhost", UiTheme.SlotMedium);
            UiKit.Center(dragGhost.Rect, 0f, 0f, UiTheme.SlotMedium, UiTheme.SlotMedium);
            dragGhost.SetInteractable(false);
            dragGhost.gameObject.AddComponent<CanvasGroup>().alpha = 0.9f;
        }

        dragGhost.SetContent(UiSlotContents.Hero(character));
        dragGhost.SetSelected(true);
        dragGhost.gameObject.SetActive(true);
        dragGhost.Rect.SetAsLastSibling();
        return dragGhost.Rect;
    }

    public void ReleaseDragGhost(RectTransform ghost)
    {
        if (ghost != null) ghost.gameObject.SetActive(false);
    }

    public void HandleDrop(CardDragSource source, int slotIndex)
    {
        if (source == null || source.Character == null) return;

        // 출전 자리에 놓았다 — 새로 넣거나, 이미 이 파티에 있으면 그 자리로 옮긴다.
        if (slotIndex >= 0)
        {
            if (!PartyDeck.Contains(source.Character))
            {
                if (WarnIfUnavailable(source.Character)) return;
                if (PartyDeck.IsFull)
                {
                    toast.Show($"파티는 {PartyDeck.Capacity}명까지입니다. 자리의 영웅을 빼고 넣으세요.", UiToastKind.Info);
                    return;
                }
            }
            PartyDeck.PlaceAt(slotIndex, source.Character);
            return;
        }

        // 목록 위에 놓았다 — 자리에서 집어 온 영웅이면 뺀다.
        if (source.SlotIndex >= 0) PartyDeck.Remove(source.Character);
    }

    // 자리 밖 허공에 놓으면 뺀다. 목록으로 정확히 되돌려 놓게 만들 이유가 없다. 목록에서 집어 온 영웅이면 아무 일도 없다.
    public void HandleDropOutside(CardDragSource source)
    {
        if (source == null || source.SlotIndex < 0) return;
        PartyDeck.RemoveAt(source.SlotIndex);
    }

    // ---- 갱신 ---------------------------------------------------------------

    private void Refresh()
    {
        if (picker == null || partyTiles.Count == 0) return;

        RefreshTabs();
        RefreshParty();
        RefreshInfo();
        RefreshList();
    }

    private void RefreshTabs()
    {
        partyTabs.Select(PartyDeck.ActiveIndex, false);
        for (int i = 0; i < PartyDeck.PartyCount; i++)
        {
            int count = PartyDeck.CountOf(i);
            partyTabs.SetLabel(i, count > 0 ? $"{i + 1}파티 · {count}명" : $"{i + 1}파티");
        }
    }

    private void RefreshParty()
    {
        IReadOnlyList<CharacterSO> members = PartyDeck.Members;
        for (int i = 0; i < partyTiles.Count; i++)
        {
            UiTile tile = partyTiles[i];
            CharacterSO member = i < members.Count ? members[i] : null;
            tile.Slot.GetComponent<CardDragSource>().Bind(this, member, i);

            if (member == null)
            {
                tile.Slot.SetEmpty(i == members.Count ? "영웅 선택" : string.Empty);
                tile.Slot.SetSelected(false);
                tile.SetText(UiTheme.Paint($"{i + 1}번 자리", UiTheme.TextMuted), string.Empty);
                continue;
            }

            UiSlotContent content = UiSlotContents.Hero(member);
            content.Tag = (i + 1).ToString();
            content.TagColor = UiTheme.Selection;
            tile.Slot.SetContent(content);
            tile.Slot.SetSelected(false);
            tile.SetText(HeroLabel.Name(member), $"{i + 1}번 · Lv.{member.Level}");
        }

        bool any = members.Count > 0;
        partyCondition.text = $"{members.Count} / {PartyDeck.Capacity}";
        partyCondition.color = any ? UiTheme.Success : UiTheme.TextSecondary;
    }

    private void RefreshInfo()
    {
        IReadOnlyList<CharacterSO> members = PartyDeck.Members;
        int party = PartyDeck.ActiveIndex + 1;

        if (members.Count == 0)
        {
            infoStats.text = UiTheme.Paint("아직 편성된 영웅이 없습니다.", UiTheme.TextMuted);
        }
        else
        {
            int levels = 0, equipped = 0;
            var stars = new int[UiTheme.MaxTier + 1];
            for (int i = 0; i < members.Count; i++)
            {
                levels += members[i].Level;
                stars[UiTheme.TierOfStars(members[i].starCount)]++;
                if (EquipmentInventory.EquippedIn(members[i], EquipSlot.MainHand) != null ||
                    EquipmentInventory.EquippedIn(members[i], EquipSlot.OffHand) != null) equipped++;
            }

            var composition = new System.Text.StringBuilder();
            for (int s = UiTheme.MaxTier; s >= 1; s--)
            {
                if (stars[s] == 0) continue;
                if (composition.Length > 0) composition.Append("  ");
                composition.Append(UiTheme.Paint(UiKit.Stars(s), UiTheme.StarColor(s))).Append(" x").Append(stars[s]);
            }

            infoStats.text =
                $"출전 인원  {members.Count} / {PartyDeck.Capacity}\n" +
                $"평균 레벨  Lv.{Mathf.RoundToInt(levels / (float)members.Count)}\n" +
                $"등급 구성  {composition}\n" +
                $"제작 장비 장착  {equipped}명";
        }

        actionTitle.text = members.Count > 0 ? $"{party}파티 · {members.Count}명 출전" : $"{party}파티 · 편성 전";
        actionNote.text = members.Count > 0 ? "출전하기를 누르면 도전할 층을 고릅니다." : "영웅을 한 명 이상 넣어야 출전할 수 있습니다.";
        departButton.interactable = members.Count > 0;
    }

    private void RefreshList()
    {
        sorted.Clear();
        IReadOnlyList<CharacterSO> members = OwnedRoster.Members;
        for (int i = 0; i < members.Count; i++) if (members[i] != null) sorted.Add(members[i]);

        bool byLevel = picker.Tabs.Selected == 1;
        sorted.Sort((a, b) =>
        {
            // 쓰러진 영웅은 맨 뒤로. 그 안에서는 고른 기준대로.
            int byFallen = PartyRoster.IsFallen(a).CompareTo(PartyRoster.IsFallen(b));
            if (byFallen != 0) return byFallen;
            int primary = byLevel ? b.Level.CompareTo(a.Level) : b.starCount.CompareTo(a.starCount);
            if (primary != 0) return primary;
            int secondary = byLevel ? b.starCount.CompareTo(a.starCount) : b.Level.CompareTo(a.Level);
            return secondary != 0 ? secondary : string.CompareOrdinal(HeroLabel.Name(a), HeroLabel.Name(b));
        });

        picker.Count.text = $"{sorted.Count}명";
        picker.Grid.Show(sorted.Count, BindHero, "보유한 영웅이 없습니다.\n소환소에서 먼저 영웅을 소환해 주세요.");
    }

    private void BindHero(UiTile tile, int index)
    {
        CharacterSO hero = sorted[index];
        tile.Payload = hero;

        bool fallen = PartyRoster.IsFallen(hero);
        int order = PartyDeck.IndexOf(hero);
        int otherParty = order >= 0 ? -1 : PartyDeck.PartyIndexOf(hero);

        UiSlotContent content = UiSlotContents.Hero(hero);
        if (order >= 0) { content.Tag = (order + 1).ToString(); content.TagColor = UiTheme.Selection; }
        tile.Slot.SetContent(content);
        tile.Slot.SetSelected(order >= 0);
        // 다른 파티 영웅과 쓰러진 영웅은 흐리게. 누를 수는 있다 — 누르면 왜 안 되는지 알려 준다.
        tile.Slot.SetDimmed(fallen || otherParty >= 0, fallen ? "쓰러짐" : $"{otherParty + 1}파티");
        tile.SetText(HeroLabel.Name(hero), fallen ? "쓰러짐" : $"Lv.{hero.Level}", fallen ? (Color?)UiTheme.TextMuted : null);

        // 목록의 칸도 끌어서 자리에 놓을 수 있다. 세로로 끌면 목록이 구른다.
        CardDragSource drag = tile.Slot.GetComponent<CardDragSource>();
        if (drag == null)
        {
            drag = tile.Slot.gameObject.AddComponent<CardDragSource>();
            drag.SetScrollPassThrough(picker.Grid.Scroll);
        }
        drag.Bind(this, fallen ? null : hero, CardDragSource.RosterSlot);
    }
}
