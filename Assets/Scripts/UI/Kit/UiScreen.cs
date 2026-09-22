using UnityEngine;
using UnityEngine.UI;

// 시설 화면(소환소·합성소·장비창·제작소)의 공통 뼈대 — 화면을 덮는 배경, 머리줄, 내용 칸, 팝업 층, 알림.
//
//   ┌ ‹  제목 ──────────────────────────────── [골드] [젬] ┐   머리줄: 뒤로가기 · 무엇을 하는 화면인지 · 보유 재화
//   │  한 줄 설명                                            │
//   ├───────────────────────────────────────────────────────┤
//   │                     내용(BuildContent)                 │
//   └───────────────────────────────────────────────────────┘
//
// 화면마다 다른 것은 제목·설명·보여 줄 재화·내용뿐이다. 나머지(배경색, 머리줄 높이, 뒤로가기 자리, 알림 자리,
// 팝업 배경막)는 여기서 한 번만 정한다 — 어느 시설에 들어가든 같은 게임의 화면으로 보이게.
//
// 여닫기 규칙(캔버스 한 번 세우기, 다시 짓기, 명단이 바뀌었을 때 미뤄 두기)은 FacilityWindow를 그대로 쓴다.
public abstract class UiScreen : FacilityWindow
{
    private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

    // 내용 칸. 머리줄 아래, 화면 가장자리에서 ScreenPadding만큼 들어간 곳.
    protected RectTransform content;
    protected Vector2 contentSize;

    // 팝업은 여기 붙인다. 내용보다 뒤에 만들어져 내용 위에 그려진다.
    protected RectTransform overlay;
    protected UiToast toast;
    protected UiConfirm confirm;

    protected abstract string Title { get; }

    // 무엇을 하는 화면인지 한 줄로. 제목 아래에 흐린 글자로 붙는다.
    protected abstract string Subtitle { get; }

    // 머리줄 오른쪽에 보여 줄 재화. 이 화면에서 쓰는 것만 둔다.
    protected virtual Currency[] HeaderCurrencies => new[] { Currency.Gold };

    // 내용을 짓는다. size는 내용 칸의 크기(캔버스가 막 만들어져 rect가 0일 때를 대비해 계산해 넘긴다).
    protected abstract void BuildContent(RectTransform root, Vector2 size);

    // 팝업처럼 overlay가 있어야 만들 수 있는 것.
    protected virtual void BuildOverlays() { }

    // 머리줄의 뒤로가기. 보통은 화면을 닫고 마을로 돌아간다. 앞 단계가 있는 화면(층 선택 → 파티 편성)은 그리로 돌아간다.
    protected virtual void OnBack() => Hide();

    protected override void BuildWindow()
    {
        UiKit.UseFont(resolvedFont);
        BuildCanvas();

        RectTransform root = UiKit.Node(canvasRect, "Screen");
        UiKit.Fill(root);
        popupRoot = root.gameObject;

        // 화면을 통째로 덮는 불투명한 배경. 뒤쪽 마을로 새는 클릭도 여기서 막힌다.
        Image background = UiKit.Image(root, "Background", null, Color.white, false);
        background.raycastTarget = true;
        background.gameObject.AddComponent<UiGradient>().Set(UiTheme.ScreenTop, UiTheme.ScreenBottom);

        BuildHeader(root);

        Vector2 canvas = CanvasSize();
        float top = UiTheme.HeaderHeight + UiTheme.Space5;
        content = UiKit.Node(root, "Content");
        UiKit.Fill(content, UiTheme.ScreenPadding, top, UiTheme.ScreenPadding, UiTheme.ScreenPadding);
        contentSize = new Vector2(canvas.x - UiTheme.ScreenPadding * 2f, canvas.y - top - UiTheme.ScreenPadding);
        BuildContent(content, contentSize);

        overlay = UiKit.Node(root, "Overlay");
        UiKit.Fill(overlay);
        confirm = new UiConfirm(overlay);
        BuildOverlays();

        toast = UiToast.Create(root, UiTheme.HeaderHeight + UiTheme.Space3);
    }

    private void BuildHeader(RectTransform root)
    {
        RectTransform bar = UiKit.Node(root, "Header");
        UiKit.TopStretch(bar, 0f, UiTheme.HeaderHeight);
        Image fill = UiKit.Image(bar, "Fill", null, UiTheme.Surface, false);
        fill.raycastTarget = true;
        Image line = UiKit.Image(bar, "Line", null, UiTheme.Border, false);
        UiKit.BottomStretch(line.rectTransform, 0f, 2f);

        const float backSize = 64f;
        UiButton back = UiButton.CreateIcon(bar, "Back", UiSprites.Icon(UiSprites.Glyph.ChevronLeft), true, backSize,
            UiButtonStyle.Secondary, OnBack);
        UiKit.LeftMiddle(back.Rect, UiTheme.ScreenPadding, backSize, backSize);

        float textX = UiTheme.ScreenPadding + backSize + UiTheme.Space5;
        var title = UiKit.Text(bar, "Title", Title, UiTheme.FontTitle, UiTheme.TextPrimary);
        UiKit.TopLeft(title.rectTransform, textX, 12f, 900f, 50f);

        var subtitle = UiKit.Text(bar, "Subtitle", Subtitle, UiTheme.FontLabel, UiTheme.TextSecondary);
        UiKit.TopLeft(subtitle.rectTransform, textX, 60f, 1100f, 32f);

        float right = UiTheme.ScreenPadding;
        Currency[] currencies = HeaderCurrencies;
        for (int i = currencies.Length - 1; i >= 0; i--)
        {
            UiCurrencyChip chip = UiCurrencyChip.Create(bar, "Chip_" + currencies[i], currencies[i]);
            UiKit.RightMiddle(chip.Rect, right, chip.Rect.sizeDelta.x, UiCurrencyChip.Height);
            right += chip.Rect.sizeDelta.x + UiTheme.Space5 + 12f;
        }
    }

    // Awake에서 막 만든 캔버스는 아직 크기가 잡히지 않아 0으로 읽힌다. 한 번 갱신시키고,
    // 그래도 비어 있으면 기준 해상도로 친다 — 여기서 0을 받으면 화면 안의 모든 자리가 왼쪽 위로 쏠린다.
    protected Vector2 CanvasSize()
    {
        Canvas.ForceUpdateCanvases();
        Vector2 size = canvasRect != null ? canvasRect.rect.size : Vector2.zero;
        if (size.x < 1f || size.y < 1f) return ReferenceResolution;
        return size;
    }
}
