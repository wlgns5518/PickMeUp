using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 이번 전투에 내보낼 파티를 짜는 화면.
//
// 마을의 시공의 틈(FacilityGate)을 누르면 열린다. 원정을 떠나는 자리이므로 누구를 데려갈지
// 고르는 것도 거기서 한다. 화면 아래 여는 버튼은 showOpenButton으로 되살릴 수 있다.
// 창 안에서는 보유 영웅 카드를 위쪽 출전 슬롯으로 끌어다 놓아 파티를 만든다.
// 슬롯 밖 허공에 놓거나 목록으로 되돌리면 빠지고, 슬롯끼리 끌면 순서가 바뀐다.
// 카드를 한 번 누르는 방식도 그대로 둔다 — 드래그는 자리를 고를 때, 클릭은 그냥 넣고 뺄 때 편하다.
//
// FloorSelectUI와 같이 캔버스부터 코드에서 만든다.
// 카드 수는 로스터에 달려 있어서, 씬에 미리 깔아두면 캐릭터가 늘 때마다 씬을 다시 만져야 한다.
[DisallowMultipleComponent]
public class DeckBuildUI : MonoBehaviour, ICardDragHost, IFacilityWindow
{
    [Header("Roster")]
    [Tooltip("보유 캐릭터 전원의 명단. 여기 있는 캐릭터가 카드로 나열된다.")]
    [SerializeField] private CharacterRosterSO roster;

    [Header("Deck")]
    [Tooltip("한 번에 출전할 수 있는 인원. 출전 슬롯 개수이기도 하다.")]
    [SerializeField, Min(1)] private int deckCapacity = 5;

    [Header("Card")]
    [Tooltip("비워두면 초상화와 이름만 있는 카드를 코드로 만든다.")]
    [SerializeField] private CharacterCard cardPrefab;
    [Tooltip("카드가 커질 수 있는 한계. 인원이 적어도 이 배율 이상으로는 키우지 않는다.")]
    [SerializeField, Range(0.2f, 1f)] private float maxCardScale = 0.55f;

    [Header("Banner")]
    [Tooltip("안내 문구가 올라갈 장식 배너(Assets/Image/UI.png). 비워두면 단색 판으로 그린다.")]
    [SerializeField] private Sprite bannerSprite;

    [Header("Open Button")]
    [Tooltip("화면 아래에 편성 창을 여는 버튼을 둔다. 꺼두면 마을의 시공의 틈을 눌러야만 열린다.")]
    [SerializeField] private bool showOpenButton;

    [Header("Depart")]
    [Tooltip("편성을 마치고 층을 고를 창. 비워두면 씬에서 찾는다.")]
    [SerializeField] private FloorSelectUI floorSelect;

    [Header("Layout")]
    [Tooltip("편성 창과 화면 가장자리 사이 여백.")]
    [SerializeField] private float screenMargin = 30f;
    [SerializeField] private float cardSpacing = 16f;

    [Header("Font")]
    [Tooltip("한글이 포함되므로 한국어 SDF 폰트를 지정해야 한다 (Assets/Fonts/NotoSansKR-Black SDF).")]
    [SerializeField] private TMP_FontAsset koreanFont;

    private const string BannerText = "파티를 구성합니다.\n영웅을 드래그 앤 드롭!";

    // 다른 파티에 있는 영웅은 받지 않는다. 막기만 하면 왜 안 들어가는지 알 수 없어 문구로 알린다.
    private const string OccupiedMessageFormat = "이미 {0}파티에 편성된 영웅입니다.\n{0}파티에서 먼저 빼주세요.";

    private const float FramePadding = 5f;   // 카드 둘레에 남기는 테두리 두께
    private const float PanelPadding = 24f;
    private const float HeaderHeight = 36f;
    private const float PartyTabWidth = 96f;
    private const float SectionLabelHeight = 28f;
    private const float BannerFallbackAspect = 2.46f;
    private const float OpenButtonWidth = 360f;
    private const float DepartWidth = 180f;

    private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
    private static readonly Vector2 FallbackCardSize = CardLayout.CardSize;
    private static readonly Color SelectedRosterFrame = new Color(0.38f, 0.31f, 0.12f, 0.96f);

    // 카드 한 장의 겉모습. 출전 슬롯은 카드를 만들어 두고 주인만 바꿔 끼우므로
    // 나중에 다른 캐릭터를 적용할 수 있어야 한다.
    private class CardVisual
    {
        public RectTransform Rect;
        public CharacterCard Prefab;
        public Image Portrait;
        public TMP_Text NameLabel;
        public TMP_Text RankLabel;

