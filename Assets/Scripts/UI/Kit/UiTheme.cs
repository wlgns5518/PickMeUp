using UnityEngine;

// 시설 화면(소환·합성·장비·제작)이 함께 쓰는 디자인 규칙 — 색, 등급 색, 간격, 모서리, 글자 크기, 크기 단계.
//
// 화면마다 색과 크기를 새로 정하면 화면을 옮겨 다닐 때 같은 게임처럼 보이지 않는다. 그래서 값은 여기 한 곳에만
// 두고, 부품(UiKit·UiButton·UiSlot…)과 화면은 이름으로만 가져다 쓴다. 숫자를 코드에 바로 적지 않는다.
//
// 기준 해상도는 1920x1080(HudFactory.CreateScreenCanvas와 같다). 프로젝트가 선형 색공간이라 반투명한 면은
// 눈에 훨씬 옅게 보인다(neon-ui-kit 메모) — 그래서 면은 전부 불투명한 색으로 두고, 투명도는 배경막에만 쓴다.
public static class UiTheme
{
    // ---- 면 -----------------------------------------------------------------
    public static readonly Color ScreenTop = Hex(0x161B26);
    public static readonly Color ScreenBottom = Hex(0x0A0D13);
    public static readonly Color Surface = Hex(0x1B2130);         // 패널
    public static readonly Color SurfaceRaised = Hex(0x252C3C);   // 카드·슬롯·버튼
    public static readonly Color SurfaceHover = Hex(0x303A4E);    // 올려놓았거나 고른 칸
    public static readonly Color SurfaceSunken = Hex(0x11151E);   // 목록 바닥·탭 트랙·재화 칩
    public static readonly Color Border = Hex(0x323B4F);
    public static readonly Color BorderStrong = Hex(0x4B5670);
    public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.55f);
    public static readonly Color Backdrop = new Color(0.015f, 0.02f, 0.035f, 0.9f);

    // ---- 글자 ---------------------------------------------------------------
    public static readonly Color TextPrimary = Hex(0xF3F5F9);
    public static readonly Color TextSecondary = Hex(0xAAB3C5);
    public static readonly Color TextMuted = Hex(0x6E778A);

    // ---- 뜻이 있는 색 --------------------------------------------------------
    // 가장 중요한 실행(소환·합성·제작·강화). 화면마다 이 색 버튼은 하나뿐이다.
    public static readonly Color Primary = Hex(0xF5B83D);
    public static readonly Color PrimaryLight = Hex(0xFFD37A);
    public static readonly Color PrimaryDeep = Hex(0xD9941B);
    public static readonly Color OnPrimary = Hex(0x241703);
    // 고른 것. 슬롯 테두리, 탭 밑줄, 체크 표시가 같은 색이다.
    public static readonly Color Selection = Hex(0x4FC3FF);
    public static readonly Color Success = Hex(0x46D08A);
    public static readonly Color Warning = Hex(0xFFB84D);
    public static readonly Color Danger = Hex(0xFF5B5B);
    public static readonly Color DangerSurface = Hex(0x3A1D24);

    // ---- 등급 ---------------------------------------------------------------
    //
    // 장비 등급(E~S)과 영웅 별(1~7성)을 한 줄의 단계로 맞춘다 — 같은 단계는 어느 화면에서든 같은 색이다.
    // E=1성=회색, D=2성=초록, C=3성=파랑, B=4성=보라, A=5성=주황, S=6성=금색, 7성=진홍.
    private static readonly Color[] TierColors =
    {
        Hex(0x9BA4B5), Hex(0x58CE7C), Hex(0x4AA3FF), Hex(0xB07CFF), Hex(0xFF9E3D), Hex(0xFFD447), Hex(0xFF5C8D),
    };

    public const int MaxTier = 7;

    public static Color Tier(int tier) => TierColors[Mathf.Clamp(tier, 1, MaxTier) - 1];
    public static int TierOf(EquipmentGrade grade) => (int)grade + 1;
    public static int TierOfStars(int stars) => Mathf.Clamp(stars, 1, MaxTier);
    public static Color GradeColor(EquipmentGrade grade) => Tier(TierOf(grade));
    public static Color StarColor(int stars) => Tier(TierOfStars(stars));

    // ---- 간격 (4의 배수) -----------------------------------------------------
    public const float Space1 = 4f;
    public const float Space2 = 8f;
    public const float Space3 = 12f;
    public const float Space4 = 16f;
    public const float Space5 = 24f;
    public const float Space6 = 32f;
    public const float Space7 = 48f;

    // 화면 가장자리와 기둥(칸) 사이.
    public const float ScreenPadding = 32f;
    public const float ColumnGap = 24f;

    // ---- 모서리 -------------------------------------------------------------
    public const int RadiusS = 8;
    public const int RadiusM = 14;
    public const int RadiusL = 20;

    // ---- 글자 크기 ----------------------------------------------------------
    public const float FontDisplay = 46f;
    public const float FontTitle = 34f;
    public const float FontHeading = 28f;
    public const float FontBody = 24f;
    public const float FontLabel = 21f;
    public const float FontCaption = 18f;

    // NotoSansKR의 줄 높이는 글자 크기의 약 1.45배다. 말줄임을 건 칸이 이보다 낮으면 글자가 통째로 사라진다.
    public const float LineHeight = 1.5f;

    // ---- 크기 단계 ----------------------------------------------------------
    public const float HeaderHeight = 104f;

    public const float ButtonLarge = 88f;
    public const float ButtonMedium = 64f;
    public const float ButtonSmall = 48f;

    public const float SlotLarge = 184f;
    public const float SlotMedium = 128f;
    public const float SlotSmall = 96f;

    public const float TabHeight = 60f;
    public const float SelectionWidth = 4f;

    // ---- 도우미 -------------------------------------------------------------

    public static Color Hex(int rgb) =>
        new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

    public static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

    // 리치 텍스트에 넣을 색 문자열("F5B83D").
    public static string Html(Color color) => ColorUtility.ToHtmlStringRGB(color);

    // "<color=#F5B83D>글자</color>"
    public static string Paint(string text, Color color) => $"<color=#{Html(color)}>{text}</color>";
}
