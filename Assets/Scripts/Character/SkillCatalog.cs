using System.Collections.Generic;
using UnityEngine;

// 스킬 하나의 정의. 이름과 설명은 여기 한 곳에만 적힌다.
//
// 캐릭터는 스킬을 통째로 들고 있지 않고 Id 문자열만 기억한다(CharacterSO.skillIds).
// 그래야 이름이나 설명을 고쳐도 이미 배운 스킬이 옛 문구를 그대로 달고 다니지 않는다.
public readonly struct SkillDefinition
{
    public readonly string Id;
    public readonly string Name;
    public readonly string Description;

    // 배울 수 있는 직업. 비어 있으면 누구나 배운다.
    public readonly JobType[] Jobs;

    // 조건이 맞으면 저절로 열리는 스킬의 조건. null이면 합성으로만 얻는다.
    //
    // 조건이 붙은 스킬은 합성 후보에서 아예 빠진다(IsCandidate 참조).
    // 조건을 걸어 둔 스킬이 합성으로도 굴러 나오면 조건이 있으나 마나가 되기 때문이다.
    // 조건을 적는 법은 SkillUnlock 참조.
    public readonly SkillUnlockCondition Unlock;

    public SkillDefinition(string id, string name, string description,
        JobType[] jobs = null, SkillUnlockCondition unlock = null)
    {
        Id = id;
        Name = name;
        Description = description;
        Jobs = jobs;
        Unlock = unlock;
    }

    // 합성이 아니라 조건으로 열리는 스킬인가.
    public bool IsConditional => Unlock != null;

    public bool CanLearn(JobType job)
    {
        if (Jobs == null || Jobs.Length == 0) return true;

        for (int i = 0; i < Jobs.Length; i++)
            if (Jobs[i] == job) return true;

        return false;
    }
}

// 스킬 표.
//
// 스킬이 손에 들어오는 길은 두 갈래다. 하나는 합성 — 재료 카드를 태워 Roll이 하나 뽑아준다.
// 다른 하나는 조건 해금 — 마지막 인자로 조건을 달아 두면 합성과 무관하게, 그 조건을 채운
// 캐릭터에게 저절로 열린다(SkillUnlocks). 아래 표에는 아직 조건이 붙은 스킬이 없다.
// 조건은 스킬을 새로 추가할 때 그 줄에 함께 적는다.
//
// JobProfile과 같은 이유로 ScriptableObject가 아니라 코드 표다 — 목록이 코드에서만 참조되고,
// 에셋으로 흩어 두면 어떤 스킬이 있는지 한눈에 볼 수 없다.
//
// 스킬은 성급으로 나누지 않는다. 표에 줄을 더하면 그만큼 후보가 늘 뿐이고, 재료가 몇 성이든
// 나올 수 있는 스킬은 같다 — 가르는 것은 직업뿐이다(Jobs). 그래서 스킬을 새로 적을 때
// 등급을 고민할 필요가 없고, 표는 계속 길어져도 된다.
public static class SkillCatalog
{
    // 한 캐릭터가 배울 수 있는 스킬 수. 넘으면 합성이 거절된다 —
    // 상한이 없으면 카드 한 장에 스킬이 끝없이 쌓여 표시할 자리도, 의미도 없어진다.
    public const int MaxSkillsPerCharacter = 4;

    private static readonly JobType[] CraftJobs =
        { JobType.Carpenter, JobType.Cook, JobType.Blacksmith, JobType.Tanner };

    private static readonly JobType[] MeleeJobs =
        { JobType.Melee, JobType.Tank, JobType.Assassin, JobType.Lancer };

