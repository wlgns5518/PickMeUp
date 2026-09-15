using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 마을 무기창고에서 여는 창.
//
// 왼쪽에서 영웅을 고르고, 오른쪽 창고 목록에서 장비를 누르면 그 영웅이 든다. 가운데는 고른 영웅이
// 지금 두 손에 무엇을 들었는지 보여 주고, 들린 제작 장비를 창고로 되돌리는 해제 버튼이 있다.
//
// 창고에 드는 것은 장비제작소가 만든 실물(EquipmentInventory)뿐이다. 캐릭터 에셋에 적힌 무기는
// "기본 장비"라 창고에 없고, 제작 장비를 해제하면 영웅은 다시 그것을 든다(CharacterLoadout).
// 다른 영웅이 든 장비를 누르면 그 손에서 가져온다 — 한 점은 한 사람 손에만 걸린다.
// 이미 고른 영웅이 든 장비를 다시 누르면 창고로 돌아간다.
//
// 쓰러진 영웅에게는 새로 들리지 못한다. 대신 들고 있던 것을 내려놓거나 다른 영웅이 가져가는 것은
// 막지 않는다 — 막으면 그 칼은 다시는 꺼낼 수 없다.
//
// 직업은 어디에도 적지 않는다. 플레이어는 초상화만 보고 누가 무엇을 하는 영웅인지 짐작한다.
// 에셋에 적힌 기본 장비의 이름도 같은 이유로 가린다 — "맨손 시전"이나 "훈련용 활"이라고 적는 순간
// 직업을 적은 것과 다름없다. 이름이 보이는 것은 플레이어가 직접 골라 들린 제작 장비뿐이다.
// 마법사가 장비를 거절하는 것처럼 행동으로 드러나는 것은 막지 않는다 — 그건 짐작의 단서다.
//
// 다른 시설 창과 같이 캔버스부터 코드에서 만든다.
[DisallowMultipleComponent]
public class ArmoryUI : FacilityWindow
{
    [Header("Roster")]
    [Tooltip("시작 명단. 실제 보유 목록은 런타임(OwnedRoster)이 들고, 이건 씬에 부트스트랩이 없을 때의 대비책이다.")]
    [SerializeField] private CharacterRosterSO roster;

    [Header("Open State")]
    [Tooltip("무기창고를 누르지 않아도 처음부터 열려 있게 하려면 켠다.")]
    [SerializeField] private bool openOnStart;

    [Header("Warning Banner")]
    [Tooltip("경고 배너가 넘지 않을 가로 길이. 모양은 킷의 장식 메시지 박스다.")]
    [SerializeField] private float bannerWidth = 900f;

    private enum Filter { All, MainHand, OffHand }

    private const float PanelWidth = 1560f;
    private const float PanelHeight = 900f;
    private static readonly Vector2 PanelPadding = new Vector2(36f, 30f);
    private const float TitleHeight = 60f;
    private const float TitleGap = 22f;
    private const float Gap = 14f;
    private const float ColumnGap = 28f;
    private const float LabelHeight = 34f;
    private const float RowGap = 8f;
    private const float RowInset = 16f;

    private const float HeroColumnWidth = 380f;
    private const float HeroRowHeight = 72f;

    private const float DetailColumnWidth = 500f;
    private const float PortraitSize = 150f;
    private const float SlotBoxHeight = 150f;
    private const float UnequipWidth = 110f;
    private const float UnequipHeight = 52f;
    private const float StatusHeight = 70f;
    private const float HintHeight = 90f;

    private const float FilterTabHeight = 52f;
    private const float ItemRowHeight = 68f;
    private const float GradeBadgeSize = 48f;
    private const float ItemStatusWidth = 160f;

    // 말줄임을 건 글자 칸이 최소한 가져야 할 높이(글자 크기 배수). RowText 주석 참조.
    private const float LineHeightRatio = 1.6f;

    private static readonly string[] FilterLabels = { "전체", "주무기", "방패" };

    private class HeroRow
    {
        public CharacterSO Character;
        public NeonButton Button;
    }

    // 가운데 칸의 손 하나.
    private class SlotView
    {
        public EquipSlot Slot;
        public TMP_Text Name;
        public TMP_Text Info;
        public GameObject UnequipButton;
    }

    private AnnouncementBanner warningBanner;

    private RectTransform panelRect;
    private RectTransform heroContent;
    private RectTransform itemContent;
    private float itemListWidth;

