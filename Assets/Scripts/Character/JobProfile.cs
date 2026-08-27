using UnityEngine;

// 직업과 장비를 실제 전투 능력치로 바꿔주는 표.
//
// 한때 JobType은 캐릭터를 만들 때 성장 가중치(Constitution)를 고르는 데만 쓰였고, 전투에서는
// 궁수도 탱커도 서포터도 완전히 같은 방식으로 싸웠다. 사거리와 체력, 경감률을 갈라놓으면서
// 파티 편성에 의미가 생겼지만, 그 단계에서 직업 사이의 차이는 여전히 "숫자 몇 개"뿐이었다 —
// 검사와 암살자는 스탯만 다른 같은 유닛이었고, 창수는 아예 존재하지 않았다.
//
// 지금 이 표는 숫자가 아니라 역할을 나눈다. 원작 <픽미업!>의 일곱 직군이 전장에서 각각
// 무엇을 하는가를 그대로 옮겼다:
//
//   탱커   어그로를 붙들고 방패로 흘려내며 전열을 사수한다(통곡의 벽)
//   검사   탱커 측면에서 패링과 약점 타격으로 변수를 만든다(근접 테크니션)
//   창수   리치 우위로 거리를 유지하며 부위를 억제한다(중거리 딜포터)
//   암살자 배후로 파고들어 급소를 연타하고 출혈을 남긴다(게릴라)
//   궁수   넓은 시야로 먼저 발견하고 카이팅하며 저격한다(스나이퍼 겸 정찰)
//   마법사 영창을 대가로 한 방을 낸다 — 시전 중에는 무방비다(포격수)
//   사제   마력을 아껴 치명상만 되살리고 상태이상을 걷어낸다(야전 메딕)
//
// ScriptableObject가 아니라 코드 표로 둔 이유: 직업 목록이 enum으로 고정돼 있어서
// 에셋이 늘어나도 대응되는 항목이 하나씩 생길 뿐이고, enum에 값을 추가했을 때
// 표를 빠뜨리면 컴파일이 아니라 런타임에 조용히 기본값으로 떨어지는 편이 낫기 때문이다.

// 전장에서 이 유닛이 서는 자리와 하는 일. 숫자(사거리, 체력)로는 표현되지 않는 성향을 담는다.
// UnitStats로 넘어가 AI가 직접 읽는다 — 상태 구조를 직업마다 나누지 않고 데이터로 가른다.
public enum JobRole
{
    None,       // 생산직·더미 몬스터. 아무 성향도 없이 눈앞의 적을 친다.
    Vanguard,   // 탱커
    Skirmisher, // 검사
    Reach,      // 창수
    Flanker,    // 암살자
    Marksman,   // 궁수
    Caster,     // 마법사
    Mender,     // 사제/지원가
}

