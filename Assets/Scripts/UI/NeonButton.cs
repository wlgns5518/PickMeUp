using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 킷 버튼 스프라이트 중 무엇을 입힐지.
public enum NeonButtonStyle
{
    Primary,    // btn_primary — 주요 버튼, 선택된 탭 (밝은 판에 어두운 글자)
    Secondary,  // btn_secondary — 보조 버튼, 고른 줄 (네온 테두리)
    Ghost,      // btn_ghost — 평소의 칸, 3차 버튼
    Danger,     // btn_danger — 경고·이탈
    Muted,      // btn_disabled 모양이지만 누를 수는 있는 칸 (다른 영웅이 든 장비 등)
    Icon,       // icon_btn — 정사각 버튼 (닫기 X)
}

// 킷 스프라이트를 입힌 버튼.
//
// 유니티 Button은 전환 방식을 하나만 고를 수 있다. 색 틴트를 고르면 비활성 스프라이트(btn_disabled)로
// 바꿀 수 없고, 스프라이트 교체를 고르면 올려놓았을 때 아무 반응이 없다. 킷은 둘 다 필요해서
// 틴트는 Button에 맡기고, 비활성으로 들어가고 나올 때만 여기서 스프라이트와 글자색을 바꾼다.
//
// 탭처럼 고른 것만 밝히는 버튼은 SetStyle로 모양을 바꾼다.
[DisallowMultipleComponent]
public class NeonButton : Button
{
    // 틴트는 스프라이트 원래 색보다 밝게 올릴 수 없다(정점 색이 1에서 잘린다). 그래서 평소를 한 단계
    // 눌러 두고, 올려놓으면 원래 밝기로 돌아오게 한다.
    private const float RestBrightness = 0.86f;

    private Image background;
    private TMP_Text label;
    private NeonButtonStyle style;
    private bool appliedDisabled;
    private bool labelColorOverridden;
    private Color labelColorOverride;

    public TMP_Text Label => label;
    public RectTransform Rect => (RectTransform)transform;

    // HudFactory.CreateButton이 AddComponent 직후에 부른다. OnEnable이 먼저 돌므로 그때는 아무것도 없다.
    public void Setup(Image target, TMP_Text text, NeonButtonStyle initialStyle)
    {
        background = target;
        label = text;
        style = initialStyle;
        targetGraphic = target;

        transition = Transition.ColorTint;
        ColorBlock block = colors;
        block.colorMultiplier = 1f;
        block.normalColor = Tint(RestBrightness);
        block.highlightedColor = Tint(1f);
        block.pressedColor = Tint(0.72f);
        // 누른 뒤 선택 상태로 남아도 밝게 남지 않게 한다. 안 그러면 방금 누른 탭만 계속 밝아 보인다.
        block.selectedColor = block.normalColor;
        // 비활성은 색이 아니라 스프라이트(btn_disabled)로 드러낸다.
        block.disabledColor = block.normalColor;
        block.fadeDuration = 0.08f;
        colors = block;

        appliedDisabled = !IsInteractable();
        ApplyLook();
    }

    public void SetStyle(NeonButtonStyle newStyle)
    {
        if (style == newStyle) return;

        style = newStyle;
        ApplyLook();
    }

    // 글자가 뜻을 가진 버튼(제작소 재료 칸의 등급색). 스타일이 정하는 글자색 대신 이 색을 쓴다.
    // 비활성일 때는 여전히 흐린 글자로 바뀐다 — 누를 수 없다는 게 먼저다.
    public void SetLabelColor(Color color)
    {
        labelColorOverridden = true;
        labelColorOverride = color;
        ApplyLabelColor();
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        base.DoStateTransition(state, instant);

        bool disabled = state == SelectionState.Disabled;
        if (disabled == appliedDisabled) return;

        appliedDisabled = disabled;
        ApplyLook();
    }

    private void ApplyLook()
    {
        if (background == null) return;

        NeonUISkin skin = NeonUISkin.Current;
        Sprite sprite = skin != null ? SpriteFor(skin, appliedDisabled ? NeonButtonStyle.Muted : style) : null;
        HudFactory.ApplySkin(background, sprite, FallbackColor(appliedDisabled ? NeonButtonStyle.Muted : style));

        ApplyLabelColor();
    }

    private void ApplyLabelColor()
    {
        if (label == null) return;

        if (appliedDisabled) label.color = BattleHudPalette.TextMuted;
        else if (labelColorOverridden) label.color = labelColorOverride;
        else label.color = LabelColorFor(style);
    }

    private static Color Tint(float brightness) => new Color(brightness, brightness, brightness, 1f);

    private static Sprite SpriteFor(NeonUISkin skin, NeonButtonStyle s)
    {
        switch (s)
        {
            case NeonButtonStyle.Primary: return skin.buttonPrimary;
            case NeonButtonStyle.Secondary: return skin.buttonSecondary;
            case NeonButtonStyle.Danger: return skin.buttonDanger;
            case NeonButtonStyle.Muted: return skin.buttonDisabled;
            case NeonButtonStyle.Icon: return skin.iconButton;
            default: return skin.buttonGhost;
        }
    }

    // 스프라이트가 없을 때 칠할 단색. 모양은 없어도 스타일끼리 밝기 차이는 남긴다.
    private static Color FallbackColor(NeonButtonStyle s)
    {
        switch (s)
        {
            case NeonButtonStyle.Primary: return BattleHudPalette.Accent;
            case NeonButtonStyle.Secondary:
            case NeonButtonStyle.Icon: return new Color(0.34f, 0.22f, 0.54f, 0.9f);
            case NeonButtonStyle.Danger: return new Color(0.45f, 0.12f, 0.20f, 0.9f);
            case NeonButtonStyle.Muted: return new Color(0.10f, 0.09f, 0.13f, 0.9f);
            default: return BattleHudPalette.PortraitFrame;
        }
    }

    private static Color LabelColorFor(NeonButtonStyle s)
    {
        return s == NeonButtonStyle.Primary ? BattleHudPalette.TextOnAccent
            : s == NeonButtonStyle.Muted ? BattleHudPalette.TextMuted
            : BattleHudPalette.TextPrimary;
    }
}
