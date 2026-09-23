using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum UiButtonStyle
{
    Primary,   // 화면의 가장 중요한 실행(소환·합성·제작·강화·장착). 화면마다 하나.
    Secondary, // 그다음 행동(1회 소환, 소환 확률, 해제).
    Ghost,     // 덜 중요한 행동(취소, 닫기).
    Danger,    // 되돌릴 수 없는 손실을 부르는 확인(재료 영웅을 태우는 합성 확인 등).
}

public enum UiButtonSize { Small, Medium, Large }

// 디자인 시스템의 버튼 하나.
//
// 상태는 넷이다 — 평소, 올려놓음, 누름, 비활성. 모양은 스타일이 정하고, 비활성이면 스타일과 무관하게 같은
// 흐린 판이 된다(무엇을 못 누르는지가 스타일보다 먼저 읽혀야 한다). 누르는 동안 살짝 작아진다.
//
// 비용을 달 수 있다(SetCost). 비용 줄은 라벨 아래에 재화 아이콘과 액수로 붙고, 모자라면 액수가 빨갛게 바뀐다.
// "무엇이 필요하고 지금 얼마 있는지"를 실행 버튼 자체가 말하게 하려는 것이다. 모자랄 때 버튼을 잠글지는
// 부르는 쪽이 interactable로 정한다(눌러서 이유를 듣게 둘 수도 있다).
[DisallowMultipleComponent]
public class UiButton : Button
{
    private Image fill;
    private Image border;
    private UiGradient sheen;
    private TMP_Text label;
    private RectTransform costRow;
    private Image costIcon;
    private TMP_Text costText;

    private UiButtonStyle style;
    private UiButtonSize size;
    private bool costAffordable = true;

    public RectTransform Rect => (RectTransform)transform;
    public TMP_Text Label => label;
    /// CreateIcon으로 만든 버튼의 그림. 글자 버튼이면 null.
    public Image Icon => icon;

    public static float HeightOf(UiButtonSize size) =>
        size == UiButtonSize.Large ? UiTheme.ButtonLarge : size == UiButtonSize.Medium ? UiTheme.ButtonMedium : UiTheme.ButtonSmall;

    private static float FontOf(UiButtonSize size) =>
        size == UiButtonSize.Large ? UiTheme.FontTitle : size == UiButtonSize.Medium ? UiTheme.FontBody : UiTheme.FontLabel;

    private static int RadiusOf(UiButtonSize size) => size == UiButtonSize.Small ? UiTheme.RadiusS : UiTheme.RadiusM;

    /// 만들기. 자리와 폭은 돌려받은 버튼의 Rect에 잡는다(높이는 크기 단계가 정한다).
    public static UiButton Create(RectTransform parent, string name, string text, UiButtonStyle style, UiButtonSize size,
        UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = UiKit.Node(parent, name);
        rect.sizeDelta = new Vector2(240f, HeightOf(size));

        int radius = RadiusOf(size);
        Image fill = UiKit.Rounded(rect, "Fill", Color.white, radius);
        // 누를 수 있는 판은 이것 하나다. 글자와 테두리는 클릭을 받지 않는다.
        fill.raycastTarget = true;
        Image border = UiKit.Line(rect, "Border", Color.clear, radius, 2);

        TMP_Text label = UiKit.Text(rect, "Label", text, FontOf(size), UiTheme.TextPrimary, TextAlignmentOptions.Center);

        var button = rect.gameObject.AddComponent<UiButton>();
        // targetGraphic을 넣는 순간 상태 전환(Apply)이 불린다. 그 전에 칠할 것을 전부 걸어 둔다.
        button.fill = fill;
        button.border = border;
        button.label = label;
        button.size = size;
        button.sheen = fill.gameObject.AddComponent<UiGradient>();
        button.style = style;
        button.transition = Transition.None;
        button.targetGraphic = fill;
        if (onClick != null) button.onClick.AddListener(onClick);
        button.Apply();
        return button;
    }

