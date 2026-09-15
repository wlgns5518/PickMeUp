using UnityEngine;

// UI의 색을 한 곳에 모아둔다. 전투 HUD와 시설 창이 같은 규칙을 쓰도록.
//
// 바탕은 네온 HUD 킷(Assets/UI/README.md)의 색상 토큰이다. 킷 스프라이트가 모양과 색을 함께 들고 있으므로
// 여기 색은 스프라이트 위에 얹는 글자, 스프라이트가 없을 때의 대비책, 그리고 킷에 없는 뜻(감정·등급)에 쓴다.
public static class BattleHudPalette
{
    // ---- 킷 색상 토큰 ------------------------------------------------------
    public static readonly Color Accent = new Color32(0xCB, 0xA6, 0xFF, 0xFF);
    public static readonly Color AccentLight = new Color32(0xF0, 0xE4, 0xFF, 0xFF);
    public static readonly Color AccentDeep = new Color32(0x8F, 0x6B, 0xFF, 0xFF);
    public static readonly Color Danger = new Color32(0xFF, 0x5F, 0x7E, 0xFF);
    public static readonly Color BgDeep = new Color32(0x05, 0x04, 0x0A, 0xFF);
    public static readonly Color BgPanel = new Color32(0x0C, 0x08, 0x18, 0xFF);
    public static readonly Color TextPrimary = new Color32(0xEC, 0xE5, 0xF7, 0xFF);
    public static readonly Color TextMuted = new Color32(0x8B, 0x81, 0xA3, 0xFF);

    // ---- 토큰에서 끌어낸 판 색 ---------------------------------------------

    // 창 뒤를 덮는 배경막.
    public static readonly Color PanelBackdrop = new Color(BgDeep.r, BgDeep.g, BgDeep.b, 0.8f);
    // 스프라이트가 없을 때의 창 판.
    public static readonly Color PanelBody = new Color(BgPanel.r, BgPanel.g, BgPanel.b, 0.96f);
    // 카드와 초상화를 두르는 얇은 테두리. 판보다 한 단계 밝은 보라.
    public static readonly Color PortraitFrame = new Color32(0x22, 0x1A, 0x36, 0xF2);
    // 게이지 빈 칸(스프라이트가 없을 때).
    public static readonly Color GaugeBackground = new Color(BgDeep.r, BgDeep.g, BgDeep.b, 0.85f);
    // 목록 바닥처럼 판 위에 한 겹 더 까는 옅은 면. 선형 색공간에서는 낮은 알파도 눈에 크게 보여 아주 옅게 둔다.
    public static readonly Color ListGround = new Color(Accent.r, Accent.g, Accent.b, 0.015f);
    // 밝은 버튼(btn_primary) 위에 올리는 글자.
    public static readonly Color TextOnAccent = BgPanel;

    // 게이지 채움(스프라이트가 없을 때). 스프라이트가 있으면 gauge_fill_hp/mp가 색을 들고 있다.
    public static readonly Color PartyHp = Danger;
    public static readonly Color Mana = AccentDeep;

    // ---- 뜻이 있는 색 (킷에 없는 것) ---------------------------------------

    // 전투 불능이 된 슬롯은 통째로 어둡게 눌러 살아있는 동료와 한눈에 구분되게 한다.
    public static readonly Color DeadTint = new Color(0.35f, 0.33f, 0.40f, 0.75f);
    public static readonly Color AliveTint = Color.white;

    public static readonly Color Fear = new Color(1.00f, 0.66f, 0.20f);
    public static readonly Color Panic = new Color(0.76f, 0.47f, 1.00f);
    public static readonly Color Bleeding = new Color(0.93f, 0.25f, 0.25f);
    public static readonly Color Dying = new Color(0.72f, 0.70f, 0.78f);
    public static readonly Color Broken = new Color(0.85f, 0.13f, 0.36f);

    // 경고 문구("더 배울 수 없습니다"). 오류(Danger)만큼 급하지 않은 것.
    public static readonly Color Warn = new Color(0.95f, 0.62f, 0.35f);

    // MVP와 최고 등급(S·5성). 보라 일색인 화면에서 가장 귀한 것만 금색으로 튀게 둔다.
    public static readonly Color Mvp = new Color(1.00f, 0.83f, 0.32f);
    public static readonly Color Victory = new Color(0.45f, 0.90f, 0.55f);

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
