using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 화면 위쪽 가운데에 걸리는 적 전체 체력바 — 원작 게임 화면의 그 긴 바.
//
// 층마다 적이 3마리에서 20마리까지 늘어나므로 한 마리만 잡아 표시하면 층의 남은 분량을 알 수 없다.
// 그래서 전투 시작 시점의 적 전체 최대 체력을 붙잡아 두고, 살아있는 적의 현재 체력 합을 그 값으로 나눈다.
// 적이 죽으면 분자만 줄어들어 바가 실제로 비어 간다(살아있는 적만으로 비율을 내면 영영 가득 찬 채로 남는다).
public class EnemyHealthBar
{
    private const float Width = 880f;
    private const float Height = 24f;
    private const float BorderThickness = 2f;
    private const float DiamondSize = 14f;

    private readonly RectTransform root;
    private readonly Image fill;
    private readonly TMP_Text countLabel;

    private float totalMaxHp;
    private float appliedRatio = -1f;
    private int appliedAlive = -1;

    private EnemyHealthBar(RectTransform parent, TMP_FontAsset font, float topOffset)
    {
        root = HudFactory.CreateGroup(parent, "EnemyHealthBar");
        root.anchorMin = new Vector2(0.5f, 1f);
        root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.sizeDelta = new Vector2(Width, Height);
        root.anchoredPosition = new Vector2(0f, -topOffset);

        // 테두리 → 안쪽 어두운 판 → 붉은 채움 순으로 겹친다.
        Image border = HudFactory.CreateImage(root, "Border", BattleHudPalette.Mvp);
        HudFactory.Stretch(border.rectTransform);

        // 팔레트의 게이지 배경은 알파 0.85라 금색 테두리 위에 얹으면 금색이 배어 올라와 빈 구간이 누렇게 보인다.
        // 이 바는 테두리를 자기 배경으로 깔고 있으므로 불투명한 판을 쓴다.
        Image background = HudFactory.CreateImage(border.rectTransform, "Background", new Color(0.05f, 0.05f, 0.08f, 1f));
        HudFactory.Stretch(background.rectTransform);
        background.rectTransform.offsetMin = new Vector2(BorderThickness, BorderThickness);
        background.rectTransform.offsetMax = new Vector2(-BorderThickness, -BorderThickness);

        fill = HudFactory.CreateImage(background.rectTransform, "Fill", BattleHudPalette.PartyHp);
        RectTransform fillRect = fill.rectTransform;
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fillRect.sizeDelta = new Vector2(Width - BorderThickness * 2f, 0f);

        // 양 끝 장식(45도 돌린 작은 사각형). 원작 화면의 마름모를 대신한다.
        CreateDiamond(-Width * 0.5f);
        CreateDiamond(Width * 0.5f);

        countLabel = HudFactory.CreateText(root, "Count", font, 18f, BattleHudPalette.PanelText);
        countLabel.alignment = TextAlignmentOptions.Right;
        RectTransform labelRect = countLabel.rectTransform;
        labelRect.anchorMin = new Vector2(1f, 1f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(1f, 0f);
        labelRect.sizeDelta = new Vector2(200f, 22f);
        labelRect.anchoredPosition = new Vector2(0f, 4f);

        root.gameObject.SetActive(false);
    }

    public static EnemyHealthBar Create(RectTransform parent, TMP_FontAsset font, float topOffset)
    {
        return new EnemyHealthBar(parent, font, topOffset);
    }

    // 전투가 시작될 때 한 번. 이 시점의 적 전체 최대 체력이 바의 기준이 된다.
    public void Bind(IReadOnlyList<UnitController> enemies)
    {
        totalMaxHp = 0f;
        if (enemies != null)
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i] == null) continue;
                totalMaxHp += Mathf.Max(0f, enemies[i].Stats.maxHp);
            }
        }

        appliedRatio = -1f;
        appliedAlive = -1;
        root.gameObject.SetActive(totalMaxHp > 0f);
        Refresh();
    }

    public void Refresh()
    {
        if (totalMaxHp <= 0f || !root.gameObject.activeSelf) return;

        float current = 0f;
        int alive = 0;
        IReadOnlyList<UnitController> enemies = UnitRegistry.Enemies;
        for (int i = 0; i < enemies.Count; i++)
        {
            UnitController enemy = enemies[i];
            if (enemy == null || enemy.IsDead) continue;

            current += Mathf.Max(0f, enemy.Stats.currentHp);
            alive++;
        }

        float ratio = Mathf.Clamp01(current / totalMaxHp);
        if (!Mathf.Approximately(ratio, appliedRatio))
        {
            appliedRatio = ratio;
            fill.rectTransform.sizeDelta = new Vector2((Width - BorderThickness * 2f) * ratio, 0f);
        }

        if (alive != appliedAlive)
        {
            appliedAlive = alive;
            countLabel.text = "적 " + alive;
        }
    }

    public void Hide()
    {
        root.gameObject.SetActive(false);
    }

    private void CreateDiamond(float x)
    {
        Image diamond = HudFactory.CreateImage(root, "Diamond", BattleHudPalette.Mvp);
        RectTransform rect = diamond.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(DiamondSize, DiamondSize);
        rect.anchoredPosition = new Vector2(x, 0f);
        rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
    }

}
