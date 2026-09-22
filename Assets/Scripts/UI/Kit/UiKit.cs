using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 시설 화면 디자인 시스템의 입구 — 판·글자·배치의 기본 부품과, 조립 부품(UiButton·UiSlot…)을 만드는 곳.
//
// 규칙(색·간격·크기)은 UiTheme에만 있고, 모양은 UiSprites가 그린다. 화면은 여기 있는 함수로만 UI를 짓는다 —
// 화면이 Image를 직접 만들어 색을 칠하기 시작하면 그 화면만 다른 게임처럼 보이게 된다.
//
// 프로젝트의 다른 UI(HudFactory)처럼 프리팹 없이 코드로 짓는다. 창을 다시 지을 때 배선할 것이 없고,
// 규칙이 바뀌면 이 폴더만 고치면 된다.
public static class UiKit
{
    // 한글 글꼴. 화면이 지어질 때 자기 인스펙터 값을 넣는다(UiScreen). 비어 있으면 TMP 기본 글꼴.
    private static TMP_FontAsset font;

    public static TMP_FontAsset Font => font != null ? font : TMP_Settings.defaultFontAsset;

    public static void UseFont(TMP_FontAsset asset)
    {
        if (asset != null) font = asset;
    }

    // ---- 배치 ---------------------------------------------------------------

    public static RectTransform Node(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent != null ? parent.gameObject.layer : LayerMask.NameToLayer("UI");
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    // 부모를 꽉 채우되 네 변에서 안쪽으로 띄운다.
    public static void Fill(RectTransform rect, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    public static void Fill(RectTransform rect, float inset) => Fill(rect, inset, inset, inset, inset);

    // 기준점(anchor)에 pivot을 맞추고 pos만큼 옮긴다. 나머지 배치 함수는 전부 이것의 줄임말이다.
    public static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = pos;
    }

    // (x, y)는 부모 왼쪽 위에서 오른쪽·아래로 잰 거리.
    public static void TopLeft(RectTransform rect, float x, float y, float width, float height) =>
        Place(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(width, height));

    public static void TopRight(RectTransform rect, float right, float top, float width, float height) =>
        Place(rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-right, -top), new Vector2(width, height));

    public static void BottomLeft(RectTransform rect, float x, float bottom, float width, float height) =>
        Place(rect, Vector2.zero, Vector2.zero, new Vector2(x, bottom), new Vector2(width, height));

