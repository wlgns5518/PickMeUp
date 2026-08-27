using UnityEngine;

// 마법 — 스킬이 아니다.
//
// 원작 <픽미업!>에서 마법은 버튼 하나로 나가는 정해진 액티브가 아니라, 마법사가 체내 마력을
// 연산하고 영창해 현상으로 구현해내는 것이다. 그래서 이 표는 SkillCatalog(합성으로 배우는 스킬)와
// 완전히 별개이고, 마법사는 아무것도 "배우지" 않는다 — 자기 속성이 곧 자기가 쓸 수 있는 마법이다.
//
// 핵심 제약은 단일 속성 귀속이다. 마법사는 평생 하나의 속성만 다룬다. 빙결 마법사가 화염구를
// 쓰는 일은 없다. 그래서 전술 판단이 "적 약점에 맞는 속성을 고른다"가 아니라
// "내 속성 안에서 지금 무엇을 쓸 것인가"가 된다.

// 마법사가 평생 귀속되는 속성 하나.
// 복수 속성은 원작에서도 극히 예외적인 천재나 특수 아티팩트에 한정되므로 여기에는 없다.
public enum MagicAffinity
{
    None,       // 마법사가 아니다.
    Fire,       // 화염 — 태우고 무너뜨린다. 파괴력이 가장 크다.
    Ice,        // 빙결 — 얼려 묶는다. 피해는 낮지만 발을 멈춘다.
    Lightning,  // 전격 — 꿰뚫고 자세를 깬다. 강인도 피해에 특화.
}

// 속성 안에서의 갈래. 같은 속성이라도 셋은 하는 일이 다르다.
public enum SpellRole
{
    // 단일. 마법사가 가장 자주 쓰는 것이고, 이것도 영창을 거친다.
    //
    // 마법사에게는 평타가 없다. 칼을 휘두르듯 즉발로 나가는 동작이 하나도 없고,
    // 모든 공격이 "마력을 모아 현상으로 구현하는" 같은 절차를 밟는다 — 다만 작은 마법일수록
    // 그 절차가 짧을 뿐이다. 가장 작은 것(화염 화살 계열)은 마력도 쓰지 않는다:
    // 체내 마력의 기본 운용이라 소모할 것이 없다.
    Bolt,
    // 지형/제어. 적의 발을 묶거나 진입을 막는다.
    Control,
    // 광역 파괴. 판을 끝내는 한 방. 긴 영창과 큰 마력을 대가로 낸다.
    Burst,
}

// 마법 한 종류의 실제 수치.
public readonly struct SpellSpec
{
    public readonly bool Exists;
    // 속성 안에서의 자리. 마법마다 쿨다운을 따로 세는 데 쓴다.
    public readonly int Index;
    public readonly string Name;
    public readonly SpellRole Role;

    // 영창 시간(초). 이 동안 무방비다(UnitStats.castVulnerabilityMultiplier).
    public readonly float CastTime;
    public readonly int ManaCost;
    public readonly float Cooldown;

    // 착탄 지점을 중심으로 한 반지름(미터). 0이면 겨눈 하나만 맞고, 탄환이 손을 떠난다.
    public readonly float Radius;

    // 기본 공격력에 곱해지는 피해 배율.
    public readonly float DamageMultiplier;
    // 강인도 피해. 전격이 크다 — 죽이는 것이 아니라 무너뜨리는 속성이라서.
    public readonly float PoiseDamage;

    // 둔화. 빙결이 크다.
    public readonly float SlowDuration;
    public readonly float SlowMultiplier;

    // 이 마법을 쓸 만한 최소 표적 수. 광역기가 하나 잡자고 나가면 마력만 버린다.
    public readonly int MinTargets;

    public SpellSpec(int index, string name, SpellRole role, float castTime, int manaCost, float cooldown,
        float radius, float damageMultiplier, float poiseDamage,
        float slowDuration, float slowMultiplier, int minTargets)
    {
        Exists = true;
        Index = index;
        Name = name;
        Role = role;
        CastTime = castTime;
        ManaCost = manaCost;
        Cooldown = cooldown;
        Radius = radius;
        DamageMultiplier = damageMultiplier;
        PoiseDamage = poiseDamage;
        SlowDuration = slowDuration;
        SlowMultiplier = slowMultiplier;
        MinTargets = minTargets;
    }
}

public static class SpellCatalog
{
    // JobProfile과 같은 이유로 코드 표다 — 목록이 enum으로 고정돼 있고, 에셋으로 흩어 두면
    // 어느 속성이 무엇을 갖는지 한눈에 볼 수 없다.
    //
    // 배열 순서가 곧 우선순위다. 마법사는 위에서부터 훑어 지금 쓸 수 있는 첫 번째를 고른다
    // (UnitController.SelectSpell). 그래서 파괴력이 큰 것부터 적고, 마지막에 언제나 쓸 수 있는
    // 기본 마법을 둔다 — 그 마지막 갈래가 없으면 마법사는 아무것도 못 하는 순간이 생긴다.
    //
    // 속성마다 성격이 갈리는 것이 중요하다. 화염은 태워 부수고, 빙결은 얼려 묶고,
    // 전격은 자세를 무너뜨린다. 속성을 골라 쓸 수 없기 때문에 어느 마법사를 데려가느냐가
    // 파티 운영을 통째로 바꾼다.

