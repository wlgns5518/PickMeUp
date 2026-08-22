using UnityEngine;

// 소환 종류. 소환소에서 무료와 유료 중 하나를 고른다.
public enum SummonKind
{
    Free, // 무료 소환
    Paid, // 유료 소환
}

// 소환으로 나오는 별 등급의 확률표.
//
// 예전에는 등급 확률이 MeshyCharacterGenerator 안에만 있었다. 소환소가 무료/유료 두 종류를
// 갖게 되면서 표가 둘로 늘었으므로 확률만 이 파일에 모은다. 생성기는 굴린 결과를 받기만 한다.
//
// UI에 적히는 퍼센트도 이 표에서 계산한다. 숫자를 손으로 두 군데 적어 두면 확률을 고쳤을 때
// 화면에 적힌 값과 실제 확률이 조용히 어긋난다.
public static class SummonTable
{
    // 가중치의 합. 두 표 모두 이 값에 맞춰 두면 소수점 셋째 자리(0.001%)까지 그대로 적을 수 있고,
    // 굴릴 때 매번 합을 다시 세지 않아도 된다. 표를 고칠 때는 합이 이 값이 되도록 맞춰야 한다.
    public const int WeightTotal = 100000;

    // 무료 소환 — 1성 90% / 2성 10%
    private static readonly int[] FreeWeights = { 90000, 10000 };

    // 유료 소환 — 1성 63.949% / 2성 30% / 3성 5% / 4성 1% / 5성 0.05% / 6성 0.001%
    // 7성은 소환으로 나오지 않는다(합성 등 다른 경로로만).
    private static readonly int[] PaidWeights = { 63949, 30000, 5000, 1000, 50, 1 };

    public static string Korean(SummonKind kind) => kind == SummonKind.Free ? "무료 소환" : "유료 소환";

    // 이 소환에서 나올 수 있는 가장 높은 별.
    public static int MaxStars(SummonKind kind) => Weights(kind).Length;

    // 별 등급(1성부터)이 나올 확률. 백분율이다.
    public static float Percent(SummonKind kind, int stars)
    {
        int[] weights = Weights(kind);
        int index = stars - 1;
        if (index < 0 || index >= weights.Length) return 0f;
        return weights[index] * 100f / WeightTotal;
    }

    // 화면에 적을 확률 문구. 0.001%까지 보여야 하므로 소수점 셋째 자리에서 끊고 뒤의 0은 지운다.
    public static string PercentText(SummonKind kind, int stars)
    {
        return Percent(kind, stars).ToString("0.###") + "%";
    }

    public static int RollStars(SummonKind kind)
    {
        int[] weights = Weights(kind);
        int roll = Random.Range(0, WeightTotal);
        int acc = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            acc += weights[i];
            if (roll < acc) return i + 1;
        }
        // 가중치 합이 WeightTotal에 못 미치면 여기로 떨어진다. 표가 어긋났다는 뜻이라 알려 둔다.
        Debug.LogWarning($"[SummonTable] {Korean(kind)} 확률표의 합이 {WeightTotal}이 아닙니다. 1성으로 처리합니다.");
        return 1;
    }

    private static int[] Weights(SummonKind kind) => kind == SummonKind.Free ? FreeWeights : PaidWeights;
}