    private readonly List<HeroRow> heroRows = new List<HeroRow>();
    private readonly List<NeonButton> filterTabs = new List<NeonButton>();
    private readonly List<OwnedEquipment> visibleItems = new List<OwnedEquipment>();

    private Image portraitImage;
    private TMP_Text portraitInitial;
    private TMP_Text heroNameText;
    private TMP_Text heroInfoText;
    private SlotView mainSlot;
    private SlotView offSlot;
    private TMP_Text statusText;
    private TMP_Text inventoryLabel;

    private CharacterSO selectedHero;
    private Filter filter = Filter.All;

    protected override string CanvasName => "ArmoryCanvas";
    // 장비제작소(98) 다음.
    protected override int SortingOrder => 99;

    private void Awake()
    {
        // 로스터 에셋은 시작 명단이다. Seed는 이미 있는 캐릭터를 건너뛰므로 두 번 불려도 결과가 같다.
        if (roster != null) OwnedRoster.Seed(roster.Members);

        EnsureBuilt();
        SetOpen(openOnStart);
    }

    private void OnEnable()
    {
        OwnedRoster.Changed += HandleDataChanged;
        EquipmentInventory.Changed += HandleDataChanged;
    }

    private void OnDisable()
    {
        OwnedRoster.Changed -= HandleDataChanged;
        EquipmentInventory.Changed -= HandleDataChanged;
    }

    protected override void TickWindow(float deltaTime)
    {
        warningBanner?.Tick(deltaTime);
    }

    public override void Show()
    {
        EnsureBuilt();
        SetStatus(string.Empty, BattleHudPalette.TextMuted);
        RefreshAll();
        SetOpen(true);
    }

    public override void Hide()
    {
        SetOpen(false);
    }

    // 닫혀 있는 동안에는 다시 그리지 않는다. 열 때 Show가 통째로 새로 그린다.
    private void HandleDataChanged()
    {
        if (IsOpen) RefreshAll();
    }

    // ---- 고르기와 장착 ------------------------------------------------------

    private void SelectHero(CharacterSO hero)
    {
        selectedHero = hero;
        SetStatus(string.Empty, BattleHudPalette.TextMuted);
        RefreshHeroHighlight();
        RebuildItemRows();
        RefreshDetail();
    }

    private void SelectFilter(Filter newFilter)
    {
        filter = newFilter;
        RefreshFilterTabs();
        RebuildItemRows();
    }

    private void ClickItem(OwnedEquipment item)
    {
        if (item == null) return;

        if (selectedHero == null)
        {
            warningBanner?.Show("먼저 왼쪽에서 영웅을 고르세요.");
            return;
        }

        string heroName = HeroLabel.Name(selectedHero);

        // 이미 든 것을 다시 누르면 내려놓는다.
        if (item.IsHeldBy(selectedHero))
        {
            EquipmentInventory.Unequip(item);
            SetStatus($"{WithObject(item.DisplayName)} 창고에 넣었습니다.", BattleHudPalette.TextMuted);
            return;
        }

        if (PartyRoster.IsFallen(selectedHero))
        {
            warningBanner?.Show($"{WithTopic(heroName)} 쓰러져 새 장비를 들 수 없습니다.");
            return;
        }

        CharacterSO previousOwner = EquipmentInventory.FindOwner(item, OwnedRoster.Members);
        OwnedEquipment droppedShield = item.Slot == EquipSlot.MainHand && item.Weapon.IsTwoHanded
            ? EquipmentInventory.EquippedIn(selectedHero, EquipSlot.OffHand)
            : null;

        string reason;
        if (!EquipmentInventory.Equip(selectedHero, item, out reason))
        {
            warningBanner?.Show(reason);
            return;
        }

        string message = previousOwner != null && previousOwner != selectedHero
            ? $"{WithSubject(heroName)} {HeroLabel.Name(previousOwner)}에게서 {WithObject(item.DisplayName)} 넘겨받았습니다."
            : $"{WithSubject(heroName)} {WithObject(item.DisplayName)} 듭니다.";
        // 왜 방패를 내려놓았는지는 가운데 칸의 보조 손이 말해 준다. 여기서는 어디로 갔는지만.
        if (droppedShield != null)
            message += $"\n{WithTopic(droppedShield.DisplayName)} 창고로 돌아갔습니다.";

        SetStatus(message, BattleHudPalette.Accent);
    }