        public void Apply(CharacterSO character)
        {
            if (character == null) return;

            if (Prefab != null)
            {
                Prefab.Apply(character);
                return;
            }

            if (Portrait != null)
            {
                Portrait.sprite = character.portrait;
                Portrait.color = character.portrait != null ? Color.white : BattleHudPalette.PortraitFrame;
            }
            if (NameLabel != null)
                NameLabel.text = string.IsNullOrEmpty(character.characterName) ? character.name : character.characterName;
            if (RankLabel != null)
                RankLabel.text = character.starCount + "성  Lv." + character.level;
        }
    }

    // 위쪽 출전 자리. 카드는 미리 만들어 두고 비었을 때만 감춘다.
    private class PartySlot
    {
        public Image Frame;
        public CardVisual Card;
        public CardDragSource Drag;
        public TMP_Text EmptyLabel;
    }

    // 아래쪽 보유 목록의 카드 한 장.
    private class RosterSlot
    {
        public CharacterSO Character;
        public Button Button;
        public Image Frame;
        public CanvasGroup CardGroup;
        public GameObject Badge;
        public TMP_Text BadgeLabel;
        public GameObject DeadOverlay;
    }

    private Canvas canvas;
    private RectTransform canvasRect;
    private RectTransform dragLayer;
    private GameObject popupRoot;
    private AnnouncementBanner announcement;
    private TMP_FontAsset resolvedFont;
    private TMP_Text countLabel;
    private RectTransform openButton;
    private TMP_Text openButtonLabel;
    private readonly List<Image> partyTabs = new List<Image>();
    private readonly List<TMP_Text> partyTabLabels = new List<TMP_Text>();
    private readonly List<PartySlot> partySlots = new List<PartySlot>();
    private readonly List<RosterSlot> rosterSlots = new List<RosterSlot>();

    // 카드를 몇 장 깔아 두고 만들었는지. 보유 인원이 여기서 달라지면 슬롯을 새로 깔아야 한다.
    private int builtRosterCount = -1;
    // 창이 닫혀 있는 동안 인원이 바뀌었다. 다음에 열 때 다시 깐다.
    private bool rosterDirty;

    public bool IsOpen => popupRoot != null && popupRoot.activeSelf;

    private void Awake()
    {
        // 로스터 에셋은 시작 명단이다. RosterBootstrap이 없는 씬에서도 카드가 나오도록 여기서도 얹는다.
        // Seed는 이미 있는 캐릭터를 건너뛰므로 두 번 불려도 결과가 같다.
        // 창을 다시 지을 때(EnsureBuilt)가 아니라 여기서만 얹는 이유는, 합성으로 사라진 시작 멤버가
        // 다시 살아 돌아오면 안 되기 때문이다.
        if (roster != null) OwnedRoster.Seed(roster.Members);

        EnsureBuilt();
        SetOpen(false);
    }

    private void OnEnable()
    {
        EnsureBuilt();
        PartyDeck.Changed += Refresh;
        OwnedRoster.Changed += HandleRosterChanged;
        Refresh();
    }

    private void OnDisable()
    {
        PartyDeck.Changed -= Refresh;
        OwnedRoster.Changed -= HandleRosterChanged;
    }

    // 소환으로 늘거나 합성으로 줄면 칸 수도 카드 크기도 달라진다. 인원이 그대로면 다시 그리기만 한다.
    private void HandleRosterChanged()
    {
        if (builtRosterCount == OwnedRoster.Count)
        {
            Refresh();
            return;
        }

        // 인원이 바뀌면 칸 수도 카드 크기도 달라져 슬롯을 새로 깔아야 한다. 다만 창이 닫혀 있으면
        // 아무도 보지 않는 화면을 다시 짓는 셈이다 — 10연차 소환은 한 명씩 열 번 늘어나므로
        // 그때마다 캔버스를 통째로 다시 지으면 그대로 멈춤이 된다. 다음에 열 때로 미룬다.
        rosterDirty = true;
        if (IsOpen) RebuildRoster();
    }

    private void RebuildRoster()
    {
        rosterDirty = false;

        bool wasOpen = IsOpen;
        // canvas를 놓으면 EnsureBuilt가 남아 있는 캔버스를 치우고 처음부터 다시 만든다.
        canvas = null;
        EnsureBuilt();
        Refresh();
        SetOpen(wasOpen);
    }

    // 전투에서 돌아오면 편성에 죽은 캐릭터가 남아 있을 수 있다.
    private void Start()
    {
        PartyDeck.PruneFallen();
        Refresh();
    }

    private void Update()
    {
        // 배너는 MonoBehaviour가 아니라 코루틴을 쓸 수 없다. 시간을 여기서 굴린다.
        announcement?.Tick(Time.deltaTime);
    }

    // ---- 열고 닫기 --------------------------------------------------------

    public void Show()
    {
        EnsureBuilt();
        if (rosterDirty) RebuildRoster();
        Refresh();
        SetOpen(true);
        // 창을 열 때마다 조작법을 한 번 알려준다. 누르거나 2초가 지나면 사라진다.
        announcement?.Show(BannerText);
    }

