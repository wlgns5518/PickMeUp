using UnityEngine;

// 장비제작소의 확률표.
//
// 무엇이 나오느냐는 넣은 재료의 등급이 정한다. 재료가 E면 결과도 E 언저리, S면 결과도 S 언저리다.
//
// 자동 제작은 퍼즐 없이 바로 나오는 대신 재료 등급 그대로 나온다 — 운이 끼어들 자리가 없다.
// 수동 제작은 재료 등급을 밑변으로 두고, 퍼즐 난이도가 그 위로 몇 단계나 오를지를 굴린다.
// 어려운 난이도일수록 위로 오를 확률과 오르는 폭이 커진다. 재료 등급 + 오른 단계가 S를 넘으면
// S에서 그친다 — 표를 넘는 등급은 없다.
public static class EquipmentCraftTable
{
    private const int WeightTotal = 10000;
    private const int GradeCount = 6; // E, D, C, B, A, S

    // 재료 등급 위로 몇 단계 오르는지(0~3단계)의 가중치. 합은 항상 WeightTotal이어야 한다.
    private static readonly int[] EasyUpgrade   = { 8000, 1800,  200,    0 };
    private static readonly int[] NormalUpgrade = { 6000, 3000,  900,  100 };
    private static readonly int[] HardUpgrade   = { 4000, 3500, 2000,  500 };
    private static readonly int[] HellUpgrade   = { 2000, 3000, 3500, 1500 };

    private static int[] UpgradeWeightsFor(PuzzleDifficulty difficulty)
    {
        switch (difficulty)
        {
            case PuzzleDifficulty.Easy:   return EasyUpgrade;
            case PuzzleDifficulty.Normal: return NormalUpgrade;
            case PuzzleDifficulty.Hard:   return HardUpgrade;
            case PuzzleDifficulty.Hell:   return HellUpgrade;
            default:                      return NormalUpgrade;
        }
    }

    // 자동 제작: 재료 등급 그대로.
    public static EquipmentGrade RollAuto(EquipmentGrade material) => material;

    // 수동 제작 성공 시 굴리는 등급. 재료 등급에서 시작해 난이도표로 위로 몇 단계 오른다.
    public static EquipmentGrade RollManual(EquipmentGrade material, PuzzleDifficulty difficulty)
    {
        int[] weights = UpgradeWeightsFor(difficulty);
        int roll = Random.Range(0, WeightTotal);
        int acc = 0;
        int step = weights.Length - 1;
        for (int i = 0; i < weights.Length; i++)
        {
            acc += weights[i];
            if (roll < acc) { step = i; break; }
        }

        int index = Mathf.Min(GradeCount - 1, (int)material + step);
        return (EquipmentGrade)index;
    }

    // 화면에 보여줄 퍼센트 문자열. 재료+난이도가 정해지면 각 결과 등급이 나올 확률을 이렇게 구한다:
    // 오르는 단계별 가중치를 결과 등급 칸에 그대로 더한다(S를 넘는 단계는 전부 S로 합쳐진다).
    public static string PercentText(EquipmentGrade material, PuzzleDifficulty difficulty, EquipmentGrade outputGrade)
    {
        int[] weights = UpgradeWeightsFor(difficulty);
        var resultWeights = new int[GradeCount];
        for (int i = 0; i < weights.Length; i++)
        {
            int index = Mathf.Min(GradeCount - 1, (int)material + i);
            resultWeights[index] += weights[i];
        }

        float percent = resultWeights[(int)outputGrade] * 100f / WeightTotal;
        return (percent % 1f == 0f ? percent.ToString("0") : percent.ToString("0.0")) + "%";
    }
}