    private void UnequipSlot(EquipSlot slot)
    {
        if (selectedHero == null) return;

        OwnedEquipment item = EquipmentInventory.EquippedIn(selectedHero, slot);
        if (item == null) return;

        EquipmentInventory.Unequip(item);
        SetStatus($"{WithObject(item.DisplayName)} 창고에 넣었습니다.\n{WithTopic(HeroLabel.Name(selectedHero))} 기본 장비를 다시 듭니다.", BattleHudPalette.TextMuted);
    }

    // ---- 만들기 -------------------------------------------------------------

    protected override void BuildWindow()
    {
        heroRows.Clear();
        filterTabs.Clear();
        visibleItems.Clear();

        BuildCanvas();
        BuildPanel(BuildPopupRoot());

        warningBanner = AnnouncementBanner.Create(canvasRect, resolvedFont, null, bannerWidth);

        RefreshAll();
    }

    private void BuildPanel(RectTransform popup)
    {
        panelRect = HudFactory.CreatePanel(popup, "Panel").rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

        float contentWidth = PanelWidth - PanelPadding.x * 2f;
        float y = PanelPadding.y;

        BuildTitleBar(panelRect, "무기창고", PanelPadding.x, y, contentWidth, TitleHeight);
        y += TitleHeight + TitleGap;

        float columnHeight = PanelHeight - PanelPadding.y - y;
        float heroX = PanelPadding.x;
        float detailX = heroX + HeroColumnWidth + ColumnGap;
        float itemX = detailX + DetailColumnWidth + ColumnGap;
        float itemWidth = PanelPadding.x + contentWidth - itemX;

        BuildHeroColumn(heroX, y, columnHeight);
        BuildDetailColumn(detailX, y, columnHeight);
        BuildItemColumn(itemX, y, itemWidth, columnHeight);
    }

    private void BuildHeroColumn(float x, float y, float height)
    {
        TMP_Text label = HudFactory.CreateText(panelRect, "HeroLabel", resolvedFont, 24f, BattleHudPalette.TextMuted);
        label.alignment = TextAlignmentOptions.Left;
        HudFactory.SetTopLeft(label.rectTransform, new Vector2(HeroColumnWidth, LabelHeight), new Vector2(x, -y));
        label.text = "보유 영웅";

        float listTop = y + LabelHeight + RowGap;
        heroContent = BuildScrollList("Heroes", new Vector2(HeroColumnWidth, y + height - listTop), new Vector2(x, -listTop));
    }

