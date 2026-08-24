using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 성장 로직 — 예전에는 CharacterSO 에셋을 직접 고쳤고, 그래서 테스트를 돌리면
// 프로젝트의 캐릭터 에셋이 실제로 레벨업하는 구조라 검증 자체가 불가능했다.
// CharacterProgress로 옮긴 지금은 런타임 값만 건드리므로 마음 놓고 돌릴 수 있다.
public class CharacterProgressTests
{
    private CharacterSO character;

    private static CharacterSO NewCharacter(int stars, float s = 1f, float i = 1f, float v = 1f, float a = 1f)
    {
        var so = ScriptableObject.CreateInstance<CharacterSO>();
        so.name = "TestCharacter";
        so.characterName = "테스트";
        so.starCount = stars;
        so.level = 1;
        so.exp = 0;
        so.expToNext = 10;
        so.constitution = new Constitution
        {
            strengthGrowth = s,
            intelligenceGrowth = i,
            vitalityGrowth = v,
            agilityGrowth = a,
        };
        so.stats = new VisibleStats { strength = 5, intelligence = 5, vitality = 5, agility = 5 };
        return so;
    }

    [SetUp]
    public void SetUp()
    {
        CharacterProgress.Clear();
        character = NewCharacter(3);
    }

    [TearDown]
    public void TearDown()
    {
        CharacterProgress.Clear();
        if (character != null) Object.DestroyImmediate(character);
    }

    [Test]
    public void 기록이_없으면_에셋의_시작값을_그대로_읽는다()
    {
        Assert.AreEqual(1, character.Level);
        Assert.AreEqual(5, character.Strength);
        Assert.AreEqual(5, character.Agility);
    }

    [Test]
    public void 경험치를_얻어도_에셋은_바뀌지_않는다()
    {
        character.GainExp(1000);

        Assert.Greater(character.Level, 1, "런타임 레벨은 올라야 한다");
        Assert.AreEqual(1, character.level, "에셋의 시작값은 그대로여야 한다");
        Assert.AreEqual(5, character.stats.strength, "에셋의 스탯도 그대로여야 한다");
    }

    [Test]
    public void 레벨업하면_필요_경험치가_늘어난다()
    {
        // 1레벨 → 2레벨에 10, 2 → 3에 15가 든다.
        character.GainExp(10);
        Assert.AreEqual(2, character.Level);
        Assert.AreEqual(0, character.Exp);
        Assert.AreEqual(15, character.ExpToNext);
    }

    [Test]
    public void 남은_경험치는_다음_레벨로_이월된다()
    {
        character.GainExp(13);

        Assert.AreEqual(2, character.Level);
        Assert.AreEqual(3, character.Exp);
    }

    [Test]
    public void 레벨업_스탯_분배의_합은_항상_정확하다()
    {
        // 가중치를 일부러 나누어떨어지지 않게 준다. 반올림으로 총합이 새면 여기서 잡힌다.
        Object.DestroyImmediate(character);
        character = NewCharacter(3, s: 1f, i: 1f, v: 1f, a: 0f);

        int before = character.Strength + character.Intelligence + character.Vitality + character.Agility;
        int perLevel = CharacterRules.StatPointsPerLevel(3);

        character.GainExp(10); // 정확히 1레벨업

        int after = character.Strength + character.Intelligence + character.Vitality + character.Agility;
        Assert.AreEqual(before + perLevel, after);
    }

    [Test]
    public void 성장_가중치가_전부_0이면_고르게_나눈다()
    {
        Object.DestroyImmediate(character);
        character = NewCharacter(3, 0f, 0f, 0f, 0f);

        int perLevel = CharacterRules.StatPointsPerLevel(3);
        character.GainExp(10);

        int gained = character.Strength - 5;
        Assert.Greater(gained, 0, "가중치가 없어도 스탯은 올라야 한다");
        Assert.AreEqual(perLevel,
            (character.Strength - 5) + (character.Intelligence - 5) + (character.Vitality - 5) + (character.Agility - 5));
    }

    [Test]
    public void 별_등급의_한계_레벨을_넘지_않는다()
    {
        Object.DestroyImmediate(character);
        character = NewCharacter(1); // 1성 = 10레벨 한계

        character.GainExp(999999);

        Assert.AreEqual(CharacterRules.MaxLevelForStars(1), character.Level);
        Assert.AreEqual(0, character.Exp, "한계에 닿으면 남은 경험치는 버린다");
    }

    [Test]
    public void 스킬은_중복되지_않고_상한을_넘지_않는다()
    {
        Assert.IsTrue(character.LearnSkill("skill_a"));
        Assert.IsFalse(character.LearnSkill("skill_a"), "같은 스킬은 두 번 배우지 않는다");
        Assert.AreEqual(1, character.SkillCount);

        for (int i = 0; i < SkillCatalog.MaxSkillsPerCharacter + 3; i++) character.LearnSkill("skill_" + i);

        Assert.AreEqual(SkillCatalog.MaxSkillsPerCharacter, character.SkillCount);
        Assert.IsTrue(character.IsSkillFull);
    }

    [Test]
    public void 스킬을_배워도_에셋은_바뀌지_않는다()
    {
        character.LearnSkill("skill_a");

        Assert.AreEqual(1, character.SkillCount);
        Assert.AreEqual(0, character.skillIds.Count, "에셋의 시작 스킬 목록은 그대로여야 한다");
    }

    [Test]
    public void 세이브에서_복원한_값이_그대로_읽힌다()
    {
        CharacterProgress.Restore(character, 7, 3, 40, 11, 12, 13, 14, new List<string> { "s1", "s2" });

        Assert.AreEqual(7, character.Level);
        Assert.AreEqual(3, character.Exp);
        Assert.AreEqual(40, character.ExpToNext);
        Assert.AreEqual(11, character.Strength);
        Assert.AreEqual(12, character.Intelligence);
        Assert.AreEqual(13, character.Vitality);
        Assert.AreEqual(14, character.Agility);
        Assert.AreEqual(2, character.SkillCount);
        Assert.IsTrue(character.HasSkill("s2"));
    }

    [Test]
    public void 복원은_이전_스킬_목록을_남기지_않는다()
    {
        character.LearnSkill("stale");
        CharacterProgress.Restore(character, 1, 0, 10, 0, 0, 0, 0, new List<string> { "fresh" });

        Assert.IsFalse(character.HasSkill("stale"));
        Assert.AreEqual(1, character.SkillCount);
    }
}
