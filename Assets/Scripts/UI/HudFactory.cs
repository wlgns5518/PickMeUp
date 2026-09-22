using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 전투 HUD를 코드에서 만들기 위한 공용 헬퍼.
// 프리팹을 두지 않고 런타임에 생성하는 이유: HP바/데미지 숫자는 씬마다 다시 배선할 일이 없고,
// 프리팹으로 두면 유닛이나 색 규칙이 바뀔 때마다 에셋과 코드를 양쪽에서 고쳐야 한다.
public static class HudFactory
{
    // Image에 스프라이트를 주지 않으면 단색 사각형으로 그려진다. 별도 에셋이 필요 없다.
    public static Image CreateImage(RectTransform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);

        var image = go.GetComponent<Image>();
        image.color = color;
        // HUD는 표시 전용이다. 레이캐스트 대상으로 두면 그 아래의 UI 클릭을 가로챈다.
        image.raycastTarget = false;
        return image;
    }

    public static TMP_Text CreateText(RectTransform parent, string name, TMP_FontAsset font, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);

        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    public static RectTransform CreateGroup(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        return rect;
    }

    // 화면을 덮는 UI 캔버스 한 장.
    // 창마다 다른 것은 이름과 정렬 순서뿐이고 나머지 설정은 전부 같다 —
    // 1920x1080 기준으로 늘어나고, 클릭을 받을 수 있도록 레이캐스터를 붙인다.
    public static Canvas CreateScreenCanvas(Transform parent, string name, int sortingOrder, out RectTransform rect)
    {
        var canvasGo = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(parent, false);
        canvasGo.layer = LayerMask.NameToLayer("UI");

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        rect = (RectTransform)canvasGo.transform;
        return canvas;
    }

    // 부모를 꽉 채우도록 늘린다. 배경막이나 팝업 루트처럼 화면 전체를 덮는 것들이 쓴다.
    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // 왼쪽 위를 기준으로 크기와 자리를 잡는다.
    // 창 안의 요소는 대부분 좌상단에서 아래로 쌓이므로, 기준점을 그쪽에 두면
    // 창 크기가 바뀌어도 offset을 다시 계산할 필요가 없다.
    public static void SetTopLeft(RectTransform rect, Vector2 size, Vector2 offset)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = offset;
    }

    // 한국어 폰트가 지정되지 않았을 때의 대비책. 기본 폰트에는 한글 글리프가 없어
    // 라벨이 네모(두부)로 보이므로, 조용히 넘어가지 않고 경고를 남긴다.
    public static TMP_FontAsset ResolveFont(TMP_FontAsset preferred, Object context)
    {
        if (preferred != null) return preferred;

        TMP_FontAsset fallback = TMP_Settings.defaultFontAsset;
        Debug.LogWarning("[HUD] 한국어 폰트가 지정되지 않아 기본 폰트를 사용합니다. " +
                         "한글이 네모로 보이면 Assets/Fonts의 NotoSansKR SDF를 인스펙터에 지정하세요.", context);
        return fallback;
    }

    // ---- 네온 HUD 킷 ------------------------------------------------------
    //
    // 스프라이트는 NeonUISkin 에셋이 들고 있다. 에셋이 없으면 같은 자리에 팔레트 색 단색 판을 그린다.

    // 킷 스프라이트를 입힌다. 스프라이트가 색을 들고 있으므로 이미지 색은 흰색으로 둔다.
    // 없으면 대비색 단색 판으로 그린다.
    public static void ApplySkin(Image image, Sprite sprite, Color fallback)
    {
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
        }
        else
        {
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = fallback;
        }

        // 스프라이트마다 원본 높이와 보더가 달라 갈아 끼울 때마다 다시 맞춘다.
        if (image.TryGetComponent(out NeonSliceFit fit)) fit.Apply();
    }

    // 킷 스프라이트를 입힌 판. 크기가 원본보다 작아져도 모서리가 뭉개지지 않게 NeonSliceFit을 붙인다.
    public static Image CreateSkinnedImage(RectTransform parent, string name, Sprite sprite, Color fallback)
    {
        Image image = CreateImage(parent, name, fallback);
        image.gameObject.AddComponent<NeonSliceFit>();
        ApplySkin(image, sprite, fallback);
        return image;
    }

    public enum GaugeFill { Hp, Mana, Xp }

    // 트랙(gauge_track) 위에 채움(gauge_fill_*)을 얹은 가로 게이지. 트랙을 돌려주고 채움은 out으로 준다.
    // 자리는 돌려받은 트랙에 잡는다 — 채움은 트랙을 꽉 채우도록 늘어나 있다.
    // 값은 SetGauge로 넣는다.
    //
    // README는 채움을 Filled(fillAmount)로 쓰라고 하지만 그러면 스프라이트 가장자리의 투명한 여백이 바 길이에
    // 비례해 늘어나고, 트랙은 9-슬라이스라 여백이 그대로여서 긴 바일수록 채움이 트랙보다 안쪽에서 시작한다.
    // 채움도 9-슬라이스로 두고 오른쪽 앵커를 비율만큼 당겨 줄인다 — 여백이 트랙과 똑같이 맞는다.
    public static Image CreateGauge(RectTransform parent, string name, GaugeFill kind, out Image fill)
    {
        NeonUISkin skin = NeonUISkin.Current;

        Image track = CreateSkinnedImage(parent, name, skin != null ? skin.gaugeTrack : null, BattleHudPalette.GaugeBackground);

        Sprite fillSprite = null;
        Color fillColor = BattleHudPalette.PartyHp;
        if (skin != null)
            fillSprite = kind == GaugeFill.Mana ? skin.gaugeFillMp : kind == GaugeFill.Xp ? skin.gaugeFillXp : skin.gaugeFillHp;
        if (kind == GaugeFill.Mana) fillColor = BattleHudPalette.Mana;
        else if (kind == GaugeFill.Xp) fillColor = BattleHudPalette.Accent;

        fill = CreateSkinnedImage(track.rectTransform, "Fill", fillSprite, fillColor);
        Stretch(fill.rectTransform);

        return track;
    }

    public static void SetGauge(Image fill, float ratio)
    {
        ratio = Mathf.Clamp01(ratio);
        fill.rectTransform.anchorMax = new Vector2(ratio, 1f);
        // 폭이 0이어도 9-슬라이스는 좌우 보더만큼 그려져 빈 게이지에 붉은 점이 남는다.
        fill.enabled = ratio > 0f;
    }

    // 장식 메시지 박스(msgbox_*). 검은 판 위에 모서리 넷과 그 사이 테두리 넷을 얹는다(README의 OrnateMessageBox와 같은 짜임).
    // 돌려주는 root의 크기를 바꾸면 판과 테두리가 함께 늘어난다. 글자는 받는 쪽이 root 안에 붙인다.
    // body는 클릭을 받을 판이다(눌러서 닫는 배너 등).
    public static RectTransform CreateOrnateBox(RectTransform parent, string name, out Image body)
    {
        NeonUISkin skin = NeonUISkin.Current;
        bool framed = skin != null && skin.HasMessageFrame;

        RectTransform root = CreateGroup(parent, name);

        body = CreateSkinnedImage(root, "Panel", skin != null ? skin.messagePanel : null, BattleHudPalette.PanelBody);
        Stretch(body.rectTransform);
        if (!framed) return root;

        // 판은 테두리보다 살짝 안쪽에 둔다(README: inset 7px). 딱 맞추면 모서리 장식 바깥으로 판이 삐져나온다.
        body.rectTransform.offsetMin = new Vector2(OrnatePanelInset, OrnatePanelInset);
        body.rectTransform.offsetMax = new Vector2(-OrnatePanelInset, -OrnatePanelInset);

        RectTransform frame = CreateGroup(root, "Frame");
        Stretch(frame);

        OrnateCorner(frame, "Corner_TL", skin.messageCornerTopLeft, new Vector2(0f, 1f));
        OrnateCorner(frame, "Corner_TR", skin.messageCornerTopRight, new Vector2(1f, 1f));
        OrnateCorner(frame, "Corner_BL", skin.messageCornerBottomLeft, new Vector2(0f, 0f));
        OrnateCorner(frame, "Corner_BR", skin.messageCornerBottomRight, new Vector2(1f, 0f));

        OrnateEdge(frame, "Edge_Top", skin.messageEdgeTop, true, true);
        OrnateEdge(frame, "Edge_Bottom", skin.messageEdgeBottom, true, false);
        OrnateEdge(frame, "Edge_Left", skin.messageEdgeLeft, false, true);
        OrnateEdge(frame, "Edge_Right", skin.messageEdgeRight, false, false);

        return root;
    }

    private const float OrnatePanelInset = 7f;
    private const float OrnateCornerSize = 90f;
    private const float OrnateEdgeThickness = 22f;

    private static void OrnateCorner(RectTransform frame, string name, Sprite sprite, Vector2 anchor)
    {
        Image corner = CreateImage(frame, name, Color.white);
        corner.sprite = sprite;
        RectTransform rect = corner.rectTransform;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.sizeDelta = new Vector2(OrnateCornerSize, OrnateCornerSize);
        rect.anchoredPosition = Vector2.zero;
    }

    // horizontal이면 위/아래 테두리(가로로 늘어남), 아니면 좌/우. first는 위 또는 왼쪽.
    private static void OrnateEdge(RectTransform frame, string name, Sprite sprite, bool horizontal, bool first)
    {
        Image edge = CreateImage(frame, name, Color.white);
        edge.sprite = sprite;
        edge.type = Image.Type.Sliced;
        RectTransform rect = edge.rectTransform;

        if (horizontal)
        {
            float y = first ? 1f : 0f;
            rect.anchorMin = new Vector2(0f, y);
            rect.anchorMax = new Vector2(1f, y);
            rect.pivot = new Vector2(0.5f, y);
            rect.offsetMin = new Vector2(OrnateCornerSize, first ? -OrnateEdgeThickness : 0f);
            rect.offsetMax = new Vector2(-OrnateCornerSize, first ? 0f : OrnateEdgeThickness);
        }
        else
        {
            float x = first ? 0f : 1f;
            rect.anchorMin = new Vector2(x, 0f);
            rect.anchorMax = new Vector2(x, 1f);
            rect.pivot = new Vector2(x, 0.5f);
            rect.offsetMin = new Vector2(first ? 0f : -OrnateEdgeThickness, OrnateCornerSize);
            rect.offsetMax = new Vector2(first ? OrnateEdgeThickness : 0f, -OrnateCornerSize);
        }
    }
}
