using System.Collections.Generic;
using TMPro;
using UnityEngine;

// 장비창 — 영웅의 장비를 장착하고, 비교하고, 강화한다.
//
//   ┌ 영웅 ──┐ ┌ 선택한 영웅 ─────────────────┐ ┌ 보유 장비 ─────────── 12개 ┐
//   │ [칸][칸]│ │ [초상화]  이름 ★5 Lv.12       │ │ [ 전체 | 무기 | 방어구 ]    │
//   │ [칸][칸]│ │           공격 x1.40 방어 x1.00│ │ [칸][칸][칸][칸]            │
//   │         │ │ [무기] [방어구] [기타(잠김)]  │ │                             │
//
// 흐름은 장착 → 비교 → 강화다. 목록에서 장비를 누르면 상세 팝업이 뜨고, 같은 자리에 지금 든 것과 나란히
// 비교해 보여 준다. 거기서 장착하거나, 강화 팝업으로 넘어간다. 상세 정보는 전부 팝업이다 — 메인 화면에는
// 영웅과 칸과 목록만 둔다.
//
// 창고에 드는 것은 장비제작소가 만든 실물(EquipmentInventory)뿐이다. 캐릭터 에셋에 적힌 무기는 "기본 장비"라
// 창고에 없고, 제작 장비를 해제하면 영웅은 다시 그것을 든다(CharacterLoadout). 다른 영웅이 든 장비를 장착하면
// 그 손에서 가져온다 — 한 점은 한 사람 손에만 걸린다. 쓰러진 영웅은 새로 들지 못한다.
//
// 직업과 기본 장비의 이름은 어디에도 적지 않는다(hidden-job-design). 기본 장비는 "기본 장비"로만 보인다.
// 칸 이름도 손 이름이 아니라 무기 / 방어구 / 기타 장비다(방어구 칸 = 방패, 기타 장비는 아직 없는 기능이라 잠김).
[DisallowMultipleComponent]
public class ArmoryUI : UiScreen
{
    [Header("Roster")]
    [Tooltip("시작 명단. 실제 보유 목록은 런타임(OwnedRoster)이 들고, 이건 씬에 부트스트랩이 없을 때의 대비책이다.")]
    [SerializeField] private CharacterRosterSO roster;

    [Header("Open State")]
    [Tooltip("무기창고를 누르지 않아도 처음부터 열려 있게 하려면 켠다.")]
    [SerializeField] private bool openOnStart;

    private const float HeroColumnWidth = 300f;
    private const float CenterWidth = 720f;
    private const float PortraitSize = 280f;
    private const float EquipSlotSize = 168f;

    private static readonly string[] EquipmentTabs = { "전체", "무기", "방어구" };

    // ---- 메인 화면 ----
    private UiPickerPanel heroPicker;
    private UiPickerPanel itemPicker;
    private UiSlot portrait;
    private TMP_Text heroName;
    private TMP_Text heroInfo;
    private TMP_Text heroPower;
    private UiSlot weaponSlot;
    private UiSlot armorSlot;
    private UiSlot accessorySlot;
    private TMP_Text weaponName;
    private TMP_Text armorName;

    // ---- 상세·비교 팝업 ----
    private UiPopup detailPopup;
    private CompareCard currentCard;
    private CompareCard selectedCard;
    private TMP_Text compareArrow;
    private TMP_Text compareDelta;
    private UiButton equipButton;
    private UiButton enhanceFromDetailButton;

    // ---- 강화 팝업 ----
    private UiPopup enhancePopup;
    private UiSlot enhanceBefore;
    private UiSlot enhanceAfter;
    private TMP_Text enhanceBeforeText;
    private TMP_Text enhanceAfterText;
    private TMP_Text enhanceChances;
    private TMP_Text enhanceGold;
    private UiButton enhanceButton;

    private readonly List<CharacterSO> heroes = new List<CharacterSO>();
    private readonly List<OwnedEquipment> items = new List<OwnedEquipment>();

    private CharacterSO selectedHero;
    private OwnedEquipment detailItem;

    private class CompareCard
    {
        public RectTransform Root;
        public TMP_Text Title;
        public UiSlot Slot;
        public TMP_Text Name;
        public TMP_Text Lines;
    }

