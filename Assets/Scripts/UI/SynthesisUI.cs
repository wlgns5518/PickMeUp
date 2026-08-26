using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 마을 합성소에서 여는 창.
//
// 왼쪽에 주카드, 오른쪽에 재료 카드를 올리고 합성하면 재료는 보유 명단에서 사라지고
// 주카드가 스킬 하나를 배운다. 주카드의 레벨과 스탯은 손대지 않는다 — 합성은 성장이 아니라
// "다른 영웅을 태워 재주 하나를 얻는" 일이다.
//
// 어떤 스킬이 나올지는 재료의 등급이 정한다(SkillCatalog.Roll). 재료가 좋을수록 높은 등급의
// 스킬이 후보에 들어가고, 그 안에서도 높은 등급일수록 드물게 나온다.
//
// 카드는 보유 명단(OwnedRoster)에서 고른다. 에셋(CharacterRosterSO)이 아니라 런타임 명단인 이유는
// OwnedRoster 주석 참조 — 여기서 재료를 없애는 조작이 원본 에셋을 영구히 바꾸면 안 된다.
//
// 카드를 올리는 방법은 두 가지다. 목록에서 위쪽 자리로 끌어다 놓거나(편성 창과 같은 부품을 쓴다),
// 그냥 한 번 누르거나. 드래그는 어느 자리에 넣을지 고를 때, 클릭은 빠르게 올렸다 내릴 때 편하다.
//
// FloorSelectUI/DeckBuildUI와 같이 캔버스부터 코드에서 만든다.
[DisallowMultipleComponent]
public class SynthesisUI : FacilityWindow, ICardDragHost
{
    [Header("Roster")]
    [Tooltip("시작 명단. 실제 보유 목록은 런타임(OwnedRoster)이 들고, 이건 씬에 부트스트랩이 없을 때의 대비책이다.")]
    [SerializeField] private CharacterRosterSO roster;

    [Header("Card")]
    [Tooltip("영웅 카드 프리팹 (Assets/Character/CharacterCard.prefab).")]
    [SerializeField] private CharacterCard cardPrefab;

    [Header("Open State")]
    [Tooltip("합성소를 누르지 않아도 처음부터 열려 있게 하려면 켠다.")]
    [SerializeField] private bool openOnStart;

    [Header("Warning Banner")]
    [Tooltip("경고를 띄울 장식 배너(Assets/Image/UI.png). 비워두면 금색 테두리에 검은 판으로 그린다.")]
    [SerializeField] private Sprite bannerSprite;
    [SerializeField] private float bannerWidth = 900f;

    [Header("Layout")]
    [SerializeField] private float screenMargin = 40f;
    [SerializeField] private float cardSpacing = 14f;
    [Tooltip("위쪽 두 자리에 올라가는 카드의 최대 배율.")]
    [SerializeField, Range(0.2f, 1f)] private float maxSlotCardScale = 1f;
    [Tooltip("아래 보유 목록 카드의 최대 배율.")]
    [SerializeField, Range(0.1f, 1f)] private float maxRosterCardScale = 0.5f;

    private const float PanelPadding = 28f;
    private const float FramePadding = 5f;
    private const float HeaderHeight = 52f;
    private const float CloseSize = 52f;
    private const float SlotTitleHeight = 34f;   // 자리 이름표가 테두리 위쪽 바깥에 붙는 높이
    private const float InfoHeight = 104f;
    private const float PlusWidth = 90f;
    private const float SlotGap = 30f;
    private const float ButtonWidth = 400f;
    private const float ButtonHeight = 74f;
    private const float ResultHeight = 40f;
    private const float SectionLabelHeight = 32f;

    // 자리(위)와 보유 목록(아래)이 남는 높이를 나눠 갖는 비율. 카드가 화면을 채우도록 위쪽을 넉넉히 준다.
    private const float SlotAreaShare = 0.64f;

    // 자리 하나가 창 너비에서 차지할 수 있는 몫. 두 자리를 좌우로 벌려도 겹치지 않는 선.
    private const float SlotColumnShare = 0.30f;

    // 위쪽 두 자리의 번호. 음수(CardDragSource.RosterSlot)는 아래쪽 보유 목록을 뜻한다.
    private const int MainSlotIndex = 0;
    private const int MaterialSlotIndex = 1;

