using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 슬롯에 올릴 내용 한 벌. 영웅·장비·재료가 모두 이 모양으로 바뀌어 올라간다(UiSlotContents).
public struct UiSlotContent
{
    public Sprite Icon;
    // 영웅 초상화처럼 칸을 가득 채우는 그림이면 true. 아이콘은 안쪽으로 조금 띄워 그린다.
    public bool FillsSlot;
    // 그림이 없을 때 가운데 적는 글자("?", 이름 첫 글자).
    public string Fallback;
    // 등급 단계(1~7, UiTheme.Tier). 0이면 등급 없음 — 테두리가 평범한 선이 된다.
    public int Tier;
    public string Badge;      // 왼쪽 위 등급 딱지("S", "★5")
    public string Corner;     // 오른쪽 위 글자("+5")
    public string Caption;    // 왼쪽 아래 글자("Lv.12")
    public string Tag;        // 오른쪽 아래 딱지("장착", "x3")
    public Color TagColor;
}

// 디자인 시스템의 슬롯 한 칸. 영웅 칸, 장비 칸, 재료 칸, 결과 칸이 모두 이 부품이다.
//
//   ┌[등급]──────[+5]┐   등급 색은 테두리와 아래쪽 물들임에도 번진다 — 등급은 어느 화면에서든 같은 자리·같은 색이다.
//   │                │
//   │     그림       │
//   │[Lv.12]   [장착]│
//   └────────────────┘
//
// 상태:
//   비어 있음 — 옅은 테두리와 "+", 무엇을 넣을지 안내 글자.
//   고름     — 칸 바깥에 선택색 고리, 오른쪽 위 모서리에 체크.
//   흐림     — 어둡게 덮고 가운데 이유("장착 중"). 누를 수는 있다(눌러서 왜 안 되는지 듣게).
//   잠김     — 자물쇠와 "준비 중". 누를 수 없다.
[DisallowMultipleComponent]
public class UiSlot : MonoBehaviour
{
    private Image background;
    private Image tierWash;
    private UiGradient washGradient;
    private Image frame;
    private RectTransform content;
    private Image icon;
    private TMP_Text fallback;

    private RectTransform placeholder;
    private TMP_Text placeholderText;

    private RectTransform badge;
    private Image badgeFill;
    private TMP_Text badgeText;
    private TMP_Text cornerText;
    private RectTransform captionPill;
    private TMP_Text captionText;
    private RectTransform tagPill;
    private Image tagFill;
    private TMP_Text tagText;

    private Image dim;
    private TMP_Text dimText;
    private RectTransform lockNode;
    private TMP_Text lockText;

    private Image selectionRing;
    private RectTransform check;
    private Image checkFill;

    private Button button;
    private float size;

    public event Action Clicked;

    public RectTransform Rect => (RectTransform)transform;

    /// 만들기. 자리는 돌려받은 Rect에 잡는다(크기는 size x size).
    public static UiSlot Create(RectTransform parent, string name, float size)
    {
        RectTransform rect = UiKit.Node(parent, name);
        rect.sizeDelta = new Vector2(size, size);

        var slot = rect.gameObject.AddComponent<UiSlot>();
        slot.size = size;
        slot.Build();
        return slot;
    }

    private void Build()
    {
        int radius = size >= UiTheme.SlotMedium ? UiTheme.RadiusM : UiTheme.RadiusS;
        float badgeFont = Mathf.Clamp(size * 0.14f, 15f, 26f);

        background = UiKit.Rounded(Rect, "Background", UiTheme.SurfaceRaised, radius);
        background.raycastTarget = true;

        tierWash = UiKit.Rounded(Rect, "TierWash", Color.clear, radius);
        washGradient = tierWash.gameObject.AddComponent<UiGradient>();
        // 위는 투명, 아래로 갈수록 등급 색이 스민다.
        washGradient.Set(new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.42f));

        // 그림은 늘 이 칸 안쪽으로만 맞춰 넣는다(Fill 여백 + preserveAspect). 그래서 가림막(RectMask2D)을 두지 않는다 —
        // 칸마다 하나씩 두면 목록의 칸 수만큼 매 프레임 잘라내기 계산이 돈다.
        content = UiKit.Node(Rect, "Content");
        UiKit.Fill(content, 3f);

        icon = UiKit.Image(content, "Icon", null, Color.white);
        fallback = UiKit.Text(content, "Fallback", string.Empty, size * 0.32f, UiTheme.TextSecondary, TextAlignmentOptions.Center);

        frame = UiKit.Line(Rect, "Frame", UiTheme.Border, radius, size >= UiTheme.SlotMedium ? 3 : 2);