    protected override string CanvasName => "ArmoryCanvas";
    // 장비제작소(98) 다음.
    protected override int SortingOrder => 99;
    protected override string Title => "장비창";

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
        PlayerAccount.Changed += HandleDataChanged;
    }

    private void OnDisable()
    {
        OwnedRoster.Changed -= HandleDataChanged;
        EquipmentInventory.Changed -= HandleDataChanged;
        PlayerAccount.Changed -= HandleDataChanged;
    }

    public override void Show()
    {
        EnsureBuilt();
        Refresh();
        SetOpen(true);
    }

    public override void Hide()
    {
        if (enhancePopup != null) enhancePopup.Hide();
        if (detailPopup != null) detailPopup.Hide();
        SetOpen(false);
    }

    // 닫혀 있는 동안에는 다시 그리지 않는다. 열 때 Show가 통째로 새로 그린다.
    private void HandleDataChanged()
    {
        if (!IsOpen) return;

        // 강화 중 파괴되거나 합성으로 사라진 장비의 팝업은 닫는다.
        if (detailItem != null && !EquipmentInventory.Contains(detailItem))
        {
            detailItem = null;
            enhancePopup.Hide();
            detailPopup.Hide();
        }
        Refresh();
    }

    // ---- 짓기 ---------------------------------------------------------------

    protected override void BuildContent(RectTransform root, Vector2 size)
    {
        heroPicker = UiPickerPanel.Create(root, "Heroes", HeroColumnWidth, "영웅", null, UiTheme.SlotSmall);
        UiKit.Column(heroPicker.Rect, 0f, HeroColumnWidth);
        heroPicker.Grid.TileClicked += tile => SelectHero(tile.Payload as CharacterSO);

        float centerX = HeroColumnWidth + UiTheme.ColumnGap;
        BuildCenter(root, centerX);

        float itemX = centerX + CenterWidth + UiTheme.ColumnGap;
        itemPicker = UiPickerPanel.Create(root, "Items", size.x - itemX, "보유 장비", EquipmentTabs, UiTheme.SlotSmall + 16f);
        UiKit.Fill(itemPicker.Rect, itemX, 0f, 0f, 0f);
        itemPicker.Tabs.Changed += _ => RefreshItems();
        itemPicker.Grid.TileClicked += tile => OpenDetail(tile.Payload as OwnedEquipment);
    }

    private void BuildCenter(RectTransform root, float x)
    {
        UiKit.Surface panel = UiKit.Panel(root, "Hero", UiTheme.Surface, UiTheme.RadiusL, UiTheme.Border);
        UiKit.Column(panel.Rect, x, CenterWidth);
        float pad = UiTheme.Space5;

        TMP_Text title = UiKit.SectionTitle(panel.Rect, "Title", "선택한 영웅");
        UiKit.TopStretch(title.rectTransform, 0f, 64f, pad, pad);

        portrait = UiSlot.Create(panel.Rect, "Portrait", PortraitSize);
        UiKit.TopLeft(portrait.Rect, pad + 8f, 64f, PortraitSize, PortraitSize);
        portrait.SetInteractable(false);

        float textX = pad + 8f + PortraitSize + pad;
        float textWidth = CenterWidth - textX - pad;
        heroName = UiKit.Ellipsis(UiKit.Text(panel.Rect, "Name", string.Empty, UiTheme.FontDisplay, UiTheme.TextPrimary));
        UiKit.TopLeft(heroName.rectTransform, textX, 70f, textWidth, 70f);
        heroInfo = UiKit.Wrap(UiKit.Text(panel.Rect, "Info", string.Empty, UiTheme.FontBody, UiTheme.TextSecondary));
        heroInfo.alignment = TextAlignmentOptions.TopLeft;
        UiKit.TopLeft(heroInfo.rectTransform, textX, 144f, textWidth, 70f);

        // 장비가 전투에 곱하는 값의 합계. 무엇을 바꾸면 무엇이 달라지는지 한눈에.
        UiKit.Surface power = UiKit.Panel(panel.Rect, "Power", UiTheme.SurfaceSunken, UiTheme.RadiusM);
        UiKit.TopLeft(power.Rect, textX, 224f, textWidth, 116f);
        heroPower = UiKit.Wrap(UiKit.Text(power.Rect, "Text", string.Empty, UiTheme.FontBody, UiTheme.TextPrimary));
        heroPower.lineSpacing = 8f;
        UiKit.Fill(heroPower.rectTransform, UiTheme.Space4, UiTheme.Space3, UiTheme.Space4, UiTheme.Space3);

        // 장비 칸 셋.
        float slotsTop = 64f + PortraitSize + UiTheme.Space6;
        TMP_Text slotsTitle = UiKit.SectionTitle(panel.Rect, "SlotsTitle", "장비");
        UiKit.TopStretch(slotsTitle.rectTransform, slotsTop, 48f, pad, pad);

        float rowTop = slotsTop + 56f;
        float spacing = (CenterWidth - pad * 2f - EquipSlotSize * 3f) / 2f;
        weaponSlot = BuildEquipSlot(panel.Rect, "Weapon", "무기", pad, rowTop, out weaponName);
        armorSlot = BuildEquipSlot(panel.Rect, "Armor", "방어구", pad + EquipSlotSize + spacing, rowTop, out armorName);
        accessorySlot = BuildEquipSlot(panel.Rect, "Accessory", "기타 장비", pad + (EquipSlotSize + spacing) * 2f, rowTop, out TMP_Text accessoryName);
        accessoryName.text = UiTheme.Paint("준비 중", UiTheme.TextMuted);

        weaponSlot.Clicked += () => ClickEquipSlot(EquipSlot.MainHand);
        armorSlot.Clicked += () => ClickEquipSlot(EquipSlot.OffHand);

    }

    private UiSlot BuildEquipSlot(RectTransform parent, string name, string label, float x, float y, out TMP_Text itemName)
    {
        UiSlot slot = UiSlot.Create(parent, name, EquipSlotSize);
        UiKit.TopLeft(slot.Rect, x, y, EquipSlotSize, EquipSlotSize);

        TMP_Text title = UiKit.Text(parent, name + "Label", label, UiTheme.FontBody, UiTheme.TextSecondary, TextAlignmentOptions.Center);
        UiKit.TopLeft(title.rectTransform, x - 20f, y + EquipSlotSize + 6f, EquipSlotSize + 40f, 36f);

        itemName = UiKit.Ellipsis(UiKit.Text(parent, name + "Item", string.Empty, UiTheme.FontLabel, UiTheme.TextPrimary,
            TextAlignmentOptions.Center));
        UiKit.TopLeft(itemName.rectTransform, x - 20f, y + EquipSlotSize + 40f, EquipSlotSize + 40f, 34f);
        return slot;
    }

    protected override void BuildOverlays()
    {
        BuildDetailPopup();
        BuildEnhancePopup();
        Refresh();
    }

    // ---- 상세·비교 팝업 ---------------------------------------------------------

    private const float CardWidth = 440f;

    private void BuildDetailPopup()
    {
        detailPopup = UiPopup.Create(overlay, "Detail", "장비 상세", new Vector2(1140f, 740f));

        currentCard = BuildCompareCard(detailPopup.Body, "Current", 0f);
        selectedCard = BuildCompareCard(detailPopup.Body, "Selected", 1140f - UiPopup.Padding * 2f - CardWidth);

        compareArrow = UiKit.Text(detailPopup.Body, "Arrow", "→", 64f, UiTheme.TextMuted, TextAlignmentOptions.Center);
        UiKit.TopCenter(compareArrow.rectTransform, 0f, 150f, 140f, 80f);
        compareDelta = UiKit.Text(detailPopup.Body, "Delta", string.Empty, UiTheme.FontHeading, UiTheme.TextPrimary, TextAlignmentOptions.Center);
        UiKit.TopCenter(compareDelta.rectTransform, 0f, 236f, 200f, 44f);

        detailPopup.AddFooterButton("닫기", UiButtonStyle.Ghost, detailPopup.Hide, 180f);
        enhanceFromDetailButton = detailPopup.AddFooterButton("강화", UiButtonStyle.Secondary, OpenEnhance, 200f);
        equipButton = detailPopup.AddFooterButton("장착", UiButtonStyle.Primary, EquipOrUnequip, 260f);
    }

    private CompareCard BuildCompareCard(RectTransform parent, string name, float x)
    {
        UiKit.Surface panel = UiKit.Panel(parent, name, UiTheme.SurfaceRaised, UiTheme.RadiusL, UiTheme.Border);
        UiKit.TopLeft(panel.Rect, x, 0f, CardWidth, 520f);

        var card = new CompareCard { Root = panel.Rect };
        card.Title = UiKit.Text(panel.Rect, "Title", string.Empty, UiTheme.FontLabel, UiTheme.TextSecondary, TextAlignmentOptions.Center);
        UiKit.TopStretch(card.Title.rectTransform, UiTheme.Space4, 32f);

        card.Slot = UiSlot.Create(panel.Rect, "Slot", 176f);
        UiKit.TopCenter(card.Slot.Rect, 0f, 60f, 176f, 176f);
        card.Slot.SetInteractable(false);

        card.Name = UiKit.Ellipsis(UiKit.Text(panel.Rect, "Name", string.Empty, UiTheme.FontHeading, UiTheme.TextPrimary,
            TextAlignmentOptions.Center));
        UiKit.TopStretch(card.Name.rectTransform, 252f, 44f, UiTheme.Space4, UiTheme.Space4);

        card.Lines = UiKit.Wrap(UiKit.Text(panel.Rect, "Lines", string.Empty, UiTheme.FontBody, UiTheme.TextSecondary,
            TextAlignmentOptions.Top));
        card.Lines.lineSpacing = 10f;
        UiKit.Fill(card.Lines.rectTransform, UiTheme.Space5, 306f, UiTheme.Space5, UiTheme.Space4);
        return card;
    }

    private void OpenDetail(OwnedEquipment item)
    {
        if (item == null) return;
        detailItem = item;
        RefreshDetail();
        detailPopup.Show();
    }

    private void RefreshDetail()
    {
        OwnedEquipment item = detailItem;
        if (item == null) return;

        bool mine = selectedHero != null && item.IsHeldBy(selectedHero);
        OwnedEquipment current = selectedHero != null ? EquipmentInventory.EquippedIn(selectedHero, item.Slot) : null;

        // 오른쪽 — 누른 장비.
        ApplyCard(selectedCard, mine ? "장착 중인 장비" : "선택한 장비", item);

        // 왼쪽 — 이 영웅이 같은 칸에 지금 든 것. 누른 장비가 바로 그것이면 비교할 것이 없어 비운다.
        bool compare = !mine && selectedHero != null;
        currentCard.Root.gameObject.SetActive(compare);
        compareArrow.gameObject.SetActive(compare);
        compareDelta.gameObject.SetActive(compare);
        if (compare)
        {
            if (current != null) ApplyCard(currentCard, "지금 장착", current);
            else ApplyBaseCard(currentCard, item.Slot);

            float before = current != null ? current.Power : EquipmentGradeRules.BasePower;
            float delta = item.Power - before;
            compareDelta.text = Mathf.Abs(delta) < 0.005f ? "변화 없음"
                : UiTheme.Paint((delta > 0 ? "+" : "") + delta.ToString("0.00"), delta > 0 ? UiTheme.Success : UiTheme.Danger);
        }

        // 가운데 정렬 — 비교하지 않을 때는 카드 하나를 가운데로.
        selectedCard.Root.anchoredPosition = new Vector2(compare ? 1140f - UiPopup.Padding * 2f - CardWidth : (1140f - UiPopup.Padding * 2f - CardWidth) * 0.5f,
            selectedCard.Root.anchoredPosition.y);

        detailPopup.SetTitle(item.DisplayName);
        equipButton.SetLabel(mine ? "해제" : "장착");
        equipButton.SetStyle(mine ? UiButtonStyle.Secondary : UiButtonStyle.Primary);
        equipButton.interactable = selectedHero != null;
        enhanceFromDetailButton.interactable = !EquipmentEnhancement.IsMaxed(item);
    }

    private void ApplyCard(CompareCard card, string title, OwnedEquipment item)
    {
        card.Title.text = title;
        UiSlotContent content = UiSlotContents.Equipment(item);
        UiSlotContents.ApplyOwnerTag(ref content, item, selectedHero);
        card.Slot.SetContent(content);
        card.Name.text = item.DisplayName;
        card.Name.color = UiTheme.GradeColor(item.Grade);

        CharacterSO owner = EquipmentInventory.FindOwner(item, OwnedRoster.Members);
        card.Lines.text =
            $"{UiTheme.Paint(EquipmentGradeNames.NameOf(item.Grade) + "등급", UiTheme.GradeColor(item.Grade))} · " +
            $"{UiSlotContents.SlotName(item.Slot)} · {CharacterRules.Korean(item.Weapon.type)}\n" +
            $"{UiTheme.Paint(UiSlotContents.StatLine(item), UiTheme.TextPrimary)}\n" +
            $"강화 +{item.Level} / {EquipmentEnhancement.MaxLevel}\n" +
            (owner != null ? $"{HeroLabel.Name(owner)} 장착 중" : "보관 중");
    }

    private void ApplyBaseCard(CompareCard card, EquipSlot slot)
    {
        card.Title.text = "지금 장착";
        card.Slot.SetContent(UiSlotContents.BaseEquipment());
        card.Name.text = "기본 장비";
        card.Name.color = UiTheme.TextPrimary;
        card.Lines.text = $"{UiSlotContents.SlotName(slot)}\n{UiSlotContents.StatName(slot)} x{EquipmentGradeRules.BasePower:0.00}";
    }

    private void EquipOrUnequip()
    {
        OwnedEquipment item = detailItem;
        if (item == null || selectedHero == null) return;

        if (item.IsHeldBy(selectedHero))
        {
            EquipmentInventory.Unequip(item);
            toast.Show($"{WithObject(item.DisplayName)} 창고에 넣었습니다. 기본 장비를 다시 듭니다.", UiToastKind.Info);
            RefreshDetail();
            return;
        }

        if (PartyRoster.IsFallen(selectedHero))
        {
            toast.Show($"{WithTopic(HeroLabel.Name(selectedHero))} 쓰러져 새 장비를 들 수 없습니다.", UiToastKind.Warning);
            return;
        }

        CharacterSO owner = EquipmentInventory.FindOwner(item, OwnedRoster.Members);
        if (owner != null && owner != selectedHero)
        {
            confirm.Ask("장비 가져오기",
                $"{WithSubject(HeroLabel.Name(owner))} 들고 있는 장비입니다.\n{WithObject(HeroLabel.Name(selectedHero))} 위해 가져올까요?",
                "가져오기", false, () => Equip(item));
            return;
        }

        Equip(item);
    }

    private void Equip(OwnedEquipment item)
    {
        OwnedEquipment droppedShield = item.Slot == EquipSlot.MainHand && item.Weapon.IsTwoHanded
            ? EquipmentInventory.EquippedIn(selectedHero, EquipSlot.OffHand)
            : null;

        if (!EquipmentInventory.Equip(selectedHero, item, out string reason))
        {
            toast.Show(reason, UiToastKind.Warning);
            return;
        }

        string message = $"{WithSubject(HeroLabel.Name(selectedHero))} {WithObject(item.DisplayName)} 장착했습니다.";
        if (droppedShield != null) message += $" 두손 무기라 {WithTopic(droppedShield.DisplayName)} 창고로 돌아갔습니다.";
        toast.Show(message, UiToastKind.Success);
        detailPopup.Hide();
    }

    // ---- 강화 팝업 ------------------------------------------------------------

    private void BuildEnhancePopup()
    {
        const float width = 1000f;
        enhancePopup = UiPopup.Create(overlay, "Enhance", "장비 강화", new Vector2(width, 690f));
        RectTransform body = enhancePopup.Body;
        float inner = width - UiPopup.Padding * 2f;

        // 현재 → 결과. 흐름 부품과 같은 순서(왼쪽이 지금, 오른쪽이 결과)로 읽힌다.
        const float slot = 176f;
        float leftX = inner * 0.25f - slot * 0.5f;
        float rightX = inner * 0.75f - slot * 0.5f;

        TMP_Text beforeTitle = UiKit.Text(body, "BeforeTitle", "현재", UiTheme.FontLabel, UiTheme.TextSecondary, TextAlignmentOptions.Center);
        UiKit.TopLeft(beforeTitle.rectTransform, leftX - 60f, 0f, slot + 120f, 32f);
        TMP_Text afterTitle = UiKit.Text(body, "AfterTitle", "성공하면", UiTheme.FontLabel, UiTheme.Success, TextAlignmentOptions.Center);
        UiKit.TopLeft(afterTitle.rectTransform, rightX - 60f, 0f, slot + 120f, 32f);

        enhanceBefore = UiSlot.Create(body, "Before", slot);
        UiKit.TopLeft(enhanceBefore.Rect, leftX, 40f, slot, slot);
        enhanceBefore.SetInteractable(false);
        enhanceAfter = UiSlot.Create(body, "After", slot);
        UiKit.TopLeft(enhanceAfter.Rect, rightX, 40f, slot, slot);
        enhanceAfter.SetInteractable(false);

        TMP_Text arrow = UiKit.Text(body, "Arrow", "→", 64f, UiTheme.TextMuted, TextAlignmentOptions.Center);
        UiKit.TopCenter(arrow.rectTransform, 0f, 40f + slot * 0.5f - 40f, 120f, 80f);

        enhanceBeforeText = UiKit.Wrap(UiKit.Text(body, "BeforeText", string.Empty, UiTheme.FontBody, UiTheme.TextPrimary, TextAlignmentOptions.Top));
        UiKit.TopLeft(enhanceBeforeText.rectTransform, leftX - 100f, 40f + slot + UiTheme.Space3, slot + 200f, 76f);
        enhanceAfterText = UiKit.Wrap(UiKit.Text(body, "AfterText", string.Empty, UiTheme.FontBody, UiTheme.TextPrimary, TextAlignmentOptions.Top));
        UiKit.TopLeft(enhanceAfterText.rectTransform, rightX - 100f, 40f + slot + UiTheme.Space3, slot + 200f, 76f);

        // 확률과 비용. 무엇이 필요하고 무엇을 잃을 수 있는지가 버튼 바로 위에 있어야 한다.
        UiKit.Surface info = UiKit.Panel(body, "Info", UiTheme.SurfaceSunken, UiTheme.RadiusM);
        UiKit.TopLeft(info.Rect, 0f, 40f + slot + 100f, inner, 150f);
        enhanceChances = UiKit.Wrap(UiKit.Text(info.Rect, "Chances", string.Empty, UiTheme.FontBody, UiTheme.TextPrimary));
        enhanceChances.lineSpacing = 10f;
        enhanceChances.alignment = TextAlignmentOptions.MidlineLeft;
        UiKit.Fill(enhanceChances.rectTransform, UiTheme.Space5, UiTheme.Space3, inner * 0.45f, UiTheme.Space3);
        enhanceGold = UiKit.Wrap(UiKit.Text(info.Rect, "Gold", string.Empty, UiTheme.FontBody, UiTheme.TextPrimary, TextAlignmentOptions.MidlineRight));
        enhanceGold.lineSpacing = 10f;
        UiKit.Fill(enhanceGold.rectTransform, inner * 0.55f, UiTheme.Space3, UiTheme.Space5, UiTheme.Space3);

        enhancePopup.AddFooterButton("닫기", UiButtonStyle.Ghost, enhancePopup.Hide, 180f);
        enhanceButton = enhancePopup.AddFooterButton("강화", UiButtonStyle.Primary, AskEnhance, 320f);
    }

    private void OpenEnhance()
    {
        if (detailItem == null) return;
        RefreshEnhance();
        enhancePopup.Show();
    }

    private void RefreshEnhance()
    {
        OwnedEquipment item = detailItem;
        if (item == null) return;

        int level = item.Level;
        bool maxed = EquipmentEnhancement.IsMaxed(item);
        string stat = UiSlotContents.StatName(item.Slot);
        float before = item.Power;
        float after = EquipmentGradeRules.PowerOf(item.Grade) * EquipmentEnhancement.Multiplier(level + 1);

        enhancePopup.SetTitle("장비 강화 — " + item.DisplayName);
        enhanceBefore.SetContent(UiSlotContents.Equipment(item));
        enhanceBeforeText.text = $"+{level}\n{stat} x{before:0.00}";

        if (maxed)
        {
            enhanceAfter.SetLocked("최대");
            enhanceAfterText.text = UiTheme.Paint($"+{EquipmentEnhancement.MaxLevel} 도달", UiTheme.TextMuted);
            enhanceChances.text = string.Empty;
            enhanceGold.text = string.Empty;
            enhanceButton.ClearCost();
            enhanceButton.interactable = false;
            return;
        }

        // 무기창고 레벨이 막은 단계. 다음 칸 자리에 필요한 레벨을 적는다.
        if (EquipmentEnhancement.IsCapped(item))
        {
            enhanceAfter.SetLocked("잠김");
            enhanceAfterText.text = UiTheme.Paint(EquipmentEnhancement.CapRequirement(item), UiTheme.Warning);
            enhanceChances.text = string.Empty;
            enhanceGold.text = string.Empty;
            enhanceButton.ClearCost();
            enhanceButton.interactable = false;
            return;
        }

        UiSlotContent next = UiSlotContents.Equipment(item);
        next.Corner = "+" + (level + 1);
        enhanceAfter.SetContent(next);
        enhanceAfterText.text = $"{UiTheme.Paint("+" + (level + 1), UiTheme.Success)}\n{stat} x{after:0.00} {UiTheme.Paint($"(+{after - before:0.00})", UiTheme.Success)}";

        float success = EquipmentEnhancement.SuccessChance(level);
        float destroy = EquipmentEnhancement.DestroyChance(level);
        float keep = Mathf.Max(0f, 1f - success - destroy);
        enhanceChances.text =
            $"성공 확률  {UiTheme.Paint(UiKit.Percent(success), UiTheme.Success)}\n" +
            $"실패 (단계 유지)  {UiTheme.Paint(UiKit.Percent(keep), UiTheme.TextSecondary)}\n" +
            $"파괴 확률  {(destroy > 0f ? UiTheme.Paint(UiKit.Percent(destroy), UiTheme.Danger) : UiTheme.Paint("없음", UiTheme.TextMuted))}";

        long cost = EquipmentEnhancement.Cost(item);
        long gold = PlayerAccount.Balance(Currency.Gold);
        enhanceGold.text =
            $"보유 골드  {UiTheme.Paint(UiKit.Amount(gold), gold >= cost ? UiTheme.Success : UiTheme.Danger)}\n" +
            $"필요 골드  {UiKit.Amount(cost)}";

        enhanceButton.SetCost(Currency.Gold, cost);
        enhanceButton.interactable = true;
    }

    private void AskEnhance()
    {
        OwnedEquipment item = detailItem;
        if (item == null) return;

        long cost = EquipmentEnhancement.Cost(item);
        if (PlayerAccount.Balance(Currency.Gold) < cost)
        {
            toast.Show($"골드가 부족합니다. ({UiKit.Amount(cost)} 필요)", UiToastKind.Warning);
            return;
        }

        float destroy = EquipmentEnhancement.DestroyChance(item.Level);
        if (destroy > 0f)
        {
            confirm.Ask("강화 확인",
                $"{UiTheme.Paint(UiKit.Percent(destroy), UiTheme.Danger)} 확률로 파괴됩니다. 강화할까요?",
                "강화", true, Enhance);
            return;
        }

        Enhance();
    }

    private void Enhance()
    {
        OwnedEquipment item = detailItem;
        if (item == null) return;

        string name = item.DisplayName;
        if (!EquipmentEnhancement.TryEnhance(item, out EnhanceOutcome outcome, out string reason))
        {
            toast.Show(reason, UiToastKind.Warning);
            return;
        }

        switch (outcome)
        {
            case EnhanceOutcome.Success:
                toast.Show($"강화 성공! +{item.Level}", UiToastKind.Success);
                break;
            case EnhanceOutcome.Fail:
                toast.Show("강화 실패 — 강화 단계는 그대로입니다.", UiToastKind.Warning);
                break;
            default:
                toast.Show($"{WithSubject(name)} 파괴되었습니다.", UiToastKind.Danger);
                break;
        }

        // 파괴됐으면 HandleDataChanged가 팝업을 닫는다. 남아 있으면 새 단계로 다시 그린다.
        if (detailItem != null)
        {
            RefreshEnhance();
            RefreshDetail();
        }
    }

    // ---- 메인 화면 --------------------------------------------------------------

    private void SelectHero(CharacterSO hero)
    {
        if (hero == null) return;
        selectedHero = hero;
        Refresh();
    }

    private void ClickEquipSlot(EquipSlot slot)
    {
        if (selectedHero == null) return;

        OwnedEquipment item = EquipmentInventory.EquippedIn(selectedHero, slot);
        if (item != null)
        {
            OpenDetail(item);
            return;
        }

        // 비어 있으면(기본 장비) 그 칸에 맞는 장비만 목록에 보여 준다.
        itemPicker.Tabs.Select(slot == EquipSlot.OffHand ? 2 : 1, true);
        toast.Show($"오른쪽 목록에서 {UiSlotContents.SlotName(slot)}를 고르세요.", UiToastKind.Info);
    }

    private void Refresh()
    {
        if (heroPicker == null || detailPopup == null) return;

        if (selectedHero != null && !OwnedRoster.Contains(selectedHero)) selectedHero = null;
        if (selectedHero == null && OwnedRoster.Count > 0) selectedHero = OwnedRoster.Members[0];

        RefreshHeroes();
        RefreshHero();
        RefreshItems();
        if (detailPopup.IsOpen) RefreshDetail();
        if (enhancePopup.IsOpen) RefreshEnhance();
    }

    private void RefreshHeroes()
    {
        heroes.Clear();
        IReadOnlyList<CharacterSO> members = OwnedRoster.Members;
        for (int i = 0; i < members.Count; i++) if (members[i] != null) heroes.Add(members[i]);

        heroPicker.Count.text = $"{heroes.Count}명";
        heroPicker.Grid.Show(heroes.Count, BindHero, "보유한 영웅이 없습니다.");
    }

    private void BindHero(UiTile tile, int index)
    {
        CharacterSO hero = heroes[index];
        tile.Payload = hero;
        tile.Slot.SetContent(UiSlotContents.Hero(hero));
        tile.Slot.SetSelected(hero == selectedHero);
        bool fallen = PartyRoster.IsFallen(hero);
        tile.Slot.SetDimmed(fallen, "쓰러짐");
        tile.SetText(HeroLabel.Name(hero), fallen ? "쓰러짐" : $"Lv.{hero.Level}", fallen ? (Color?)UiTheme.TextMuted : null);
    }

    private void RefreshHero()
    {
        if (selectedHero == null)
        {
            portrait.SetEmpty(string.Empty);
            heroName.text = "영웅 없음";
            heroInfo.text = string.Empty;
            heroPower.text = string.Empty;
            weaponSlot.SetLocked(string.Empty);
            armorSlot.SetLocked(string.Empty);
            accessorySlot.SetLocked("준비 중");
            weaponName.text = armorName.text = string.Empty;
            return;
        }

        portrait.SetContent(UiSlotContents.Hero(selectedHero));
        heroName.text = HeroLabel.Name(selectedHero);
        heroName.color = UiTheme.StarColor(selectedHero.starCount);
        string info = $"{UiTheme.Paint(UiKit.Stars(selectedHero.starCount), UiTheme.StarColor(selectedHero.starCount))} · Lv.{selectedHero.Level}";
        if (PartyRoster.IsFallen(selectedHero)) info += "  " + UiTheme.Paint("쓰러짐", UiTheme.Warning);
        heroInfo.text = info;

        heroPower.text =
            $"공격 배율  {UiTheme.Paint("x" + CharacterLoadout.MainHandPower(selectedHero).ToString("0.00"), UiTheme.TextPrimary)}\n" +
            $"방어 배율  {UiTheme.Paint("x" + CharacterLoadout.ShieldPower(selectedHero).ToString("0.00"), UiTheme.TextPrimary)}";

        ApplyEquipSlot(weaponSlot, weaponName, EquipSlot.MainHand);
        ApplyEquipSlot(armorSlot, armorName, EquipSlot.OffHand);
        // 칸 아래 이름줄이 이미 "준비 중"이라 칸 안에는 자물쇠만.
        accessorySlot.SetLocked(string.Empty, UiIconLibrary.Accessory);
    }

    private void ApplyEquipSlot(UiSlot slot, TMP_Text label, EquipSlot equipSlot)
    {
        OwnedEquipment item = EquipmentInventory.EquippedIn(selectedHero, equipSlot);
        slot.SetSelected(false);
        slot.SetDimmed(false);

        if (item != null)
        {
            slot.SetContent(UiSlotContents.Equipment(item));
            label.text = item.DisplayName;
            label.color = UiTheme.GradeColor(item.Grade);
            return;
        }

        // 방패를 못 드는 이유가 직접 들린 제작 두손 무기일 때만 말한다. 기본 장비가 두손 무기라서 못 드는 것이라면
        // 그렇게 적는 순간 기본 장비가 무엇인지 드러난다.
        if (equipSlot == EquipSlot.OffHand && EquipmentInventory.EquippedIn(selectedHero, EquipSlot.MainHand) != null
            && !CharacterLoadout.CanHoldShield(selectedHero))
        {
            // 빈 칸("+")으로 두면 넣을 수 있는 것처럼 보인다. 막힌 칸으로 보여 준다.
            slot.SetLocked("두손 무기", UiIconLibrary.Weapon(WeaponType.Shield));
            label.text = UiTheme.Paint("두손 무기를 들고 있음", UiTheme.TextMuted);
            return;
        }

        // 기본 장비. 비어 있는 손도 똑같이 적는다 — "비어 있음"과 "기본 방패"가 갈리면 그것만으로 직업이 드러난다.
        slot.SetContent(UiSlotContents.BaseEquipment());
        label.text = UiTheme.Paint("기본 장비", UiTheme.TextSecondary);
    }

    private void RefreshItems()
    {
        items.Clear();
        int filter = itemPicker.Tabs.Selected;
        IReadOnlyList<OwnedEquipment> all = EquipmentInventory.Items;
        for (int i = 0; i < all.Count; i++)
        {
            OwnedEquipment item = all[i];
            if (filter == 1 && item.Slot != EquipSlot.MainHand) continue;
            if (filter == 2 && item.Slot != EquipSlot.OffHand) continue;
            items.Add(item);
        }
        // 좋은 것부터. 같은 등급이면 강화 단계가 높은 것, 그다음 주무기가 방패보다 앞.
        items.Sort((a, b) =>
        {
            int byGrade = ((int)b.Grade).CompareTo((int)a.Grade);
            if (byGrade != 0) return byGrade;
            int byLevel = b.Level.CompareTo(a.Level);
            if (byLevel != 0) return byLevel;
            return ((int)a.Slot).CompareTo((int)b.Slot);
        });

        itemPicker.Count.text = $"{all.Count}개";
        itemPicker.Grid.Show(items.Count, BindItem,
            all.Count == 0 ? "장비가 없습니다." : "해당하는 장비가 없습니다.");
    }

    private void BindItem(UiTile tile, int index)
    {
        OwnedEquipment item = items[index];
        tile.Payload = item;

        UiSlotContent content = UiSlotContents.Equipment(item);
        UiSlotContents.ApplyOwnerTag(ref content, item, selectedHero);
        tile.Slot.SetContent(content);
        tile.Slot.SetSelected(item.IsHeldBy(selectedHero));
        tile.Slot.SetDimmed(false);
        tile.SetText(item.DisplayName, UiSlotContents.StatLine(item), UiTheme.GradeColor(item.Grade));
    }

    // "아엘리아이(가)" 대신 "아엘리아가". 영웅 이름도 무기 이름도 무엇이 올지 모른다.
    private static string WithSubject(string word) => word + HeroLabel.SubjectParticle(word);
    private static string WithObject(string word) => word + HeroLabel.ObjectParticle(word);
    private static string WithTopic(string word) => word + HeroLabel.TopicParticle(word);
}