    private static readonly Color MainFrame = new Color(0.42f, 0.34f, 0.12f, 0.96f);
    private static readonly Color MaterialFrame = new Color(0.44f, 0.18f, 0.20f, 0.96f);
    private static readonly Color HintText = new Color(0.62f, 0.62f, 0.66f);
    private static readonly Color WarnText = new Color(0.95f, 0.62f, 0.35f);

    private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

    private static readonly StringBuilder Builder = new StringBuilder(128);

    // 한 자리에 올라간 카드. 카드 인스턴스는 만들 때 한 번만 찍고 이후에는 내용만 갈아 끼운다.
    private class CardSlot
    {
        public int Index;
        public Image Frame;
        public CharacterCard Card;
        public CardDragSource Drag;
        public TMP_Text EmptyLabel;
        public TMP_Text Info;
        public Color IdleColor;
        public Color FilledColor;
    }

    private class RosterSlot
    {
        public Image Frame;
        public CanvasGroup Group;
        public CharacterSO Character;
    }

    private RectTransform dragLayer;

    private AnnouncementBanner warningBanner;

    private CardSlot mainSlot;
    private CardSlot materialSlot;
    private Button synthesizeButton;
    private TMP_Text resultText;
    private readonly List<RosterSlot> rosterSlots = new List<RosterSlot>();

    private CharacterSO main;
    private CharacterSO material;

    // 목록 카드의 배율. 끌고 다니는 유령 카드를 같은 크기로 만들어야 손끝에서 크기가 튀지 않는다.
    private float rosterCardScale = 0.34f;

    protected override string CanvasName => "SynthesisCanvas";
    // 소환 창(96)보다 위.
    protected override int SortingOrder => 97;

    private void Awake()
    {
        // 로스터 에셋은 시작 명단이다. Seed는 이미 있는 캐릭터를 건너뛰므로 두 번 불려도 결과가 같고,
        // 창을 다시 지을 때가 아니라 여기서만 얹으므로 합성으로 사라진 멤버가 되살아나지 않는다.
        if (roster != null) OwnedRoster.Seed(roster.Members);

        EnsureBuilt();
        SetOpen(openOnStart);
    }

    private void OnEnable()
    {
        OwnedRoster.Changed += HandleRosterChanged;
    }

    private void OnDisable()
    {
        OwnedRoster.Changed -= HandleRosterChanged;
    }

    protected override void TickWindow(float deltaTime)
    {
        warningBanner?.Tick(deltaTime);
    }

    public override void Show()
    {
        EnsureBuilt();
        if (rosterDirty) RebuildRoster();
        SetOpen(true);
    }

    // 창을 닫으면 올려둔 카드를 내려놓는다. 지난번에 뭘 올렸는지 창이 기억하고 있으면,
    // 다시 열었을 때 재료가 걸려 있는 줄 모르고 합성을 눌러 엉뚱한 카드를 태우게 된다.
    // X와 배경막 어느 쪽으로 닫아도 같아야 하므로 두 버튼이 함께 부르는 여기서 비운다.
    public override void Hide()
    {
        main = null;
        material = null;
        RefreshSlots();

        SetOpen(false);
    }

    // ---- 고르기 ---------------------------------------------------------

    // 목록에서 카드를 한 번 누르면 빈 자리에 올라가고, 이미 올라간 카드를 누르면 내려온다.
    // 두 자리가 다 찼을 때 새 카드를 누르면 재료 쪽이 바뀐다 — 주카드는 보통 그대로 두고
    // 재료만 갈아 끼우기 때문이다.
    private void Pick(CharacterSO character)
    {
        if (character == null) return;

        if (character == main) main = null;
        else if (character == material) material = null;
        else if (main == null) main = character;
        else material = character;

        RefreshSlots();
    }

    private void ClearMain()
    {
        main = null;
        RefreshSlots();
    }

    private void ClearMaterial()
    {
        material = null;
        RefreshSlots();
    }

    // ---- 드래그 앤 드롭 --------------------------------------------------