        // 비어 있을 때 — "+"와 안내 글자.
        placeholder = UiKit.Node(Rect, "Placeholder");
        UiKit.Fill(placeholder);
        Image plus = UiKit.Glyph(placeholder, "Plus", UiSprites.Glyph.Plus, UiTheme.TextMuted);
        UiKit.Center(plus.rectTransform, 0f, size * 0.08f, size * 0.34f, size * 0.34f);
        placeholderText = UiKit.Text(placeholder, "Hint", string.Empty, Mathf.Clamp(size * 0.13f, 15f, 22f),
            UiTheme.TextMuted, TextAlignmentOptions.Center);
        UiKit.BottomStretch(placeholderText.rectTransform, size * 0.1f, size * 0.26f, 6f, 6f);

        // 네 모서리 표시.
        float pillHeight = badgeFont * 1.45f;
        float edge = Mathf.Max(5f, size * 0.05f);

        badge = UiKit.Node(Rect, "Badge");
        UiKit.TopLeft(badge, edge, edge, pillHeight * 1.5f, pillHeight);
        badgeFill = UiKit.Rounded(badge, "Fill", Color.white, UiTheme.RadiusS);
        badgeText = UiKit.Text(badge, "Text", string.Empty, badgeFont, UiTheme.OnPrimary, TextAlignmentOptions.Center);

        cornerText = UiKit.Text(Rect, "Corner", string.Empty, badgeFont * 1.05f, UiTheme.TextPrimary, TextAlignmentOptions.TopRight);
        UiKit.TopRight(cornerText.rectTransform, edge + 2f, edge - 2f, size * 0.5f, pillHeight + 4f);

        captionPill = UiKit.Node(Rect, "Caption");
        UiKit.BottomLeft(captionPill, edge, edge, size * 0.5f, pillHeight);
        UiKit.Rounded(captionPill, "Fill", UiTheme.WithAlpha(UiTheme.SurfaceSunken, 0.92f), UiTheme.RadiusS);
        captionText = UiKit.Text(captionPill, "Text", string.Empty, badgeFont * 0.92f, UiTheme.TextPrimary, TextAlignmentOptions.Center);

        tagPill = UiKit.Node(Rect, "Tag");
        UiKit.BottomRight(tagPill, edge, edge, size * 0.46f, pillHeight);
        tagFill = UiKit.Rounded(tagPill, "Fill", UiTheme.Selection, UiTheme.RadiusS);
        tagText = UiKit.Text(tagPill, "Text", string.Empty, badgeFont * 0.92f, UiTheme.OnPrimary, TextAlignmentOptions.Center);

        // 흐림 — 누를 수는 있지만 지금은 안 되는 칸.
        dim = UiKit.Rounded(Rect, "Dim", new Color(0.02f, 0.03f, 0.05f, 0.72f), radius);
        dimText = UiKit.Wrap(UiKit.Text((RectTransform)dim.transform, "Reason", string.Empty, Mathf.Clamp(size * 0.13f, 15f, 22f),
            UiTheme.TextPrimary, TextAlignmentOptions.Center));
        UiKit.Fill(dimText.rectTransform, 6f);

        // 잠김.
        lockNode = UiKit.Node(Rect, "Lock");
        UiKit.Fill(lockNode);
        Image lockGlyph = UiKit.Glyph(lockNode, "Glyph", UiSprites.Glyph.Lock, UiTheme.TextMuted);
        UiKit.Center(lockGlyph.rectTransform, 0f, size * 0.08f, size * 0.36f, size * 0.36f);
        lockText = UiKit.Text(lockNode, "Label", string.Empty, Mathf.Clamp(size * 0.13f, 15f, 22f),
            UiTheme.TextMuted, TextAlignmentOptions.Center);
        UiKit.BottomStretch(lockText.rectTransform, size * 0.1f, size * 0.24f, 4f, 4f);

        // 고름 — 칸 바깥의 고리와 모서리 체크.
        selectionRing = UiKit.Line(Rect, "Selection", UiTheme.Selection, radius + 5, (int)UiTheme.SelectionWidth);
        UiKit.Fill(selectionRing.rectTransform, -5f);