    public static void BottomRight(RectTransform rect, float right, float bottom, float width, float height) =>
        Place(rect, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-right, bottom), new Vector2(width, height));

    // 가로 가운데에서 offsetX만큼, 위에서 top만큼.
    public static void TopCenter(RectTransform rect, float offsetX, float top, float width, float height) =>
        Place(rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(offsetX, -top), new Vector2(width, height));

    public static void Center(RectTransform rect, float x, float y, float width, float height) =>
        Place(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(width, height));

    // 왼쪽 끝에서 x, 세로 가운데. 한 줄로 늘어놓는 띠 안의 요소가 쓴다.
    public static void LeftMiddle(RectTransform rect, float x, float width, float height) =>
        Place(rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(width, height));

    public static void RightMiddle(RectTransform rect, float right, float width, float height) =>
        Place(rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-right, 0f), new Vector2(width, height));

    // 세로로 늘어나는 기둥. 왼쪽에서 x, 폭 width. 화면 높이가 달라도 위아래를 꽉 채운다.
    public static void Column(RectTransform rect, float x, float width)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.offsetMin = new Vector2(x, 0f);
        rect.offsetMax = new Vector2(x + width, 0f);
    }

    // 가로로 늘어나는 띠. 위에서 top, 높이 height, 좌우로 left/right만큼 띄운다.
    public static void TopStretch(RectTransform rect, float top, float height, float left = 0f, float right = 0f)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, -(top + height));
        rect.offsetMax = new Vector2(-right, -top);
    }

    public static void BottomStretch(RectTransform rect, float bottom, float height, float left = 0f, float right = 0f)
    {
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, bottom + height);
    }

    // ---- 그림 ---------------------------------------------------------------

    // 표시 전용 그림(레이캐스트 꺼짐). 부모를 꽉 채운다.
    public static Image Image(RectTransform parent, string name, Sprite sprite, Color color, bool preserveAspect = true)
    {
        RectTransform rect = Node(parent, name);
        Fill(rect);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.preserveAspect = preserveAspect;
        image.raycastTarget = false;
        return image;
    }

    // 둥근 판(속이 참). 부모를 꽉 채운다.
    public static Image Rounded(RectTransform parent, string name, Color color, int radius)
    {
        Image image = Image(parent, name, UiSprites.Rounded(radius), color, false);
        image.type = UnityEngine.UI.Image.Type.Sliced;
        return image;
    }

    // 둥근 테두리. 부모를 꽉 채운다.
    public static Image Line(RectTransform parent, string name, Color color, int radius, int thickness)
    {
        Image image = Image(parent, name, UiSprites.Outline(radius, thickness), color, false);
        image.type = UnityEngine.UI.Image.Type.Sliced;
        return image;
    }

    public static Image Glyph(RectTransform parent, string name, UiSprites.Glyph glyph, Color color) =>
        Image(parent, name, UiSprites.Icon(glyph), color);

    // 판 하나 — 그림자(선택) + 채움 + 테두리(선택). 내용은 Rect에 이어 붙이면 판 위에 그려진다.
    public struct Surface
    {
        public RectTransform Rect;
        public Image Fill;
        public Image Border;
        public Image Shadow;
    }

    public static Surface Panel(RectTransform parent, string name, Color fill, int radius = UiTheme.RadiusL,
        Color? border = null, int shadow = 0)
    {
        var surface = new Surface { Rect = Node(parent, name) };

        if (shadow > 0)
        {
            surface.Shadow = Image(surface.Rect, "Shadow", UiSprites.Shadow(radius, shadow), UiTheme.Shadow, false);
            surface.Shadow.type = UnityEngine.UI.Image.Type.Sliced;
            // 그림자는 아래로 조금 내려 떠 있는 느낌을 준다.
            Fill(surface.Shadow.rectTransform, -shadow, -shadow + shadow * 0.25f, -shadow, -shadow - shadow * 0.25f);
        }

        surface.Fill = Rounded(surface.Rect, "Fill", fill, radius);
        if (border.HasValue) surface.Border = Line(surface.Rect, "Border", border.Value, radius, 2);
        return surface;
    }

    // ---- 글자 ---------------------------------------------------------------

    public static TMP_Text Text(RectTransform parent, string name, string text, float size, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.Left)
    {
        RectTransform rect = Node(parent, name);
        Fill(rect);

        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = Font;
        label.fontSize = size;
        label.color = color;
        label.alignment = alignment;
        label.raycastTarget = false;
        label.richText = true;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.text = text;
        return label;
    }

    // 넘치면 말줄임. 칸 높이가 줄 높이보다 낮으면 말줄임이 줄을 통째로 버리므로 여기서 모자란 높이를 채운다.
    public static TMP_Text Ellipsis(TMP_Text label)
    {
        label.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform rect = label.rectTransform;
        float min = label.fontSize * UiTheme.LineHeight;
        if (rect.anchorMin.y == rect.anchorMax.y && rect.sizeDelta.y < min)
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, min);
        return label;
    }

    public static TMP_Text Wrap(TMP_Text label)
    {
        label.textWrappingMode = TextWrappingModes.Normal;
        return label;
    }

    // 구역 제목("재료 선택", "보유 장비"). 모든 화면에서 같은 크기·색·굵기다.
    public static TMP_Text SectionTitle(RectTransform parent, string name, string text) =>
        Text(parent, name, text, UiTheme.FontHeading, UiTheme.TextPrimary);

    // ---- 목록 ---------------------------------------------------------------

    /// 세로로만 구르는 목록. 돌려주는 content에 줄을 붙이고, 다 붙인 뒤 높이를 SetContentHeight로 알린다.
    public static RectTransform ScrollArea(RectTransform parent, string name, out ScrollRect scroll)
    {
        RectTransform viewport = Node(parent, name);
        Fill(viewport);
        // 줄 사이 빈 곳에서도 휠과 끌기를 받으려면 투명한 판이 있어야 한다(레이캐스트는 알파를 보지 않는다).
        var hit = viewport.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = Node(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;

        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        return content;
    }

    public static void SetContentHeight(RectTransform content, float height) =>
        content.sizeDelta = new Vector2(0f, Mathf.Max(0f, height));

    // ---- 숫자 ---------------------------------------------------------------

    // "3,361,233"
    public static string Amount(long value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    // 0~1을 "12.5%"로. 소수 첫째 자리까지, 뒤의 0은 지운다.
    public static string Percent(float ratio)
    {
        float percent = Mathf.Round(ratio * 1000f) / 10f;
        return percent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    // 별 등급 글자("★5"). 별을 다섯 개씩 늘어놓으면 7성에서 칸을 넘친다.
    public static string Stars(int stars) => "★" + stars;
}
