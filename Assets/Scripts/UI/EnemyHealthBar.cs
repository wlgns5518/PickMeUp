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
    private const float Height = 30f;
    private const float DiamondSize = 12f;
    private const float DiamondGap = 14f;

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

        // 킷 게이지(트랙 + 붉은 채움). 트랙이 바 전체를 차지한다.
        Image track = HudFactory.CreateGauge(root, "Gauge", HudFactory.GaugeFill.Hp, out fill);
        HudFactory.Stretch(track.rectTransform);

        // 양 끝 장식(45도 돌린 작은 사각형). 원작 화면의 마름모를 대신한다. 게이지에 붙이면 글로우와 겹쳐 바깥에 띄운다.
        CreateDiamond(-(Width * 0.5f + DiamondGap));
        CreateDiamond(Width * 0.5f + DiamondGap);

        countLabel = HudFactory.CreateText(root, "Count", font, 18f, BattleHudPalette.TextPrimary);
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

        // 엔티티가 된 적도 같은 바에 더한다. 이 바는 개체마다 하나가 아니라 팀 전체를
        // 하나로 보여 주는 것이라, 두 세계의 합을 그대로 더하면 된다.
        //
        // 이게 없으면 적이 엔티티로 바뀌는 순간 바가 통째로 사라진다 — 최대 체력이 0이라
        // 아예 켜지지도 않는다.
        EnemyWorldBridge.SumEnemyHealth(out _, out float entityMax, out _);
        totalMaxHp += entityMax;

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

        EnemyWorldBridge.SumEnemyHealth(out float entityCurrent, out _, out int entityAlive);
        current += entityCurrent;
        alive += entityAlive;

        float ratio = Mathf.Clamp01(current / totalMaxHp);
        if (!Mathf.Approximately(ratio, appliedRatio))
        {
            appliedRatio = ratio;
            HudFactory.SetGauge(fill, ratio);
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
        Image diamond = HudFactory.CreateImage(root, "Diamond", BattleHudPalette.Accent);
        RectTransform rect = diamond.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(DiamondSize, DiamondSize);
        rect.anchoredPosition = new Vector2(x, 0f);
        rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
    }

}