        float checkSize = Mathf.Clamp(size * 0.24f, 26f, 40f);
        check = UiKit.Node(Rect, "Check");
        UiKit.Place(check, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-checkSize * 0.1f, -checkSize * 0.1f),
            new Vector2(checkSize, checkSize));
        checkFill = UiKit.Rounded(check, "Fill", UiTheme.Selection, Mathf.RoundToInt(checkSize * 0.5f));
        Image checkGlyph = UiKit.Glyph(check, "Glyph", UiSprites.Glyph.Check, UiTheme.OnPrimary);
        UiKit.Fill(checkGlyph.rectTransform, checkSize * 0.16f);

        button = gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => Clicked?.Invoke());

        SetSelected(false);
        SetDimmed(false);
        SetEmpty(string.Empty);
    }

    // ---- 내용 ---------------------------------------------------------------

    public void SetContent(UiSlotContent c)
    {
        placeholder.gameObject.SetActive(false);
        lockNode.gameObject.SetActive(false);
        button.interactable = true;

        icon.sprite = c.Icon;
        icon.enabled = c.Icon != null;
        icon.color = Color.white;
        UiKit.Fill(icon.rectTransform, c.FillsSlot ? 0f : size * 0.1f);
        fallback.text = c.Icon == null ? c.Fallback ?? string.Empty : string.Empty;

        Color tierColor = c.Tier > 0 ? UiTheme.Tier(c.Tier) : UiTheme.Border;
        frame.color = tierColor;
        tierWash.color = c.Tier > 0 ? tierColor : Color.clear;

        SetPill(badge, badgeText, c.Badge);
        badgeFill.color = tierColor;
        // 딱지 폭은 글자 수에 맞춘다("S"와 "★7").
        if (!string.IsNullOrEmpty(c.Badge))
        {
            float h = badge.sizeDelta.y;
            badge.sizeDelta = new Vector2(Mathf.Max(h * 1.2f, badgeText.GetPreferredValues(c.Badge).x + h * 0.6f), h);
        }

        cornerText.text = c.Corner ?? string.Empty;
        SetPill(captionPill, captionText, c.Caption);
        FitPill(captionPill, captionText, c.Caption);
        SetPill(tagPill, tagText, c.Tag);
        FitPill(tagPill, tagText, c.Tag);
        Color tagColor = c.TagColor.a > 0f ? c.TagColor : UiTheme.Selection;
        tagFill.color = tagColor;
        // 밝은 딱지에는 어두운 글자, 어두운 딱지에는 밝은 글자.
        tagText.color = Luminance(tagColor) > 0.42f ? UiTheme.OnPrimary : UiTheme.TextPrimary;
    }

    private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

    /// 빈 칸. hint는 무엇을 넣을지("베이스 선택").
    public void SetEmpty(string hint)
    {
        ClearContent();
        placeholder.gameObject.SetActive(true);
        placeholderText.text = hint ?? string.Empty;
        lockNode.gameObject.SetActive(false);
        button.interactable = true;
    }

    /// 잠긴 칸(아직 없는 기능). ghost가 있으면 무엇이 들어갈 칸인지 흐리게 비춰 둔다.
    public void SetLocked(string label, Sprite ghost = null)
    {
        ClearContent();
        placeholder.gameObject.SetActive(false);
        lockNode.gameObject.SetActive(true);
        lockText.text = label ?? string.Empty;
        background.color = UiTheme.SurfaceSunken;
        button.interactable = false;

        if (ghost != null)
        {
            icon.sprite = ghost;
            icon.enabled = true;
            icon.color = new Color(1f, 1f, 1f, 0.18f);
            UiKit.Fill(icon.rectTransform, size * 0.16f);
        }
    }

    public void SetSelected(bool on) => SetSelected(on, UiTheme.Selection);

    public void SetSelected(bool on, Color color)
    {
        selectionRing.enabled = on;
        selectionRing.color = color;
        check.gameObject.SetActive(on);
        checkFill.color = color;
    }

    public void SetDimmed(bool on, string reason = null)
    {
        dim.enabled = on;
        dimText.text = on ? reason ?? string.Empty : string.Empty;
    }

    public void SetInteractable(bool interactable) => button.interactable = interactable;

    // ---- 도우미 -------------------------------------------------------------

    private void ClearContent()
    {
        icon.sprite = null;
        icon.enabled = false;
        fallback.text = string.Empty;
        frame.color = UiTheme.Border;
        tierWash.color = Color.clear;
        background.color = UiTheme.SurfaceRaised;
        badge.gameObject.SetActive(false);
        cornerText.text = string.Empty;
        captionPill.gameObject.SetActive(false);
        tagPill.gameObject.SetActive(false);
    }

    private static void SetPill(RectTransform pill, TMP_Text text, string value)
    {
        bool show = !string.IsNullOrEmpty(value);
        pill.gameObject.SetActive(show);
        text.text = show ? value : string.Empty;
    }

    private void FitPill(RectTransform pill, TMP_Text text, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        float h = pill.sizeDelta.y;
        float width = Mathf.Min(size * 0.9f, text.GetPreferredValues(value).x + h * 0.7f);
        pill.sizeDelta = new Vector2(width, h);
    }
}