// 날아오는 공격을 어떻게 받아내는가. 탱커의 "막기"와 검사의 "흘려내기"는 다른 동작이다.
public enum GuardStyle
{
    None,   // 받아내지 않는다. 원거리 직군과 암살자는 맞기 전에 빠지는 쪽이다.
    Shield, // 방패. 넓은 각도를 오래 버티고 피해를 통째로 흘린다. 대신 읽어내는 판정은 낮다.
    Parry,  // 검신으로 쳐낸다. 각도가 좁고 오래 못 버티지만 읽어내면 그 자리에서 반격이 열린다.
}

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

    // ---- 여기부터가 "역할" ----

    public readonly JobRole Role;
    public readonly GuardStyle Guard;

    // 적이 나를 얼마나 우선해서 노리는가(어그로). 1이 기준이고, 탱커가 높고 후방 직군이 낮다.
    // 예전에는 이 자리를 isTank라는 참/거짓 하나가 맡아서, 탱커가 아니면 전부 똑같이 노려졌다 —
    // 힐러가 최전선의 검사와 같은 확률로 물리는 상황이 나왔다.
    public readonly float ThreatWeight;

    // 적 진영의 후방(원거리·치유 직군)을 얼마나 우선해서 찾아 들어가는가. 암살자만 크다.
    // 0이면 예전처럼 가장 가까운 적을 친다.
    public readonly float BacklinePreference;

    // 나보다 약한 아군에게 붙어 있는 적을 얼마나 우선해서 노리는가(도발). 탱커만 크다.
    //
    // 어그로를 "적이 나를 고르게 하는 힘"으로만 두면 탱커는 수동적이 된다 — 이미 사제를 물고
    // 있는 적은 사제가 죽을 때까지 그대로다. 실측에서 정확히 그 일이 났다(탱커 1마리 / 검사 3마리).
    // 이 값이 탱커를 그쪽으로 걸어가게 만들고, 한 대 치는 순간 위협 비교(ForceSetAttackTarget)가
    // 적을 탱커에게 넘긴다. 원작의 도발이 실제로 하는 일이 이것이다.
    public readonly float PeelBonus;

    // 교전 중 서고 싶은 방위. 적의 정면을 0도로 보고, 90이면 측면, 180이면 등 뒤다.
    // 좌우 중 어느 쪽인지는 유닛마다 따로 정해진다(UnitController.flankSign).
    //
    // 이 값이 "탱커가 정면을 붙들고 검사가 측면을 문다"를 실제 움직임으로 만든다.
    // 아군 탱커의 위치를 직접 참조하지 않는 이유: 적의 정면은 이미 어그로가 붙은 탱커가
    // 차지하고 있으므로, "적 기준 측면"이 곧 "탱커 옆"이 된다. 탱커가 죽거나 없으면
    // 기준이 사라지는 참조 방식과 달리 이쪽은 언제나 성립한다.
    public readonly float EngageAngle;

    // 사거리의 이 비율 안쪽까지 적이 붙으면 물러선다. 0이면 붙어서 싸운다.
    // 원거리 직군만의 것이 아니다 — 창수는 근접이면서도 이 값을 갖는다(리치 우위 유지).
    public readonly float KeepDistanceRatio;

    // 평타가 출혈을 남길 확률. 급소를 노리는 암살자만 크고, 등 뒤를 잡으면 두 배가 된다.
    public readonly float BleedChanceOnHit;

    // 평타가 상대의 발을 묶는 시간(초)과 그동안의 이동 속도 배율. 창수의 부위 억제.
    public readonly float SlowOnHitDuration;
    public readonly float SlowOnHitMultiplier;

    // 영창(치유·스킬 시전) 중에 받는 피해 배율. 마법사와 사제가 크다 —
    // "시전 시간 동안 완전히 무방비"라는 원작 설정이 이 숫자 하나로 성립한다.
    public readonly float CastVulnerability;

    // 시야각(도). 0이면 TargetScanner의 기본값을 쓴다. 정찰을 겸하는 궁수만 넓다.
    public readonly float ViewAngle;

    public JobCombatProfile(
        float hpMultiplier,
        float attackMultiplier,
        float attackRange,
        float detectRange,
        float speedMultiplier,
        float manaMultiplier,
        float damageReduction,
        JobRole role = JobRole.None,
        GuardStyle guard = GuardStyle.None,
        float threatWeight = 1f,
        float backlinePreference = 0f,
        float peelBonus = 0f,
        float engageAngle = 0f,
        float keepDistanceRatio = 0f,
        float bleedChanceOnHit = 0f,
        float slowOnHitDuration = 0f,
        float slowOnHitMultiplier = 1f,
        float castVulnerability = 1f,
        float viewAngle = 0f,
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

        Role = role;
        Guard = guard;
        ThreatWeight = threatWeight;
        BacklinePreference = backlinePreference;
        PeelBonus = peelBonus;
        EngageAngle = engageAngle;
        KeepDistanceRatio = keepDistanceRatio;
        BleedChanceOnHit = bleedChanceOnHit;
        SlowOnHitDuration = slowOnHitDuration;
        SlowOnHitMultiplier = slowOnHitMultiplier;
        CastVulnerability = castVulnerability;
        ViewAngle = viewAngle;
    }
}