    // 끌고 다니는 동안 손끝을 따라다니는 카드. 드롭 판정을 가리지 않도록 레이캐스트를 끈다.
    public RectTransform CreateDragGhost(CharacterSO character)
    {
        if (dragLayer == null || character == null || cardPrefab == null) return null;

        CharacterCard card = Instantiate(cardPrefab, dragLayer);
        card.name = "DragGhost";
        card.Apply(character);

        var rect = card.transform as RectTransform;
        if (rect == null)
        {
            Destroy(card.gameObject);
            return null;
        }

        CardLayout.CenterInSlot(rect, rosterCardScale);
        BlockRaycasts(rect).alpha = 0.85f;
        return rect;
    }

    public void HandleDrop(CardDragSource source, int slotIndex)
    {
        if (source == null || source.Character == null) return;

        // 아래쪽 목록 위에 놓았다 — 위쪽 자리에서 내려놓는 동작.
        if (slotIndex < 0)
        {
            if (source.SlotIndex == MainSlotIndex) main = null;
            else if (source.SlotIndex == MaterialSlotIndex) material = null;
            else return;

            RefreshSlots();
            return;
        }

        Place(source.Character, slotIndex);
    }

    // 자리 밖 허공에 놓았다. 자리에서 집어 온 카드면 그 자리를 비운다 —
    // 목록으로 정확히 되돌려 놓게 만들 이유가 없다.
    public void HandleDropOutside(CardDragSource source)
    {
        if (source == null) return;

        HandleDrop(source, CardDragSource.RosterSlot);
    }

    private void Place(CharacterSO character, int slotIndex)
    {
        if (character == null) return;

        // 반대쪽 자리에 있던 카드를 끌어오면 두 자리를 맞바꾼다. 한쪽이 비어버리는 것보다
        // 주카드와 재료를 서로 바꿔보려던 것으로 보는 편이 맞다.
        if (slotIndex == MainSlotIndex)
        {
            bool swap = character == material;
            CharacterSO previous = main;
            main = character;
            if (swap) material = previous;
        }
        else
        {
            bool swap = character == main;
            CharacterSO previous = material;
            material = character;
            if (swap) main = previous;
        }

        RefreshSlots();
    }

    // ---- 합성 -----------------------------------------------------------

    private void Synthesize()
    {
        if (main == null || material == null) return;

        if (main.IsSkillFull)
        {
            warningBanner?.Show($"{main.characterName}은(는) 더 배울 수 없습니다.\n스킬은 최대 {SkillCatalog.MaxSkillsPerCharacter}개입니다.");
            return;
        }

        if (!SkillCatalog.HasCandidate(main, material.starCount))
        {
            warningBanner?.Show("이 재료로는 배울 수 있는 스킬이 없습니다.\n등급이 더 높은 재료가 필요합니다.");
            return;
        }

        string skillId = SkillCatalog.Roll(main, material.starCount);
        if (string.IsNullOrEmpty(skillId) || !main.LearnSkill(skillId))
        {
            warningBanner?.Show("합성에 실패했습니다.");
            return;
        }

        CharacterSO consumed = material;
        string consumedName = consumed.characterName;

        // 자리를 먼저 비운다. 아래에서 명단이 바뀌면 목록이 다시 그려지는데,
        // 그때까지 사라진 카드를 재료 자리에 물고 있으면 없는 캐릭터가 화면에 남는다.
        material = null;
        OwnedRoster.Remove(consumed);

        // 결과 문구는 목록이 다시 그려진 뒤에 적는다. 순서가 반대면 새로 그린 화면이 문구를 지운다.
        // 낫표(「」)는 NotoSansKR 아틀라스에 없어 네모로 그려진다. 대괄호를 쓴다.
        SetResult($"{consumedName}을(를) 합성해 [{SkillCatalog.NameOf(skillId)}]을(를) 배웠습니다.", BattleHudPalette.Mvp);
    }

    // ---- 만들기 ---------------------------------------------------------

    private void HandleRosterChanged()
    {
        // 명단에서 사라진 카드를 자리에 물고 있으면 없는 캐릭터로 합성하게 된다.
        if (main != null && !OwnedRoster.Contains(main)) main = null;
        if (material != null && !OwnedRoster.Contains(material)) material = null;

        if (builtRosterCount == OwnedRoster.Count)
        {
            RefreshSlots();
            return;
        }

        // 창이 닫혀 있으면 아무도 보지 않는 화면을 다시 짓는 셈이다 — 10연차 소환은 한 명씩
        // 열 번 늘어나므로 그때마다 캔버스를 통째로 다시 지으면 그대로 멈춤이 된다.
        rosterDirty = true;
        if (IsOpen) RebuildRoster();
    }

