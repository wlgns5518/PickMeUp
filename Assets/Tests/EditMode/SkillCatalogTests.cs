using NUnit.Framework;
using UnityEngine;

// 스킬 표는 성급으로 나뉘지 않는다. 무엇을 배울 수 있는지 가르는 것은 직업과 "이미 배웠는가"뿐이다.
public class SkillCatalogTests
{
    private CharacterSO character;

    [SetUp]
    public void SetUp()
    {
        CharacterProgress.Clear();
        character = Make(JobType.Mage, 1);
    }

    [TearDown]
    public void TearDown()
    {
        CharacterProgress.Clear();
        if (character != null) Object.DestroyImmediate(character);
    }

    private static CharacterSO Make(JobType job, int stars)
    {
        var made = ScriptableObject.CreateInstance<CharacterSO>();
        made.name = "TestCharacter";
        made.characterName = "테스트";
        made.starCount = stars;
        made.level = 1;
        made.expToNext = 10;
        made.job = job;
        made.constitution = new Constitution
        {
            strengthGrowth = 1f, intelligenceGrowth = 1f, vitalityGrowth = 1f, agilityGrowth = 1f,
        };
        made.stats = new VisibleStats { strength = 5, intelligence = 5, vitality = 5, agility = 5 };
        return made;
    }

    [Test]
    public void 뽑히는_스킬은_그_직업이_배울_수_있는_것뿐이다()
    {
        for (int i = 0; i < 100; i++)
        {
            string id = SkillCatalog.Roll(character);
            Assert.IsFalse(string.IsNullOrEmpty(id), "배울 스킬이 남아 있으면 하나는 나와야 한다");

            SkillDefinition? found = SkillCatalog.Find(id);
            Assert.IsTrue(found.HasValue, $"'{id}'는 표에 있어야 한다");
            Assert.IsTrue(found.Value.CanLearn(JobType.Mage), $"'{id}'는 마법사가 배울 수 없는 스킬이다");
        }
    }

    [Test]
    public void 성급과_무관하게_모든_후보가_나온다()
    {
        // 성급으로 나누지 않으므로 1성 재료든 7성 재료든 후보는 같다. 1성 베이스로 충분히 돌리면
        // 직업이 배울 수 있는 스킬이 결국 전부 한 번씩은 나온다.
        var seen = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < 2000; i++)
        {
            string id = SkillCatalog.Roll(character);
            if (!string.IsNullOrEmpty(id)) seen.Add(id);
        }

        int learnable = 0;
        for (int i = 0; i < SkillCatalog.AllSkills.Count; i++)
        {
            SkillDefinition skill = SkillCatalog.AllSkills[i];
            if (!skill.IsConditional && skill.CanLearn(JobType.Mage)) learnable++;
        }

        Assert.AreEqual(learnable, seen.Count, "직업이 배울 수 있는 스킬은 전부 나올 수 있어야 한다");
    }

    [Test]
    public void 이미_배운_스킬은_다시_나오지_않는다()
    {
        string first = SkillCatalog.Roll(character);
        Assert.IsTrue(character.LearnSkill(first));

        for (int i = 0; i < 100; i++)
            Assert.AreNotEqual(first, SkillCatalog.Roll(character));
    }

    [Test]
    public void 배울_것이_남아_있으면_후보가_있다고_답한다()
    {
        Assert.IsTrue(SkillCatalog.HasCandidate(character));
        Assert.IsFalse(SkillCatalog.HasCandidate(null));
    }
}