    private void BuildDetailColumn(float x, float y, float height)
    {
        float bottom = y + height;

        // 초상화 테두리는 정사각 아이콘 판(icon_btn). 잘린 모서리가 초상화에 가리지 않게 안쪽을 넉넉히 비운다.
        NeonUISkin skin = NeonUISkin.Current;
        Image portraitFrame = HudFactory.CreateSkinnedImage(panelRect, "Portrait",
            skin != null ? skin.iconButton : null, BattleHudPalette.PortraitFrame);
        HudFactory.SetTopLeft(portraitFrame.rectTransform, new Vector2(PortraitSize, PortraitSize), new Vector2(x, -y));

        const float portraitInset = 12f;
        portraitImage = HudFactory.CreateImage(portraitFrame.rectTransform, "Image", Color.white);
        portraitImage.preserveAspect = true;
        HudFactory.Stretch(portraitImage.rectTransform);
        portraitImage.rectTransform.offsetMin = new Vector2(portraitInset, portraitInset);
        portraitImage.rectTransform.offsetMax = new Vector2(-portraitInset, -portraitInset);

        portraitInitial = HudFactory.CreateText(portraitFrame.rectTransform, "Initial", resolvedFont, 64f, BattleHudPalette.TextMuted);
        HudFactory.Stretch(portraitInitial.rectTransform);

        float textX = x + PortraitSize + 20f;
        float textWidth = DetailColumnWidth - PortraitSize - 20f;

        heroNameText = HudFactory.CreateText(panelRect, "HeroName", resolvedFont, 38f, BattleHudPalette.TextPrimary);
        heroNameText.alignment = TextAlignmentOptions.Left;
        heroNameText.overflowMode = TextOverflowModes.Ellipsis;
        HudFactory.SetTopLeft(heroNameText.rectTransform, new Vector2(textWidth, 38f * LineHeightRatio), new Vector2(textX, -(y + 4f)));

        heroInfoText = HudFactory.CreateText(panelRect, "HeroInfo", resolvedFont, 23f, BattleHudPalette.TextMuted);
        heroInfoText.alignment = TextAlignmentOptions.TopLeft;
        heroInfoText.textWrappingMode = TextWrappingModes.Normal;
        HudFactory.SetTopLeft(heroInfoText.rectTransform, new Vector2(textWidth, 80f), new Vector2(textX, -(y + 70f)));

        y += PortraitSize + Gap * 2f;

        mainSlot = BuildSlotBox(EquipSlot.MainHand, "주무기", x, y);
        y += SlotBoxHeight + Gap;

        offSlot = BuildSlotBox(EquipSlot.OffHand, "보조 손", x, y);
        y += SlotBoxHeight + Gap;

        statusText = HudFactory.CreateText(panelRect, "Status", resolvedFont, 23f, BattleHudPalette.TextMuted);
        statusText.alignment = TextAlignmentOptions.TopLeft;
        statusText.textWrappingMode = TextWrappingModes.Normal;
        HudFactory.SetTopLeft(statusText.rectTransform, new Vector2(DetailColumnWidth, StatusHeight), new Vector2(x, -y));

        TMP_Text hint = HudFactory.CreateText(panelRect, "Hint", resolvedFont, 21f, BattleHudPalette.TextMuted);
        hint.alignment = TextAlignmentOptions.BottomLeft;
        hint.textWrappingMode = TextWrappingModes.Normal;
        HudFactory.SetTopLeft(hint.rectTransform, new Vector2(DetailColumnWidth, HintHeight), new Vector2(x, -(bottom - HintHeight)));
        hint.text = "오른쪽 창고에서 장비를 누르면 고른 영웅이 듭니다. 이미 든 장비를 다시 누르거나 해제하면 " +
                    "창고로 돌아가고, 영웅은 원래 들고 있던 기본 장비를 다시 듭니다.";
    }

    private SlotView BuildSlotBox(EquipSlot slot, string title, float x, float y)
    {
        NeonUISkin skin = NeonUISkin.Current;
        Image frame = HudFactory.CreateSkinnedImage(panelRect, "Slot_" + slot,
            skin != null ? skin.buttonGhost : null, BattleHudPalette.PortraitFrame);
        HudFactory.SetTopLeft(frame.rectTransform, new Vector2(DetailColumnWidth, SlotBoxHeight), new Vector2(x, -y));
        RectTransform rect = frame.rectTransform;

        TMP_Text titleText = RowText(rect, "Title", 22f, BattleHudPalette.TextMuted, TextAlignmentOptions.Left, 14f, 28f, 20f, 20f);
        titleText.text = title;

        var view = new SlotView
        {
            Slot = slot,
            Name = RowText(rect, "Name", 32f, BattleHudPalette.TextPrimary, TextAlignmentOptions.Left, 46f, 46f, 20f, UnequipWidth + 36f),
            Info = RowText(rect, "Info", 22f, BattleHudPalette.TextMuted, TextAlignmentOptions.Left, 98f, 36f, 20f, 20f),
        };

        NeonButton unequip = HudFactory.CreateButton(rect, "Unequip", NeonButtonStyle.Secondary,
            resolvedFont, "해제", 24f, () => UnequipSlot(slot));
        RectTransform buttonRect = unequip.Rect;
        buttonRect.anchorMin = new Vector2(1f, 1f);
        buttonRect.anchorMax = new Vector2(1f, 1f);
        buttonRect.pivot = new Vector2(1f, 1f);
        buttonRect.sizeDelta = new Vector2(UnequipWidth, UnequipHeight);
        buttonRect.anchoredPosition = new Vector2(-20f, -44f);

        view.UnequipButton = unequip.gameObject;
        return view;
    }