    protected override void BuildWindow()
    {
        if (cardPrefab == null)
            Debug.LogError("[SynthesisUI] 카드 프리팹이 없어 카드를 그릴 수 없습니다. Assets/Character/CharacterCard.prefab을 지정하세요.", this);

        rosterSlots.Clear();
        mainSlot = null;
        materialSlot = null;

        BuildCanvas();
        BuildPopup();

        // 끌고 다니는 카드가 창 밖으로 나가도 잘리지 않도록 캔버스 최상단에 따로 층을 둔다.
        dragLayer = HudFactory.CreateGroup(canvasRect, "DragLayer");
        HudFactory.Stretch(dragLayer);

        // 경고 배너는 팝업 밖(캔버스 직속)에 둔다. 창이 닫혀도 같은 자리에 뜬다.
        warningBanner = AnnouncementBanner.Create(canvasRect, resolvedFont, bannerSprite, null, bannerWidth);

        RefreshSlots();
    }

    private void BuildPopup()
    {
        RectTransform popup = BuildPopupRoot();

        Image panel = HudFactory.CreateImage(popup, "Panel", BattleHudPalette.PanelBody);
        // 창 안을 누른 클릭이 배경막으로 내려가지 않도록 여기서 받아 둔다.
        panel.raycastTarget = true;
        RectTransform panelRect = panel.rectTransform;
        HudFactory.Stretch(panelRect);
        panelRect.offsetMin = new Vector2(screenMargin, screenMargin);
        panelRect.offsetMax = new Vector2(-screenMargin, -screenMargin);

        BuildPanelContent(panelRect, CanvasSize() - new Vector2(screenMargin * 2f, screenMargin * 2f));
    }

    private void BuildPanelContent(RectTransform panel, Vector2 panelSize)
    {
        float availableWidth = panelSize.x - PanelPadding * 2f;
        float y = PanelPadding;

        BuildHeader(panel, availableWidth, y);
        // 자리 이름표("주 카드"/"재료 카드")가 테두리 위쪽 바깥에 붙는다. 그만큼 띄워야 제목줄과 겹치지 않는다.
        y += HeaderHeight + SlotTitleHeight + 8f;

        // 자리 위아래로 고정 높이를 먼저 떼어 두고, 남는 높이를 자리와 보유 목록이 나눠 갖는다.
        // 카드 크기를 상수로 박아두면 화면이 커져도 창 위쪽이 텅 빈 채로 남는다.
        float below = 14f + ButtonHeight + 8f + ResultHeight + 10f + SectionLabelHeight + 6f;
        float free = Mathf.Max(240f, panelSize.y - PanelPadding - y - below);
        float cardHeight = free * SlotAreaShare - InfoHeight - 8f;

        // 두 자리가 "+" 를 사이에 두고 나란히 선다. 폭과 높이 중 더 빡빡한 쪽에 맞춘다.
        // 폭은 창의 절반 가까이까지 내주되, 보통은 높이 쪽이 먼저 걸린다.
        float byWidth = (availableWidth * SlotColumnShare - FramePadding * 2f) / CardLayout.CardSize.x;
        float byHeight = (cardHeight - FramePadding * 2f) / CardLayout.CardSize.y;
        float slotCardScale = Mathf.Clamp(Mathf.Min(byWidth, byHeight), 0.2f, maxSlotCardScale);
        Vector2 slotSize = CardLayout.SlotSize(slotCardScale, FramePadding);

        // 두 자리를 창 좌우로 벌린다. 붙여 놓으면 넓은 화면에서 가운데만 차고 양옆이 통째로 빈다.
        // 최소한 자리 하나와 가운데 "+" 자리는 확보한다.
        float minHalfStep = (slotSize.x + PlusWidth) * 0.5f + SlotGap;
        float halfStep = Mathf.Max(minHalfStep, availableWidth * 0.24f);
        // 설명 글은 카드보다 넓게 잡는다. 좁으면 이름과 스킬 줄이 금방 두 줄로 접힌다.
        float infoWidth = Mathf.Min(availableWidth * 0.42f, (halfStep - SlotGap) * 2f);

        mainSlot = BuildCardSlot(panel, "MainSlot", MainSlotIndex, "주 카드", "주 카드를 끌어다 놓으세요",
            slotSize, slotCardScale, -halfStep, y, infoWidth, MainFrame, ClearMain);
        materialSlot = BuildCardSlot(panel, "MaterialSlot", MaterialSlotIndex, "재료 카드", "재료 카드를 끌어다 놓으세요",
            slotSize, slotCardScale, halfStep, y, infoWidth, MaterialFrame, ClearMaterial);

        TMP_Text plus = HudFactory.CreateText(panel, "Plus", resolvedFont, 64f, HintText);
        SetTopCenter(plus.rectTransform, new Vector2(PlusWidth, slotSize.y), 0f, y);
        plus.text = "+";

        y += slotSize.y + 8f + InfoHeight + 14f;

        BuildSynthesizeButton(panel, y);
        y += ButtonHeight + 8f;

        resultText = HudFactory.CreateText(panel, "Result", resolvedFont, 26f, HintText);
        SetTopCenter(resultText.rectTransform, new Vector2(availableWidth, ResultHeight), 0f, y);
        y += ResultHeight + 10f;

        TMP_Text label = HudFactory.CreateText(panel, "RosterLabel", resolvedFont, 28f, BattleHudPalette.PanelText);
        label.alignment = TextAlignmentOptions.Left;
        HudFactory.SetTopLeft(label.rectTransform, new Vector2(availableWidth, SectionLabelHeight), new Vector2(PanelPadding, -y));
        label.text = "보유 영웅";
        y += SectionLabelHeight + 6f;

        float rosterHeight = Mathf.Max(120f, panelSize.y - PanelPadding - y);
        BuildRosterArea(panel, availableWidth, rosterHeight, y);
    }