    private static readonly SkillDefinition[] All =
    {
        // 누구나 ------------------------------------------------------------
        new SkillDefinition("power_strike",  "강타",        "힘을 실어 내리친다. 한 방이 묵직해진다."),
        new SkillDefinition("iron_will",     "굳은 의지",   "겁에 쉽게 흔들리지 않는다."),
        new SkillDefinition("quick_step",    "잰걸음",      "발이 가벼워져 먼저 자리를 잡는다."),
        new SkillDefinition("counter",       "반격 자세",   "막아낸 직후 곧바로 되받아친다."),
        new SkillDefinition("execute",       "처형",        "빈사에 몰린 적에게 치명적인 일격을 넣는다."),
        new SkillDefinition("berserk",       "광폭화",      "피를 볼수록 공격이 매서워진다."),
        new SkillDefinition("unyielding",    "불굴",        "쓰러지기 직전 한 번은 버텨낸다."),
        new SkillDefinition("heros_blow",    "영웅의 일격", "전장을 가르는 필살의 한 방."),

        // 근접 계열 ----------------------------------------------------------
        new SkillDefinition("double_slash",  "연속 베기",   "한 호흡에 두 번 벤다.", MeleeJobs),
        new SkillDefinition("whirlwind",     "회전 베기",   "몸을 돌려 주위를 한꺼번에 쓸어낸다.", MeleeJobs),

        // 직업 전용 ----------------------------------------------------------
        // 마법사가 쓰는 마법 자체는 여기 없다. 그건 배우는 것이 아니라 속성이 주는 것이라
        // SpellCatalog가 따로 들고 있다 — 화염 마법사는 화염구를 "배우지" 않고 처음부터 쓴다.
        // 여기 남는 것은 그 마법을 어떻게 다루는가에 붙는 숙련이다.
        new SkillDefinition("swift_chant",   "속성",        "영창이 짧아진다. 무방비로 서 있는 시간이 줄어든다.", new[] { JobType.Mage }),
        new SkillDefinition("mana_economy",  "마력 절약",   "같은 마력으로 마법을 한 번 더 짜낸다.", new[] { JobType.Mage }),
        new SkillDefinition("wide_matrix",   "확장 술식",   "펼치는 마법의 범위가 넓어진다.", new[] { JobType.Mage }),

        new SkillDefinition("piercing_shot", "관통 사격",   "한 발로 여럿을 꿰뚫는다.", new[] { JobType.Archer }),
        new SkillDefinition("multi_shot",    "다중 사격",   "화살 여러 대를 한 번에 메긴다.", new[] { JobType.Archer }),

        new SkillDefinition("vital_strike",  "급소 찌르기", "약한 곳을 정확히 노린다.", new[] { JobType.Assassin }),
        new SkillDefinition("shadow_step",   "그림자 도약", "그림자를 밟고 등 뒤로 돌아간다.", new[] { JobType.Assassin }),

        new SkillDefinition("taunt",         "도발",        "적의 시선을 자신에게 끌어온다.", new[] { JobType.Tank }),
        new SkillDefinition("iron_wall",     "철벽",        "자리를 지키며 피해를 크게 덜어낸다.", new[] { JobType.Tank }),

        new SkillDefinition("parry_riposte", "받아넘기기", "상대 검을 흘려낸 그 자리에서 되받아친다.", new[] { JobType.Melee }),
        new SkillDefinition("sword_aura",    "검기",        "칼끝에 마력을 실어 장갑째 베어낸다.", new[] { JobType.Melee }),

        new SkillDefinition("leg_sweep",     "다리 걸기",   "정강이를 찔러 적의 발을 묶는다.", new[] { JobType.Lancer }),
        new SkillDefinition("brace",         "창벽",        "창을 세워 달려드는 적을 멈춰 세운다.", new[] { JobType.Lancer }),
        new SkillDefinition("impale",        "꿰뚫기",      "한 번에 깊게 찔러 부위를 망가뜨린다.", new[] { JobType.Lancer }),

        new SkillDefinition("healing_hand",  "치유의 손길", "다친 동료의 상처를 아물게 한다.", new[] { JobType.Support }),
        new SkillDefinition("blessing",      "축복",        "동료의 몸놀림을 한동안 끌어올린다.", new[] { JobType.Support }),
        new SkillDefinition("last_prayer",   "마지막 기도", "쓰러진 동료를 한 번 일으켜 세운다.", new[] { JobType.Support }),

        // 생산 계열 ----------------------------------------------------------
        new SkillDefinition("deft_hands",    "손재주",      "도구를 다루는 솜씨가 늘어 작업이 빨라진다.", CraftJobs),
        new SkillDefinition("masters_eye",   "명장의 눈",   "재료의 좋고 나쁨을 한눈에 알아본다.", CraftJobs),
        new SkillDefinition("masterpiece",   "역작",        "이따금 자기 실력을 뛰어넘는 물건을 만들어 낸다.", CraftJobs),
    };

    // 표 전체. SkillUnlocks가 정산 때마다 조건부 스킬을 훑을 때 쓴다.
    // 배열을 그대로 넘기면 밖에서 원소를 갈아끼울 수 있으므로 읽기 전용으로만 내준다.
    public static IReadOnlyList<SkillDefinition> AllSkills => All;

    public static SkillDefinition? Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        for (int i = 0; i < All.Length; i++)
            if (All[i].Id == id) return All[i];

        return null;
    }

    // 화면에 적을 이름. 표에서 사라진 id는 그대로 보여준다 — 조용히 빈칸이 되면 더 헷갈린다.
    public static string NameOf(string id)
    {
        SkillDefinition? found = Find(id);
        return found.HasValue ? found.Value.Name : id;
    }

    /// 주카드가 배울 스킬 하나를 고른다. 배울 게 없으면 null.
    ///
    /// 후보는 "주카드 직업이 배울 수 있음 + 아직 안 배움"이고, 그 안에서는 전부 같은 확률이다.
    /// 재료가 몇 성인지는 보지 않는다 — 성급으로 스킬을 가르지 않기 때문이다.
    public static string Roll(CharacterSO main)
    {
        if (main == null) return null;

        int total = CountCandidates(main);
        if (total <= 0) return null;

        int roll = Random.Range(0, total);
        for (int i = 0; i < All.Length; i++)
        {
            if (!IsCandidate(All[i], main)) continue;
            if (roll == 0) return All[i].Id;
            roll--;
        }

        return null;
    }

    /// 이 영웅이 더 배울 수 있는 스킬이 하나라도 있는지. 합성 버튼을 잠글지 판단할 때 쓴다.
    public static bool HasCandidate(CharacterSO main) => CountCandidates(main) > 0;

    private static int CountCandidates(CharacterSO main)
    {
        if (main == null) return 0;

        int count = 0;
        for (int i = 0; i < All.Length; i++)
            if (IsCandidate(All[i], main)) count++;

        return count;
    }

    private static bool IsCandidate(SkillDefinition skill, CharacterSO main)
    {
        // 조건 해금 스킬은 합성으로 나오지 않는다. 조건을 채워서 여는 것이 그 스킬의 값어치다.
        if (skill.IsConditional) return false;
        if (!skill.CanLearn(main.job)) return false;

        return !main.HasSkill(skill.Id);
    }
}
