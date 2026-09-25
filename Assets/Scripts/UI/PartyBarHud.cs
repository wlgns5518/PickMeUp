using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 마을 화면 왼쪽 아래에 늘 떠 있는 동그란 파티 편성 버튼.
//
//     ╭────╮3/5
//    │ 사람 │
//    │ 편성 │
//     ╰────╯
//
// 누르면 편성 화면(DeckBuildUI)이 열린다. 시공의 틈으로 들어가면 여기서 짠 파티가 그대로 출전하므로
// 몇 명을 데려가는지만 배지로 붙여 둔다 — 누구인지는 편성 화면이 보여 준다.
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

    private const float EdgeMargin = 32f;
    private const float Diameter = 136f;
    private const float BadgeWidth = 72f;
    private const float BadgeHeight = 36f;

    private RectTransform canvasRect;
    private TMP_Text count;

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
        // 상단바처럼 어두운 판으로 — 뒤 풍경이 비치는 반투명이라 그림자는 깔지 않는다(깔면 판이 도로 불투명해 보인다).
        UiButton button = UiButton.CreateIcon(canvasRect, "PartyButton", UiSprites.Icon(UiSprites.Glyph.Party), true,
            Diameter, UiButtonStyle.Glass, OpenDeck);
        UiKit.BottomLeft(button.Rect, EdgeMargin, EdgeMargin, Diameter, Diameter);

        // 기호는 위쪽에, 라벨은 그 아래에.
        UiKit.Fill(button.Icon.rectTransform, Diameter * 0.28f, Diameter * 0.14f, Diameter * 0.28f, Diameter * 0.42f);
        button.SetLabel("편성");
        UiKit.Fill(button.Label.rectTransform, 0f, Diameter * 0.58f, 0f, Diameter * 0.14f);

        // 데려가는 인원. 버튼 오른쪽 위 어깨에 걸친다.
        UiKit.Surface badge = UiKit.Panel(button.Rect, "Count", UiTheme.SurfaceSunken,
            Mathf.RoundToInt(BadgeHeight * 0.5f) - 2, UiTheme.BorderStrong);
        UiKit.TopRight(badge.Rect, -BadgeWidth * 0.35f, -6f, BadgeWidth, BadgeHeight);
        count = UiKit.Text(badge.Rect, "Label", string.Empty, UiTheme.FontCaption, UiTheme.TextPrimary, TextAlignmentOptions.Center);
    }

    private void Refresh()
    {
        if (count == null) return;
        count.text = $"{PartyDeck.Count}/{PartyDeck.Capacity}";
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