    private void BuildHeader(RectTransform panel, float width, float y)
    {
        TMP_Text title = HudFactory.CreateText(panel, "Title", resolvedFont, 40f, BattleHudPalette.PanelText);
        title.alignment = TextAlignmentOptions.Left;
        HudFactory.SetTopLeft(title.rectTransform, new Vector2(width - CloseSize, HeaderHeight), new Vector2(PanelPadding, -y));
        title.text = "합성소";

        Image close = HudFactory.CreateImage(panel, "Close", BattleHudPalette.PanelBackdrop);
        close.raycastTarget = true;
        HudFactory.SetTopLeft(close.rectTransform, new Vector2(CloseSize, CloseSize),
            new Vector2(PanelPadding + width - CloseSize, -y));

        var button = close.gameObject.AddComponent<Button>();
        button.targetGraphic = close;
        button.onClick.AddListener(Hide);

        TMP_Text closeLabel = HudFactory.CreateText(close.rectTransform, "Label", resolvedFont, 24f, BattleHudPalette.PanelText);
        HudFactory.Stretch(closeLabel.rectTransform);
        // 곱셈 기호(U+2715 등)는 NotoSansKR 아틀라스에 없어 네모로 그려진다. 알파벳 X를 쓴다.
        closeLabel.text = "X";
    }

