using System;
using System.Collections.Generic;
using UnityEngine;

// 조건이 맞으면 스킬을 저절로 열어주는 곳.
//
// 스킬을 얻는 길은 두 갈래다.
//   1) 합성 — 재료 카드를 태워 SkillCatalog.Roll이 운으로 하나 뽑아준다.
//   2) 조건 — 정해진 조건을 채우면 뽑기와 무관하게 열린다. 이 파일이 그쪽이다.
//
// 합성만 있던 시절에는 캐릭터가 어떻게 굴렀든 결과가 같았다. 100판을 뛴 검사와 방금 뽑은 검사가
// 재료만 같으면 같은 확률로 같은 스킬을 받았다는 뜻이다. 조건 해금은 그 반대다 —
// "레벨 20을 넘겼다", "힘이 40을 넘겼다" 같은 사실 자체가 열쇠가 된다.
//
// 조건은 SkillCatalog의 표에 스킬과 나란히 적는다(아직 조건이 붙은 스킬은 없다).
// 조건이 붙은 스킬은 합성 후보에서 빠진다 — 조건을 걸어 둔 스킬이 합성으로도 굴러 나오면
// 조건이 있으나 마나가 되기 때문이다(SkillCatalog.WeightOf 참조).
public delegate bool SkillUnlockCondition(CharacterSO character);

public static class SkillUnlocks
{
    // 새 스킬이 열린 순간. 정산 화면 말고 다른 곳(마을 UI 등)에서도 알림을 띄우고 싶을 때 쓴다.
    // BattleManager와 같은 이유로 정적 이벤트다 — 도메인 리로드 뒤에도 다시 붙을 수 있어야 한다.
    public static event Action<CharacterSO, string> OnSkillUnlocked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticEvents() => OnSkillUnlocked = null;

    /// 지금 조건이 맞는 스킬을 전부 열어준다. 새로 열린 개수를 돌려주고,
    /// unlocked를 넘기면 그 목록에 열린 스킬 id를 담아 준다(결과창이 읽는다).
    ///
    /// 값이 바뀐 직후에 부르면 된다 — 조건은 상태를 보는 것이지 사건을 듣는 것이 아니라서,
    /// 몇 번을 다시 불러도 이미 배운 스킬이 두 번 붙지는 않는다.
    public static int Evaluate(CharacterSO character, List<string> unlocked = null)
    {
        if (character == null) return 0;

        IReadOnlyList<SkillDefinition> all = SkillCatalog.AllSkills;
        int count = 0;

        for (int i = 0; i < all.Count; i++)
        {
            SkillDefinition skill = all[i];
            if (!skill.IsConditional) continue;
            if (!skill.CanLearn(character.job)) continue;
            if (character.HasSkill(skill.Id)) continue;

            // 자리가 없으면 더 볼 것도 없다. 조건은 계속 참으로 남으므로 자리가 생기면 그때 열린다 —
            // 조건을 채웠다는 사실이 여기서 사라지지는 않는다.
            if (character.IsSkillFull) break;

            bool met;
            try
            {
                met = skill.Unlock(character);
            }
            catch (Exception e)
            {
                // 조건식 하나가 터졌다고 전투 정산 전체가 멈추면 안 된다.
                Debug.LogError($"[SkillUnlocks] '{skill.Id}' 조건을 확인하다 실패했습니다: {e.Message}");
                continue;
            }

            if (!met || !character.LearnSkill(skill.Id)) continue;

            count++;
            unlocked?.Add(skill.Id);
            OnSkillUnlocked?.Invoke(character, skill.Id);
        }

        return count;
    }

    /// 명단 전체를 한 번에 훑는다. 세이브를 막 읽어온 직후처럼
    /// "그동안 조건을 채웠는데 아직 열리지 않은" 스킬이 남아 있을 수 있는 자리에서 쓴다.
    public static int EvaluateAll(IReadOnlyList<CharacterSO> characters)
    {
        if (characters == null) return 0;

        int count = 0;
        for (int i = 0; i < characters.Count; i++) count += Evaluate(characters[i]);

        return count;
    }
}

// 조건을 적을 때 쓰는 조각들. SkillCatalog의 표에서 이렇게 쓴다:
//
//     new SkillDefinition("second_wind", "재기", "...", 3, MeleeJobs,
//         SkillUnlock.All(SkillUnlock.AtLevel(20), SkillUnlock.Vitality(40))),
//
// 여기 없는 조건이 필요하면 람다를 그대로 적어도 된다. 다만 조건은 "지금 상태"만 보고
// 판단할 수 있어야 한다 — 한 판 동안의 활약처럼 흘러가 버리는 값은 어딘가에 누적으로
// 남긴 뒤에야 조건이 될 수 있다.
public static class SkillUnlock
{
    public static SkillUnlockCondition AtLevel(int level) =>
        c => c != null && c.Level >= level;

    public static SkillUnlockCondition Strength(int value) =>
        c => c != null && c.Strength >= value;

    public static SkillUnlockCondition Intelligence(int value) =>
        c => c != null && c.Intelligence >= value;

    public static SkillUnlockCondition Vitality(int value) =>
        c => c != null && c.Vitality >= value;

    public static SkillUnlockCondition Agility(int value) =>
        c => c != null && c.Agility >= value;

    public static SkillUnlockCondition Stars(int stars) =>
        c => c != null && c.starCount >= stars;

    public static SkillUnlockCondition Affinity(MagicAffinity affinity) =>
        c => c != null && c.Affinity == affinity;

    // 선행 스킬. 합성으로 얻은 스킬도 조건이 될 수 있다 — 두 길이 이렇게 이어진다.
    public static SkillUnlockCondition AfterSkills(params string[] skillIds)
    {
        return c =>
        {
            if (c == null || skillIds == null) return false;
            for (int i = 0; i < skillIds.Length; i++)
                if (!c.HasSkill(skillIds[i])) return false;

            return true;
        };
    }

    public static SkillUnlockCondition All(params SkillUnlockCondition[] conditions)
    {
        return c =>
        {
            if (conditions == null || conditions.Length == 0) return false;
            for (int i = 0; i < conditions.Length; i++)
                if (conditions[i] == null || !conditions[i](c)) return false;

            return true;
        };
    }

    public static SkillUnlockCondition Any(params SkillUnlockCondition[] conditions)
    {
        return c =>
        {
            if (conditions == null) return false;
            for (int i = 0; i < conditions.Length; i++)
                if (conditions[i] != null && conditions[i](c)) return true;

            return false;
        };
    }
}