    public void Hide()
    {
        SetOpen(false);
        // 창이 닫혔는데 안내만 화면에 남아 있으면 앞뒤가 맞지 않는다.
        announcement?.Clear();
    }

    public void Toggle()
    {
        if (IsOpen) Hide();
        else Show();
    }

    // 편성을 마치고 층 선택으로 넘어간다. 편성 창은 닫는다 — 두 창이 겹쳐 떠 있으면
    // 어느 쪽이 지금 조작 대상인지 알 수 없다.
    private void Depart()
    {
        if (floorSelect == null) floorSelect = FindAnyObjectByType<FloorSelectUI>(FindObjectsInactive.Include);
        if (floorSelect == null)
        {
            announcement?.Show("층 선택 창을 찾지 못했습니다.");
            return;
        }

        // 빈 파티로 넘어가면 층을 고른 뒤에야 막혀 되돌아와야 한다. 여기서 먼저 알린다.
        if (PartyDeck.Count == 0)
        {
            announcement?.Show($"{PartyDeck.ActiveIndex + 1}파티에 출전할 영웅이 없습니다.\n먼저 영웅을 편성해주세요.");
            return;
        }

        Hide();
        floorSelect.Show();
    }

    private void SetOpen(bool open)
    {
        if (popupRoot != null) popupRoot.SetActive(open);
        // 창이 화면을 덮으면 여는 버튼은 아래쪽에 삐져나온 조각으로만 보인다. 함께 감춘다.
        if (openButton != null) openButton.gameObject.SetActive(!open);
    }

    // ---- 드래그 앤 드롭 --------------------------------------------------

    // 끌고 다니는 동안 손끝을 따라다니는 카드. 드롭 판정을 가리지 않도록 레이캐스트를 끈다.
    public RectTransform CreateDragGhost(CharacterSO character)
    {
        if (dragLayer == null || character == null) return null;

        CardVisual visual = BuildCard(dragLayer, character);
        RectTransform rect = visual.Rect;
        rect.name = "DragGhost";
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one * partyCardScale;

        CanvasGroup group = rect.GetComponent<CanvasGroup>();
        if (group == null) group = rect.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0.85f;
        group.blocksRaycasts = false;

        return rect;
    }

    public void HandleDrop(CardDragSource source, int slotIndex)
    {
        if (source == null || source.Character == null) return;

        // 출전 슬롯에 떨어뜨렸다 — 새로 넣거나, 이미 편성돼 있으면 그 자리로 옮긴다.
        if (slotIndex >= 0)
        {
            if (WarnIfOccupied(source.Character)) return;

            PartyDeck.PlaceAt(slotIndex, source.Character);
            return;
        }

        // 보유 목록 위에 떨어뜨렸다 — 출전에서 빼는 동작.
        if (source.SlotIndex >= 0) PartyDeck.Remove(source.Character);
    }

    // 슬롯 밖 허공에 놓으면 출전에서 뺀다. 목록으로 되돌리려고 정확히 조준하게 만들 이유가 없다.
    // 보유 목록에서 집어 온 카드라면 아무 일도 일어나지 않는다.
    public void HandleDropOutside(CardDragSource source)
    {
        if (source == null || source.SlotIndex < 0) return;

        RemoveFromParty(source.SlotIndex);
    }

    public void RemoveFromParty(int slotIndex)
    {
        PartyDeck.RemoveAt(slotIndex);
    }

    // 다른 파티 소속이면 문구를 띄우고 true를 돌려준다(= 이 조작은 취소).
    private bool WarnIfOccupied(CharacterSO character)
    {
        if (!PartyDeck.IsInOtherParty(character)) return false;

        int party = PartyDeck.PartyIndexOf(character) + 1;
        announcement?.Show(string.Format(OccupiedMessageFormat, party));
        return true;
    }

    // 카드를 눌러 넣고 뺄 때도 같은 규칙을 지켜야 한다.
    private void ToggleFromRoster(CharacterSO character)
    {
        if (WarnIfOccupied(character)) return;

        PartyDeck.Toggle(character);
    }

    // ---- 화면 짓기 --------------------------------------------------------

    private float partyCardScale = 0.5f;
    private float rosterCardScale = 0.5f;

    private void EnsureBuilt()
    {
        if (canvas != null) return;

        PartyDeck.SetCapacity(deckCapacity);
        resolvedFont = HudFactory.ResolveFont(koreanFont, this);

        // 참조만 잃고 남아 있는 이전 캔버스를 먼저 치운다(도메인 리로드 대비).
        Transform stale = transform.Find("DeckBuildCanvas");
        if (stale != null) DestroyImmediate(stale.gameObject);

        partySlots.Clear();
        rosterSlots.Clear();
        BuildCanvas();
        BuildOpenButton();
        BuildPopup();

        // 안내 배너는 창이 아니라 캔버스에 매단다. 창을 닫아도 같은 자리(화면 정중앙)에 뜬다.
        announcement = AnnouncementBanner.Create(canvasRect, resolvedFont, bannerSprite, null,
            Mathf.Min(900f, CanvasSize().x * 0.55f));

        BuildDragLayer();
    }