    private void BuildItemColumn(float x, float y, float width, float height)
    {
        float bottom = y + height;

        inventoryLabel = HudFactory.CreateText(panelRect, "InventoryLabel", resolvedFont, 24f, BattleHudPalette.TextMuted);
        inventoryLabel.alignment = TextAlignmentOptions.Left;
        HudFactory.SetTopLeft(inventoryLabel.rectTransform, new Vector2(width, LabelHeight), new Vector2(x, -y));
        y += LabelHeight + RowGap;

        float tabWidth = (width - Gap * (FilterLabels.Length - 1)) / FilterLabels.Length;
        for (int i = 0; i < FilterLabels.Length; i++)
        {
            var thisFilter = (Filter)i;

            NeonButton tab = HudFactory.CreateButton(panelRect, "Filter_" + thisFilter, NeonButtonStyle.Ghost,
                resolvedFont, FilterLabels[i], 24f, () => SelectFilter(thisFilter));
            HudFactory.SetTopLeft(tab.Rect, new Vector2(tabWidth, FilterTabHeight), new Vector2(x + i * (tabWidth + Gap), -y));

            filterTabs.Add(tab);
        }
        y += FilterTabHeight + Gap;

        // 목록 너비는 여기서 기억해 둔다. Awake에서 막 만든 캔버스는 아직 레이아웃 전이라 rect가 0으로 읽힌다.
        itemListWidth = width;
        itemContent = BuildScrollList("Items", new Vector2(width, bottom - y), new Vector2(x, -y));
    }

    // 세로로만 구르는 목록 하나. 영웅이 소환으로 늘고 장비가 제작으로 늘어나므로 칸 수를 미리 정할 수 없다.
    private RectTransform BuildScrollList(string listName, Vector2 size, Vector2 offset)
    {
        Image viewport = HudFactory.CreateImage(panelRect, listName, BattleHudPalette.ListGround);
        // 줄 사이 빈 곳에서도 휠과 끌기를 받아야 한다.
        viewport.raycastTarget = true;
        HudFactory.SetTopLeft(viewport.rectTransform, size, offset);
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = HudFactory.CreateGroup(viewport.rectTransform, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport.rectTransform;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        return content;
    }

    // ---- 갱신 ---------------------------------------------------------------

    private void RefreshAll()
    {
        if (selectedHero != null && !OwnedRoster.Contains(selectedHero)) selectedHero = null;
        if (selectedHero == null && OwnedRoster.Count > 0) selectedHero = OwnedRoster.Members[0];

        RebuildHeroRows();
        RefreshFilterTabs();
        RebuildItemRows();
        RefreshDetail();
    }

    private void RebuildHeroRows()
    {
        if (heroContent == null) return;

        ClearRows(heroContent);
        heroRows.Clear();

        IReadOnlyList<CharacterSO> members = OwnedRoster.Members;
        if (members.Count == 0)
        {
            SetContentHeight(heroContent, EmptyRow(heroContent, "보유한 영웅이 없습니다.\n소환소에서 먼저 영웅을 뽑아주세요."));
            return;
        }

        float y = 0f;
        for (int i = 0; i < members.Count; i++)
        {
            CharacterSO hero = members[i];
            if (hero == null) continue;

            // 글자가 여러 줄 따로 깔리므로 버튼 자체의 글자는 두지 않는다.
            NeonButton row = HudFactory.CreateButton(heroContent, "Hero_" + i, NeonButtonStyle.Ghost,
                resolvedFont, null, 0f, () => SelectHero(hero));
            HudFactory.SetTopLeft(row.Rect, new Vector2(HeroColumnWidth, HeroRowHeight), new Vector2(0f, -y));

            RectTransform rect = row.Rect;
            bool fallen = PartyRoster.IsFallen(hero);

            TMP_Text nameText = RowText(rect, "Name", 26f, fallen ? BattleHudPalette.TextMuted : BattleHudPalette.TextPrimary,
                TextAlignmentOptions.Left, 8f, 34f, RowInset, 170f);
            nameText.text = HeroLabel.Name(hero);

            // 제작 장비를 든 영웅만 무기 이름이 뜬다. 기본 장비 이름은 직업을 드러낸다.
            OwnedEquipment crafted = EquipmentInventory.EquippedIn(hero, EquipSlot.MainHand);
            if (crafted != null)
            {
                TMP_Text weaponText = RowText(rect, "Weapon", 20f, EquipmentGradeNames.ColorOf(crafted.Grade),
                    TextAlignmentOptions.Right, 12f, 28f, 200f, RowInset);
                weaponText.text = crafted.DisplayName;
            }

            TMP_Text infoText = RowText(rect, "Info", 20f, fallen ? BattleHudPalette.Warn : BattleHudPalette.TextMuted,
                TextAlignmentOptions.Left, 42f, 26f, RowInset, RowInset);
            infoText.text = $"Lv.{hero.Level} · {hero.starCount}성" + (fallen ? " · 쓰러짐" : string.Empty);

            heroRows.Add(new HeroRow { Character = hero, Button = row });
            y += HeroRowHeight + RowGap;
        }

        SetContentHeight(heroContent, y - RowGap);
        RefreshHeroHighlight();
    }

    private void RefreshHeroHighlight()
    {
        // 고른 줄은 네온 테두리로. 밝은 판(Primary)을 깔면 줄 안의 밝은 글자와 등급색이 묻힌다.
        for (int i = 0; i < heroRows.Count; i++)
            heroRows[i].Button.SetStyle(heroRows[i].Character == selectedHero ? NeonButtonStyle.Secondary : NeonButtonStyle.Ghost);
    }

    private void RefreshFilterTabs()
    {
        for (int i = 0; i < filterTabs.Count; i++)
            filterTabs[i].SetStyle((Filter)i == filter ? NeonButtonStyle.Primary : NeonButtonStyle.Ghost);
    }

    private void RebuildItemRows()
    {
        if (itemContent == null) return;

        ClearRows(itemContent);

        IReadOnlyList<OwnedEquipment> items = EquipmentInventory.Items;
        int stored = 0;
        visibleItems.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            if (!items[i].IsEquipped) stored++;
            if (PassesFilter(items[i])) visibleItems.Add(items[i]);
        }
        visibleItems.Sort(CompareItems);

        inventoryLabel.text = $"제작한 장비 {items.Count}점 · 보관 중 {stored}점";

        if (visibleItems.Count == 0)
        {
            string message = items.Count == 0
                ? "제작한 장비가 없습니다.\n장비제작소에서 먼저 만들어 주세요."
                : "이 분류에 해당하는 장비가 없습니다.";
            SetContentHeight(itemContent, EmptyRow(itemContent, message));
            return;
        }

        float y = 0f;
        for (int i = 0; i < visibleItems.Count; i++)
        {
            BuildItemRow(visibleItems[i], itemListWidth, y, i);
            y += ItemRowHeight + RowGap;
        }

        SetContentHeight(itemContent, y - RowGap);
    }

