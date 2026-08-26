using System;
using System.Collections.Generic;
using UnityEngine;

public enum JobType
{
    // 전투 계열
    Melee,
    Mage,
    Archer,
    Assassin,
    Tank,
    Support,
    // 생산 계열
    Carpenter,
    Cook,
    Blacksmith,
    Tanner,
}

// 값은 CharacterSO 에셋에 정수로 직렬화된다. 순서를 바꾸면 기존 캐릭터가 들고 있던 무기가
// 통째로 바뀌므로 새 종류는 반드시 끝에만 덧붙인다.
public enum WeaponType
{
    None,
    SwordOneHand,
    SwordTwoHand,
    Bow,
    Spear,
    Dagger,
    Axe,      // 도끼 — 한손. 검보다 무겁고 느리다.
    Blunt,    // 둔기 — 망치/철퇴. 가장 아프고 가장 느리다.
    Polearm,  // 장병기 — 할버드/폴액스/전투낫. 두손이고 사거리가 길다.
    Shield,   // 보조 손 전용. 주무기 자리에 오면 맨손으로 취급된다.
    Magic,    // 맨손 시전 — 손에 드는 것이 없다. 무기 모델 없이 두 손으로 탄환을 던진다.
              // 새 종류는 반드시 끝에 붙인다 — 중간에 끼우면 이미 저장된 캐릭터의 무기가 통째로 밀린다.
}

public enum OffHandType { None, Shield }

// WeaponDefinition이 어느 손에 들어가는지. 방패만 보조 손을 쓴다.
public enum EquipSlot { MainHand, OffHand }

[Flags]
public enum EmotionState
{
    None     = 0,
    Fear     = 1 << 0, // 모든 능력치 30% 감소
    Panic    = 1 << 1, // 행동불가
    Bleeding = 1 << 2, // 일정시간마다 HP 감소
    Dying    = 1 << 3, // HP 3% 미만, 행동불가
    Broken   = 1 << 4, // 스트레스 한계 초과 — 정신이 무너져 전투를 지속할 수 없음

    // 스스로 행동을 결정할 수 없는 상태 묶음.
    ActionBlocking = Panic | Dying | Broken,
}

[Serializable]
public class VisibleStats
{
    public int strength;     // 힘
    public int intelligence; // 지능
    public int vitality;     // 체력
    public int agility;      // 민첩
}

[Serializable]
public class HiddenStats
{
    public int diligence; // 성실성 — 훈련 참여도
    public int stamina;   // 스테미너 — 전투 활동량
    public int stress;    // 스트레스 지수 — 자살 가능 한계
    public int mental;    // 멘탈 — 상태이상 저항
    public int skill;     // 기술 — 스킬
    public int body;      // 육체 — 방어력/HP
    public int sanity;    // 이성
}

// 체질: 레벨업 시 4스텟이 오르는 비율 가중치.
[Serializable]
public class Constitution
{
    public string name = "균형";
    [Min(0f)] public float strengthGrowth = 1f;
    [Min(0f)] public float intelligenceGrowth = 1f;
    [Min(0f)] public float vitalityGrowth = 1f;
    [Min(0f)] public float agilityGrowth = 1f;
}

[Serializable]
public class FriendshipEntry
{
    public CharacterSO target;
    // 음수: 악의적, 양수: 친밀
    public int value;
}

public static class CharacterRules
{
    // 별 등급별 한계 레벨
    public static int MaxLevelForStars(int stars)
    {
        switch (stars)
        {
            case 1: return 10;
            case 2: return 30;
            case 3: return 50;
            case 4: return 70;
            default: return 99; // 5~7성
        }
    }

    // 레벨당 분배되는 스탯 포인트 총합 (별이 높으면 더 많이 오름)
    public static int StatPointsPerLevel(int stars) => 3 + Mathf.Clamp(stars, 1, 7);

    public static bool IsTwoHanded(WeaponType w) =>
        w == WeaponType.SwordTwoHand || w == WeaponType.Bow || w == WeaponType.Spear ||
        w == WeaponType.Polearm || w == WeaponType.Magic;

    public static bool IsShield(WeaponType w) => w == WeaponType.Shield;

    // 1~2성은 멘탈이 약함 — 첫 전투 100% 공포/패닉
    public static bool IsFragileMental(int stars) => stars <= 2;

    // 한국어 표시 이름
    private static readonly Dictionary<JobType, string> JobKr = new Dictionary<JobType, string>
    {
        { JobType.Melee, "근접" }, { JobType.Mage, "마법사" }, { JobType.Archer, "궁수" },
        { JobType.Assassin, "암살자" }, { JobType.Tank, "탱커" }, { JobType.Support, "서포터" },
        { JobType.Carpenter, "목수" }, { JobType.Cook, "요리사" },
        { JobType.Blacksmith, "대장장이" }, { JobType.Tanner, "무두장이" },
    };
    private static readonly Dictionary<WeaponType, string> WeaponKr = new Dictionary<WeaponType, string>
    {
        { WeaponType.None, "없음" },
        { WeaponType.SwordOneHand, "검(한손)" }, { WeaponType.SwordTwoHand, "검(두손)" },
        { WeaponType.Bow, "활" }, { WeaponType.Spear, "창" }, { WeaponType.Dagger, "단검" },
        { WeaponType.Axe, "도끼" }, { WeaponType.Blunt, "둔기" },
        { WeaponType.Polearm, "장병기" }, { WeaponType.Shield, "방패" },
    };

    public static string Korean(JobType t) => JobKr.TryGetValue(t, out var v) ? v : t.ToString();
    public static string Korean(WeaponType t) => WeaponKr.TryGetValue(t, out var v) ? v : t.ToString();

    // 생산 계열인지 판별
    public static bool IsCraftJob(JobType t) =>
        t == JobType.Carpenter || t == JobType.Cook || t == JobType.Blacksmith || t == JobType.Tanner;
}

