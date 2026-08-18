using System.Text;
using TMPro;
using UnityEngine;

// 전투가 끝났을 때 뜨는 결과창. 승패 / MVP / 영구 사망자만 보여준다.
// 영구 사망 목록은 승리했을 때도 항상 표시한다 — 이 게임에서 이긴 판과 잃지 않은 판은 다르다.
//
// 경험치는 BattleManager가 계속 정산하지만(레벨업은 그대로 적용된다) 이 화면에는 띄우지 않는다.
public class BattleResultPanel
{
    private const float PanelWidth = 760f;
    private const float PanelHeight = 440f;

    private static readonly StringBuilder Builder = new StringBuilder(256);

    private readonly RectTransform root;
    private readonly TMP_Text titleText;
    private readonly TMP_Text mvpText;
    private readonly TMP_Text bodyText;

    private BattleResultPanel(RectTransform parent, TMP_FontAsset font)
    {
        root = HudFactory.CreateGroup(parent, "BattleResultPanel");
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        var backdrop = HudFactory.CreateImage(root, "Backdrop", BattleHudPalette.PanelBackdrop);
        backdrop.rectTransform.anchorMin = Vector2.zero;
        backdrop.rectTransform.anchorMax = Vector2.one;
        backdrop.rectTransform.offsetMin = Vector2.zero;
        backdrop.rectTransform.offsetMax = Vector2.zero;

        var body = HudFactory.CreateImage(root, "Body", BattleHudPalette.PanelBody);
        RectTransform bodyRect = body.rectTransform;
        bodyRect.anchorMin = new Vector2(0.5f, 0.5f);
        bodyRect.anchorMax = new Vector2(0.5f, 0.5f);
        bodyRect.pivot = new Vector2(0.5f, 0.5f);
        bodyRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        bodyRect.anchoredPosition = Vector2.zero;

        titleText = HudFactory.CreateText(bodyRect, "Title", font, 64f, BattleHudPalette.Victory);
        titleText.rectTransform.sizeDelta = new Vector2(PanelWidth - 40f, 90f);
        titleText.rectTransform.anchoredPosition = new Vector2(0f, PanelHeight * 0.5f - 62f);

        // MVP는 결과창에서 가장 먼저 눈에 들어와야 하므로 제목 바로 아래에 금색으로 따로 둔다.
        mvpText = HudFactory.CreateText(bodyRect, "Mvp", font, 34f, BattleHudPalette.Mvp);
        mvpText.rectTransform.sizeDelta = new Vector2(PanelWidth - 40f, 46f);
        mvpText.rectTransform.anchoredPosition = new Vector2(0f, PanelHeight * 0.5f - 126f);

        bodyText = HudFactory.CreateText(bodyRect, "Body", font, 28f, BattleHudPalette.PanelText);
        bodyText.rectTransform.sizeDelta = new Vector2(PanelWidth - 60f, PanelHeight - 210f);
        bodyText.rectTransform.anchoredPosition = new Vector2(0f, -45f);
        // 본문은 공백으로 열을 맞춘 목록이라 가운데 정렬하면 열이 어긋난다.
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.textWrappingMode = TextWrappingModes.Normal;

        // 파티가 커지면 사망자 줄이 늘어 본문이 패널 밖으로 넘친다.
        // 고정 크기로 두면 인원수에 따라 글자가 상자 밖에 그려지므로 자동 축소를 켠다.
        bodyText.enableAutoSizing = true;
        bodyText.fontSizeMin = 16f;
        bodyText.fontSizeMax = 28f;
        bodyText.overflowMode = TextOverflowModes.Truncate;

        root.gameObject.SetActive(false);
    }

    public static BattleResultPanel Create(RectTransform parent, TMP_FontAsset font)
    {
        return new BattleResultPanel(parent, font);
    }

    public void Show(BattleResult result)
    {
        if (result == null) return;

        titleText.text = result.KoreanOutcome;
        titleText.color = result.Outcome == BattleOutcome.Victory
            ? BattleHudPalette.Victory
            : BattleHudPalette.Defeat;

        ApplyMvp(result.Mvp);
        ApplyBody(result);

        root.gameObject.SetActive(true);
        // 늦게 만들어진 위젯이 결과창을 덮지 않도록 항상 맨 앞으로 올린다.
        root.SetAsLastSibling();
    }

    public void Hide()
    {
        root.gameObject.SetActive(false);
    }

    private void ApplyMvp(BattleReward mvp)
    {
        mvpText.text = mvp != null ? "MVP   " + mvp.DisplayName : "";
    }

    private void ApplyBody(BattleResult result)
    {
        Builder.Clear();

        if (result.FallenCharacters.Count == 0)
        {
            Builder.Append("잃은 동료 없음");
        }
        else
        {
            Builder.Append("영구 사망 (").Append(result.FallenCharacters.Count).Append("명)").Append('\n');
            for (int i = 0; i < result.FallenCharacters.Count; i++)
            {
                CharacterSO fallen = result.FallenCharacters[i];
                if (fallen == null) continue;

                // NotoSansKR 아틀라스에는 ASCII와 한글밖에 없다. 가운뎃점(U+00B7)을 쓰면
                // 동료가 죽을 때마다 글리프 없음 경고가 뜨고 □로 그려진다.
                Builder.Append("  - ");
                Builder.Append(string.IsNullOrEmpty(fallen.characterName) ? "이름 없음" : fallen.characterName);
                Builder.Append("  ").Append(fallen.starCount).Append('성');
                Builder.Append("  Lv.").Append(fallen.level).Append('\n');
            }
        }

        bodyText.SetText(Builder);
    }
}
