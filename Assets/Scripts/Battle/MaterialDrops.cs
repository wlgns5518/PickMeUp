using System.Collections.Generic;
using UnityEngine;

// 층을 클리어했을 때 떨어지는 제작 재료.
//
// 이긴 판에만 준다. 재료가 장비의 유일한 원천이라, 진 판에도 주면 전멸을 반복하는 것만으로 장비가 쌓인다.
//
// 등급은 층 구간으로 정해진다(GradeBands). 한 개마다 UpgradeChance 확률로 한 단계 좋은 재료가 나오고,
// 그 밖에는 구간 등급 그대로다 — 낮게 나오는 일은 없다. 좋은 재료를 노리려면 윗층으로 올라가야 한다.
// 종류(강철·참나무·가죽)는 같은 확률로 굴린다 — 어느 계열 무기를 노릴지는 재료를 모아서 정한다.
//
// 구간표와 확률을 BattleRewardSettings(씬 인스펙터)에 두지 않는 이유: 전투 씬이 구간마다 스무 개라
// 씬마다 따로 저장된 값이 서로 어긋난다.
public static class MaterialDrops
{
    // 구간의 마지막 층과 그 구간의 재료 등급(2026-09 사용자 결정).
    // 1~10층 E, 11~30층 D, 31~50층 C, 51~70층 B, 71~90층 A, 91층부터 S.
    private static readonly (int LastFloor, EquipmentGrade Grade)[] GradeBands =
    {
        (10, EquipmentGrade.E),
        (30, EquipmentGrade.D),
        (50, EquipmentGrade.C),
        (70, EquipmentGrade.B),
        (90, EquipmentGrade.A),
        (FloorProgress.LastFloor, EquipmentGrade.S),
    };

    // 재료 한 개가 구간 등급보다 한 단계 높게 나올 확률. S 구간에서는 더 오를 곳이 없다.
    public const float UpgradeChance = 0.01f;

    public static EquipmentGrade BaseGrade(int floor)
    {
        foreach ((int lastFloor, EquipmentGrade grade) in GradeBands)
            if (floor <= lastFloor) return grade;
        return EquipmentGrade.S;
    }

    public static void Roll(int floor, BattleRewardSettings settings, List<CraftMaterial> results)
    {
        results.Clear();
        if (settings == null) return;

        int min = Mathf.Max(0, settings.materialsMin);
        int max = Mathf.Max(min, settings.materialsMax);
        int count = Random.Range(min, max + 1);
        EquipmentGrade grade = BaseGrade(floor);

        for (int i = 0; i < count; i++)
        {
            MaterialKind kind = MaterialNames.AllKinds[Random.Range(0, MaterialNames.AllKinds.Length)];
            results.Add(new CraftMaterial(kind, RollGrade(grade, Random.value)));
        }
    }

    // roll은 0~1의 주사위. 테스트가 확률 경계를 직접 찌를 수 있게 밖에서 받는다.
    public static EquipmentGrade RollGrade(EquipmentGrade baseGrade, float roll)
    {
        if (roll < UpgradeChance && baseGrade < EquipmentGrade.S) return baseGrade + 1;
        return baseGrade;
    }
}
