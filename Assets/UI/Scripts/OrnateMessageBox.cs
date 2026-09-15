using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 글자 길이에 맞춰 가로·세로가 자동으로 늘어나는 장식 메시지 박스.
///
/// 사용법
///   1) 빈 UI 오브젝트(Canvas 자식)에 이 스크립트를 붙입니다.
///   2) 인스펙터에서 스프라이트 9장(코너 4 + 테두리 4 + 패널)과 폰트를 지정합니다.
///   3) box.SetMessage("몰몬트(★★)가 여신의 품으로 돌아갔습니다.\n그의 투지는 영원히 기억될 것입니다.");
///
/// 계층은 Awake에서 자동 생성되므로 프리팹을 직접 조립할 필요가 없습니다.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class OrnateMessageBox : MonoBehaviour
{
    [Header("Sprites")]
    public Sprite cornerTL, cornerTR, cornerBL, cornerBR;
    public Sprite edgeTop, edgeBottom, edgeLeft, edgeRight;
    public Sprite panel;

    [Header("Text")]
    public TMP_FontAsset font;
    public float fontSize = 27f;
    public Color textColor = new Color(0.957f, 0.945f, 0.973f);
    [Tooltip("한 줄 최대 폭(px). 넘으면 자동 줄바꿈되고 박스 높이가 늘어납니다.")]
    public float maxTextWidth = 560f;

    [Header("Layout")]
    public float cornerSize = 90f;
    public float edgeThickness = 22f;
    public Vector2 padding = new Vector2(95f, 80f); // 좌우, 상하
    public float panelInset = 7f;
    public float minWidth = 240f;

    [Header("Animation")]
    public bool animateIn = true;
    public float animateDuration = 0.28f;

    RectTransform _rt, _textRT;
    TextMeshProUGUI _text;
    CanvasGroup _group;

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
        _group = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        Build();
    }

    void Build()
    {
        // --- 패널 (프레임보다 뒤) ---
        var p = NewImage("Panel", panel, _rt);
        Stretch(p, panelInset);
        p.type = Image.Type.Sliced;
        p.raycastTarget = false;

        // --- 텍스트 ---
        var tgo = new GameObject("Text", typeof(RectTransform));
        tgo.transform.SetParent(_rt, false);
        _textRT = tgo.GetComponent<RectTransform>();
        _text = tgo.AddComponent<TextMeshProUGUI>();
        if (font != null) _text.font = font;
        _text.fontSize = fontSize;
        _text.color = textColor;
        _text.alignment = TextAlignmentOptions.Center;
        _text.textWrappingMode = TextWrappingModes.Normal;
        _text.enableAutoSizing = false;
        _text.raycastTarget = false;
        _textRT.anchorMin = _textRT.anchorMax = new Vector2(0.5f, 0.5f);
        _textRT.pivot = new Vector2(0.5f, 0.5f);

        // --- 프레임 ---
        var frame = new GameObject("Frame", typeof(RectTransform)).GetComponent<RectTransform>();
        frame.SetParent(_rt, false);
        Stretch(frame, 0f);

        MakeCorner("Corner_TL", cornerTL, frame, new Vector2(0, 1));
        MakeCorner("Corner_TR", cornerTR, frame, new Vector2(1, 1));
        MakeCorner("Corner_BL", cornerBL, frame, new Vector2(0, 0));
        MakeCorner("Corner_BR", cornerBR, frame, new Vector2(1, 0));

        MakeEdge("Edge_Top", edgeTop, frame, true, true);
        MakeEdge("Edge_Bottom", edgeBottom, frame, true, false);
        MakeEdge("Edge_Left", edgeLeft, frame, false, true);
        MakeEdge("Edge_Right", edgeRight, frame, false, false);
    }

    Image NewImage(string name, Sprite sprite, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    static void Stretch(Component c, float inset)
    {
        var r = c.transform as RectTransform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(inset, inset);
        r.offsetMax = new Vector2(-inset, -inset);
    }

    void MakeCorner(string name, Sprite s, RectTransform parent, Vector2 anchor)
    {
        var img = NewImage(name, s, parent);
        var r = img.rectTransform;
        r.anchorMin = r.anchorMax = r.pivot = anchor;
        r.sizeDelta = new Vector2(cornerSize, cornerSize);
        r.anchoredPosition = Vector2.zero;
    }

    /// <param name="horizontal">true = 상/하 테두리(가로로 늘어남)</param>
    /// <param name="firstSide">horizontal이면 top, 아니면 left</param>
    void MakeEdge(string name, Sprite s, RectTransform parent, bool horizontal, bool firstSide)
    {
        var img = NewImage(name, s, parent);
        img.type = Image.Type.Sliced;
        var r = img.rectTransform;

        if (horizontal)
        {
            float y = firstSide ? 1f : 0f;
            r.anchorMin = new Vector2(0f, y);
            r.anchorMax = new Vector2(1f, y);
            r.pivot = new Vector2(0.5f, y);
            r.offsetMin = new Vector2(cornerSize, firstSide ? -edgeThickness : 0f);
            r.offsetMax = new Vector2(-cornerSize, firstSide ? 0f : edgeThickness);
        }
        else
        {
            float x = firstSide ? 0f : 1f;
            r.anchorMin = new Vector2(x, 0f);
            r.anchorMax = new Vector2(x, 1f);
            r.pivot = new Vector2(x, 0.5f);
            r.offsetMin = new Vector2(firstSide ? 0f : -edgeThickness, cornerSize);
            r.offsetMax = new Vector2(firstSide ? edgeThickness : 0f, -cornerSize);
        }
    }

    /// <summary>메시지를 넣고 박스 크기를 글자에 맞춰 다시 계산합니다.</summary>
    public void SetMessage(string message)
    {
        if (_text == null) Awake();
        _text.text = message;
        Resize();
        if (animateIn) StartCoroutine(PlayIn());
    }

    void Resize()
    {
        _text.ForceMeshUpdate();
        // 자연 길이(줄바꿈 없이)를 먼저 재고, 최대 폭을 넘으면 그 폭으로 래핑
        Vector2 pref = _text.GetPreferredValues(_text.text, Mathf.Infinity, Mathf.Infinity);
        float w = Mathf.Min(pref.x, maxTextWidth);
        Vector2 wrapped = _text.GetPreferredValues(_text.text, w, Mathf.Infinity);

        _textRT.sizeDelta = new Vector2(w, wrapped.y);
        float boxW = Mathf.Max(minWidth, w + padding.x * 2f);
        float boxH = wrapped.y + padding.y * 2f;
        _rt.sizeDelta = new Vector2(boxW, boxH);
    }

    System.Collections.IEnumerator PlayIn()
    {
        float t = 0f;
        var target = _rt.localScale == Vector3.zero ? Vector3.one : _rt.localScale;
        while (t < animateDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / animateDuration);
            float e = 1f - Mathf.Pow(1f - k, 3f);
            _group.alpha = e;
            _rt.localScale = Vector3.Lerp(target * 0.97f, target, e);
            yield return null;
        }
        _group.alpha = 1f;
        _rt.localScale = target;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying && _text != null) Resize();
    }
#endif
}
