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

    // 이 스킬이 나오려면 재료 카드가 몇 성이어야 하는지. 높을수록 귀하다.
    public readonly int Tier;

    // 배울 수 있는 직업. 비어 있으면 누구나 배운다.
    public readonly JobType[] Jobs;

    public SkillDefinition(string id, string name, string description, int tier, JobType[] jobs = null)
    {
        Id = id;
        Name = name;
        Description = description;
        Tier = tier;
        Jobs = jobs;
    }

    public bool CanLearn(JobType job)
    {
        if (Jobs == null || Jobs.Length == 0) return true;

        for (int i = 0; i < Jobs.Length; i++)
            if (Jobs[i] == job) return true;

        return false;
    }
}

// 합성으로 얻는 스킬 표.
//
// JobProfile과 같은 이유로 ScriptableObject가 아니라 코드 표다 — 목록이 코드에서만 참조되고,
// 에셋으로 흩어 두면 어느 스킬이 어느 등급에서 나오는지 한눈에 볼 수 없다.
//
// 등급(Tier)은 "이 스킬을 뽑으려면 재료가 몇 성이어야 하는가"다. 재료가 좋을수록 후보가 넓어지고,
// 넓어진 후보 안에서도 높은 등급일수록 덜 나온다. 확률 계산은 Roll 참조.
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
        new SkillDefinition("power_strike",  "강타",        "힘을 실어 내리친다. 한 방이 묵직해진다.", 1),
        new SkillDefinition("iron_will",     "굳은 의지",   "겁에 쉽게 흔들리지 않는다.", 1),
        new SkillDefinition("quick_step",    "잰걸음",      "발이 가벼워져 먼저 자리를 잡는다.", 1),
        new SkillDefinition("counter",       "반격 자세",   "막아낸 직후 곧바로 되받아친다.", 2),
        new SkillDefinition("execute",       "처형",        "빈사에 몰린 적에게 치명적인 일격을 넣는다.", 3),
        new SkillDefinition("berserk",       "광폭화",      "피를 볼수록 공격이 매서워진다.", 4),
        new SkillDefinition("unyielding",    "불굴",        "쓰러지기 직전 한 번은 버텨낸다.", 5),
        new SkillDefinition("heros_blow",    "영웅의 일격", "전장을 가르는 필살의 한 방.", 6),

        // 근접 계열 ----------------------------------------------------------
        new SkillDefinition("double_slash",  "연속 베기",   "한 호흡에 두 번 벤다.", 2, MeleeJobs),
        new SkillDefinition("whirlwind",     "회전 베기",   "몸을 돌려 주위를 한꺼번에 쓸어낸다.", 3, MeleeJobs),

        // 직업 전용 ----------------------------------------------------------
        // 마법사가 쓰는 마법 자체는 여기 없다. 그건 배우는 것이 아니라 속성이 주는 것이라
        // SpellCatalog가 따로 들고 있다 — 화염 마법사는 화염구를 "배우지" 않고 처음부터 쓴다.
        // 여기 남는 것은 그 마법을 어떻게 다루는가에 붙는 숙련이다.
        new SkillDefinition("swift_chant",   "속성",        "영창이 짧아진다. 무방비로 서 있는 시간이 줄어든다.", 2, new[] { JobType.Mage }),
        new SkillDefinition("mana_economy",  "마력 절약",   "같은 마력으로 마법을 한 번 더 짜낸다.", 3, new[] { JobType.Mage }),
        new SkillDefinition("wide_matrix",   "확장 술식",   "펼치는 마법의 범위가 넓어진다.", 5, new[] { JobType.Mage }),

        new SkillDefinition("piercing_shot", "관통 사격",   "한 발로 여럿을 꿰뚫는다.", 2, new[] { JobType.Archer }),
        new SkillDefinition("multi_shot",    "다중 사격",   "화살 여러 대를 한 번에 메긴다.", 3, new[] { JobType.Archer }),

        new SkillDefinition("vital_strike",  "급소 찌르기", "약한 곳을 정확히 노린다.", 2, new[] { JobType.Assassin }),
        new SkillDefinition("shadow_step",   "그림자 도약", "그림자를 밟고 등 뒤로 돌아간다.", 4, new[] { JobType.Assassin }),

        new SkillDefinition("taunt",         "도발",        "적의 시선을 자신에게 끌어온다.", 2, new[] { JobType.Tank }),
        new SkillDefinition("iron_wall",     "철벽",        "자리를 지키며 피해를 크게 덜어낸다.", 3, new[] { JobType.Tank }),

        new SkillDefinition("parry_riposte", "받아넘기기", "상대 검을 흘려낸 그 자리에서 되받아친다.", 2, new[] { JobType.Melee }),
        new SkillDefinition("sword_aura",    "검기",        "칼끝에 마력을 실어 장갑째 베어낸다.", 4, new[] { JobType.Melee }),

        new SkillDefinition("leg_sweep",     "다리 걸기",   "정강이를 찔러 적의 발을 묶는다.", 2, new[] { JobType.Lancer }),
        new SkillDefinition("brace",         "창벽",        "창을 세워 달려드는 적을 멈춰 세운다.", 3, new[] { JobType.Lancer }),
        new SkillDefinition("impale",        "꿰뚫기",      "한 번에 깊게 찔러 부위를 망가뜨린다.", 4, new[] { JobType.Lancer }),

        new SkillDefinition("healing_hand",  "치유의 손길", "다친 동료의 상처를 아물게 한다.", 2, new[] { JobType.Support }),
        new SkillDefinition("blessing",      "축복",        "동료의 몸놀림을 한동안 끌어올린다.", 3, new[] { JobType.Support }),
        new SkillDefinition("last_prayer",   "마지막 기도", "쓰러진 동료를 한 번 일으켜 세운다.", 4, new[] { JobType.Support }),

        // 생산 계열 ----------------------------------------------------------
        new SkillDefinition("deft_hands",    "손재주",      "도구를 다루는 솜씨가 늘어 작업이 빨라진다.", 1, CraftJobs),
        new SkillDefinition("masters_eye",   "명장의 눈",   "재료의 좋고 나쁨을 한눈에 알아본다.", 2, CraftJobs),
        new SkillDefinition("masterpiece",   "역작",        "이따금 자기 실력을 뛰어넘는 물건을 만들어 낸다.", 4, CraftJobs),
    };

    // 표에 있는 가장 높은 등급. 재료 등급을 여기까지만 쳐준다(7성 재료도 6등급이 상한).
    public static readonly int MaxTier = FindMaxTier();

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

    /// 재료 카드로 주카드가 배울 스킬 하나를 고른다. 배울 게 없으면 null.
    ///
    /// 후보는 "재료 등급 이하 + 주카드 직업이 배울 수 있음 + 아직 안 배움"이고,
    /// 그 안에서 등급이 낮을수록 자주 나온다(가중치 = 재료등급 - 스킬등급 + 1).
    /// 그래서 6성 재료라야 영웅의 일격이 후보에 들어가고, 들어가도 가장 드물게 나온다.
    public static string Roll(CharacterSO main, int materialStars)
    {
        if (main == null) return null;

        int maxTier = Mathf.Clamp(materialStars, 1, MaxTier);

        int total = 0;
        for (int i = 0; i < All.Length; i++)
            total += WeightOf(All[i], main, maxTier);

        if (total <= 0) return null;

        int roll = Random.Range(0, total);
        int acc = 0;
        for (int i = 0; i < All.Length; i++)
        {
            acc += WeightOf(All[i], main, maxTier);
            if (roll < acc) return All[i].Id;
        }

        return null;
    }

    /// 이 재료로 주카드가 배울 수 있는 스킬이 하나라도 있는지. 합성 버튼을 잠글지 판단할 때 쓴다.
    public static bool HasCandidate(CharacterSO main, int materialStars)
    {
        if (main == null) return false;

        int maxTier = Mathf.Clamp(materialStars, 1, MaxTier);
        for (int i = 0; i < All.Length; i++)
            if (WeightOf(All[i], main, maxTier) > 0) return true;

        return false;
    }

    private static int WeightOf(SkillDefinition skill, CharacterSO main, int maxTier)
    {
        if (skill.Tier > maxTier) return 0;
        if (!skill.CanLearn(main.job)) return 0;
        if (main.HasSkill(skill.Id)) return 0;

        return maxTier - skill.Tier + 1;
    }

    private static int FindMaxTier()
    {
        int max = 1;
        for (int i = 0; i < All.Length; i++)
            if (All[i].Tier > max) max = All[i].Tier;

        return max;
    }
}