    // 영창 시간에 대하여.
    //
    // 한때 기본 마법이 0.5초, 유성 낙하가 2.6초였다. 손이 너무 빨라서 "마력을 모은다"가 아니라
    // 손짓 한 번으로 보였다. 지금은 가장 작은 마법이 1초, 나머지는 그 셋 이상이다 —
    // 유성 낙하는 7.8초를 통째로 무방비로 서 있어야 한다.
    //
    // 그 시간이 곧 이 직군의 값이다. 피해도 늘어난 영창만큼 함께 올렸으므로
    // (유성 낙하 x4.5 → x13.5) 끝까지 버텨 낸 한 방은 판을 통째로 정리한다.
    // 그리고 그 시간을 벌어 주는 것이 탱커와 전열의 일이다 — 마법사 혼자서는 결코 완주하지 못한다.
    //
    // 강인도 피해만 그대로 두었다. 그쪽은 상한이 100으로 고정된 축이라 함께 세 배로 올리면
    // 모든 큰 마법이 무조건 자세를 깨게 되어, 그걸로 먹고사는 전격의 자리가 사라진다.
    //
    //                                    자리 이름          역할            영창  마나  쿨   반경  피해   강인도 둔화  둔화율 최소표적
    private static readonly SpellSpec[] Fire =
    {
        new SpellSpec(0, "유성 낙하", SpellRole.Burst,   7.8f, 40, 16f, 5.0f, 13.5f, 90f, 0f,   1f,    3),
        new SpellSpec(1, "화염 폭발", SpellRole.Burst,   4.8f, 26,  9f, 3.6f,  7.8f, 55f, 0f,   1f,    2),
        new SpellSpec(2, "화염 장판", SpellRole.Control, 3.6f, 18,  8f, 3.2f,  4.8f, 30f, 0.8f, 0.85f, 2),
        new SpellSpec(3, "화염구",    SpellRole.Bolt,    2.7f,  8, 2.5f, 0f,   6.0f, 35f, 0f,   1f,    1),
        new SpellSpec(4, "화염 화살", SpellRole.Bolt,    1.0f,  0, 0.4f, 0f,   2.3f, 15f, 0f,   1f,    1),
    };

    private static readonly SpellSpec[] Ice =
    {
        new SpellSpec(0, "빙하 폭발", SpellRole.Burst,   7.2f, 38, 15f, 4.6f, 10.2f, 70f, 1.4f, 0.55f, 3),
        new SpellSpec(1, "서리 파열", SpellRole.Burst,   4.5f, 24,  9f, 3.4f,  6.0f, 45f, 1.6f, 0.50f, 2),
        new SpellSpec(2, "서리 감옥", SpellRole.Control, 3.3f, 16,  7f, 3.6f,  2.1f, 20f, 2.6f, 0.35f, 2),
        new SpellSpec(3, "얼음 창",   SpellRole.Bolt,    2.55f, 8, 2.5f, 0f,   4.5f, 25f, 1.2f, 0.60f, 1),
        new SpellSpec(4, "고드름",    SpellRole.Bolt,    1.0f,  0, 0.4f, 0f,   1.7f, 12f, 1.0f, 0.70f, 1),
    };

    private static readonly SpellSpec[] Lightning =
    {
        new SpellSpec(0, "뇌격 폭풍", SpellRole.Burst,   7.5f, 39, 15f, 4.4f, 11.4f, 120f, 0f,   1f,   3),
        new SpellSpec(1, "낙뢰",      SpellRole.Burst,   4.2f, 25,  9f, 3.2f,  7.2f,  80f, 0f,   1f,   2),
        new SpellSpec(2, "연쇄 감전", SpellRole.Control, 3.0f, 17,  7f, 3.0f,  3.3f,  70f, 0.6f, 0.8f, 2),
        new SpellSpec(3, "전격 창",   SpellRole.Bolt,    2.4f,  8, 2.5f, 0f,   3.9f,  55f, 0f,   1f,   1),
        new SpellSpec(4, "전격 화살", SpellRole.Bolt,    1.0f,  0, 0.4f, 0f,   2.1f,  30f, 0f,   1f,   1),
    };

    private static readonly SpellSpec[] Empty = new SpellSpec[0];

    // 이 속성의 마법 전부. 순서가 우선순위다.
    public static SpellSpec[] SpellsOf(MagicAffinity affinity)
    {
        switch (affinity)
        {
            case MagicAffinity.Fire:      return Fire;
            case MagicAffinity.Ice:       return Ice;
            case MagicAffinity.Lightning: return Lightning;
            default:                      return Empty;
        }
    }

    // 이 속성이 가진 마법 수. 유닛이 마법별 쿨다운 배열을 잡을 때 쓴다.
    public static int CountOf(MagicAffinity affinity) => SpellsOf(affinity).Length;

    private static readonly string[] AffinityKr = { "무속성", "화염", "빙결", "전격" };

    public static string Korean(MagicAffinity affinity)
    {
        int index = (int)affinity;
        return index >= 0 && index < AffinityKr.Length ? AffinityKr[index] : affinity.ToString();
    }

    // 마법사에게 속성을 하나 뽑아 준다. 캐릭터를 만들 때 한 번만 정해지고 평생 바뀌지 않는다.
    public static MagicAffinity RollAffinity()
    {
        return (MagicAffinity)Random.Range((int)MagicAffinity.Fire, (int)MagicAffinity.Lightning + 1);
    }
}
