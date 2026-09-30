using UnityEngine;

// 새로 뽑은 캐릭터의 속을 굴리는 곳 — 직업, 성격, 등급, 능력치, 체질.
//
// 그림(초상화)이나 이름과는 상관이 없다. 예전에는 이 표와 굴림이 Meshy 통신과 한 클래스
// (MeshyCharacterGenerator)에 섞여 있어서, 능력치 곡선을 고치려면 HTTP 코드 사이를 헤집어야 했다.
public static class CharacterRoller
{
    // 직업 하나에 딸린 것을 한 줄로 묶는다. 예전에는 enum, 영어 프롬프트, 가중치가 세 배열로 흩어져 있어
    // 한 줄을 빼먹으면 순서가 밀려 검사가 궁수 그림으로 나왔다.
    public readonly struct JobEntry
    {
        public readonly JobType job;
        public readonly string portraitWord;   // 초상화 프롬프트에 들어갈 영어 한 단어
        public readonly int weight;

        public JobEntry(JobType job, string portraitWord, int weight)
        {
            this.job = job;
            this.portraitWord = portraitWord;
            this.weight = weight;
        }
    }

    public readonly struct Trait
    {
        public readonly string korean;
        public readonly string english;

        public Trait(string korean, string english)
        {
            this.korean = korean;
            this.english = english;
        }
    }

    private static readonly JobEntry[] Jobs =
    {
        new JobEntry(JobType.Melee,      "swordsman",     12),
        new JobEntry(JobType.Mage,       "mage",          12),
        new JobEntry(JobType.Archer,     "archer",        12),
        new JobEntry(JobType.Assassin,   "assassin",      12),
        new JobEntry(JobType.Tank,       "tank knight",   12),
        new JobEntry(JobType.Support,    "priest healer", 12),
        new JobEntry(JobType.Lancer,     "spearman",      12),
        new JobEntry(JobType.Carpenter,  "carpenter",      6),
        new JobEntry(JobType.Cook,       "cook",           5),
        new JobEntry(JobType.Blacksmith, "blacksmith",     6),
        new JobEntry(JobType.Tanner,     "leatherworker",  5),
    };

    private static readonly Trait[] Traits =
    {
        new Trait("냉소적인",   "cynical"),
        new Trait("낙천적인",   "cheerful"),
        new Trait("고독한",     "lonely"),
        new Trait("충직한",     "loyal"),
        new Trait("야망 있는",  "ambitious"),
        new Trait("수줍은",     "shy"),
        new Trait("비밀스러운", "mysterious"),
    };

    public static JobEntry RollJob()
    {
        int total = 0;
        for (int i = 0; i < Jobs.Length; i++) total += Jobs[i].weight;
        if (total <= 0) return Jobs[0];

        int roll = Random.Range(0, total);
        int acc = 0;
        for (int i = 0; i < Jobs.Length; i++)
        {
            acc += Jobs[i].weight;
            if (roll < acc) return Jobs[i];
        }
        return Jobs[Jobs.Length - 1];
    }

    public static Trait RollTrait() => Traits[Random.Range(0, Traits.Length)];

    /// 새 캐릭터 한 명의 뼈대를 만든다. 이름과 초상화는 비어 있다.
    /// forcedStars가 1 이상이면 그 등급으로 고정한다 — 소환소가 확률표로 굴린 결과를 넘긴다.
    public static CharacterSO Create(JobEntry job, int forcedStars)
    {
        CharacterSO so = ScriptableObject.CreateInstance<CharacterSO>();
        // 식별자는 무엇보다 먼저 박는다. 에셋으로 저장할 때 이 값이 같이 실려야
        // 그 id로 굽는 몸(CharacterModelStore)과 세이브 기록이 에디터를 다시 켜도 이어진다.
        so.EnsureId();
        // 등급은 소환 확률표가 정한다. 넘겨받은 값이 없으면 고급 소환과 같은 확률로 굴린다.
        so.starCount = forcedStars > 0 ? Mathf.Clamp(forcedStars, 1, 7) : SummonTable.RollStars(SummonKind.Paid);
        so.level = 1; so.exp = 0; so.expToNext = 10;
        so.job = job.job;
        // 마법사는 태어날 때 속성이 하나 정해지고 평생 바뀌지 않는다.
        // 그래서 같은 마법사라도 어느 속성을 뽑았느냐가 그 캐릭터의 쓸모를 가른다 —
        // 소환의 결과가 등급뿐 아니라 속성으로도 갈리는 셈이다.
        so.affinity = so.job == JobType.Mage ? SpellCatalog.RollAffinity() : MagicAffinity.None;
        return so;
    }

