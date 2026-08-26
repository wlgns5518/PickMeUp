using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 전투가 끝났을 때 뜨는 결과창.
//
// 이겼을 때는 레벨업/보상/MVP를, 졌을 때는 전멸을 알린다.
// 영구 사망자는 이겼든 졌든 항상 따로 적는다 — 이 게임에서 이긴 판과 잃지 않은 판은 다르다.
//
// 배너 아트는 가로로 긴 그림이라 승리 화면처럼 세로가 긴 판에는 그대로 쓸 수 없다.
// 임포터에서 9슬라이스 테두리를 잡아 두었으므로 모서리 장식은 그대로 두고 가운데만 늘린다.
public class BattleResultPanel
{
    private static readonly Vector2 VictorySize = new Vector2(780f, 660f);
    private static readonly Vector2 DefeatSize = new Vector2(880f, 430f);

    // 장식 테두리 안쪽에만 글자가 들어가도록 비워두는 비율.
    private const float PaddingRatioX = 0.12f;
    private const float PaddingRatioY = 0.16f;

    private static readonly StringBuilder Builder = new StringBuilder(256);

    private readonly RectTransform root;
    private readonly RectTransform frameRect;
    private readonly TMP_Text bodyText;
    private readonly bool hasStarSprites;

    private BattleResultPanel(RectTransform parent, TMP_FontAsset font, Sprite frameSprite, TMP_SpriteAsset starSprites)
    {
        root = HudFactory.CreateGroup(parent, "BattleResultPanel");
        HudFactory.Stretch(root);

        var backdrop = HudFactory.CreateImage(root, "Backdrop", BattleHudPalette.PanelBackdrop);
        HudFactory.Stretch(backdrop.rectTransform);

        Image frame;
        if (frameSprite != null)
        {
            frame = HudFactory.CreateImage(root, "Frame", Color.white);
            frame.sprite = frameSprite;
            // 테두리가 잡혀 있으면 9슬라이스로, 아니면 통짜로 늘린다.
            frame.type = frameSprite.border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced;
            // 9슬라이스는 모서리를 원본 픽셀 크기로 그린다. 그대로 두면 장식이 판을 잡아먹으므로
            // 이 배율만큼 줄여서 그린다(1.35면 240px 모서리가 약 178px로).
            frame.pixelsPerUnitMultiplier = 1.35f;
        }
        else
        {
            // 배너 아트를 아직 넣지 않았을 때의 대비책 — 금색 테두리에 검은 판.
            frame = HudFactory.CreateImage(root, "Frame", BattleHudPalette.Mvp);
            Image body = HudFactory.CreateImage(frame.rectTransform, "Body", new Color(0.03f, 0.03f, 0.04f, 0.97f));
            HudFactory.Stretch(body.rectTransform);
            body.rectTransform.offsetMin = new Vector2(3f, 3f);
            body.rectTransform.offsetMax = new Vector2(-3f, -3f);
        }

        frameRect = frame.rectTransform;
        frameRect.anchorMin = new Vector2(0.5f, 0.5f);
        frameRect.anchorMax = new Vector2(0.5f, 0.5f);
        frameRect.pivot = new Vector2(0.5f, 0.5f);
        frameRect.anchoredPosition = Vector2.zero;
        frameRect.sizeDelta = VictorySize;

        bodyText = HudFactory.CreateText(frameRect, "Body", font, 30f, BattleHudPalette.PanelText);
        HudFactory.Stretch(bodyText.rectTransform);
        bodyText.alignment = TextAlignmentOptions.Center;
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        // 파티가 커지면 레벨업 줄이 늘어 판을 넘친다. 넘치는 대신 줄어들게 한다.
        bodyText.enableAutoSizing = true;
        bodyText.fontSizeMin = 16f;
        bodyText.fontSizeMax = 30f;
        bodyText.spriteAsset = starSprites != null ? starSprites : HeroLabel.LoadStarSprites();
        hasStarSprites = bodyText.spriteAsset != null;

        root.gameObject.SetActive(false);
    }

