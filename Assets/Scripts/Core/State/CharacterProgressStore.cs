using System.Collections.Generic;
using UnityEngine;

// 캐릭터의 성장 상태(레벨/경험치/능력치/배운 스킬/출전 횟수). CharacterSO의 대문자 프로퍼티와 정산·합성이 이것으로 본다.
public interface ICharacterProgress
{
    CharacterProgressStore.Entry Of(CharacterSO character);
    bool Has(CharacterSO character);
    int LevelOf(CharacterSO character);
    int BattlesFoughtOf(CharacterSO character);
    bool IsFirstBattle(CharacterSO character);
    void MarkBattleEntered(CharacterSO character);
    int ExpOf(CharacterSO character);
    int ExpToNextOf(CharacterSO character);
    int StrengthOf(CharacterSO character);
    int IntelligenceOf(CharacterSO character);
    int VitalityOf(CharacterSO character);
    int AgilityOf(CharacterSO character);
    void GainExp(CharacterSO character, int amount);
    IReadOnlyList<string> SkillsOf(CharacterSO character);
    int SkillCountOf(CharacterSO character);
    bool IsSkillFull(CharacterSO character);
    bool HasSkill(CharacterSO character, string skillId);
    bool LearnSkill(CharacterSO character, string skillId);
}

// 에셋은 "시작값 템플릿"이고, 굴러가는 값은 전부 여기 있다.
//
// 이게 생기기 전에는 CharacterSO의 필드를 직접 고쳤다. CharacterSO는 에셋이라 에디터에서 한 판 돌릴 때마다
// 원본 캐릭터가 영구히 레벨업하고 스킬을 달았다. 진짜 진행도는 세이브(CharacterSaveSection)가 파일로 남긴다.
//
// 기록이 없는 캐릭터는 에셋에 적힌 값을 시작값으로 본다(StressLedger.Get과 같은 규칙).
public sealed class CharacterProgressStore : ICharacterProgress
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

        // 전투에 나선 횟수. 1성의 생애 첫 전투를 가르는 데 쓴다(CharacterRules.PanicsThroughFirstBattle).
        // 이긴 판만이 아니라 나선 판을 센다 — 첫 전투에서 쓰러져도 첫 전투는 치른 것이다.
        public int BattlesFought;
    }

    private readonly Dictionary<CharacterSO, Entry> entries = new Dictionary<CharacterSO, Entry>();

    // 기록이 없으면 에셋의 시작값으로 하나 만들어 둔다. 이 함수를 거친 뒤로는
    // 그 캐릭터의 값이 전부 런타임 쪽에 있으므로 에셋은 더 이상 건드리지 않는다.
    public Entry Of(CharacterSO character)
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

    public bool Has(CharacterSO character) => character != null && entries.ContainsKey(character);

    // ---- 읽기 ------------------------------------------------------------

    public int LevelOf(CharacterSO character) => character != null ? Of(character).Level : 1;

    public int BattlesFoughtOf(CharacterSO character) => character != null ? Of(character).BattlesFought : 0;

    // 아직 한 번도 전투에 나서지 않았는가.
    public bool IsFirstBattle(CharacterSO character) => BattlesFoughtOf(character) == 0;

    // 전투에 나섰다. 스포너가 이 캐릭터를 전장에 세운 직후에 부른다(세운 순간의 판단이 끝난 뒤).
    public void MarkBattleEntered(CharacterSO character)
    {
        if (character == null) return;
        Of(character).BattlesFought++;
    }

    public int ExpOf(CharacterSO character) => character != null ? Of(character).Exp : 0;

    public int ExpToNextOf(CharacterSO character) => character != null ? Of(character).ExpToNext : 1;

    public int StrengthOf(CharacterSO character) => character != null ? Of(character).Strength : 0;

    public int IntelligenceOf(CharacterSO character) => character != null ? Of(character).Intelligence : 0;

    public int VitalityOf(CharacterSO character) => character != null ? Of(character).Vitality : 0;

    public int AgilityOf(CharacterSO character) => character != null ? Of(character).Agility : 0;

    // ---- 성장 ------------------------------------------------------------

    // 레벨업 시 체질(Constitution) 가중치로 4스탯을 나눠 올린다. 필요 경험치 곡선은 CharacterProgress.ExpForLevel.
    public void GainExp(CharacterSO character, int amount)
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
            entry.ExpToNext = CharacterProgress.ExpForLevel(entry.Level);
        }

        // 최대 레벨에 닿으면 남은 경험치는 버린다(더 올릴 곳이 없다).
        if (entry.Level >= maxLevel) entry.Exp = 0;
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

    public IReadOnlyList<string> SkillsOf(CharacterSO character)
    {
        return character != null ? Of(character).SkillIds : System.Array.Empty<string>();
    }

    public int SkillCountOf(CharacterSO character) => character != null ? Of(character).SkillIds.Count : 0;

    public bool IsSkillFull(CharacterSO character) => SkillCountOf(character) >= SkillCatalog.MaxSkillsPerCharacter;

    public bool HasSkill(CharacterSO character, string skillId)
    {
        if (character == null || string.IsNullOrEmpty(skillId)) return false;
        return Of(character).SkillIds.Contains(skillId);
    }

    // 이미 배웠거나 자리가 없으면 false. 부르는 쪽이 그 이유를 미리 확인한다.
    public bool LearnSkill(CharacterSO character, string skillId)
    {
        if (character == null || string.IsNullOrEmpty(skillId)) return false;
        if (IsSkillFull(character) || HasSkill(character, skillId)) return false;

        Of(character).SkillIds.Add(skillId);
        return true;
    }

    // ---- 세이브 연동 ------------------------------------------------------

    // 세이브에서 읽어온 값을 얹는다. 없는 값을 만들어내지 않도록 세이브만 부른다.
    public void Restore(CharacterSO character, int level, int exp, int expToNext,
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

    // 세이브에 남은 출전 횟수를 얹는다. 이 값이 없던 시절의 세이브는 0으로 읽혀, 이미 성장한
    // 캐릭터도 아직 첫 전투를 치르지 않은 것으로 본다(2026-09 결정).
    public void RestoreBattlesFought(CharacterSO character, int battlesFought)
    {
        if (character == null) return;
        Of(character).BattlesFought = Mathf.Max(0, battlesFought);
    }

    public void Clear() => entries.Clear();
}