    private CardSlot BuildCardSlot(RectTransform panel, string name, int slotIndex, string title, string emptyText,
        Vector2 slotSize, float cardScale, float offsetX, float y, float infoWidth, Color filledColor,
        UnityEngine.Events.UnityAction onClick)
    {
        Image frame = HudFactory.CreateImage(panel, name, BattleHudPalette.PortraitFrame);
        // 끌어다 놓은 카드를 받으려면 테두리가 레이캐스트 대상이어야 한다.
        frame.raycastTarget = true;
        SetTopCenter(frame.rectTransform, slotSize, offsetX, y);

        // 올려둔 카드를 눌러 내려놓는다. 목록에서 다시 찾아 누르지 않아도 된다.
        var button = frame.gameObject.AddComponent<Button>();
        button.targetGraphic = frame;
        button.onClick.AddListener(onClick);

        frame.gameObject.AddComponent<CardDropTarget>().Bind(this, slotIndex);

        TMP_Text titleLabel = HudFactory.CreateText(frame.rectTransform, "Title", resolvedFont, 24f, HintText);
        titleLabel.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        titleLabel.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        titleLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
        titleLabel.rectTransform.sizeDelta = new Vector2(slotSize.x, SlotTitleHeight);
        titleLabel.rectTransform.anchoredPosition = new Vector2(0f, 4f);
        titleLabel.text = title;

        TMP_Text empty = HudFactory.CreateText(frame.rectTransform, "Empty", resolvedFont, 24f, HintText);
        // 기본은 줄바꿈 없음이라 안내 문구가 자리 밖으로 삐져나와 가운데 "+"를 덮는다.
        empty.textWrappingMode = TextWrappingModes.Normal;
        HudFactory.Stretch(empty.rectTransform);
        empty.rectTransform.offsetMin = new Vector2(10f, 10f);
        empty.rectTransform.offsetMax = new Vector2(-10f, -10f);
        empty.text = emptyText;

        var slot = new CardSlot
        {
            Index = slotIndex,
            Frame = frame,
            EmptyLabel = empty,
            IdleColor = BattleHudPalette.PortraitFrame,
            FilledColor = filledColor,
            // 자리에 올라간 카드도 다시 집어 들 수 있다. 무엇이 올라가 있는지는 ApplySlot이 다시 알려준다.
            Drag = frame.gameObject.AddComponent<CardDragSource>(),
        };

        if (cardPrefab != null)
        {
            slot.Card = Instantiate(cardPrefab, frame.rectTransform);
            slot.Card.name = "Card";
            var rect = slot.Card.transform as RectTransform;
            if (rect != null)
            {
                CardLayout.CenterInSlot(rect, cardScale);
                // 카드가 클릭을 먹으면 테두리의 버튼까지 닿지 않아 올려둔 카드를 내려놓을 수 없다.
                BlockRaycasts(rect);
            }
            slot.Card.gameObject.SetActive(false);
        }

        slot.Info = HudFactory.CreateText(panel, name + "Info", resolvedFont, 23f, BattleHudPalette.PanelText);
        slot.Info.alignment = TextAlignmentOptions.Top;
        slot.Info.textWrappingMode = TextWrappingModes.Normal;
        SetTopCenter(slot.Info.rectTransform, new Vector2(infoWidth, InfoHeight), offsetX, y + slotSize.y + 8f);

        return slot;
    }

    private void BuildSynthesizeButton(RectTransform panel, float y)
    {
        Image background = HudFactory.CreateImage(panel, "Synthesize", MainFrame);
        background.raycastTarget = true;
        SetTopCenter(background.rectTransform, new Vector2(ButtonWidth, ButtonHeight), 0f, y);

        synthesizeButton = background.gameObject.AddComponent<Button>();
        synthesizeButton.targetGraphic = background;
        synthesizeButton.onClick.AddListener(Synthesize);

        TMP_Text label = HudFactory.CreateText(background.rectTransform, "Label", resolvedFont, 28f, BattleHudPalette.Mvp);
        HudFactory.Stretch(label.rectTransform);
        label.text = "합성하기";
    }

