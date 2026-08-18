using UnityEngine;

// 직업과 장비를 실제 전투 능력치로 바꿔주는 표.
//
// 지금까지 JobType은 캐릭터를 만들 때 성장 가중치(Constitution)를 고르는 데만 쓰였고,
// 전투에서는 궁수도 탱커도 서포터도 완전히 같은 방식으로 싸웠다.
// 여기서 직업마다 사거리와 체력, 경감률을 갈라놓아야 파티 편성에 의미가 생긴다.
//
// ScriptableObject가 아니라 코드 표로 둔 이유: 직업 목록이 enum으로 고정돼 있어서
// 에셋이 늘어나도 대응되는 항목이 하나씩 생길 뿐이고, enum에 값을 추가했을 때
// 표를 빠뜨리면 컴파일이 아니라 런타임에 조용히 기본값으로 떨어지는 편이 낫기 때문이다.
public readonly struct JobCombatProfile
{
    public readonly float HpMultiplier;
    public readonly float AttackMultiplier;
    public readonly float AttackRange;
    public readonly float DetectRange;
    public readonly float SpeedMultiplier;
    public readonly float ManaMultiplier;
    // 상시 피해 경감. 방어 자세(블록)와 별개로 항상 적용된다.
    public readonly float DamageReduction;
    public readonly bool IsHealer;

    public JobCombatProfile(
        float hpMultiplier,
        float attackMultiplier,
        float attackRange,
        float detectRange,
        float speedMultiplier,
        float manaMultiplier,
        float damageReduction,
        bool isHealer = false)
    {
        HpMultiplier = hpMultiplier;
        AttackMultiplier = attackMultiplier;
        AttackRange = attackRange;
        DetectRange = detectRange;
        SpeedMultiplier = speedMultiplier;
        ManaMultiplier = manaMultiplier;
        DamageReduction = damageReduction;
        IsHealer = isHealer;
    }
}

public readonly struct WeaponCombatProfile
{
    public readonly float AttackMultiplier;
    // 직업 사거리에 곱해진다. 창처럼 같은 근접이어도 조금 더 멀리 닿는 무기를 표현.
    public readonly float RangeMultiplier;
    // 무기 자체가 강제하는 최소 사거리. 활은 누가 들어도 원거리가 된다.
    public readonly float MinRange;
    public readonly float AttackSpeedMultiplier;

    public WeaponCombatProfile(float attackMultiplier, float rangeMultiplier, float minRange, float attackSpeedMultiplier)
    {
        AttackMultiplier = attackMultiplier;
        RangeMultiplier = rangeMultiplier;
        MinRange = minRange;
        AttackSpeedMultiplier = attackSpeedMultiplier;
    }
}

public static class JobProfile
{
    // 전투 계열은 역할이 뚜렷하게 갈리도록, 생산 계열은 전투에 끌려나오면 확실히 약하도록 잡았다.
    // (원작에서도 생산직을 전투에 내보내는 건 그 자체가 나쁜 선택이다)
    public static JobCombatProfile For(JobType job)
    {
        switch (job)
        {
            //                                    HP    ATK   사거리 탐지  속도   마나  경감   힐러
            case JobType.Melee:    return new JobCombatProfile(1.00f, 1.00f, 1.8f,  9f, 1.00f, 1.0f, 0.05f);
            case JobType.Tank:     return new JobCombatProfile(1.65f, 0.70f, 1.8f,  8f, 0.85f, 0.8f, 0.25f);
            case JobType.Assassin: return new JobCombatProfile(0.75f, 1.45f, 1.6f, 10f, 1.25f, 1.0f, 0.00f);
            case JobType.Archer:   return new JobCombatProfile(0.80f, 1.05f, 9.0f, 14f, 1.05f, 1.0f, 0.00f);
            case JobType.Mage:     return new JobCombatProfile(0.65f, 1.15f, 7.5f, 13f, 0.95f, 1.6f, 0.00f);
            case JobType.Support:  return new JobCombatProfile(0.90f, 0.60f, 6.0f, 12f, 1.00f, 1.8f, 0.05f, true);
            default:               return new JobCombatProfile(0.70f, 0.50f, 1.6f,  7f, 0.95f, 0.6f, 0.00f);
        }
    }

    public static WeaponCombatProfile For(WeaponType weapon)
    {
        switch (weapon)
        {
            //                                          ATK   사거리배율 최소사거리 공속
            case WeaponType.SwordOneHand: return new WeaponCombatProfile(1.00f, 1.00f, 0f, 1.00f);
            case WeaponType.SwordTwoHand: return new WeaponCombatProfile(1.30f, 1.10f, 0f, 0.80f);
            case WeaponType.Spear:        return new WeaponCombatProfile(1.05f, 1.60f, 0f, 0.90f);
            case WeaponType.Dagger:       return new WeaponCombatProfile(0.85f, 0.90f, 0f, 1.35f);
            // 활은 직업과 무관하게 원거리를 보장한다.
            case WeaponType.Bow:          return new WeaponCombatProfile(1.00f, 1.00f, 9f, 0.85f);
            default:                      return new WeaponCombatProfile(0.80f, 1.00f, 0f, 1.00f); // 맨손
        }
    }

    // 방패 보정. 두손 무기와는 같이 들 수 없다(CharacterSO.CanEquipShield가 막는다).
    public const float ShieldDamageReduction = 0.10f;
    public const float ShieldBlockBonus = 0.15f;

    // 원거리 유닛이 자기 사거리 밖을 못 보면 영원히 접근만 하다 끝난다.
    // 탐지 범위는 항상 사거리보다 넉넉히 넓게 유지한다.
    public const float DetectRangeMargin = 3f;
}