    private void BuildItemRow(OwnedEquipment item, float width, float y, int index)
    {
        bool mine = item.IsHeldBy(selectedHero);
        bool other = item.IsEquipped && !mine;

        // 고른 영웅이 든 것은 네온 테두리, 다른 영웅이 든 것은 흐린 판(누르면 가져온다), 창고에 있는 것은 옅은 판.
        NeonButton row = HudFactory.CreateButton(itemContent, "Item_" + index,
            mine ? NeonButtonStyle.Secondary : other ? NeonButtonStyle.Muted : NeonButtonStyle.Ghost,
            resolvedFont, null, 0f, () => ClickItem(item));
        HudFactory.SetTopLeft(row.Rect, new Vector2(width, ItemRowHeight), new Vector2(0f, -y));

        RectTransform rect = row.Rect;

        NeonUISkin skin = NeonUISkin.Current;
        Image badge = HudFactory.CreateSkinnedImage(rect, "Grade", skin != null ? skin.iconButton : null, BattleHudPalette.GaugeBackground);
        HudFactory.SetTopLeft(badge.rectTransform, new Vector2(GradeBadgeSize, GradeBadgeSize),
            new Vector2(RowInset, -(ItemRowHeight - GradeBadgeSize) * 0.5f));

        TMP_Text gradeText = HudFactory.CreateText(badge.rectTransform, "Label", resolvedFont, 30f, EquipmentGradeNames.ColorOf(item.Grade));
        HudFactory.Stretch(gradeText.rectTransform);
        gradeText.text = EquipmentGradeNames.NameOf(item.Grade);

        float textLeft = RowInset + GradeBadgeSize + 14f;

        TMP_Text nameText = RowText(rect, "Name", 26f, other ? BattleHudPalette.TextMuted : BattleHudPalette.TextPrimary,
            TextAlignmentOptions.Left, 6f, 34f, textLeft, ItemStatusWidth + RowInset);
        nameText.text = item.DisplayName;

        TMP_Text typeText = RowText(rect, "Type", 20f, BattleHudPalette.TextMuted,
            TextAlignmentOptions.Left, 38f, 26f, textLeft, ItemStatusWidth + RowInset);
        typeText.text = $"{CharacterRules.Korean(item.Weapon.type)} · {EffectText(item)}";

        TMP_Text statusLabel = RowText(rect, "Status", 21f, mine ? BattleHudPalette.Accent : BattleHudPalette.TextMuted,
            TextAlignmentOptions.Right, 0f, ItemRowHeight, width - ItemStatusWidth - RowInset, RowInset);
        statusLabel.text = mine ? "장착 중" : other ? OwnerLabel(item) : "보관 중";
    }