    private void BuildRosterArea(RectTransform panel, float width, float height, float y)
    {
        Image area = HudFactory.CreateImage(panel, "RosterArea", new Color(1f, 1f, 1f, 0.03f));
        // 카드 사이 빈 곳에 놓아도 받아야 하므로 바닥 전체가 레이캐스트 대상이어야 한다.
        area.raycastTarget = true;
        HudFactory.SetTopLeft(area.rectTransform, new Vector2(width, height), new Vector2(PanelPadding, -y));

        // 위쪽 자리에서 집어 온 카드를 여기 놓으면 그 자리에서 내려온다.
        area.gameObject.AddComponent<CardDropTarget>().Bind(this, CardDragSource.RosterSlot);

        IReadOnlyList<CharacterSO> members = OwnedRoster.Members;
        builtRosterCount = members.Count;

        if (members.Count == 0)
        {
            TMP_Text empty = HudFactory.CreateText(area.rectTransform, "Empty", resolvedFont, 22f, HintText);
            HudFactory.Stretch(empty.rectTransform);
            empty.text = "보유한 영웅이 없습니다. 소환소에서 먼저 영웅을 뽑아주세요.";
            return;
        }

        var areaSize = new Vector2(width, height);
        int columns;
        float scale = CardLayout.Fit(members.Count, areaSize, cardSpacing, FramePadding, maxRosterCardScale, out columns);
        Vector2 slotSize = CardLayout.SlotSize(scale, FramePadding);
        rosterCardScale = scale;

        // 카드가 몇 장이든 가운데로 모은다. 왼쪽에 붙여 놓으면 두세 장일 때 화면이 비어 보인다.
        float gridWidth = columns * slotSize.x + (columns - 1) * cardSpacing;
        float startX = Mathf.Max(0f, (width - gridWidth) * 0.5f);

        for (int i = 0; i < members.Count; i++)
        {
            int column = i % columns;
            int row = i / columns;
            var position = new Vector2(
                startX + column * (slotSize.x + cardSpacing),
                -row * (slotSize.y + cardSpacing));

            rosterSlots.Add(BuildRosterSlot(area.rectTransform, members[i], slotSize, scale, position, i));
        }
    }

    private RosterSlot BuildRosterSlot(RectTransform parent, CharacterSO character, Vector2 slotSize,
        float scale, Vector2 position, int index)
    {
        Image frame = HudFactory.CreateImage(parent, "Card_" + index, BattleHudPalette.PortraitFrame);
        frame.raycastTarget = true;
        HudFactory.SetTopLeft(frame.rectTransform, slotSize, position);

        var button = frame.gameObject.AddComponent<Button>();
        button.targetGraphic = frame;
        // 클로저가 반복 변수를 붙잡지 않도록 지역 변수에 복사해 넘긴다.
        CharacterSO captured = character;
        button.onClick.AddListener(() => Pick(captured));

        frame.gameObject.AddComponent<CardDragSource>().Bind(this, character, CardDragSource.RosterSlot);

        CanvasGroup group = null;
        if (cardPrefab != null)
        {
            CharacterCard card = Instantiate(cardPrefab, frame.rectTransform);
            card.name = "Card";
            card.Apply(character);

            var rect = card.transform as RectTransform;
            if (rect != null)
            {
                CardLayout.CenterInSlot(rect, scale);
                group = BlockRaycasts(rect);
            }
        }

        return new RosterSlot { Frame = frame, Group = group, Character = character };
    }

    // 카드가 클릭을 먹으면 아래 테두리의 버튼까지 닿지 않는다. 프리팹에 이미 있으면 그걸 쓴다.
    private static CanvasGroup BlockRaycasts(RectTransform rect)
    {
        CanvasGroup group = rect.GetComponent<CanvasGroup>();
        if (group == null) group = rect.gameObject.AddComponent<CanvasGroup>();

        group.blocksRaycasts = false;
        return group;
    }

    // ---- 갱신 -----------------------------------------------------------

    private void RefreshSlots()
    {
        ApplySlot(mainSlot, main, DescribeMain(main));
        ApplySlot(materialSlot, material, DescribeMaterial(material));

        if (synthesizeButton != null) synthesizeButton.interactable = main != null && material != null;

        for (int i = 0; i < rosterSlots.Count; i++)
        {
            RosterSlot slot = rosterSlots[i];
            if (slot == null || slot.Frame == null) continue;

            bool isMain = slot.Character == main;
            bool isMaterial = slot.Character == material;

            slot.Frame.color = isMain ? MainFrame : isMaterial ? MaterialFrame : BattleHudPalette.PortraitFrame;
            // 이미 위쪽 자리에 올라간 카드는 눌러 둔다. 같은 카드가 두 군데 떠 있는 것처럼 보이지 않게.
            if (slot.Group != null) slot.Group.alpha = isMain || isMaterial ? 0.45f : 1f;
        }

        RefreshGuideText();
    }

