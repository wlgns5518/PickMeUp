using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

// 전투가 끝났을 때 뜨는 결과창.
//
// 이겼을 때는 레벨업/보상/MVP를, 졌을 때는 전멸을 알린다.
// 영구 사망자는 이겼든 졌든 항상 따로 적는다 — 이 게임에서 이긴 판과 잃지 않은 판은 다르다.
//
// 판은 킷의 장식 메시지 박스(msgbox_*)다. 부고 배너와 같은 테두리를 써서 "전투가 남긴 말"이 한 모양으로 읽힌다.
// 한 장짜리 테두리를 9슬라이스로 늘리므로 모서리 장식은 그대로 두고 가운데만 늘어난다.
public class BattleResultPanel
{
    private static readonly Vector2 VictorySize = new Vector2(820f, 680f);
    private static readonly Vector2 DefeatSize = new Vector2(880f, 440f);

    // 장식 테두리 안쪽에만 글자가 들어가도록 비워두는 여백(README: 좌우 95, 상하 80).
    private const float PaddingX = 95f;
    private const float PaddingY = 80f;

    private static readonly StringBuilder Builder = new StringBuilder(256);

    private static readonly string TitleColor = ColorUtility.ToHtmlStringRGB(BattleHudPalette.AccentLight);
    private static readonly string AccentColor = ColorUtility.ToHtmlStringRGB(BattleHudPalette.Accent);
    private static readonly string MvpColor = ColorUtility.ToHtmlStringRGB(BattleHudPalette.Mvp);
    private static readonly string DangerColor = ColorUtility.ToHtmlStringRGB(BattleHudPalette.Danger);

    private readonly RectTransform root;
    private readonly RectTransform frameRect;
    private readonly TMP_Text bodyText;
    private readonly bool hasStarSprites;

    private BattleResultPanel(RectTransform parent, TMP_FontAsset font, TMP_SpriteAsset starSprites)
    {
        root = HudFactory.CreateGroup(parent, "BattleResultPanel");
        HudFactory.Stretch(root);

        var backdrop = HudFactory.CreateImage(root, "Backdrop", BattleHudPalette.PanelBackdrop);
        HudFactory.Stretch(backdrop.rectTransform);

        frameRect = HudFactory.CreateOrnateBox(root, "Frame", out _);
        frameRect.anchorMin = new Vector2(0.5f, 0.5f);
        frameRect.anchorMax = new Vector2(0.5f, 0.5f);
        frameRect.pivot = new Vector2(0.5f, 0.5f);
        frameRect.anchoredPosition = Vector2.zero;
        frameRect.sizeDelta = VictorySize;

        bodyText = HudFactory.CreateText(frameRect, "Body", font, 30f, BattleHudPalette.TextPrimary);
        HudFactory.Stretch(bodyText.rectTransform);
        // 여백이 비율이 아니라 픽셀인 이유: 테두리 장식은 판이 커져도 같은 크기로 그려진다.
        bodyText.rectTransform.offsetMin = new Vector2(PaddingX, PaddingY);
        bodyText.rectTransform.offsetMax = new Vector2(-PaddingX, -PaddingY);
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

    public static BattleResultPanel Create(RectTransform parent, TMP_FontAsset font, TMP_SpriteAsset starSprites = null)
    {
        return new BattleResultPanel(parent, font, starSprites);
    }

    public void Show(BattleResult result)
    {
        if (result == null) return;

        bool victory = result.Outcome == BattleOutcome.Victory;
        frameRect.sizeDelta = victory ? VictorySize : DefeatSize;

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
        Builder.Append("<size=46><color=#").Append(TitleColor).Append(">스테이지 클리어!</color></size>\n\n");

        AppendLevelUps(result);
        AppendSkillUnlocks(result);
        AppendGold(result);
        AppendMaterials(result);

        if (result.Mvp != null && result.Mvp.Character != null)
        {
            Builder.Append("\n<size=38><color=#").Append(MvpColor).Append(">MVP</color> - ");
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
            Builder.Append("<size=52><color=#").Append(TitleColor).Append(">Draw</color></size>");
        }
        else
        {
            Builder.Append("<size=38>파티가 전멸했습니다.</size>\n");
            Builder.Append("<size=52><color=#").Append(DangerColor).Append(">You Lose!</color></size>");
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

    // 조건을 채워 열린 스킬. 합성과 달리 플레이어가 누른 적 없이 붙는 것이라,
    // 여기서 알리지 않으면 카드 상세를 열어보기 전까지 생긴 줄도 모른다.
    private void AppendSkillUnlocks(BattleResult result)
    {
        int shown = 0;

        for (int i = 0; i < result.Rewards.Count; i++)
        {
            BattleReward reward = result.Rewards[i];
            if (reward == null || reward.Character == null || reward.UnlockedSkills.Count == 0) continue;

            for (int s = 0; s < reward.UnlockedSkills.Count; s++)
            {
                Builder.Append(HeroLabel.NameWithStars(reward.Character, hasStarSprites));
                Builder.Append(" — ");
                Builder.Append(SkillCatalog.NameOf(reward.UnlockedSkills[s]));
                Builder.Append(" 각성!\n");
                shown++;
            }
        }

        if (shown > 0) Builder.Append('\n');
    }

    // 받은 제작 재료. 같은 재료는 묶어서 "B급 강철 x2"로 적는다. 등급 글자색은 제작소·무기창고와 같다.
    private void AppendMaterials(BattleResult result)
    {
        if (result.Materials.Count == 0) return;

        var stacks = new List<CraftMaterial>();
        var counts = new List<int>();
        for (int i = 0; i < result.Materials.Count; i++)
        {
            int index = stacks.IndexOf(result.Materials[i]);
            if (index < 0)
            {
                stacks.Add(result.Materials[i]);
                counts.Add(1);
            }
            else
            {
                counts[index]++;
            }
        }

        Builder.Append("<color=#").Append(AccentColor).Append(">재료 획득</color>\n");
        for (int i = 0; i < stacks.Count; i++)
        {
            if (i > 0) Builder.Append(",  ");
            string color = ColorUtility.ToHtmlStringRGB(EquipmentGradeNames.ColorOf(stacks[i].Grade));
            Builder.Append("<color=#").Append(color).Append('>').Append(stacks[i].DisplayName).Append("</color>");
            if (counts[i] > 1) Builder.Append(" x").Append(counts[i]);
        }
        Builder.Append('\n');
    }

    private void AppendGold(BattleResult result)
    {
        if (result.Gold <= 0) return;

        Builder.Append("<color=#").Append(AccentColor).Append(">골드 획득</color>  ")
            .Append(result.Gold.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
    }

    // 영구 사망은 승패와 무관하게 항상 알린다. 이 게임에서 되돌릴 수 없는 유일한 손실이다.
    private void AppendFallen(BattleResult result)
    {
        if (result.FallenCharacters.Count == 0) return;

        Builder.Append("\n<size=26><color=#").Append(DangerColor).Append('>');
        for (int i = 0; i < result.FallenCharacters.Count; i++)
        {
            CharacterSO fallen = result.FallenCharacters[i];
            if (fallen == null) continue;

            Builder.Append(HeroLabel.NameWithStars(fallen, hasStarSprites));
            Builder.Append(" 전사\n");
        }
        Builder.Append("</color></size>");
    }
}