    /// 이름이 정해진 뒤에 채우는 것들 — 설명, 체질, 능력치, 에셋 이름.
    public static void Finish(CharacterSO so, Trait trait)
    {
        // 마법사는 속성이 곧 정체성이라 설명에도 적는다 — "빙결 마법사"와 "화염 마법사"는 다른 캐릭터다.
        string jobLabel = so.job == JobType.Mage
            ? $"{SpellCatalog.Korean(so.affinity)} {CharacterRules.Korean(so.job)}"
            : CharacterRules.Korean(so.job);
        so.description  = $"{trait.korean} 인간 {jobLabel}";
        so.constitution = RollConstitution(so.job);
        RollInitialStats(so);
        so.name = $"{so.characterName} ({so.starCount}★)";
    }

    private static void RollInitialStats(CharacterSO so)
    {
        int baseV = 5 + so.starCount * 2;
        so.stats.strength     = baseV + Random.Range(0, 4);
        so.stats.intelligence = baseV + Random.Range(0, 4);
        so.stats.vitality     = baseV + Random.Range(0, 4);
        so.stats.agility      = baseV + Random.Range(0, 4);

        int hidden = 5 + so.starCount;
        so.hiddenStats.diligence = hidden + Random.Range(-2, 3);
        so.hiddenStats.stamina   = hidden + Random.Range(-2, 3);
        so.hiddenStats.stress    = Random.Range(0, 10);
        so.hiddenStats.mental = CharacterRules.IsFragileMental(so.starCount)
            ? Random.Range(1, 4)
            : hidden + Random.Range(-1, 4);
        so.hiddenStats.skill  = hidden + Random.Range(-2, 3);
        so.hiddenStats.body   = hidden + Random.Range(-1, 4);
        so.hiddenStats.sanity = hidden + Random.Range(-1, 4);
    }

    private static Constitution RollConstitution(JobType job)
    {
        Constitution c = new Constitution { name = "균형" };
        switch (job)
        {
            case JobType.Melee:      c.name = "근육질"; c.strengthGrowth = 1.6f; c.vitalityGrowth = 1.2f; c.agilityGrowth = 0.8f; c.intelligenceGrowth = 0.4f; break;
            case JobType.Mage:       c.name = "현자";   c.intelligenceGrowth = 1.8f; c.agilityGrowth = 0.6f; c.vitalityGrowth = 0.6f; c.strengthGrowth = 0.4f; break;
            case JobType.Archer:     c.name = "민첩한"; c.agilityGrowth = 1.6f; c.strengthGrowth = 1f;   c.intelligenceGrowth = 0.8f; c.vitalityGrowth = 0.6f; break;
            case JobType.Assassin:   c.name = "그림자"; c.agilityGrowth = 1.8f; c.strengthGrowth = 1.0f; c.intelligenceGrowth = 0.7f; c.vitalityGrowth = 0.5f; break;
            case JobType.Tank:       c.name = "강건한"; c.vitalityGrowth = 1.8f; c.strengthGrowth = 1.2f; c.intelligenceGrowth = 0.5f; c.agilityGrowth = 0.5f; break;
            case JobType.Support:    c.name = "조화";   c.intelligenceGrowth = 1.4f; c.vitalityGrowth = 1.0f; c.agilityGrowth = 0.8f; c.strengthGrowth = 0.8f; break;
            // 창수는 검사보다 팔이 길고 자세가 낮다 — 힘보다 균형과 지구력에 가깝게 잡았다.
            case JobType.Lancer:     c.name = "곧은";   c.strengthGrowth = 1.3f; c.agilityGrowth = 1.2f; c.vitalityGrowth = 1.0f; c.intelligenceGrowth = 0.5f; break;
            case JobType.Carpenter:  c.name = "근면";   c.strengthGrowth = 1.2f; c.vitalityGrowth = 1.1f; c.agilityGrowth = 0.9f; c.intelligenceGrowth = 0.8f; break;
            case JobType.Cook:       c.name = "온화";   c.vitalityGrowth = 1.2f; c.intelligenceGrowth = 1.1f; c.agilityGrowth = 0.9f; c.strengthGrowth = 0.8f; break;
            case JobType.Blacksmith: c.name = "강인";   c.strengthGrowth = 1.5f; c.vitalityGrowth = 1.3f; c.agilityGrowth = 0.6f; c.intelligenceGrowth = 0.6f; break;
            case JobType.Tanner:     c.name = "손재주"; c.agilityGrowth = 1.3f; c.intelligenceGrowth = 1.1f; c.strengthGrowth = 0.9f; c.vitalityGrowth = 0.7f; break;
        }
        c.strengthGrowth     *= Random.Range(0.85f, 1.15f);
        c.intelligenceGrowth *= Random.Range(0.85f, 1.15f);
        c.vitalityGrowth     *= Random.Range(0.85f, 1.15f);
        c.agilityGrowth      *= Random.Range(0.85f, 1.15f);
        return c;
    }
}
