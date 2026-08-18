using UnityEngine;

// 전투 HUD의 색을 한 곳에 모아둔다. 파티 패널과 결과창이 같은 규칙을 쓰도록.
public static class BattleHudPalette
{
    // 파티 패널 게이지 (원작 게임 화면의 붉은 체력바를 따른다)
    public static readonly Color PartyHp = new Color(0.93f, 0.29f, 0.40f);
    public static readonly Color GaugeBackground = new Color(0.05f, 0.05f, 0.08f, 0.85f);
    public static readonly Color PortraitFrame = new Color(0.16f, 0.15f, 0.19f, 0.95f);

    // 전투 불능이 된 슬롯은 통째로 어둡게 눌러 살아있는 동료와 한눈에 구분되게 한다.
    public static readonly Color DeadTint = new Color(0.35f, 0.35f, 0.38f, 0.75f);
    public static readonly Color AliveTint = Color.white;

    public static readonly Color Mana = new Color(0.36f, 0.62f, 0.98f);

    public static readonly Color Fear = new Color(1.00f, 0.66f, 0.20f);
    public static readonly Color Panic = new Color(0.76f, 0.47f, 1.00f);
    public static readonly Color Bleeding = new Color(0.93f, 0.25f, 0.25f);
    public static readonly Color Dying = new Color(0.72f, 0.72f, 0.76f);
    public static readonly Color Broken = new Color(0.85f, 0.13f, 0.36f);

    public static readonly Color PanelBackdrop = new Color(0f, 0f, 0f, 0.72f);
    public static readonly Color PanelBody = new Color(0.09f, 0.09f, 0.12f, 0.96f);
    public static readonly Color PanelText = new Color(0.92f, 0.92f, 0.94f);
    public static readonly Color Mvp = new Color(1.00f, 0.83f, 0.32f);
    public static readonly Color Victory = new Color(0.45f, 0.90f, 0.55f);
    public static readonly Color Defeat = new Color(0.93f, 0.35f, 0.35f);

    public static Color ForEmotion(EmotionState state)
    {
        switch (state)
        {
            case EmotionState.Panic: return Panic;
            case EmotionState.Bleeding: return Bleeding;
            case EmotionState.Dying: return Dying;
            case EmotionState.Broken: return Broken;
            default: return Fear;
        }
    }
}
