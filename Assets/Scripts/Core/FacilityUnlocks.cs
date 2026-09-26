using Kind = VillageBlockout.Kind;

// 시설 레벨이 여는 기능 — 원작의 "시설 업그레이드"(2026-09-26 사용자 결정).
//
//   Lv.1 기본 시설   — 필요한 설비만 갖춘다.
//   Lv.2 부가 시설   — 곁채가 붙으며 기능 하나가 더 열린다.
//   Lv.3 2층 개방    — 시설이 확장된다.
//
//   | 시설       | Lv.1            | Lv.2              | Lv.3                          |
//   |------------|-----------------|-------------------|-------------------------------|
//   | 소환소     | 1회 소환        | 10회 소환         | 고급 10회 소환에 4성 이상 확정 |
//   | 합성소     | 스킬 2개까지    | 스킬 3개까지      | 스킬 4개까지                  |
//   | 장비제작소 | 자동 제작       | 수동 제작(퍼즐)   | 장비 합성                     |
//   | 무기창고   | 장착            | 강화 +5까지       | 강화 +10까지                  |
//   | 훈련소     | 파티 1개        | 파티 2개          | 파티 3개                      |
//   | 시공의 틈  | 메인 던전       | 요일 던전         | 탐험 던전                     |
//
// 각 기능은 여기에 "지금 되는가 / 몇 레벨이 필요한가"만 묻는다. 표를 고칠 때는 이 파일만 고친다.
// 일반 소환은 2성까지만 나와서 4성 확정은 고급 소환에만 건다(SummonTable).
// 비행선착장은 하는 일이 없어 업그레이드 목록에서 뺐다 — 기능이 생기면 Upgradeable에 넣는다.
public static class FacilityUnlocks
{
    public static readonly Kind[] Upgradeable =
    {
        Kind.Summoning, Kind.Synthesis, Kind.EquipmentWorkshop, Kind.Armory, Kind.Training, Kind.Rift,
    };

    public static bool IsUpgradeable(Kind kind) => System.Array.IndexOf(Upgradeable, kind) >= 0;

    public static int Level(Kind kind) => FacilityLevels.Get(kind);

    // ---- 이름과 문구 ----------------------------------------------------------

    // 마을에 선 건물 이름. 시설 화면 제목(캐릭터 소환소·장비창 …)이 아니라 영지 관리 목록에 적는 이름이다.
    public static string Name(Kind kind)
    {
        switch (kind)
        {
            case Kind.Summoning:         return "소환소";
            case Kind.Synthesis:         return "합성소";
            case Kind.EquipmentWorkshop: return "장비제작소";
            case Kind.Armory:            return "무기창고";
            case Kind.Training:          return "훈련소";
            case Kind.Rift:              return "시공의 틈";
            default:                     return kind.ToString();
        }
    }

    // 그 레벨에서 무엇이 되는지 한 줄로.
    public static string Feature(Kind kind, int level)
    {
        switch (kind)
        {
            case Kind.Summoning:
                return level >= 3 ? $"고급 10회 소환 {GuaranteedStars}성 이상 확정" : level == 2 ? "10회 소환" : "1회 소환";
            case Kind.Synthesis:
                return $"스킬 {SkillCapAt(level)}개까지";
            case Kind.EquipmentWorkshop:
                return level >= 3 ? "장비 합성" : level == 2 ? "수동 제작" : "자동 제작";
            case Kind.Armory:
                return level >= 2 ? $"강화 +{EnhanceCapAt(level)}까지" : "장착";
            case Kind.Training:
                return $"파티 {PartySlotsAt(level)}개";
            case Kind.Rift:
                return level >= 3 ? "탐험 던전" : level == 2 ? "요일 던전" : "메인 던전";
            default:
                return string.Empty;
        }
    }

    // 잠긴 기능을 눌렀을 때 띄우는 말.
    public static string Requirement(Kind kind, int level) => $"{Name(kind)} Lv.{level} 필요";

    // ---- 소환소 --------------------------------------------------------------

    public const int TenSummonLevel = 2;
    public const int GuaranteeLevel = 3;
    public const int GuaranteedStars = 4;

    public static bool CanSummonTen => Level(Kind.Summoning) >= TenSummonLevel;

    /// 이번 10회 소환에 확정으로 끼워 줄 최소 별. 확정이 없으면 0.
    public static int GuaranteeFor(SummonKind kind, int count)
    {
        if (count < 10 || kind != SummonKind.Paid || Level(Kind.Summoning) < GuaranteeLevel) return 0;
        return GuaranteedStars;
    }

    // ---- 합성소 --------------------------------------------------------------

    public static int SkillCapAt(int level) =>
        System.Math.Min(SkillCatalog.MaxSkillsPerCharacter, 1 + UnityEngine.Mathf.Clamp(level, 1, FacilityLevels.MaxLevel));

    /// 합성으로 배울 수 있는 스킬 수. 스킬 상한(4)보다 크지 않다.
    public static int SynthesisSkillCap => SkillCapAt(Level(Kind.Synthesis));

    // ---- 장비제작소 ----------------------------------------------------------

    public const int ManualCraftLevel = 2;
    public const int EquipmentSynthesisLevel = 3;

    public static bool CanCraftManually => Level(Kind.EquipmentWorkshop) >= ManualCraftLevel;
    public static bool CanSynthesizeEquipment => Level(Kind.EquipmentWorkshop) >= EquipmentSynthesisLevel;

    // ---- 무기창고 ------------------------------------------------------------

    public static int EnhanceCapAt(int level) =>
        level >= 3 ? EquipmentEnhancement.MaxLevel : level == 2 ? EquipmentEnhancement.MaxLevel / 2 : 0;

    /// 지금 강화로 올릴 수 있는 가장 높은 단계. 0이면 강화 자체가 잠겨 있다.
    public static int EnhanceCap => EnhanceCapAt(Level(Kind.Armory));

    /// level 단계 장비를 한 번 더 올리려면 필요한 무기창고 레벨.
    public static int EnhanceLevelFor(int itemLevel) => itemLevel < EnhanceCapAt(2) ? 2 : 3;

    // ---- 훈련소 --------------------------------------------------------------

    public static int PartySlotsAt(int level) =>
        UnityEngine.Mathf.Clamp(level, 1, PartyDeck.PartyCount);

    /// 쓸 수 있는 파티 수(1파티부터). 잠긴 파티의 편성은 지우지 않고 그대로 둔다.
    public static int PartySlots => PartySlotsAt(Level(Kind.Training));

    public static bool IsPartyUsable(int index) => index >= 0 && index < PartySlots;

    // ---- 시공의 틈 -----------------------------------------------------------

    public static int RiftLevelFor(DungeonKind dungeon)
    {
        switch (dungeon)
        {
            case DungeonKind.Daily:      return 2;
            case DungeonKind.Expedition: return 3;
            default:                     return 1;
        }
    }

    public static bool IsDungeonOpen(DungeonKind dungeon) => Level(Kind.Rift) >= RiftLevelFor(dungeon);
}
