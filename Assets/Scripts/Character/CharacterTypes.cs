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

public enum WeaponType
{
    None,
    SwordOneHand,
    SwordTwoHand,
    Bow,
    Spear,
    Dagger,
}

public enum OffHandType { None, Shield }

// 캐릭터 의상 세트 (프리팹의 GameObject와 대응)
public enum OutfitSet
{
    A,     // 전사 갑옷
    C,     // 마법사 로브
    Base,  // 꾸질꾸질한 천옷
    No,    // 아무것도 안 입음
}

public enum Gender { Male, Female }

[Flags]
public enum EmotionState
{
    None     = 0,
    Fear     = 1 << 0, // 모든 능력치 30% 감소
    Panic    = 1 << 1, // 행동불가
    Bleeding = 1 << 2, // 일정시간마다 HP 감소
    Dying    = 1 << 3, // HP 3% 미만, 행동불가
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
    // ============================================================
    //  CharacterRules.cs 에 추가할 AI 계산 메서드
    //  기존 CharacterRules 클래스 내부에 붙여넣으세요.
    // ============================================================

    // ── 이동 속도 ────────────────────────────────────────────────
    /// <summary>기본 이동 속도 (agility 기반)</summary>
    public static float MoveSpeed(int agility)
        => 3.0f + agility * 0.08f;

    /// <summary>도주/추격 속도 (moveSpeed × 1.6)</summary>
    public static float RunSpeed(int agility)
        => MoveSpeed(agility) * 1.6f;

    // ── 쿨타임 ──────────────────────────────────────────────────
    /// <summary>공격 쿨타임 (agility가 높을수록 빠름, 최소 0.3초)</summary>
    public static float AttackCooldown(int agility)
        => Mathf.Max(0.3f, 0.9f - agility * 0.012f);

    /// <summary>스킬 쿨타임 (agility가 높을수록 빠름, 최소 2.0초)</summary>
    public static float SkillCooldown(int agility)
        => Mathf.Max(2.0f, 6.0f - agility * 0.08f);

    // ── 회복 ────────────────────────────────────────────────────
    /// <summary>포션 1회 회복량 (vitality 기반)</summary>
    public static int PotionHealAmount(int vitality)
        => 20 + vitality;

    // ── 전투 범위 (직업별) ───────────────────────────────────────
    public struct CombatRanges
    {
        public float attackRange;
        public float skillRange;
        public float detectRange;
    }

    /// <summary>직업별 전투 범위 반환</summary>
    public static CombatRanges GetCombatRanges(JobType job)
    {
        switch (job)
        {
            case JobType.Melee:
            case JobType.Tank:
                return new CombatRanges { attackRange = 1.5f, skillRange = 3.0f, detectRange = 7.0f };

            case JobType.Assassin:
                return new CombatRanges { attackRange = 1.2f, skillRange = 4.0f, detectRange = 9.0f };

            case JobType.Archer:
                return new CombatRanges { attackRange = 5.0f, skillRange = 7.0f, detectRange = 12.0f };

            case JobType.Mage:
            case JobType.Support:
                return new CombatRanges { attackRange = 2.0f, skillRange = 5.0f, detectRange = 8.0f };

            default: // 생산직 등
                return new CombatRanges { attackRange = 1.5f, skillRange = 3.0f, detectRange = 6.0f };
        }
    }
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
        w == WeaponType.SwordTwoHand || w == WeaponType.Bow || w == WeaponType.Spear;

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
    };

    public static string Korean(JobType t) => JobKr.TryGetValue(t, out var v) ? v : t.ToString();
    public static string Korean(WeaponType t) => WeaponKr.TryGetValue(t, out var v) ? v : t.ToString();

    // 생산 계열인지 판별
    public static bool IsCraftJob(JobType t) =>
        t == JobType.Carpenter || t == JobType.Cook || t == JobType.Blacksmith || t == JobType.Tanner;

    // 직업 → 의상 세트 매핑
    public static OutfitSet OutfitFor(JobType job)
    {
        switch (job)
        {
            case JobType.Melee:
            case JobType.Tank:
                return OutfitSet.A;       // 전사 갑옷
            case JobType.Mage:
            case JobType.Support:
                return OutfitSet.C;       // 마법사 로브
            default:
                return OutfitSet.Base;    // 그 외(궁수, 암살자, 생산직) — 평범한 천옷
        }
    }

    // 의상 세트 영어 프롬프트
    public static string OutfitPromptEn(OutfitSet s)
    {
        switch (s)
        {
            case OutfitSet.A:    return "wearing heavy steel warrior armor, pauldrons, breastplate";
            case OutfitSet.C:    return "wearing dark wizard robes with hood, mystical patterns";
            case OutfitSet.Base: return "wearing dirty ragged peasant cloth, simple shirt and trousers";
            default:             return "wearing simple undergarments";
        }
    }
}

