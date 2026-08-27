using NUnit.Framework;
using UnityEngine;

// 조건 해금의 조각들. 표에 아직 조건이 붙은 스킬은 없지만, 조건을 적을 때 쓰는 도구가
// 먼저 맞아야 나중에 스킬을 추가하면서 조건을 잘못 걸어도 여기서 걸린다.
public class SkillUnlockTests
{
    private CharacterSO character;

    [SetUp]
    public void SetUp()
    {
        CharacterProgress.Clear();

        character = ScriptableObject.CreateInstance<CharacterSO>();
        character.name = "TestCharacter";
        character.characterName = "테스트";
        character.starCount = 3;
        character.level = 1;
        character.exp = 0;
        character.expToNext = 10;
        character.job = JobType.Melee;
        character.constitution = new Constitution
        {
            strengthGrowth = 1f, intelligenceGrowth = 1f, vitalityGrowth = 1f, agilityGrowth = 1f,
        };
        character.stats = new VisibleStats { strength = 5, intelligence = 5, vitality = 5, agility = 5 };
    }

    [TearDown]
    public void TearDown()
    {
        CharacterProgress.Clear();
        if (character != null) Object.DestroyImmediate(character);
    }

    [Test]
    public void 레벨_조건은_그_레벨에_닿아야_참이_된다()
    {
        SkillUnlockCondition atFive = SkillUnlock.AtLevel(5);

        Assert.IsFalse(atFive(character));

        character.GainExp(10000);
        Assert.GreaterOrEqual(character.Level, 5);
        Assert.IsTrue(atFive(character));
    }

    [Test]
    public void 선행_스킬_조건은_전부_배웠을_때만_참이다()
    {
        SkillUnlockCondition after = SkillUnlock.AfterSkills("power_strike", "counter");

        Assert.IsFalse(after(character));

        character.LearnSkill("power_strike");
        Assert.IsFalse(after(character), "하나만 배운 상태로는 열리지 않는다");

        character.LearnSkill("counter");
        Assert.IsTrue(after(character));
    }

    [Test]
    public void All은_전부_Any는_하나만_맞으면_된다()
    {
        SkillUnlockCondition yes = SkillUnlock.AtLevel(1);
        SkillUnlockCondition no = SkillUnlock.AtLevel(99);

        Assert.IsTrue(SkillUnlock.All(yes, yes)(character));
        Assert.IsFalse(SkillUnlock.All(yes, no)(character));
        Assert.IsTrue(SkillUnlock.Any(yes, no)(character));
        Assert.IsFalse(SkillUnlock.Any(no, no)(character));
    }

    [Test]
    public void 조건은_캐릭터가_없어도_터지지_않는다()
    {
        Assert.IsFalse(SkillUnlock.AtLevel(1)(null));
        Assert.IsFalse(SkillUnlock.AfterSkills("power_strike")(null));
        Assert.AreEqual(0, SkillUnlocks.Evaluate(null));
        Assert.AreEqual(0, SkillUnlocks.EvaluateAll(null));
    }

    [Test]
    public void 조건_스킬이_없으면_해금은_아무것도_하지_않는다()
    {
        // 표에 조건이 붙은 스킬이 하나도 없는 지금은, 몇 번을 훑어도 스킬이 늘지 않아야 한다.
        Assert.AreEqual(0, SkillUnlocks.Evaluate(character));
        Assert.AreEqual(0, character.SkillCount);
    }

    [Test]
    public void 합성_후보에는_조건_스킬이_들어가지_않는다()
    {
        // 지금은 표 전체가 합성용이라 후보가 잡혀야 한다. 나중에 조건 스킬을 추가했을 때
        // 그것들이 Roll로 새어 나오면 이 검사가 아니라 WeightOf의 IsConditional이 막는다.
        for (int i = 0; i < 50; i++)
        {
            string id = SkillCatalog.Roll(character, 6);
            if (string.IsNullOrEmpty(id)) continue;

            SkillDefinition? found = SkillCatalog.Find(id);
            Assert.IsTrue(found.HasValue, $"'{id}'는 표에 있어야 한다");
            Assert.IsFalse(found.Value.IsConditional, $"'{id}'는 조건 해금 스킬이라 합성으로 나오면 안 된다");
        }
    }
}