    private void RefreshDetail()
    {
        if (heroNameText == null) return;

        if (selectedHero == null)
        {
            heroNameText.text = "영웅 없음";
            heroInfoText.text = "왼쪽에서 장비를 들 영웅을 고르세요.";
            portraitImage.enabled = false;
            portraitInitial.text = "?";
            ApplyEmpty(mainSlot);
            ApplyEmpty(offSlot);
            return;
        }

        heroNameText.text = HeroLabel.Name(selectedHero);

        string info = $"Lv.{selectedHero.Level} · {selectedHero.starCount}성";
        if (PartyRoster.IsFallen(selectedHero)) info += "\n<color=#F29E59>쓰러진 영웅 — 새 장비를 들 수 없습니다</color>";
        heroInfoText.text = info;

        bool hasPortrait = selectedHero.portrait != null;
        portraitImage.enabled = hasPortrait;
        portraitImage.sprite = selectedHero.portrait;
        portraitInitial.text = hasPortrait ? string.Empty : HeroLabel.Name(selectedHero).Substring(0, 1);

        ApplyMainHand(mainSlot, selectedHero);
        ApplyOffHand(offSlot, selectedHero);
    }

    private static void ApplyEmpty(SlotView view)
    {
        view.Name.text = "-";
        view.Name.color = BattleHudPalette.TextMuted;
        view.Info.text = string.Empty;
        view.UnequipButton.SetActive(false);
    }

    private static void ApplyMainHand(SlotView view, CharacterSO hero)
    {
        OwnedEquipment item = EquipmentInventory.EquippedIn(hero, EquipSlot.MainHand);
        if (item != null)
        {
            ApplyCrafted(view, item);
            return;
        }

        ApplyBase(view);
    }

    private static void ApplyOffHand(SlotView view, CharacterSO hero)
    {
        OwnedEquipment item = EquipmentInventory.EquippedIn(hero, EquipSlot.OffHand);
        if (item != null)
        {
            ApplyCrafted(view, item);
            return;
        }

        // 방패를 못 드는 이유가 직접 들린 제작 두손 무기일 때만 말한다. 기본 장비가 두손 무기라서
        // 못 드는 것이라면 그렇게 적는 순간 기본 장비가 무엇인지(활·창·맨손 시전) 드러난다.
        if (EquipmentInventory.EquippedIn(hero, EquipSlot.MainHand) != null && !CharacterLoadout.CanHoldShield(hero))
        {
            view.UnequipButton.SetActive(false);
            view.Name.text = "비어 있음";
            view.Name.color = BattleHudPalette.TextMuted;
            view.Info.text = "두손 무기를 들고 있어 방패를 들 수 없습니다";
            return;
        }

        ApplyBase(view);
    }

    // 에셋에 적힌 기본 장비. 무엇인지는 적지 않는다 — 기본 장비의 이름과 종류는 곧 직업이다.
    // 비어 있는 손도 똑같이 적는다. "비어 있음"과 "기본 방패"가 갈리면 그것만으로 방패를 든 직업이 드러난다.
    private static void ApplyBase(SlotView view)
    {
        view.UnequipButton.SetActive(false);
        view.Name.text = "기본 장비";
        view.Name.color = BattleHudPalette.TextPrimary;
        view.Info.text = string.Empty;
    }

    private static void ApplyCrafted(SlotView view, OwnedEquipment item)
    {
        view.Name.text = item.DisplayName;
        view.Name.color = EquipmentGradeNames.ColorOf(item.Grade);
        view.Info.text = $"{EquipmentGradeNames.NameOf(item.Grade)}등급 · {CharacterRules.Korean(item.Weapon.type)} · {EffectText(item)}";
        view.UnequipButton.SetActive(true);
    }

    private void SetStatus(string message, Color color)
    {
        if (statusText == null) return;

        statusText.text = message;
        statusText.color = color;
    }

    // ---- 도우미 -------------------------------------------------------------

