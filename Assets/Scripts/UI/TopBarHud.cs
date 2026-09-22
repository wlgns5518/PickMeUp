using TMPro;
using UnityEngine;

// 마을 화면 위쪽에 늘 떠 있는 줄.
//
//   ┌ 이름 ──────┐                                  [금화 3,361,233] [보석 47] (편지) (톱니)
//   └ 최고 12층 ─┘
//
// 왼쪽 위는 플레이어 본인(이름과 가장 높이 깬 층), 오른쪽 위는 재화 둘과 우편함·설정 버튼이다.
// 시설 화면과 같은 디자인 시스템(UiKit)으로 짓는다 — 재화 칩은 시설 화면 머리줄의 것과 같은 부품이다.
// 아이콘은 Meshy로 그려 256x256으로 다듬은 것이다(에디터 메뉴 PickMeUp/UI/아이콘 굽기).
//
// 캔버스는 시설 화면(91~99)보다 아래에 둔다. 시설 화면은 화면 전체를 덮고 자기 머리줄에 재화를 다시 보여 준다.
[DisallowMultipleComponent]
public class TopBarHud : MonoBehaviour
{
    // 이 줄이 화면 위에서 차지하는 높이(1920x1080 기준). 위쪽에 붙는 다른 띠(소환 결과)는 이 아래에 놓는다.
    public const float ReservedHeight = ProfileTop + ProfileHeight;

    [Header("Font")]
    [Tooltip("한글 폰트. 비워두면 프로젝트 기본 폰트를 찾아 쓴다.")]
    [SerializeField] private TMP_FontAsset koreanFont;

    [Header("Icons (256x256, Assets/UI/Icons/TopBar)")]
    [SerializeField] private Sprite mailIcon;
    [SerializeField] private Sprite settingsIcon;

    private const string CanvasName = "TopBarCanvas";
    private const int SortingOrder = 90;

    private const float EdgeMargin = 24f;

    private const float ProfileTop = 20f;
    private const float ProfileWidth = 400f;
    private const float ProfileHeight = 104f;
    private const float ProfilePadding = 28f;

    // 오른쪽 줄은 버튼 높이의 가운데에 재화 칩을 맞춘다.
    private const float ButtonTop = 22f;
    private const float ButtonSize = 84f;
    private const float ButtonGap = 12f;
    private const float GoldChipWidth = 290f;
    private const float GemChipWidth = 230f;
    // 칩 아이콘은 칩 왼쪽 끝 밖으로 조금 나온다. 그만큼 옆 칸과 간격을 더 둔다.
    private const float ChipGap = 40f;

    // 아직 만들지 않은 곳을 눌렀을 때 뜨는 알림.
    private const string MailboxNotReady = "우편함은 아직 준비 중입니다.";
    private const string SettingsNotReady = "설정은 아직 준비 중입니다.";

    private RectTransform canvasRect;
    private TMP_Text nameText;
    private TMP_Text floorText;
    private UiToast toast;

    private void Awake()
    {
        UiKit.UseFont(HudFactory.ResolveFont(koreanFont, this));

        // 참조만 잃고 남아 있는 이전 캔버스를 먼저 치운다(도메인 리로드 대비).
        Transform stale = transform.Find(CanvasName);
        if (stale != null) DestroyImmediate(stale.gameObject);

        HudFactory.CreateScreenCanvas(transform, CanvasName, SortingOrder, out canvasRect);
        BuildProfile();
        BuildRightCluster();
        toast = UiToast.Create(canvasRect, ReservedHeight + UiTheme.Space4);
    }

    private void OnEnable()
    {
        PlayerAccount.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        PlayerAccount.Changed -= Refresh;
    }

    // 층 해금은 전투 씬에서만 바뀌고, 돌아오면 이 씬이 새로 뜬다. 세이브를 읽는 RosterBootstrap(-100)보다
    // 늦게 한 번 더 읽어 두면 된다.
    private void Start()
    {
        Refresh();
    }

    private void Refresh()
    {
        if (nameText == null) return;

        nameText.text = PlayerAccount.Name;

        int highest = FloorProgress.HighestCleared;
        floorText.text = highest > 0
            ? $"최고 {UiTheme.Paint(highest + "층", UiTheme.Primary)}"
            : "최고 기록 없음";
    }

    // ---- 왼쪽 위: 이름과 최고 층 ------------------------------------------

    private void BuildProfile()
    {
        UiKit.Surface card = UiKit.Panel(canvasRect, "Profile", UiTheme.Surface, UiTheme.RadiusL, UiTheme.Border, 16);
        UiKit.TopLeft(card.Rect, EdgeMargin, ProfileTop, ProfileWidth, ProfileHeight);
        card.Fill.raycastTarget = true;

        float textWidth = ProfileWidth - ProfilePadding * 2f;

        nameText = UiKit.Ellipsis(UiKit.Text(card.Rect, "Name", string.Empty, UiTheme.FontTitle, UiTheme.TextPrimary));
        UiKit.TopLeft(nameText.rectTransform, ProfilePadding, 8f, textWidth, 54f);

        floorText = UiKit.Text(card.Rect, "HighestFloor", string.Empty, UiTheme.FontLabel, UiTheme.TextSecondary);
        UiKit.TopLeft(floorText.rectTransform, ProfilePadding, 60f, textWidth, 34f);
    }

    // ---- 오른쪽 위: 골드 · 젬 · 우편함 · 설정 -----------------------------

    private void BuildRightCluster()
    {
        // 오른쪽 끝에서부터 왼쪽으로 쌓는다. right는 다음 칸의 오른쪽 끝이 화면 오른쪽에서 떨어진 거리다.
        float right = EdgeMargin;

        UiButton settings = UiButton.CreateIcon(canvasRect, "Settings", settingsIcon, false, ButtonSize,
            UiButtonStyle.Ghost, () => toast.Show(SettingsNotReady));
        UiKit.TopRight(settings.Rect, right, ButtonTop, ButtonSize, ButtonSize);
        right += ButtonSize + ButtonGap;

        UiButton mail = UiButton.CreateIcon(canvasRect, "Mailbox", mailIcon, false, ButtonSize,
            UiButtonStyle.Ghost, () => toast.Show(MailboxNotReady));
        UiKit.TopRight(mail.Rect, right, ButtonTop, ButtonSize, ButtonSize);
        right += ButtonSize + ChipGap - 16f;

        float chipTop = ButtonTop + (ButtonSize - UiCurrencyChip.Height) * 0.5f;

        UiCurrencyChip gem = UiCurrencyChip.Create(canvasRect, "Gem", Currency.Gem, GemChipWidth);
        UiKit.TopRight(gem.Rect, right, chipTop, GemChipWidth, UiCurrencyChip.Height);
        right += GemChipWidth + ChipGap;

        UiCurrencyChip gold = UiCurrencyChip.Create(canvasRect, "Gold", Currency.Gold, GoldChipWidth);
        UiKit.TopRight(gold.Rect, right, chipTop, GoldChipWidth, UiCurrencyChip.Height);
    }
}
