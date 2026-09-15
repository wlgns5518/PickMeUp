using System.Collections.Generic;
using UnityEngine;

// 층을 클리어했을 때 떨어지는 제작 재료.
//
// 이긴 판에만 준다. 재료가 장비의 유일한 원천이라, 진 판에도 주면 전멸을 반복하는 것만으로 장비가 쌓인다.
//
// 높은 층일수록 좋은 재료가 나온다. 층마다 "중심 등급"이 있고(floorsPerGrade층마다 한 단계),
// 한 개마다 중심에서 한 단계 내려가거나 올라갈 확률을 굴린다. 그래서 같은 층을 돌아도
// 가끔 한 단계 좋은 재료가 섞이고, 윗층으로 올라갈 이유가 등급으로 보인다.
// 종류(강철·참나무·가죽)는 같은 확률로 굴린다 — 어느 계열 무기를 노릴지는 재료를 모아서 정한다.
public static class MaterialDrops
{
    public static EquipmentGrade CenterGrade(int floor, int floorsPerGrade)
    {
        int step = Mathf.Max(0, floor - FloorProgress.FirstFloor) / Mathf.Max(1, floorsPerGrade);
        return (EquipmentGrade)Mathf.Min(step, (int)EquipmentGrade.S);
    }

    public static void Roll(int floor, BattleRewardSettings settings, List<CraftMaterial> results)
    {
        results.Clear();
        if (settings == null) return;

        int min = Mathf.Max(0, settings.materialsMin);
        int max = Mathf.Max(min, settings.materialsMax);
        int count = Random.Range(min, max + 1);
        EquipmentGrade center = CenterGrade(floor, settings.floorsPerGrade);

        for (int i = 0; i < count; i++)
        {
            MaterialKind kind = MaterialNames.AllKinds[Random.Range(0, MaterialNames.AllKinds.Length)];
            results.Add(new CraftMaterial(kind, RollGrade(center, settings)));
        }
    }

    private static EquipmentGrade RollGrade(EquipmentGrade center, BattleRewardSettings settings)
    {
        float roll = Random.value;
        int grade = (int)center;
        if (roll < settings.gradeDownChance) grade--;
        else if (roll < settings.gradeDownChance + settings.gradeUpChance) grade++;

        return (EquipmentGrade)Mathf.Clamp(grade, (int)EquipmentGrade.E, (int)EquipmentGrade.S);
    }
}
