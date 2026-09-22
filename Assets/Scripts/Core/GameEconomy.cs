using UnityEngine;

// 재화가 드나드는 값 전부 — 무엇에 얼마가 들고, 어디서 얼마가 들어오는지.
//
// 비용을 각 기능(소환소·제작소·합성소·강화)에 흩어 두면 한쪽만 올려 경제가 한쪽으로 쏠려도 알아채기 어렵다.
// 들어오는 곳(층 클리어)과 나가는 곳을 한 파일에 나란히 적어 두고, 조정은 여기서만 한다.
//
// 2026-09-22 사용자 결정: 비용은 실제로 차감하고, 층 클리어에 골드를 준다. 젬은 처음에 한 번 준다.
// 사용자가 정한 값은 층 클리어 골드와 소환 비용이고, 제작·합성·강화 비용은 제안값이다.
public static class GameEconomy
{
    // ---- 들어오는 곳 --------------------------------------------------------

    // 처음 시작할 때(이 값이 생기기 전의 세이브도 한 번) 주는 젬. 고급 소환 10회 한 번 몫이다.
    public const long StarterGems = 3000;

    // 층을 이겼을 때 주는 골드(사용자 결정). 1층이 1,000이고 다섯 층 구간이 오를 때마다 500씩 는다 —
    // 1~5층 1,000, 6~10층 1,500 … 96~100층 10,500. 구간은 전장 맵과 같은 다섯 층 묶음이다(FloorProgress.FloorsPerStage).
    public const long FloorClearGoldBase = 1000;
    public const long FloorClearGoldPerStage = 500;

    public static long FloorClearGold(int floor)
    {
        int stage = (Mathf.Clamp(floor, FloorProgress.FirstFloor, FloorProgress.LastFloor) - FloorProgress.FirstFloor)
                    / FloorProgress.FloorsPerStage;
        return FloorClearGoldBase + FloorClearGoldPerStage * stage;
    }

    // ---- 나가는 곳 ----------------------------------------------------------

    // 일반 소환은 골드, 고급 소환은 젬(2026-09-22 사용자 결정). 10회는 1회의 딱 열 배 — 깎아 주지 않는다.
    public const long NormalSummonGold = 50000;
    public const long NormalSummonTenGold = 500000;
    public const long PaidSummonGems = 300;
    public const long PaidSummonTenGems = 3000;

    public static Currency SummonCurrency(SummonKind kind) => kind == SummonKind.Paid ? Currency.Gem : Currency.Gold;

    public static long SummonCost(SummonKind kind, int count)
    {
        if (count <= 0) return 0;

        bool paid = kind == SummonKind.Paid;
        if (count == 10) return paid ? PaidSummonTenGems : NormalSummonTenGold;
        return (paid ? PaidSummonGems : NormalSummonGold) * count;
    }

    // 장비 제작 한 번. 좋은 재료일수록 비싸다(재료 평균 등급이 기준).
    public static long CraftGold(EquipmentGrade baseGrade) => 200L + 100L * (int)baseGrade;

    // 장비 합성 한 번. 결과 등급이 기준이다.
    public static long EquipmentSynthesisGold(EquipmentGrade resultGrade) => 200L * ((int)resultGrade + 1);

    // 강화 한 번(level → level+1). 등급이 높을수록, 단계가 높을수록 비싸다.
    public static long EnhanceGold(EquipmentGrade grade, int level) =>
        (100L + 50L * (int)grade) * (Mathf.Clamp(level, 0, EquipmentEnhancement.MaxLevel) + 1);

    // 캐릭터 합성 한 번. 재료 영웅의 등급이 기준이다 — 좋은 재료일수록 좋은 스킬이 나온다.
    public static long CharacterSynthesisGold(int materialStars) => 300L * Mathf.Max(1, materialStars);
}