public readonly struct WeaponCombatProfile
{
    public readonly float AttackMultiplier;
    // 직업 사거리에 곱해진다. 창처럼 같은 근접이어도 조금 더 멀리 닿는 무기를 표현.
    public readonly float RangeMultiplier;
    // 무기 자체가 강제하는 최소 사거리. 활은 누가 들어도 원거리가 된다.
    public readonly float MinRange;
    // 무기 자체가 강제하는 최대 사거리. 근접 무기는 누가 들어도 붙어야 닿는다 —
    // MinRange의 짝이다. 이게 없으면 직업 사거리가 그대로 통과해서, 사거리 7.5m짜리
    // 마법사가 단검을 들고 6.75m 밖의 적을 찌른다(허공을 긋는데 피해는 들어간다).
    // 값은 "근접 직업 사거리 1.8m x 무기 배율" 기준이라 근접 직업은 영향을 받지 않는다.
    public readonly float MaxRange;
    public readonly float AttackSpeedMultiplier;

    public WeaponCombatProfile(float attackMultiplier, float rangeMultiplier, float minRange, float attackSpeedMultiplier)
        : this(attackMultiplier, rangeMultiplier, minRange, float.PositiveInfinity, attackSpeedMultiplier)
    {
    }

    public WeaponCombatProfile(float attackMultiplier, float rangeMultiplier, float minRange, float maxRange, float attackSpeedMultiplier)
    {
        AttackMultiplier = attackMultiplier;
        RangeMultiplier = rangeMultiplier;
        MinRange = minRange;
        MaxRange = maxRange;
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
            // 검사 — 탱커의 부담을 나누는 근접 테크니션.
            // 측면(55도)을 물고, 검신으로 쳐내며(Parry), 흘려낸 직후 반격이 열린다.
            // 방패처럼 오래 버티지는 못하는 대신 읽어내는 판정이 높다(UnitStats 쪽 perfectGuardChance).
            case JobType.Melee:
                return new JobCombatProfile(1.00f, 1.00f, 1.8f, 9f, 1.00f, 1.0f, 0.05f,
                    role: JobRole.Skirmisher, guard: GuardStyle.Parry,
                    threatWeight: 1.0f, engageAngle: 55f);

            // 탱커 — 어그로를 통째로 끌어안고 정면을 막는다.
            // 위협 가중치가 압도적으로 높아야 후방이 살아남는다(원작: 방어선이 뚫리면 파티 괴멸).
            // PeelBonus는 그 어그로를 능동적으로 만든다 — 약한 아군을 물고 있는 적을 찾아가 친다.
            case JobType.Tank:
                return new JobCombatProfile(1.65f, 0.70f, 1.8f, 8f, 0.85f, 0.8f, 0.25f,
                    role: JobRole.Vanguard, guard: GuardStyle.Shield,
                    threatWeight: 3.2f, peelBonus: 6f, engageAngle: 0f);

            // 창수 — 리치 우위. 근접이면서도 붙지 않는 유일한 직군이다.
            // 사거리 0.62배 안쪽으로 파고들면 물러서고, 찌른 상대의 발을 묶는다(부위 억제).
            case JobType.Lancer:
                return new JobCombatProfile(0.95f, 1.10f, 2.6f, 11f, 0.95f, 1.0f, 0.08f,
                    role: JobRole.Reach, guard: GuardStyle.None,
                    threatWeight: 0.85f, engageAngle: 28f, keepDistanceRatio: 0.62f,
                    slowOnHitDuration: 1.6f, slowOnHitMultiplier: 0.6f);

            // 암살자 — 배후(180도)로 파고들어 급소를 연타한다.
            // 위협 가중치가 낮아 적이 잘 안 쳐다보고, 그만큼 뒤를 잡을 시간을 번다.
            // BacklinePreference가 적 진영의 궁수/마법사/사제를 먼저 찾아 들어가게 만든다.
            case JobType.Assassin:
                return new JobCombatProfile(0.75f, 1.45f, 1.6f, 10f, 1.25f, 1.0f, 0.00f,
                    role: JobRole.Flanker, guard: GuardStyle.None,
                    threatWeight: 0.55f, backlinePreference: 7f, engageAngle: 180f,
                    bleedChanceOnHit: 0.35f);

            // 궁수 — 고지 선점은 아직 없지만, 시야만은 먼저 확보한다.
            // 탐지 15m에 시야각 200도로 팀에서 가장 먼저 적을 발견해 게시판에 올린다(정찰).
            case JobType.Archer:
                return new JobCombatProfile(0.80f, 1.05f, 9.0f, 15f, 1.05f, 1.0f, 0.00f,
                    role: JobRole.Marksman, guard: GuardStyle.None,
                    threatWeight: 0.40f, keepDistanceRatio: 0.30f, viewAngle: 200f);

            // 마법사 — 영창 중 무방비. 시전 중 받는 피해가 2.2배다.
            // 그 대가로 스킬 한 방이 크고(마나 1.6배로 자주 쓴다) 사거리가 길다.
            case JobType.Mage:
                return new JobCombatProfile(0.65f, 1.15f, 7.5f, 13f, 0.95f, 1.6f, 0.00f,
                    role: JobRole.Caster, guard: GuardStyle.None,
                    threatWeight: 0.40f, keepDistanceRatio: 0.32f, castVulnerability: 2.2f);

            // 사제 — 치유도 영창이다. 마법사보다는 덜하지만 시전 중에는 그대로 맞는다.
            // 위협 가중치가 가장 낮다 — 대신 적 암살자의 BacklinePreference가 정확히 이쪽을 노린다.
            case JobType.Support:
                return new JobCombatProfile(0.90f, 0.60f, 6.0f, 12f, 1.00f, 1.8f, 0.05f,
                    role: JobRole.Mender, guard: GuardStyle.None,
                    threatWeight: 0.30f, keepDistanceRatio: 0.35f, castVulnerability: 1.8f,
                    isHealer: true);

            // 생산 계열. 역할 없이 눈앞의 적을 친다 — 전투에 끌려나온 것 자체가 나쁜 선택이다.
            default:
                return new JobCombatProfile(0.70f, 0.50f, 1.6f, 7f, 0.95f, 0.6f, 0.00f,
                    role: JobRole.None, guard: GuardStyle.None,
                    threatWeight: 0.9f);
        }
    }

    public static WeaponCombatProfile For(WeaponType weapon)
    {
        switch (weapon)
        {
            // 최대 사거리는 "근접 직업 사거리 1.8m x 사거리배율"이다. 근접 직업(1.8)과 탱커(1.8)는
            // 이 값에 정확히 걸리므로 달라지는 것이 없고, 사거리가 긴 직업(마법사 7.5, 서포터 6.0)이
            // 근접 무기를 들었을 때만 붙잡아 준다.
            //
            // 창수(2.6)는 이 상한 때문에 창을 들어야만 제 리치가 나온다 — 창수가 검을 들면
            // 1.8m로 깎여 검사와 같아진다. 무기가 리치를 정하는 것이라 그게 맞다.
            //                                          ATK   사거리배율 최소 최대  공속
            case WeaponType.SwordOneHand: return new WeaponCombatProfile(1.00f, 1.00f, 0f, 1.80f, 1.00f);
            case WeaponType.SwordTwoHand: return new WeaponCombatProfile(1.30f, 1.10f, 0f, 1.98f, 0.80f);
            case WeaponType.Spear:        return new WeaponCombatProfile(1.05f, 1.60f, 0f, 2.88f, 0.90f);
            case WeaponType.Dagger:       return new WeaponCombatProfile(0.85f, 0.90f, 0f, 1.62f, 1.35f);
            case WeaponType.Axe:          return new WeaponCombatProfile(1.15f, 0.95f, 0f, 1.71f, 0.90f);
            case WeaponType.Blunt:        return new WeaponCombatProfile(1.25f, 0.95f, 0f, 1.71f, 0.75f);
            // 장병기는 창보다 무겁지만 사거리는 비슷하게 가져간다.
            case WeaponType.Polearm:      return new WeaponCombatProfile(1.20f, 1.55f, 0f, 2.79f, 0.80f);
            // 활은 직업과 무관하게 원거리를 보장한다. 위쪽 한계는 없다.
            case WeaponType.Bow:          return new WeaponCombatProfile(1.00f, 1.00f, 9f, 0.85f);
            // 맨손 시전. 손에 든 것이 없어도 마법은 멀리 나간다. 활보다 가깝게 잡은 건
            // 마법사가 활보다 앞에 서서 탄환을 던지는 그림을 원해서다 — 활 9m, 마법 6m.
            case WeaponType.Magic:        return new WeaponCombatProfile(1.10f, 1.00f, 6f, 0.80f);
            // 방패가 주무기 자리에 오는 건 설계상 없어야 하지만, 와도 맨손과 같게 둔다.
            case WeaponType.Shield:       return new WeaponCombatProfile(0.80f, 1.00f, 0f, 1.80f, 1.00f);
            // 맨손도 근접이다. 주먹이 7.5m를 때리면 안 된다.
            default:                      return new WeaponCombatProfile(0.80f, 1.00f, 0f, 1.80f, 1.00f);
        }
    }

    // 이 역할이 원거리에서 싸우는가. "붙어서 싸우지 않는다"와는 다르다 —
    // 창수(Reach)는 거리를 유지하지만 발차기도 하고 스윙으로 앞을 쓸기도 하는 근접이다.
    //
    // 예전에는 이 판단이 전부 "사거리 >= 4m"라는 숫자 하나였다(minKeepDistanceRange).
    // 그 방식은 무기에 따라 결과가 흔들린다 — 단검을 든 마법사가 근접으로 잡히고,
    // 창수의 리치를 조금만 올리면 발차기를 잃는 식이다. 역할로 물으면 그런 일이 없다.
    public static bool IsRangedRole(JobRole role) =>
        role == JobRole.Marksman || role == JobRole.Caster || role == JobRole.Mender;

    // 적 진영에서 "후방"으로 치는 역할. 암살자가 찾아 들어가는 대상이다.
    public static bool IsBacklineRole(JobRole role) => IsRangedRole(role);

    // 방패 보정. 두손 무기와는 같이 들 수 없다(CharacterSO.CanEquipShield가 막는다).
    public const float ShieldDamageReduction = 0.10f;
    public const float ShieldBlockBonus = 0.15f;

    // 원거리 유닛이 자기 사거리 밖을 못 보면 영원히 접근만 하다 끝난다.
    // 탐지 범위는 항상 사거리보다 넉넉히 넓게 유지한다.
    public const float DetectRangeMargin = 3f;
}