    /// 동그란 아이콘 버튼(뒤로가기·닫기·정보). 기호(UiSprites.Icon)는 글자색으로 칠하고, 그림 아이콘은 그대로 둔다.
    public static UiButton CreateIcon(RectTransform parent, string name, Sprite icon, bool tintIcon, float diameter,
        UiButtonStyle style, UnityEngine.Events.UnityAction onClick)
    {
        UiButton button = Create(parent, name, string.Empty, style, UiButtonSize.Medium, onClick);
        int radius = Mathf.RoundToInt(diameter * 0.5f);
        button.fill.sprite = UiSprites.Rounded(radius);
        button.border.sprite = UiSprites.Outline(radius, 2);
        button.Rect.sizeDelta = new Vector2(diameter, diameter);

        button.icon = UiKit.Image(button.Rect, "Icon", icon, Color.white);
        UiKit.Fill(button.icon.rectTransform, diameter * (tintIcon ? 0.28f : 0.14f));
        button.tintIcon = tintIcon;
        button.Apply();
        return button;
    }

    private Image icon;
    private bool tintIcon;

    public void SetLabel(string text)
    {
        if (label != null) label.text = text;
    }

    public void SetStyle(UiButtonStyle newStyle)
    {
        style = newStyle;
        Apply();
    }

    /// 라벨 아래에 비용을 단다. icon이 null이면 글자만("무료").
    public void SetCost(Sprite icon, string amount, bool affordable)
    {
        EnsureCostRow();
        costRow.gameObject.SetActive(true);
        costIcon.sprite = icon;
        costIcon.gameObject.SetActive(icon != null);
        costText.text = amount;
        costAffordable = affordable;
        LayoutLabel();
        LayoutCost();
        Apply();
    }

    public void SetCost(Currency currency, long amount) =>
        SetCost(UiIconLibrary.Currency(currency), UiKit.Amount(amount), PlayerAccount.Balance(currency) >= amount);

    public void ClearCost()
    {
        if (costRow != null) costRow.gameObject.SetActive(false);
        costAffordable = true;
        LayoutLabel();
        Apply();
    }

    private void EnsureCostRow()
    {
        if (costRow != null) return;

        float costSize = size == UiButtonSize.Large ? UiTheme.FontLabel : UiTheme.FontCaption;
        float iconSize = costSize * 1.4f;

        costRow = UiKit.Node(Rect, "Cost");
        costRow.anchorMin = new Vector2(0f, 0f);
        costRow.anchorMax = new Vector2(1f, 0f);
        costRow.pivot = new Vector2(0.5f, 0f);
        costRow.offsetMin = new Vector2(0f, HeightOf(size) * 0.12f);
        costRow.offsetMax = new Vector2(0f, HeightOf(size) * 0.12f + iconSize);

        costText = UiKit.Text(costRow, "Amount", string.Empty, costSize, UiTheme.TextPrimary, TextAlignmentOptions.Center);
        // 아이콘은 글자 폭을 따라 왼쪽에 붙는다(LayoutCost).
        RectTransform iconRect = UiKit.Node(costRow, "Icon");
        costIcon = iconRect.gameObject.AddComponent<Image>();
        costIcon.preserveAspect = true;
        costIcon.raycastTarget = false;
        UiKit.Center(iconRect, 0f, 0f, iconSize, iconSize);
    }

    // 비용이 있으면 라벨을 위로 올려 두 줄로 쓴다.
    private void LayoutLabel()
    {
        bool hasCost = costRow != null && costRow.gameObject.activeSelf;
        float h = HeightOf(size);
        if (hasCost) UiKit.Fill(label.rectTransform, 0f, h * 0.08f, 0f, h * 0.42f);
        else UiKit.Fill(label.rectTransform);
        label.fontSize = hasCost ? FontOf(size) * 0.82f : FontOf(size);
    }

