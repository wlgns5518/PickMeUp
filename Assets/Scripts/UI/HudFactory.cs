using TMPro;
using UnityEngine;
using UnityEngine.Events;
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

    // 좌측 기준으로 늘어나는 게이지(HP/공포 바)의 채움 부분.
    public static Image CreateFillBar(RectTransform parent, string name, Color color, Vector2 size, float yOffset)
    {
        Image image = CreateImage(parent, name, color);
        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(-size.x * 0.5f, yOffset);
        return image;
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

    // 한국어 폰트가 지정되지 않았을 때의 대비책. 기본 폰트에는 한글 글리프가 없어
    // 라벨이 네모(두부)로 보이므로, 조용히 넘어가지 않고 경고를 남긴다.
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

    // 시설 창의 바탕(panel.png). 돌려주는 이미지는 창 안을 누른 클릭이 배경막으로 내려가지 않게 받는 판이고,
    // 보이는 모양은 그 자식들이 그린다. 창 내용은 돌려받은 판의 크기를 기준으로 그대로 배치하면 된다.
    public static Image CreatePanel(RectTransform parent, string name)
    {
        NeonUISkin skin = NeonUISkin.Current;
        Sprite sprite = skin != null ? skin.panel : null;

        if (sprite == null)
        {
            Image flat = CreateImage(parent, name, BattleHudPalette.PanelBody);
            flat.raycastTarget = true;
            return flat;
        }

        // 투명한 판은 그려지지 않아도 클릭은 받는다(레이캐스트는 알파를 보지 않는다).
        Image panel = CreateImage(parent, name, Color.clear);
        panel.raycastTarget = true;

        // panel.png는 알파 72%이고, 프로젝트가 선형 색공간이라 어두운 판을 반투명으로 겹치면 눈에는 훨씬 옅게 보인다.
        // 그대로 두면 뒤의 마을이 글자와 겹쳐 읽힌다. 모서리 장식 안쪽(보더 안)에는 불투명한 판을 먼저 깔고,
        // 그 위에 같은 스프라이트를 두 겹 얹어 잘린 모서리와 글로우 모양은 그대로 둔 채 가장자리까지 막는다.
        float inset = Mathf.Max(sprite.border.x, sprite.border.w) - PanelSpritePadding;
        Image underlay = CreateImage(panel.rectTransform, "Underlay", BattleHudPalette.BgPanel);
        Stretch(underlay.rectTransform);
        underlay.rectTransform.offsetMin = new Vector2(inset, inset);
        underlay.rectTransform.offsetMax = new Vector2(-inset, -inset);

        for (int i = 0; i < 2; i++)
        {
            Image layer = CreateSkinnedImage(panel.rectTransform, i == 0 ? "Back" : "Body", sprite, BattleHudPalette.PanelBody);
            // 스프라이트 가장자리의 투명한 여백만큼 바깥으로 넓힌다. 그래야 보이는 테두리가 판의 경계에 맞고,
            // 창마다 잡아 둔 안쪽 여백이 원래 뜻대로 테두리에서부터 잰 거리가 된다.
            Stretch(layer.rectTransform);
            layer.rectTransform.offsetMin = new Vector2(-PanelSpritePadding, -PanelSpritePadding);
            layer.rectTransform.offsetMax = new Vector2(PanelSpritePadding, PanelSpritePadding);
        }
        return panel;
    }

    // panel.png 둘레의 투명한 여백(원본 560x400에서 보이는 판은 16..543, 16..383).
    private const float PanelSpritePadding = 16f;

    // 화면 위쪽에 가로로 걸리는 알림 띠. 창을 접은 뒤에도 남는 소환·제작 결과가 쓴다.
    // 띠 위를 누른 클릭이 뒤쪽 세계로 새지 않도록 클릭을 받아 둔다.
    //
    // 킷의 네임바(namebar_frame)는 판 바깥 여백까지 불투명한 검정이라 하늘 위에 검은 사각형으로 뜨고,
    // 오른쪽 아래 회로 장식이 띠 안의 버튼과 겹친다. 창과 같은 panel.png를 쓰되, 띠는 낮아서 모서리 안쪽에
    // 불투명한 판을 깔 자리가 없으므로 두 겹만 겹쳐 비침을 막는다.
    public static Image CreateHudStrip(RectTransform parent, string name)
    {
        NeonUISkin skin = NeonUISkin.Current;
        Sprite sprite = skin != null ? skin.panel : null;

        Image strip = CreateSkinnedImage(parent, name, sprite, BattleHudPalette.PanelBody);
        strip.raycastTarget = true;
        if (sprite != null)
        {
            Image body = CreateSkinnedImage(strip.rectTransform, "Body", sprite, BattleHudPalette.PanelBody);
            Stretch(body.rectTransform);
        }
        return strip;
    }

    // 제목 아래에 긋는 구분선(divider.png). 왼쪽에서 오른쪽으로 옅어진다.
    public static Image CreateDivider(RectTransform parent, string name)
    {
        NeonUISkin skin = NeonUISkin.Current;
        Sprite sprite = skin != null ? skin.divider : null;
        Image divider = CreateSkinnedImage(parent, name, sprite,
            new Color(BattleHudPalette.Accent.r, BattleHudPalette.Accent.g, BattleHudPalette.Accent.b, 0.35f));
        return divider;
    }

    // 킷 버튼. text가 null이면 글자 없이 판만 만든다(안에 글자를 여러 개 따로 까는 줄 등).
    public static NeonButton CreateButton(RectTransform parent, string name, NeonButtonStyle style,
        TMP_FontAsset font, string text, float fontSize, UnityAction onClick)
    {
        Image background = CreateImage(parent, name, Color.white);
        // HudFactory 이미지는 표시 전용이 기본이다. 버튼은 도로 켜야 한다.
        background.raycastTarget = true;
        background.gameObject.AddComponent<NeonSliceFit>();

        TMP_Text label = null;
        if (text != null)
        {
            label = CreateText(background.rectTransform, "Label", font, fontSize, BattleHudPalette.TextPrimary);
            Stretch(label.rectTransform);
            label.text = text;
        }

        var button = background.gameObject.AddComponent<NeonButton>();
        button.Setup(background, label, style);
        if (onClick != null) button.onClick.AddListener(onClick);
        return button;
    }

    // 창 오른쪽 위의 닫기 버튼(icon_btn). 곱셈 기호(U+2715 등)는 NotoSansKR 아틀라스에 없어
    // 네모로 그려지므로 알파벳 X를 쓴다.
    public static NeonButton CreateCloseButton(RectTransform parent, TMP_FontAsset font, float size, UnityAction onClick)
    {
        NeonButton close = CreateButton(parent, "Close", NeonButtonStyle.Icon, font, "X", size * 0.5f, onClick);
        close.Rect.sizeDelta = new Vector2(size, size);
        return close;
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
