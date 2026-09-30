using System.Collections.Generic;
using UnityEngine;

// 캐릭터의 성장 상태(레벨/경험치/능력치/배운 스킬)를 전투 밖에서 들고 있는 곳.
//
// 필요 경험치 곡선(ExpForLevel)은 여기 있고, 캐릭터마다 굴러가는 값은 CharacterProgressStore가 든다.
// 생성자로 의존성을 받을 수 있는 코드는 ICharacterProgress(GameServices.Progress)를 받는다.
public static class CharacterProgress
{
    private static CharacterProgressStore State => GameServices.ProgressState;

    public static CharacterProgressStore.Entry Of(CharacterSO character) => State.Of(character);

    public static bool Has(CharacterSO character) => State.Has(character);

    // ---- 읽기 ------------------------------------------------------------

    public static int LevelOf(CharacterSO character) => State.LevelOf(character);
    public static int BattlesFoughtOf(CharacterSO character) => State.BattlesFoughtOf(character);
    public static bool IsFirstBattle(CharacterSO character) => State.IsFirstBattle(character);
    public static void MarkBattleEntered(CharacterSO character) => State.MarkBattleEntered(character);
    public static int ExpOf(CharacterSO character) => State.ExpOf(character);
    public static int ExpToNextOf(CharacterSO character) => State.ExpToNextOf(character);
    public static int StrengthOf(CharacterSO character) => State.StrengthOf(character);
    public static int IntelligenceOf(CharacterSO character) => State.IntelligenceOf(character);
    public static int VitalityOf(CharacterSO character) => State.VitalityOf(character);
    public static int AgilityOf(CharacterSO character) => State.AgilityOf(character);

    // ---- 성장 ------------------------------------------------------------

    public static void GainExp(CharacterSO character, int amount) => State.GainExp(character, amount);

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

    // ---- 스킬 ------------------------------------------------------------

    public static IReadOnlyList<string> SkillsOf(CharacterSO character) => State.SkillsOf(character);
    public static int SkillCountOf(CharacterSO character) => State.SkillCountOf(character);
    public static bool IsSkillFull(CharacterSO character) => State.IsSkillFull(character);
    public static bool HasSkill(CharacterSO character, string skillId) => State.HasSkill(character, skillId);
    public static bool LearnSkill(CharacterSO character, string skillId) => State.LearnSkill(character, skillId);

    // ---- 세이브 연동 ------------------------------------------------------

    public static void Restore(CharacterSO character, int level, int exp, int expToNext,
        int strength, int intelligence, int vitality, int agility, IReadOnlyList<string> skillIds) =>
        State.Restore(character, level, exp, expToNext, strength, intelligence, vitality, agility, skillIds);

    public static void RestoreBattlesFought(CharacterSO character, int battlesFought) =>
        State.RestoreBattlesFought(character, battlesFought);

    public static void Clear() => State.Clear();
}
