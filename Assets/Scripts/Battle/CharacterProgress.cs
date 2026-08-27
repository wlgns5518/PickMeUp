using System.Collections.Generic;
using UnityEngine;

// 캐릭터의 성장 상태(레벨/경험치/능력치/배운 스킬)를 전투 밖에서 들고 있는 곳.
//
// 이 파일이 생기기 전에는 CharacterSO의 필드를 직접 고쳤다. CharacterSO는 에셋이라
// 에디터에서 한 판 돌릴 때마다 원본 캐릭터가 영구히 레벨업하고 스킬을 달았다.
// RosterBaseline이 플레이를 나갈 때 되돌려 주긴 했지만, 그건 "더럽힌 뒤 닦는" 방식이라
// 에디터가 강제 종료되거나 Capture가 한 번 빠지면 그대로 남는다.
//
// PartyRoster(사망 기록) / OwnedRoster(보유 명단) / CharacterStress(스트레스)가 이미 쓰고 있는
// 규칙을 성장에도 그대로 적용한다: 에셋은 "시작값 템플릿"이고, 굴러가는 값은 전부 여기 있다.
// 진짜 진행도는 SaveSystem이 파일로 남긴다.
//
// 기록이 없는 캐릭터는 에셋에 적힌 값을 시작값으로 본다(CharacterStress.Get과 같은 규칙).
public static class CharacterProgress
{
    // 한 캐릭터의 굴러가는 값 전부. CharacterSO의 같은 이름 필드는 이제 시작값으로만 읽는다.
    public class Entry
    {
        public int Level;
        public int Exp;
        public int ExpToNext;
        public int Strength;
        public int Intelligence;
        public int Vitality;
        public int Agility;
        public readonly List<string> SkillIds = new List<string>();
    }

