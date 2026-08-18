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
    public static TMP_FontAsset ResolveFont(TMP_FontAsset preferred, Object context)
    {
        if (preferred != null) return preferred;

        TMP_FontAsset fallback = TMP_Settings.defaultFontAsset;
        Debug.LogWarning("[HUD] 한국어 폰트가 지정되지 않아 기본 폰트를 사용합니다. " +
                         "한글이 네모로 보이면 Assets/Fonts의 NotoSansKR SDF를 인스펙터에 지정하세요.", context);
        return fallback;
    }
}
