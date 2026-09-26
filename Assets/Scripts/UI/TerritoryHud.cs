using TMPro;
using UnityEngine;

// 마을 화면 왼쪽 아래, 편성 버튼(PartyBarHud) 오른쪽에 늘 떠 있는 동그란 영지 관리 버튼.
//
//     ╭────╮ ╭────╮
//    │ 편성 │ │ 영지 │
//     ╰────╯ ╰────╯
//
// 누르면 영지 관리 화면(TerritoryUI)이 열린다. 편성 버튼과 같은 모양·같은 층(90)이다 —
// 마을 풍경 위에 떠 있는 버튼은 어두운 반투명 판(UiButtonStyle.Glass)으로 통일한다.
[DisallowMultipleComponent]
public class TerritoryHud : MonoBehaviour
{
    [Header("Font")]
    [Tooltip("한글 폰트. 비워두면 프로젝트 기본 폰트를 찾아 쓴다.")]
    [SerializeField] private TMP_FontAsset koreanFont;

    [Header("Territory")]
    [Tooltip("누르면 열릴 영지 관리 화면. 비워두면 씬에서 찾는다.")]
    [SerializeField] private TerritoryUI territory;

    private const string CanvasName = "TerritoryHudCanvas";
    private const int SortingOrder = 90;

    // 편성 버튼(왼쪽 끝 32, 지름 136, 오른쪽 어깨에 인원 배지가 약 25 걸친다)의 오른쪽.
    private const float EdgeMargin = 32f;
    private const float Diameter = 136f;
    private const float Left = EdgeMargin + Diameter + 48f;

    private RectTransform canvasRect;

    private void Awake()
    {
        UiKit.UseFont(HudFactory.ResolveFont(koreanFont, this));

        // 참조만 잃고 남아 있는 이전 캔버스를 먼저 치운다(도메인 리로드 대비).
        Transform stale = transform.Find(CanvasName);
        if (stale != null) DestroyImmediate(stale.gameObject);

        HudFactory.CreateScreenCanvas(transform, CanvasName, SortingOrder, out canvasRect);

        UiButton button = UiButton.CreateIcon(canvasRect, "TerritoryButton", UiSprites.Icon(UiSprites.Glyph.Castle), true,
            Diameter, UiButtonStyle.Glass, Open);
        UiKit.BottomLeft(button.Rect, Left, EdgeMargin, Diameter, Diameter);

        // 기호는 위쪽에, 라벨은 그 아래에(편성 버튼과 같은 비율).
        UiKit.Fill(button.Icon.rectTransform, Diameter * 0.28f, Diameter * 0.14f, Diameter * 0.28f, Diameter * 0.42f);
        button.SetLabel("영지");
        UiKit.Fill(button.Label.rectTransform, 0f, Diameter * 0.58f, 0f, Diameter * 0.14f);
    }

    private void Open()
    {
        if (territory == null) territory = FindAnyObjectByType<TerritoryUI>(FindObjectsInactive.Include);
        if (territory == null)
        {
            Debug.LogWarning("[TerritoryHud] 영지 관리 화면을 찾지 못했습니다.", this);
            return;
        }

        territory.Show();
    }
}