    // 아이콘을 액수 글자 왼쪽에 붙이고 둘을 함께 가운데에 둔다.
    //
    // 예전에는 버튼마다 LateUpdate에서 매 프레임 글자 폭을 읽어 맞췄다 — 비용이 없는 버튼도 매 프레임 불려
    // 마을에 떠 있는 버튼 수만큼 스크립트가 돌았다. 글자 폭은 GetPreferredValues로 그리기 전에도 잴 수 있어
    // 액수가 바뀔 때 한 번만 맞춘다.
    private void LayoutCost()
    {
        if (costRow == null || !costIcon.gameObject.activeSelf)
        {
            if (costText != null) costText.rectTransform.anchoredPosition = Vector2.zero;
            return;
        }

        float textWidth = costText.GetPreferredValues(costText.text).x;
        float iconSize = costIcon.rectTransform.sizeDelta.x;
        costIcon.rectTransform.anchoredPosition = new Vector2(-(textWidth + iconSize) * 0.5f, 0f);
        costText.rectTransform.anchoredPosition = new Vector2(iconSize * 0.5f + 2f, 0f);
    }

    // ---- 상태 ---------------------------------------------------------------

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        base.DoStateTransition(state, instant);
        Apply(state);
    }

    private void Apply() => Apply(currentSelectionState);

    private void Apply(SelectionState state)
    {
        if (fill == null || sheen == null) return;

        bool disabled = state == SelectionState.Disabled;
        bool hover = state == SelectionState.Highlighted;
        bool down = state == SelectionState.Pressed;

        Color fillColor, borderColor, textColor;
        Color top = Color.white, bottom = Color.white;

        if (disabled)
        {
            fillColor = UiTheme.SurfaceRaised;
            borderColor = UiTheme.Border;
            textColor = UiTheme.TextMuted;
        }
        else
        {
            switch (style)
            {
                case UiButtonStyle.Primary:
                    fillColor = down ? UiTheme.PrimaryDeep : UiTheme.Primary;
                    // 위가 밝고 아래가 조금 어두운 윤기. 눌렀을 때는 평평하게.
                    top = down ? Color.white : hover ? new Color(1.2f, 1.2f, 1.2f, 1f) : new Color(1.1f, 1.1f, 1.1f, 1f);
                    bottom = down ? Color.white : new Color(0.88f, 0.88f, 0.88f, 1f);
                    borderColor = down ? UiTheme.PrimaryDeep : UiTheme.PrimaryLight;
                    textColor = UiTheme.OnPrimary;
                    break;
                case UiButtonStyle.Danger:
                    fillColor = down ? UiTheme.Surface : hover ? Color.Lerp(UiTheme.DangerSurface, UiTheme.Danger, 0.15f) : UiTheme.DangerSurface;
                    borderColor = UiTheme.Danger;
                    textColor = UiTheme.Danger;
                    break;
                case UiButtonStyle.Ghost:
                    fillColor = down ? UiTheme.SurfaceSunken : hover ? UiTheme.SurfaceRaised : UiTheme.Surface;
                    borderColor = UiTheme.Border;
                    textColor = UiTheme.TextSecondary;
                    break;
                default:
                    fillColor = down ? UiTheme.SurfaceRaised : hover ? UiTheme.BorderStrong : UiTheme.SurfaceHover;
                    borderColor = UiTheme.BorderStrong;
                    textColor = UiTheme.TextPrimary;
                    break;
            }
        }

        fill.color = fillColor;
        sheen.Set(top, bottom);
        border.color = borderColor;
        label.color = textColor;
        if (icon != null) icon.color = tintIcon ? textColor : disabled ? new Color(1f, 1f, 1f, 0.4f) : Color.white;

        if (costText != null)
        {
            // 모자라면 빨강. 비활성이어도 빨강이 먼저 읽혀야 왜 못 누르는지 안다.
            costText.color = !costAffordable ? UiTheme.Danger : disabled ? UiTheme.TextMuted : textColor;
        }

        transform.localScale = down && !disabled ? new Vector3(0.97f, 0.97f, 1f) : Vector3.one;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        transform.localScale = Vector3.one;
    }
}