    private void BuildCanvas()
    {
        var canvasGo = new GameObject("DeckBuildCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.layer = LayerMask.NameToLayer("UI");

        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 91;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasRect = (RectTransform)canvasGo.transform;
    }

    // 화면을 꽉 채우는 창이라 실제 캔버스 크기를 알아야 배치를 계산할 수 있다.
    // 방금 만든 캔버스는 아직 크기가 0이므로 한 번 갱신시켜 읽는다.
    private Vector2 CanvasSize()
    {
        Canvas.ForceUpdateCanvases();
        Vector2 size = canvasRect != null ? canvasRect.rect.size : Vector2.zero;
        if (size.x < 1f || size.y < 1f) return ReferenceResolution;
        return size;
    }

    // 끌고 다니는 카드가 창 밖으로 나가도 잘리지 않도록 캔버스 최상단에 따로 층을 둔다.
    private void BuildDragLayer()
    {
        dragLayer = HudFactory.CreateGroup(canvasRect, "DragLayer");
        Stretch(dragLayer);
        dragLayer.pivot = new Vector2(0.5f, 0.5f);
        dragLayer.SetAsLastSibling();

        var group = dragLayer.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
    }

    // 평소에 화면 아래에 떠 있는 버튼. 이것만 누르면 편성 창이 열린다.
    private void BuildOpenButton()
    {
        // 여는 자리는 마을의 시공의 틈이다. 화면에 늘 떠 있는 버튼은 기본으로 두지 않는다.
        if (!showOpenButton) return;

        float height = OpenButtonWidth / BannerAspect();

        RectTransform button;
        if (bannerSprite != null)
        {
            Image image = HudFactory.CreateImage(canvasRect, "OpenPartyButton", Color.white);
            image.sprite = bannerSprite;
            image.raycastTarget = true;
            button = image.rectTransform;
            AttachButton(image, Show);
        }
        else
        {
            Image border = HudFactory.CreateImage(canvasRect, "OpenPartyButton", BattleHudPalette.Mvp);
            border.raycastTarget = true;
            Image body = HudFactory.CreateImage(border.rectTransform, "Body", new Color(0.04f, 0.04f, 0.06f, 0.97f));
            Stretch(body.rectTransform);
            body.rectTransform.offsetMin = new Vector2(3f, 3f);
            body.rectTransform.offsetMax = new Vector2(-3f, -3f);
            button = border.rectTransform;
            AttachButton(border, Show);
        }

        button.anchorMin = new Vector2(0.5f, 0f);
        button.anchorMax = new Vector2(0.5f, 0f);
        button.pivot = new Vector2(0.5f, 0f);
        button.sizeDelta = new Vector2(OpenButtonWidth, height);
        button.anchoredPosition = new Vector2(0f, 24f);
        openButton = button;

        openButtonLabel = HudFactory.CreateText(button, "Label", resolvedFont, 26f, BattleHudPalette.PanelText);
        Stretch(openButtonLabel.rectTransform);
        openButtonLabel.rectTransform.offsetMin = new Vector2(OpenButtonWidth * 0.14f, height * 0.28f);
        openButtonLabel.rectTransform.offsetMax = new Vector2(-OpenButtonWidth * 0.14f, -height * 0.28f);
        openButtonLabel.enableAutoSizing = true;
        openButtonLabel.fontSizeMin = 14f;
        openButtonLabel.fontSizeMax = 26f;
    }

    private void BuildPopup()
    {
        RectTransform popup = HudFactory.CreateGroup(canvasRect, "Popup");
        Stretch(popup);
        popup.pivot = new Vector2(0.5f, 0.5f);
        popupRoot = popup.gameObject;

        // 배경막과 창은 형제로 둔다. 창을 자식으로 넣으면 창 안을 누른 클릭이
        // 배경막까지 거슬러 올라가 창이 곧바로 닫힌다.
        Image backdrop = HudFactory.CreateImage(popup, "Backdrop", BattleHudPalette.PanelBackdrop);
        backdrop.raycastTarget = true;
        Stretch(backdrop.rectTransform);
        AttachButton(backdrop, Hide, Selectable.Transition.None);

        Image panel = HudFactory.CreateImage(popup, "Panel", BattleHudPalette.PanelBody);
        // 창 안을 누른 클릭이 배경막으로 내려가지 않도록 여기서 받아 둔다.
        panel.raycastTarget = true;
        RectTransform panelRect = panel.rectTransform;
        Stretch(panelRect);
        panelRect.offsetMin = new Vector2(screenMargin, screenMargin);
        panelRect.offsetMax = new Vector2(-screenMargin, -screenMargin);

        BuildPanelContent(panelRect, CanvasSize() - new Vector2(screenMargin * 2f, screenMargin * 2f));
    }

    private void BuildPanelContent(RectTransform panel, Vector2 panelSize)
    {
        // 명단은 에셋이 아니라 런타임 쪽(OwnedRoster)이 정답이다. 소환으로 늘고 합성으로 줄기 때문이다.
        IReadOnlyList<CharacterSO> members = OwnedRoster.Members;
        if (roster == null && members.Count == 0)
            Debug.LogWarning("[DeckBuildUI] 로스터 에셋이 지정되지 않아 보여줄 카드가 없습니다.", this);

        int rosterCount = members.Count;
        builtRosterCount = rosterCount;
        int capacity = Mathf.Max(1, deckCapacity);
        float availableWidth = panelSize.x - PanelPadding * 2f;

        // 출전 슬롯은 한 줄에 다 들어가야 한다. 폭에 맞춰 카드 크기를 정한다.
        partyCardScale = Mathf.Min(maxCardScale,
            (availableWidth - (capacity - 1) * cardSpacing) / capacity / FallbackCardSize.x);
        Vector2 partySlot = SlotSize(partyCardScale);

        // 안내 문구는 창 안에 붙박이로 두지 않는다. 화면 정중앙 배너로 잠깐 떴다 사라진다.
        float y = PanelPadding;
        BuildHeader(panel, availableWidth, y);
        y += HeaderHeight + 10f;

        BuildPartySlots(panel, capacity, partySlot, y);
        y += partySlot.y + 18f;

        TMP_Text rosterLabel = HudFactory.CreateText(panel, "RosterLabel", resolvedFont, 24f, BattleHudPalette.PanelText);
        rosterLabel.alignment = TextAlignmentOptions.Left;
        SetTopLeft(rosterLabel.rectTransform, new Vector2(availableWidth, SectionLabelHeight), new Vector2(PanelPadding, -y));
        rosterLabel.text = "보유 영웅";
        y += SectionLabelHeight + 6f;

        float rosterHeight = Mathf.Max(partySlot.y, panelSize.y - PanelPadding - y);
        BuildRosterArea(panel, members, rosterCount, availableWidth, rosterHeight, y);
    }

    private void BuildHeader(RectTransform panel, float width, float y)
    {
        // 1파티 ~ 3파티 탭. 누르면 그 파티를 편성 대상으로 바꾼다.
        partyTabs.Clear();
        partyTabLabels.Clear();
        for (int i = 0; i < PartyDeck.PartyCount; i++)
        {
            Image tab = HudFactory.CreateImage(panel, "PartyTab_" + i, BattleHudPalette.PanelBackdrop);
            // HudFactory는 표시 전용 HUD가 기본이라 레이캐스트를 꺼 둔다. 버튼은 도로 켜야 한다.
            tab.raycastTarget = true;
            SetTopLeft(tab.rectTransform, new Vector2(PartyTabWidth, HeaderHeight),
                new Vector2(PanelPadding + i * (PartyTabWidth + 6f), -y));

            // 클로저가 반복 변수를 붙잡지 않도록 지역 변수에 복사해 넘긴다.
            int captured = i;
            AttachButton(tab, () => PartyDeck.SetActive(captured));

            TMP_Text label = HudFactory.CreateText(tab.rectTransform, "Label", resolvedFont, 22f, BattleHudPalette.PanelText);
            Stretch(label.rectTransform);
            label.text = (i + 1) + "파티";

            partyTabs.Add(tab);
            partyTabLabels.Add(label);
        }

        float tabsWidth = PartyDeck.PartyCount * (PartyTabWidth + 6f);
        countLabel = HudFactory.CreateText(panel, "Count", resolvedFont, 26f, BattleHudPalette.Mvp);
        countLabel.alignment = TextAlignmentOptions.Left;
        SetTopLeft(countLabel.rectTransform, new Vector2(width - tabsWidth - 70f, HeaderHeight),
            new Vector2(PanelPadding + tabsWidth + 14f, -y));

        // 닫기는 글자 대신 X 하나. 정사각형으로 두어야 X가 가운데에 온다.
        Image closeBackground = HudFactory.CreateImage(panel, "Close", BattleHudPalette.PanelBackdrop);
        closeBackground.raycastTarget = true;
        SetTopLeft(closeBackground.rectTransform, new Vector2(HeaderHeight, HeaderHeight),
            new Vector2(PanelPadding + width - HeaderHeight, -y));
        AttachButton(closeBackground, Hide);

        // 편성을 마치면 여기서 바로 층을 고른다. 창을 닫고 마을에서 다시 시공의 틈을 눌러야 하면
        // 편성 → 출전이 한 흐름으로 이어지지 않는다.
        Image departBackground = HudFactory.CreateImage(panel, "Depart", SelectedRosterFrame);
        departBackground.raycastTarget = true;
        SetTopLeft(departBackground.rectTransform, new Vector2(DepartWidth, HeaderHeight),
            new Vector2(PanelPadding + width - HeaderHeight - 10f - DepartWidth, -y));
        AttachButton(departBackground, Depart);

        TMP_Text departLabel = HudFactory.CreateText(departBackground.rectTransform, "Label", resolvedFont, 26f, BattleHudPalette.Mvp);
        Stretch(departLabel.rectTransform);
        departLabel.text = "출전하기";
        TMP_Text closeLabel = HudFactory.CreateText(closeBackground.rectTransform, "Label", resolvedFont, 24f, BattleHudPalette.PanelText);
        Stretch(closeLabel.rectTransform);
        // 곱셈 기호(U+2715 등)는 NotoSansKR 아틀라스에 없어 네모로 그려진다. 알파벳 X를 쓴다.
        closeLabel.text = "X";
    }

    private void BuildPartySlots(RectTransform panel, int capacity, Vector2 slotSize, float y)
    {
        float rowWidth = capacity * slotSize.x + (capacity - 1) * cardSpacing;
        RectTransform row = HudFactory.CreateGroup(panel, "PartySlots");
        SetTopCenter(row, new Vector2(rowWidth, slotSize.y), y);

        for (int i = 0; i < capacity; i++)
        {
            Image frame = HudFactory.CreateImage(row, "PartySlot_" + i, BattleHudPalette.PortraitFrame);
            // 드롭을 받으려면 레이캐스트 대상이어야 한다.
            frame.raycastTarget = true;
            SetTopLeft(frame.rectTransform, slotSize, new Vector2(i * (slotSize.x + cardSpacing), 0f));

            frame.gameObject.AddComponent<CardDropTarget>().Bind(this, i);

            TMP_Text empty = HudFactory.CreateText(frame.rectTransform, "Empty", resolvedFont, 20f, BattleHudPalette.Dying);
            Stretch(empty.rectTransform);
            empty.text = "여기로\n드래그";

            CardVisual card = BuildCard(frame.rectTransform, null);
            CenterInSlot(card.Rect, partyCardScale);

            var drag = frame.gameObject.AddComponent<CardDragSource>();
            drag.Bind(this, null, i);

            partySlots.Add(new PartySlot
            {
                Frame = frame,
                Card = card,
                Drag = drag,
                EmptyLabel = empty,
            });
        }
    }

    private void BuildRosterArea(RectTransform panel, IReadOnlyList<CharacterSO> members, int count,
        float width, float height, float y)
    {
        // 목록 바닥 전체가 "파티에서 빼는 자리"다. 카드 위에 떨어뜨려도 이벤트가 여기까지 올라온다.
        Image area = HudFactory.CreateImage(panel, "RosterArea", new Color(1f, 1f, 1f, 0.03f));
        area.raycastTarget = true;
        SetTopLeft(area.rectTransform, new Vector2(width, height), new Vector2(PanelPadding, -y));
        area.gameObject.AddComponent<CardDropTarget>().Bind(this, CardDragSource.RosterSlot);

        if (count == 0)
        {
            TMP_Text empty = HudFactory.CreateText(area.rectTransform, "Empty", resolvedFont, 24f, BattleHudPalette.Dying);
            Stretch(empty.rectTransform);
            empty.text = "보유한 캐릭터가 없습니다";
            return;
        }

        // 인원이 늘어도 창 밖으로 넘치지 않도록 열 수와 카드 크기를 여기서 정한다.
        int columns;
        rosterCardScale = FitCards(count, new Vector2(width, height), out columns);
        Vector2 slotSize = SlotSize(rosterCardScale);

        int rows = Mathf.CeilToInt(count / (float)columns);
        float gridWidth = columns * slotSize.x + (columns - 1) * cardSpacing;
        float gridHeight = rows * slotSize.y + (rows - 1) * cardSpacing;
        Vector2 origin = new Vector2((width - gridWidth) * 0.5f, -(height - gridHeight) * 0.5f);

        for (int i = 0; i < count; i++)
        {
            CharacterSO so = members[i];
            if (so == null) continue;

            int col = i % columns;
            int row = i / columns;
            Vector2 position = origin + new Vector2(col * (slotSize.x + cardSpacing), -row * (slotSize.y + cardSpacing));

            rosterSlots.Add(BuildRosterSlot(area.rectTransform, so, slotSize, position, i));
        }
    }

    // 주어진 넓이에 카드 count장을 가장 크게 넣을 수 있는 배율과 열 수를 찾는다.
    private float FitCards(int count, Vector2 area, out int columns)
    {
        return CardLayout.Fit(count, area, cardSpacing, FramePadding, maxCardScale, out columns);
    }

    private RosterSlot BuildRosterSlot(RectTransform parent, CharacterSO character, Vector2 slotSize, Vector2 position, int index)
    {
        Image frame = HudFactory.CreateImage(parent, "Card_" + index, BattleHudPalette.PortraitFrame);
        frame.raycastTarget = true;
        SetTopLeft(frame.rectTransform, slotSize, position);

        var button = frame.gameObject.AddComponent<Button>();
        button.targetGraphic = frame;
        // 클로저가 반복 변수를 붙잡지 않도록 지역 변수에 복사해 넘긴다.
        CharacterSO captured = character;
        button.onClick.AddListener(() => ToggleFromRoster(captured));

        frame.gameObject.AddComponent<CardDragSource>().Bind(this, character, CardDragSource.RosterSlot);

        CardVisual card = BuildCard(frame.rectTransform, character);
        CenterInSlot(card.Rect, rosterCardScale);

        CanvasGroup group = card.Rect.GetComponent<CanvasGroup>();
        if (group == null) group = card.Rect.gameObject.AddComponent<CanvasGroup>();

        // 출전 순서. 전투 씬의 스폰 자리가 이 번호를 따른다.
        Image badge = HudFactory.CreateImage(frame.rectTransform, "Order", BattleHudPalette.Mvp);
        SetTopLeft(badge.rectTransform, new Vector2(30f, 30f), new Vector2(6f, -6f));
        TMP_Text badgeLabel = HudFactory.CreateText(badge.rectTransform, "Label", resolvedFont, 20f, Color.black);
        Stretch(badgeLabel.rectTransform);
        badge.gameObject.SetActive(false);

        Image deadOverlay = HudFactory.CreateImage(frame.rectTransform, "Dead", new Color(0f, 0f, 0f, 0.66f));
        Stretch(deadOverlay.rectTransform);
        TMP_Text deadLabel = HudFactory.CreateText(deadOverlay.rectTransform, "Label", resolvedFont, 26f, BattleHudPalette.Defeat);
        Stretch(deadLabel.rectTransform);
        deadLabel.text = "사망";
        deadOverlay.gameObject.SetActive(false);

        return new RosterSlot
        {
            Character = character,
            Button = button,
            Frame = frame,
            CardGroup = group,
            Badge = badge.gameObject,
            BadgeLabel = badgeLabel,
            DeadOverlay = deadOverlay.gameObject,
        };
    }

    private void Refresh()
    {
        for (int i = 0; i < partySlots.Count; i++)
        {
            PartySlot slot = partySlots[i];
            CharacterSO member = i < PartyDeck.Count ? PartyDeck.Members[i] : null;

            if (member != null) slot.Card.Apply(member);
            slot.Card.Rect.gameObject.SetActive(member != null);
            slot.EmptyLabel.gameObject.SetActive(member == null);
            slot.Frame.color = member != null ? BattleHudPalette.Mvp : BattleHudPalette.PortraitFrame;
            slot.Drag.Bind(this, member, i);
        }

        for (int i = 0; i < rosterSlots.Count; i++)
        {
            RosterSlot slot = rosterSlots[i];
            if (slot == null || slot.Frame == null) continue;

            bool fallen = PartyRoster.IsFallen(slot.Character);
            int order = PartyDeck.IndexOf(slot.Character);
            bool selected = order >= 0;
            // 다른 파티에 들어 있는 캐릭터. 한 사람은 한 파티에만 들어가므로 여기서 알려줘야
            // "왜 이 카드가 빈 파티에 안 들어가지"를 헤매지 않는다.
            int otherParty = selected ? -1 : PartyDeck.PartyIndexOf(slot.Character);

            // 출전 중인 카드에 금테를 두르면 눌러 놓은 카드 위로 금색이 배어 나와 탁해진다.
            // 금테는 위쪽 출전 슬롯의 몫이고, 여기서는 어두운 금색으로만 표시한다.
            slot.Frame.color = fallen ? BattleHudPalette.DeadTint
                : selected ? SelectedRosterFrame
                : BattleHudPalette.PortraitFrame;

            // 이미 출전 슬롯에 올라간 카드와 다른 파티에 있는 카드는 눌러 둔다.
            if (slot.CardGroup != null)
                slot.CardGroup.alpha = fallen ? 0.4f : selected ? 0.55f : otherParty >= 0 ? 0.5f : 1f;

            if (slot.Badge != null)
            {
                bool showBadge = selected || otherParty >= 0;
                slot.Badge.SetActive(showBadge);
                if (showBadge && slot.BadgeLabel != null)
                {
                    // 활성 파티면 출전 순서, 다른 파티면 그 파티 번호.
                    slot.BadgeLabel.text = selected ? (order + 1).ToString() : (otherParty + 1) + "P";
                    slot.BadgeLabel.color = selected ? Color.black : BattleHudPalette.PanelText;
                }
                var badgeImage = slot.Badge.GetComponent<Image>();
                if (badgeImage != null)
                    badgeImage.color = selected ? BattleHudPalette.Mvp : BattleHudPalette.PanelBody;
            }

            if (slot.DeadOverlay != null) slot.DeadOverlay.SetActive(fallen);
            // 영구 사망한 캐릭터는 골라도 출전하지 못하므로 아예 누르지 못하게 막는다.
            if (slot.Button != null) slot.Button.interactable = !fallen;
        }

        // 탭: 지금 만지는 파티만 금색으로, 나머지는 인원수만 알려준다.
        for (int i = 0; i < partyTabs.Count; i++)
        {
            bool active = i == PartyDeck.ActiveIndex;
            partyTabs[i].color = active ? BattleHudPalette.Mvp : BattleHudPalette.PanelBackdrop;
            partyTabLabels[i].color = active ? Color.black : BattleHudPalette.PanelText;
            partyTabLabels[i].text = (i + 1) + "파티 " + PartyDeck.CountOf(i);
        }

        string count = PartyDeck.Count + " / " + PartyDeck.Capacity;
        if (countLabel != null) countLabel.text = (PartyDeck.ActiveIndex + 1) + "파티 출전 " + count;
        if (openButtonLabel != null)
            openButtonLabel.text = "파티 편성\n" + (PartyDeck.ActiveIndex + 1) + "파티 " + count;
    }

    // ---- 카드 만들기 ------------------------------------------------------

    private CardVisual BuildCard(RectTransform parent, CharacterSO character)
    {
        if (cardPrefab != null)
        {
            CharacterCard card = Instantiate(cardPrefab, parent);
            card.name = "Card";
            if (character != null) card.Apply(character);

            var rect = card.transform as RectTransform;
            if (rect != null) return new CardVisual { Rect = rect, Prefab = card };

            // UI 프리팹이 아니면 캔버스 안에서 자리를 잡을 수 없다. 기본 카드로 대신한다.
            Debug.LogWarning("[DeckBuildUI] 카드 프리팹이 UI(RectTransform)가 아니라 기본 카드를 씁니다.", this);
            Destroy(card.gameObject);
        }

        return BuildFallbackCard(parent, character);
    }

    // 카드 프리팹을 지정하지 않았을 때의 대비책. 초상화와 이름, 등급만 있는 최소한의 카드.
    private CardVisual BuildFallbackCard(RectTransform parent, CharacterSO character)
    {
        Image background = HudFactory.CreateImage(parent, "Card", BattleHudPalette.PanelBody);
        RectTransform rect = background.rectTransform;
        rect.sizeDelta = FallbackCardSize;

        Image portrait = HudFactory.CreateImage(rect, "Portrait", BattleHudPalette.PortraitFrame);
        portrait.preserveAspect = true;
        SetTopLeft(portrait.rectTransform, new Vector2(260f, 260f), new Vector2(20f, -20f));

        TMP_Text nameLabel = HudFactory.CreateText(rect, "Name", resolvedFont, 34f, BattleHudPalette.PanelText);
        SetTopLeft(nameLabel.rectTransform, new Vector2(260f, 48f), new Vector2(20f, -296f));

        // 별 기호(U+2605)는 NotoSansKR 아틀라스에 없어 네모로 그려진다. 숫자로 적는다.
        TMP_Text rankLabel = HudFactory.CreateText(rect, "Rank", resolvedFont, 30f, BattleHudPalette.Mvp);
        SetTopLeft(rankLabel.rectTransform, new Vector2(260f, 40f), new Vector2(20f, -352f));

        var visual = new CardVisual
        {
            Rect = rect,
            Portrait = portrait,
            NameLabel = nameLabel,
            RankLabel = rankLabel,
        };
        visual.Apply(character);
        return visual;
    }

    private Vector2 SlotSize(float scale)
    {
        return CardLayout.SlotSize(scale, FramePadding);
    }

    private static void CenterInSlot(RectTransform card, float scale)
    {
        CardLayout.CenterInSlot(card, scale);
    }

    private float BannerAspect()
    {
        if (bannerSprite == null || bannerSprite.rect.height <= 0f) return BannerFallbackAspect;
        return bannerSprite.rect.width / bannerSprite.rect.height;
    }

    private static Button AttachButton(Image target, UnityEngine.Events.UnityAction action,
        Selectable.Transition transition = Selectable.Transition.ColorTint)
    {
        var button = target.gameObject.AddComponent<Button>();
        button.transition = transition;
        if (transition == Selectable.Transition.ColorTint) button.targetGraphic = target;
        button.onClick.AddListener(action);
        return button;
    }

    private static void SetTopLeft(RectTransform rect, Vector2 size, Vector2 offset)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = offset;
    }

    // 화면 폭이 달라져도 가운데를 유지해야 하는 것들(배너, 출전 슬롯 줄).
    private static void SetTopCenter(RectTransform rect, Vector2 size, float y)
    {
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(0f, -y);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
