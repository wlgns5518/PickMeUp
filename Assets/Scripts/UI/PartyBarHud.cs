using System.Collections.Generic;
using TMPro;
using UnityEngine;

// 마을 화면 왼쪽 아래에 늘 떠 있는 출전 파티 칸.
//
//   ┌ 1파티 ──────────────── [ 편성 ] ┐
//   │ [칸][칸][칸][칸][칸]             │
//   └──────────────────────────────────┘
//
// 시공의 틈으로 들어가면 여기 보이는 파티가 그대로 출전한다 — 떠나기 전에 누구를 데려가는지
// 마을에서 바로 보이게 하려는 것이다. 칸이든 버튼이든 누르면 편성 화면(DeckBuildUI)이 열린다.
//
// 상단바(TopBarHud)와 같은 층(90)에 그린다. 시설 화면(91~99)은 화면을 통째로 덮으므로 그 위에 겹치지 않는다.
[DisallowMultipleComponent]
public class PartyBarHud : MonoBehaviour
{
    [Header("Font")]
    [Tooltip("한글 폰트. 비워두면 프로젝트 기본 폰트를 찾아 쓴다.")]
    [SerializeField] private TMP_FontAsset koreanFont;

    [Header("Party")]
    [Tooltip("누르면 열릴 파티 편성 화면. 비워두면 씬에서 찾는다.")]
    [SerializeField] private DeckBuildUI deckBuild;

    private const string CanvasName = "PartyBarCanvas";
    private const int SortingOrder = 90;

    private const float EdgeMargin = 24f;
    private const float Padding = UiTheme.Space5;
    private const float SlotSize = UiTheme.SlotSmall;
    private const float SlotGap = UiTheme.Space3;
    private const float HeaderHeight = 52f;
    private const float EditWidth = 140f;

    private RectTransform canvasRect;
    private TMP_Text partyName;
    private readonly List<UiSlot> slots = new List<UiSlot>();

    private void Awake()
    {
        UiKit.UseFont(HudFactory.ResolveFont(koreanFont, this));

        // 참조만 잃고 남아 있는 이전 캔버스를 먼저 치운다(도메인 리로드 대비).
        Transform stale = transform.Find(CanvasName);
        if (stale != null) DestroyImmediate(stale.gameObject);

        HudFactory.CreateScreenCanvas(transform, CanvasName, SortingOrder, out canvasRect);
        Build();
    }

    private void OnEnable()
    {
        PartyDeck.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        PartyDeck.Changed -= Refresh;
    }

    // 전투에서 돌아오면 쓰러진 영웅이 편성에서 빠져 있을 수 있다. 씬이 뜨고 한 번 더 읽는다.
    private void Start() => Refresh();

    private void Build()
    {
        int capacity = Mathf.Max(1, PartyDeck.Capacity);
        float width = Padding * 2f + capacity * SlotSize + (capacity - 1) * SlotGap;
        float height = Padding * 2f + HeaderHeight + SlotSize;

        UiKit.Surface card = UiKit.Panel(canvasRect, "PartyBar", UiTheme.Surface, UiTheme.RadiusL, UiTheme.Border, 16);
        UiKit.BottomLeft(card.Rect, EdgeMargin, EdgeMargin, width, height);
        card.Fill.raycastTarget = true;

        partyName = UiKit.Text(card.Rect, "PartyName", string.Empty, UiTheme.FontHeading, UiTheme.TextPrimary);
        UiKit.TopLeft(partyName.rectTransform, Padding, Padding, width - Padding * 2f - EditWidth, 40f);

        UiButton edit = UiButton.Create(card.Rect, "Edit", "편성", UiButtonStyle.Primary, UiButtonSize.Small, OpenDeck);
        UiKit.TopRight(edit.Rect, Padding, Padding, EditWidth, UiTheme.ButtonSmall);

        float top = Padding + HeaderHeight;
        for (int i = 0; i < capacity; i++)
        {
            UiSlot slot = UiSlot.Create(card.Rect, "Party_" + i, SlotSize);
            UiKit.TopLeft(slot.Rect, Padding + i * (SlotSize + SlotGap), top, SlotSize, SlotSize);
            slot.Clicked += OpenDeck;
            slots.Add(slot);
        }
    }

    private void Refresh()
    {
        if (partyName == null) return;

        partyName.text = $"{PartyDeck.ActiveIndex + 1}파티  {UiTheme.Paint($"{PartyDeck.Count} / {PartyDeck.Capacity}", UiTheme.TextSecondary)}";

        IReadOnlyList<CharacterSO> members = PartyDeck.Members;
        for (int i = 0; i < slots.Count; i++)
        {
            if (i < members.Count) slots[i].SetContent(UiSlotContents.Hero(members[i]));
            else slots[i].SetEmpty(string.Empty);
        }
    }

    private void OpenDeck()
    {
        if (deckBuild == null) deckBuild = FindAnyObjectByType<DeckBuildUI>(FindObjectsInactive.Include);
        if (deckBuild == null)
        {
            Debug.LogWarning("[PartyBarHud] 파티 편성 화면을 찾지 못했습니다.", this);
            return;
        }

        deckBuild.Show();
    }
}