    private static readonly Dictionary<CharacterSO, Entry> entries = new Dictionary<CharacterSO, Entry>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 이전 플레이 값이 남지 않도록 비운다.
        // 실제 값은 세이브에서 다시 읽어 온다.
        entries.Clear();
    }

    // 기록이 없으면 에셋의 시작값으로 하나 만들어 둔다. 이 함수를 거친 뒤로는
    // 그 캐릭터의 값이 전부 런타임 쪽에 있으므로 에셋은 더 이상 건드리지 않는다.
    public static Entry Of(CharacterSO character)
    {
        if (character == null) return null;

        if (entries.TryGetValue(character, out Entry existing)) return existing;

        var entry = new Entry
        {
            Level = Mathf.Max(1, character.level),
            Exp = Mathf.Max(0, character.exp),
            ExpToNext = Mathf.Max(1, character.expToNext),
            Strength = character.stats != null ? character.stats.strength : 0,
            Intelligence = character.stats != null ? character.stats.intelligence : 0,
            Vitality = character.stats != null ? character.stats.vitality : 0,
            Agility = character.stats != null ? character.stats.agility : 0,
        };

        if (character.skillIds != null) entry.SkillIds.AddRange(character.skillIds);

        entries[character] = entry;
        return entry;
    }

    public static bool Has(CharacterSO character) => character != null && entries.ContainsKey(character);

    // ---- 읽기 ------------------------------------------------------------

    public static int LevelOf(CharacterSO character) => character != null ? Of(character).Level : 1;

    public static int ExpOf(CharacterSO character) => character != null ? Of(character).Exp : 0;

    public static int ExpToNextOf(CharacterSO character) => character != null ? Of(character).ExpToNext : 1;

    public static int StrengthOf(CharacterSO character) => character != null ? Of(character).Strength : 0;

    public static int IntelligenceOf(CharacterSO character) => character != null ? Of(character).Intelligence : 0;

    public static int VitalityOf(CharacterSO character) => character != null ? Of(character).Vitality : 0;

    public static int AgilityOf(CharacterSO character) => character != null ? Of(character).Agility : 0;

    // ---- 성장 ------------------------------------------------------------

    // CharacterSO.GainExp에서 옮겨왔다. 레벨업 시 체질(Constitution) 가중치로 4스탯을 나눠 올린다.
    public static void GainExp(CharacterSO character, int amount)
    {
        if (character == null || amount <= 0) return;

        Entry entry = Of(character);
        int maxLevel = character.MaxLevel;

        entry.Exp += amount;
        while (entry.Exp >= entry.ExpToNext && entry.Level < maxLevel)
        {
            entry.Exp -= entry.ExpToNext;
            entry.Level++;
            ApplyLevelUpStats(character, entry);
            entry.ExpToNext = ExpForLevel(entry.Level);
        }

        // 최대 레벨에 닿으면 남은 경험치는 버린다(더 올릴 곳이 없다).
        if (entry.Level >= maxLevel) entry.Exp = 0;
    }

    // ---- 필요 경험치 곡선 --------------------------------------------------

    private const int BaseExp = 10;              // 1→2 레벨에 드는 경험치. 에셋 시작값과 같아야 한다.
    private const float Growth = 1.35f;          // 곡선의 가파름. 1이면 예전과 같은 직선이 된다.
    private const int WallEvery = 10;            // 이 레벨마다 벽을 하나씩 더 세운다.
    private const float WallMultiplier = 1.15f;  // 벽 하나가 필요 경험치에 곱하는 배수.

    /// level에서 level+1로 올라가는 데 필요한 경험치.
    ///
    /// 예전에는 10 + (level-1)*15, 곧은 직선이었다. 전투 한 판이 주는 경험치는 거의 고정인데
    /// 필요치가 일정한 간격으로만 늘면 레벨 2→3에 드는 판수와 40→41에 드는 판수가 몇 배밖에
    /// 차이 나지 않는다. 성장이 느려지는 구간이 없으니 별 등급이 상한을 10/30/50/70/99로
    /// 갈라 놓은 것도 체감되지 않았다 — 어차피 다들 같은 속도로 올랐다.
    ///
    ///     필요 경험치 = 10 * level^1.35, 여기에 10레벨마다 1.15배짜리 벽이 하나씩 더 붙는다.
    ///
    /// 지수가 완만한 오르막을 만들고, 벽이 "여기서부터는 다른 구간"이라는 마디를 만든다.
    /// 지수만 키우면 후반이 통째로 막히고, 벽만 두면 구간 안이 다시 직선이 된다.
    ///
    /// level 1에서 정확히 10이 나오도록 맞춰 뒀다. CharacterSO.expToNext의 시작값과 어긋나면
    /// 세이브가 없는 첫 캐릭터만 첫 레벨업 기준이 달라진다.
    ///
    /// 대략적인 필요치:  1→2 10,  9→10 194,  29→30 1,305,  49→50 3,439,  98→99 17,163
    public static int ExpForLevel(int level)
    {
        if (level < 1) level = 1;

        float curve = BaseExp * Mathf.Pow(level, Growth);
        float wall = Mathf.Pow(WallMultiplier, (level - 1) / WallEvery);

        return Mathf.Max(1, Mathf.RoundToInt(curve * wall));
    }

    private static void ApplyLevelUpStats(CharacterSO character, Entry entry)
    {
        int total = CharacterRules.StatPointsPerLevel(character.starCount);
        Constitution constitution = character.constitution;

        float wS = constitution != null ? Mathf.Max(0f, constitution.strengthGrowth) : 1f;
        float wI = constitution != null ? Mathf.Max(0f, constitution.intelligenceGrowth) : 1f;
        float wV = constitution != null ? Mathf.Max(0f, constitution.vitalityGrowth) : 1f;
        float wA = constitution != null ? Mathf.Max(0f, constitution.agilityGrowth) : 1f;

        float sum = wS + wI + wV + wA;
        if (sum <= 0f) { wS = wI = wV = wA = 1f; sum = 4f; }

        int s = Mathf.RoundToInt(total * (wS / sum));
        int i = Mathf.RoundToInt(total * (wI / sum));
        int v = Mathf.RoundToInt(total * (wV / sum));
        int a = total - s - i - v; // 나머지는 민첩이 받는다 — 합계가 정확히 total로 떨어지도록.

        entry.Strength += s;
        entry.Intelligence += i;
        entry.Vitality += v;
        entry.Agility += a;
    }

    // ---- 스킬 ------------------------------------------------------------
    // 두 갈래로 늘어난다: 합성(SkillCatalog.Roll)과 조건 해금(SkillUnlocks).
    // 어느 쪽이든 결국 LearnSkill로 들어오므로 여기서는 둘을 구분하지 않는다.
    // CharacterSO.skillIds는 이제 시작값으로만 읽는다.

    public static IReadOnlyList<string> SkillsOf(CharacterSO character)
    {
        return character != null ? Of(character).SkillIds : System.Array.Empty<string>();
    }

    public static int SkillCountOf(CharacterSO character) => character != null ? Of(character).SkillIds.Count : 0;

    public static bool IsSkillFull(CharacterSO character)
    {
        return SkillCountOf(character) >= SkillCatalog.MaxSkillsPerCharacter;
    }

    public static bool HasSkill(CharacterSO character, string skillId)
    {
        if (character == null || string.IsNullOrEmpty(skillId)) return false;
        return Of(character).SkillIds.Contains(skillId);
    }

    // 이미 배웠거나 자리가 없으면 false. 부르는 쪽이 그 이유를 미리 확인한다.
    public static bool LearnSkill(CharacterSO character, string skillId)
    {
        if (character == null || string.IsNullOrEmpty(skillId)) return false;
        if (IsSkillFull(character) || HasSkill(character, skillId)) return false;

        Of(character).SkillIds.Add(skillId);
        return true;
    }

    // ---- 세이브 연동 ------------------------------------------------------

    // 세이브에서 읽어온 값을 얹는다. 없는 값을 만들어내지 않도록 SaveSystem만 부른다.
    public static void Restore(CharacterSO character, int level, int exp, int expToNext,
        int strength, int intelligence, int vitality, int agility, IReadOnlyList<string> skillIds)
    {
        if (character == null) return;

        Entry entry = Of(character);
        entry.Level = Mathf.Max(1, level);
        entry.Exp = Mathf.Max(0, exp);
        entry.ExpToNext = Mathf.Max(1, expToNext);
        entry.Strength = strength;
        entry.Intelligence = intelligence;
        entry.Vitality = vitality;
        entry.Agility = agility;

        entry.SkillIds.Clear();
        if (skillIds == null) return;

        for (int i = 0; i < skillIds.Count; i++)
        {
            if (!string.IsNullOrEmpty(skillIds[i])) entry.SkillIds.Add(skillIds[i]);
        }
    }

    public static void Clear() => entries.Clear();
}