    private bool PassesFilter(OwnedEquipment item)
    {
        switch (filter)
        {
            case Filter.MainHand: return item.Slot == EquipSlot.MainHand;
            case Filter.OffHand:  return item.Slot == EquipSlot.OffHand;
            default:              return true;
        }
    }

    // 좋은 것부터. 같은 등급이면 주무기가 방패보다 앞, 그 안에서는 종류와 이름 순.
    private static int CompareItems(OwnedEquipment a, OwnedEquipment b)
    {
        int byGrade = ((int)b.Grade).CompareTo((int)a.Grade);
        if (byGrade != 0) return byGrade;

        int bySlot = ((int)a.Slot).CompareTo((int)b.Slot);
        if (bySlot != 0) return bySlot;

        int byType = ((int)a.Weapon.type).CompareTo((int)b.Weapon.type);
        if (byType != 0) return byType;

        return string.CompareOrdinal(a.DisplayName, b.DisplayName);
    }

    // 등급이 무엇을 얼마나 올리는지. 주무기는 공격력, 방패는 피해 감소와 막기에 곱해진다(EquipmentGradeRules).
    // 기본 장비가 1이므로, 1보다 작으면 원래 들던 것보다 못하다는 뜻이다.
    private static string EffectText(OwnedEquipment item)
    {
        string stat = item.Slot == EquipSlot.OffHand ? "방어" : "공격";
        return $"{stat} x{item.Power:0.00}";
    }

    private static string OwnerLabel(OwnedEquipment item)
    {
        CharacterSO owner = EquipmentInventory.FindOwner(item, OwnedRoster.Members);
        return owner != null ? HeroLabel.Name(owner) + " 장착" : "다른 영웅 장착";
    }

    // "아엘리아이(가)" 대신 "아엘리아가". 영웅 이름도 무기 이름도 무엇이 올지 모른다.
    private static string WithSubject(string word) => word + HeroLabel.SubjectParticle(word);
    private static string WithObject(string word) => word + HeroLabel.ObjectParticle(word);
    private static string WithTopic(string word) => word + HeroLabel.TopicParticle(word);

    // 줄 안의 글자 한 칸. 줄의 왼쪽/오른쪽 여백과 위에서부터의 자리로 잡는다.
    //
    // 칸 높이가 글꼴의 줄 높이보다 낮으면 말줄임(Ellipsis)이 첫 줄부터 잘라내 글자가 통째로 사라진다.
    // NotoSansKR는 줄 높이가 글자 크기의 1.45배쯤이라 눈으로 맞춘 높이로는 곧잘 모자란다 —
    // 모자라면 같은 가운데를 두고 위아래로 넓힌다(정렬이 가운데 줄 기준이라 보이는 자리는 그대로다).
    private TMP_Text RowText(RectTransform parent, string textName, float size, Color color,
        TextAlignmentOptions alignment, float top, float height, float left, float right)
    {
        float minHeight = size * LineHeightRatio;
        if (height < minHeight)
        {
            top -= (minHeight - height) * 0.5f;
            height = minHeight;
        }

        TMP_Text text = HudFactory.CreateText(parent, textName, resolvedFont, size, color);
        text.alignment = alignment;
        text.overflowMode = TextOverflowModes.Ellipsis;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, -(top + height));
        rect.offsetMax = new Vector2(-right, -top);
        return text;
    }

    private float EmptyRow(RectTransform content, string message)
    {
        const float height = 120f;

        TMP_Text text = HudFactory.CreateText(content, "Empty", resolvedFont, 22f, BattleHudPalette.TextMuted);
        text.textWrappingMode = TextWrappingModes.Normal;
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(RowInset, -height);
        rect.offsetMax = new Vector2(-RowInset, 0f);
        text.text = message;
        return height;
    }

    // 목록을 다시 깔 때 옛 줄을 치운다. Destroy는 프레임 끝에 일어나므로, 그 사이 새 줄과 겹쳐
    // 클릭을 가로채지 않도록 먼저 꺼 둔다.
    private static void ClearRows(RectTransform content)
    {
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            GameObject row = content.GetChild(i).gameObject;
            row.SetActive(false);
            Destroy(row);
        }
    }

    private static void SetContentHeight(RectTransform content, float height)
    {
        content.sizeDelta = new Vector2(0f, Mathf.Max(0f, height));
    }
}
