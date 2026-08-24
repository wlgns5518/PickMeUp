using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

// 세이브 왕복. 실제 세이브 파일 경로를 쓰기 때문에 SetUp에서 기존 파일을 통째로 들고 있다가
// TearDown에서 반드시 되돌려 놓는다 — 테스트를 돌렸다고 플레이 기록이 날아가면 안 된다.
public class SaveSystemTests
{
    private string backup;
    private bool hadSave;
    private int savedFloor;
    private float savedRecoveryPerHour;

    private static CharacterSO NewCharacter(string assetName)
    {
        var so = ScriptableObject.CreateInstance<CharacterSO>();
        so.name = assetName;
        so.characterName = assetName;
        so.starCount = 5;
        so.level = 1;
        so.exp = 0;
        so.expToNext = 10;
        so.constitution = new Constitution();
        so.stats = new VisibleStats();
        return so;
    }

    [SetUp]
    public void SetUp()
    {
        hadSave = SaveSystem.HasSave;
        backup = hadSave ? File.ReadAllText(SaveSystem.SavePath) : null;
        savedFloor = FloorProgress.HighestCleared;

        // 스트레스 회복은 실제 시계로 계산된다. 왕복 자체를 보는 테스트라 시간이 끼어들지 않도록 끈다.
        // (회복 계산은 CharacterStressTests가 따로 본다.)
        savedRecoveryPerHour = CharacterStress.RecoveryPerHour;
        CharacterStress.RecoveryPerHour = 0f;

        SaveSystem.Delete();
        CharacterProgress.Clear();
        PartyRoster.Clear();
        CharacterStress.Clear();
        FloorProgress.RestoreCleared(0);
    }

    [TearDown]
    public void TearDown()
    {
        CharacterProgress.Clear();
        PartyRoster.Clear();
        CharacterStress.Clear();

        SaveSystem.Delete();
        if (hadSave && backup != null) File.WriteAllText(SaveSystem.SavePath, backup);
        FloorProgress.RestoreCleared(savedFloor);
        CharacterStress.RecoveryPerHour = savedRecoveryPerHour;
    }

    [Test]
    public void 성장과_스킬이_왕복해도_그대로다()
    {
        CharacterSO so = NewCharacter("Hero_A");
        var roster = new List<CharacterSO> { so };

        try
        {
            CharacterProgress.Restore(so, 12, 4, 65, 21, 22, 23, 24, new List<string> { "s1", "s2" });
            CharacterStress.Set(so, 37f);
            FloorProgress.MarkCleared(4);

            SaveSystem.Save(roster);
            CharacterProgress.Clear();
            CharacterStress.Clear();
            FloorProgress.RestoreCleared(0);

            Assert.IsTrue(SaveSystem.Load(roster));

            Assert.AreEqual(12, so.Level);
            Assert.AreEqual(4, so.Exp);
            Assert.AreEqual(65, so.ExpToNext);
            Assert.AreEqual(21, so.Strength);
            Assert.AreEqual(24, so.Agility);
            Assert.AreEqual(2, so.SkillCount, "배운 스킬도 저장돼야 한다");
            Assert.IsTrue(so.HasSkill("s1"));
            Assert.AreEqual(37f, CharacterStress.Get(so), 0.001f);
            Assert.AreEqual(4, FloorProgress.HighestCleared);
        }
        finally
        {
            Object.DestroyImmediate(so);
        }
    }

    [Test]
    public void 에셋_이름을_바꿔도_진행도가_따라온다()
    {
        // 예전에는 세이브 키가 에셋 이름이라, 리네임하는 순간 그 캐릭터의 진행도가 사라졌다.
        CharacterSO so = NewCharacter("Hero_OldName");
        var roster = new List<CharacterSO> { so };

        try
        {
            CharacterProgress.Restore(so, 9, 0, 50, 30, 0, 0, 0, null);
            SaveSystem.Save(roster);

            CharacterProgress.Clear();
            so.name = "Hero_RenamedInEditor";

            Assert.IsTrue(SaveSystem.Load(roster));
            Assert.AreEqual(9, so.Level, "식별자가 이름과 분리돼 있으므로 리네임을 견뎌야 한다");
            Assert.AreEqual(30, so.Strength);
        }
        finally
        {
            Object.DestroyImmediate(so);
        }
    }

    [Test]
    public void 식별자가_없던_시절의_세이브도_읽는다()
    {
        // id 칸에 에셋 이름이 들어 있고 assetName/skillIds가 아예 없는 옛 형식.
        CharacterSO so = NewCharacter("Hero_Legacy");
        var roster = new List<CharacterSO> { so };

        try
        {
            const string legacyJson =
                "{\"highestClearedFloor\":2,\"characters\":[{" +
                "\"id\":\"Hero_Legacy\",\"level\":6,\"exp\":1,\"expToNext\":35," +
                "\"strength\":17,\"intelligence\":3,\"vitality\":4,\"agility\":5," +
                "\"fallen\":false,\"stress\":12.5}]}";
            File.WriteAllText(SaveSystem.SavePath, legacyJson);

            Assert.IsTrue(SaveSystem.Load(roster));

            Assert.AreEqual(6, so.Level);
            Assert.AreEqual(17, so.Strength);
            Assert.AreEqual(2, FloorProgress.HighestCleared);
            Assert.AreEqual(0, so.SkillCount, "옛 세이브에는 스킬 칸이 없다");
        }
        finally
        {
            Object.DestroyImmediate(so);
        }
    }

    [Test]
    public void 영구_사망_기록이_왕복한다()
    {
        CharacterSO so = NewCharacter("Hero_Fallen");
        var roster = new List<CharacterSO> { so };

        try
        {
            PartyRoster.MarkFallen(so);
            SaveSystem.Save(roster);

            PartyRoster.Clear();
            Assert.IsFalse(PartyRoster.IsFallen(so));

            SaveSystem.Load(roster);
            Assert.IsTrue(PartyRoster.IsFallen(so));
        }
        finally
        {
            Object.DestroyImmediate(so);
        }
    }

    [Test]
    public void 세이브가_없으면_불러오기는_실패를_알린다()
    {
        Assert.IsFalse(SaveSystem.HasSave);
        Assert.IsFalse(SaveSystem.Load(new List<CharacterSO>()));
    }
}