    public static BattleResultPanel Create(RectTransform parent, TMP_FontAsset font,
        Sprite frameSprite = null, TMP_SpriteAsset starSprites = null)
    {
        return new BattleResultPanel(parent, font, frameSprite, starSprites);
    }

    public void Show(BattleResult result)
    {
        if (result == null) return;

        bool victory = result.Outcome == BattleOutcome.Victory;
        frameRect.sizeDelta = victory ? VictorySize : DefeatSize;
        ApplyTextPadding(frameRect.sizeDelta);

        bodyText.text = victory ? BuildVictory(result) : BuildDefeat(result);

        root.gameObject.SetActive(true);
        // 늦게 만들어진 위젯이 결과창을 덮지 않도록 항상 맨 앞으로 올린다.
        root.SetAsLastSibling();
    }

    public void Hide()
    {
        root.gameObject.SetActive(false);
    }

    // 스테이지 클리어! / 레벨업 / 보상 안내 / MVP
    private string BuildVictory(BattleResult result)
    {
        Builder.Clear();
        Builder.Append("<size=46>스테이지 클리어!</size>\n\n");

        AppendLevelUps(result);

        Builder.Append("보상이 지급됩니다.\n");
        Builder.Append("우편함을 확인해주세요.\n");

        if (result.Mvp != null && result.Mvp.Character != null)
        {
            Builder.Append("\n<size=38>MVP - ");
            Builder.Append(HeroLabel.NameWithStars(result.Mvp.Character, hasStarSprites));
            Builder.Append("</size>");
        }

        AppendFallen(result);
        return Builder.ToString();
    }

    // 파티가 전멸했습니다. / You Lose!
    private string BuildDefeat(BattleResult result)
    {
        Builder.Clear();

        if (result.Outcome == BattleOutcome.Draw)
        {
            Builder.Append("<size=38>양쪽 모두 쓰러졌습니다.</size>\n");
            Builder.Append("<size=52>Draw</size>");
        }
        else
        {
            Builder.Append("<size=38>파티가 전멸했습니다.</size>\n");
            Builder.Append("<size=52>You Lose!</size>");
        }

        AppendFallen(result);
        return Builder.ToString();
    }

    private void AppendLevelUps(BattleResult result)
    {
        int shown = 0;
        int extra = 0;

        for (int i = 0; i < result.Rewards.Count; i++)
        {
            BattleReward reward = result.Rewards[i];
            if (reward == null || reward.Character == null || !reward.LeveledUp) continue;

            // 파티가 커지면 레벨업만으로 판이 가득 찬다. 앞의 몇 명만 적고 나머지는 숫자로 줄인다.
            if (shown >= 3) { extra++; continue; }

            Builder.Append(HeroLabel.NameWithStars(reward.Character, hasStarSprites));
            Builder.Append(" 레벨업!\n");
            shown++;
        }

        if (extra > 0) Builder.Append("외 ").Append(extra).Append("명 레벨업!\n");
        if (shown > 0 || extra > 0) Builder.Append('\n');
    }

    // 영구 사망은 승패와 무관하게 항상 알린다. 이 게임에서 되돌릴 수 없는 유일한 손실이다.
    private void AppendFallen(BattleResult result)
    {
        if (result.FallenCharacters.Count == 0) return;

        string color = ColorUtility.ToHtmlStringRGB(BattleHudPalette.Defeat);
        Builder.Append("\n<size=26><color=#").Append(color).Append('>');
        for (int i = 0; i < result.FallenCharacters.Count; i++)
        {
            CharacterSO fallen = result.FallenCharacters[i];
            if (fallen == null) continue;

            Builder.Append(HeroLabel.NameWithStars(fallen, hasStarSprites));
            Builder.Append(" 전사\n");
        }
        Builder.Append("</color></size>");
    }

    private void ApplyTextPadding(Vector2 size)
    {
        bodyText.rectTransform.offsetMin = new Vector2(size.x * PaddingRatioX, size.y * PaddingRatioY);
        bodyText.rectTransform.offsetMax = new Vector2(-size.x * PaddingRatioX, -size.y * PaddingRatioY);
    }

}