    private void ApplySlot(CardSlot slot, CharacterSO character, string info)
    {
        if (slot == null) return;

        bool filled = character != null;
        slot.Frame.color = filled ? slot.FilledColor : slot.IdleColor;
        slot.EmptyLabel.gameObject.SetActive(!filled);
        slot.Info.text = info;
        // 비어 있으면 Character가 null이라 드래그가 시작되지 않는다.
        if (slot.Drag != null) slot.Drag.Bind(this, character, slot.Index);

        if (slot.Card == null) return;

        slot.Card.gameObject.SetActive(filled);
        if (!filled) return;

        // 다른 캐릭터로 갈아 끼울 때 별이 그대로 남지 않도록 비우고 다시 그린다.
        slot.Card.ResetCard();
        slot.Card.Apply(character);
    }

    // 합성 결과가 적혀 있으면 지우지 않는다. 방금 무엇을 배웠는지가 안내 문구보다 중요하다.
    private void RefreshGuideText()
    {
        if (resultText == null) return;

        if (main == null)
        {
            SetResult("아래 목록에서 스킬을 배울 주 카드를 끌어다 놓으세요.", HintText);
            return;
        }

        if (material == null)
        {
            SetResult("태워 넣을 재료 카드를 끌어다 놓으세요. 재료는 사라집니다.", HintText);
            return;
        }

        if (main.IsSkillFull)
        {
            SetResult($"주 카드가 이미 스킬 {SkillCatalog.MaxSkillsPerCharacter}개를 배웠습니다.", WarnText);
            return;
        }

        if (!SkillCatalog.HasCandidate(main, material.starCount))
        {
            SetResult("이 재료로 배울 수 있는 스킬이 남아 있지 않습니다.", WarnText);
            return;
        }

        SetResult($"합성하면 {material.starCount}등급까지의 스킬 하나를 배웁니다.", HintText);
    }

    private void SetResult(string message, Color color)
    {
        if (resultText == null) return;

        resultText.text = message;
        resultText.color = color;
    }

    private string DescribeMain(CharacterSO character)
    {
        if (character == null) return "레벨과 스탯은 그대로, 스킬만 늘어납니다.";

        Builder.Clear();
        AppendHeadline(character);
        Builder.Append('\n');
        Builder.Append("스킬 ").Append(character.SkillCount).Append('/').Append(SkillCatalog.MaxSkillsPerCharacter)
            .Append("  ").Append(SkillList(character));
        return Builder.ToString();
    }

    private string DescribeMaterial(CharacterSO character)
    {
        if (character == null) return "여기 올린 카드는 합성하면 사라집니다.";

        Builder.Clear();
        AppendHeadline(character);
        Builder.Append('\n');
        Builder.Append(character.starCount).Append("성 재료 — 이 등급까지의 스킬이 나옵니다.");
        return Builder.ToString();
    }

    private static void AppendHeadline(CharacterSO character)
    {
        Builder.Append(string.IsNullOrEmpty(character.characterName) ? "이름 없음" : character.characterName);
        Builder.Append('\n');
        Builder.Append("Lv.").Append(character.Level)
            .Append("  ").Append(character.starCount).Append('성')
            .Append("  ").Append(CharacterRules.Korean(character.job));
    }

    private static string SkillList(CharacterSO character)
    {
        if (character.SkillCount == 0) return "(없음)";

        IReadOnlyList<string> skills = character.Skills;
        var list = new StringBuilder(64);
        for (int i = 0; i < skills.Count; i++)
        {
            if (i > 0) list.Append(", ");
            list.Append(SkillCatalog.NameOf(skills[i]));
        }
        return list.ToString();
    }

    // ---- 자리 잡기 ------------------------------------------------------

    // Awake에서 막 만든 캔버스는 아직 크기가 잡히지 않아 0으로 읽힌다. 한 번 갱신시키고,
    // 그래도 비어 있으면 기준 해상도로 친다 — 여기서 0을 받으면 창 안의 모든 자리가 왼쪽 위로 쏠린다.
    private Vector2 CanvasSize()
    {
        Canvas.ForceUpdateCanvases();
        Vector2 size = canvasRect != null ? canvasRect.rect.size : Vector2.zero;
        if (size.x < 1f || size.y < 1f) return ReferenceResolution;
        return size;
    }

    // 창 가로 한가운데를 기준으로 offsetX만큼 옆에. 두 자리를 좌우 대칭으로 놓을 때 쓴다.
    private static void SetTopCenter(RectTransform rect, Vector2 size, float offsetX, float y)
    {
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(offsetX, -y);
    }

}
