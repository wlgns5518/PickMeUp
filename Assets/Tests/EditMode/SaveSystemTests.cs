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

    [Test]
    public void 출전_횟수가_왕복한다()
    {
        CharacterSO so = NewCharacter("Hero_Veteran");
        var roster = new List<CharacterSO> { so };

        try
        {
            CharacterProgress.MarkBattleEntered(so);
            CharacterProgress.MarkBattleEntered(so);
            SaveSystem.Save(roster);

            CharacterProgress.Clear();
            Assert.IsTrue(CharacterProgress.IsFirstBattle(so));

            SaveSystem.Load(roster);
            Assert.AreEqual(2, CharacterProgress.BattlesFoughtOf(so));
            Assert.IsFalse(CharacterProgress.IsFirstBattle(so));
        }
        finally
        {
            Object.DestroyImmediate(so);
        }
    }

    [Test]
    public void 출전_기록이_없던_세이브의_캐릭터는_아직_첫_전투를_치르지_않았다()
    {
        // 레벨까지 올린 캐릭터라도 battlesFought 칸이 없으면 첫 전투 전이다(2026-09 결정).
        CharacterSO so = NewCharacter("Hero_Grown");
        var roster = new List<CharacterSO> { so };

        try
        {
            string json =
                "{\"highestClearedFloor\":9,\"characters\":[{" +
                "\"id\":\"" + so.Id + "\",\"assetName\":\"Hero_Grown\",\"level\":10,\"exp\":0,\"expToNext\":55," +
                "\"strength\":18,\"intelligence\":16,\"vitality\":18,\"agility\":16," +
                "\"fallen\":false,\"stress\":0.0,\"skillIds\":[]}]}";
            File.WriteAllText(SaveSystem.SavePath, json);

            Assert.IsTrue(SaveSystem.Load(roster));
            Assert.AreEqual(10, so.Level);
            Assert.IsTrue(CharacterProgress.IsFirstBattle(so));
        }
        finally
        {
            Object.DestroyImmediate(so);
        }
    }

    [Test]
    public void 깬_층은_꼭대기를_넘지_않는다()
    {
        FloorProgress.RestoreCleared(250);
        Assert.AreEqual(FloorProgress.LastFloor, FloorProgress.HighestCleared);
        Assert.AreEqual(FloorProgress.LastFloor, FloorProgress.HighestUnlocked, "100층을 깨도 101층은 열리지 않는다");
        Assert.IsFalse(FloorProgress.IsUnlocked(FloorProgress.LastFloor + 1));

        FloorProgress.RestoreCleared(0);
        FloorProgress.MarkCleared(FloorProgress.LastFloor + 5);
        Assert.AreEqual(FloorProgress.LastFloor, FloorProgress.HighestCleared);
    }

    [Test]
    public void 재화가_파일로_왕복한다()
    {
        PlayerAccount.Add(Currency.Gold, 3361233);
        PlayerAccount.Add(Currency.Gem, 47);

        // 들고 있던 값을 버리면 다음에 읽을 때 파일에서 다시 올라온다.
        PlayerAccount.Forget();

        Assert.AreEqual(3361233, PlayerAccount.Balance(Currency.Gold));
        Assert.AreEqual(GameEconomy.StarterGems + 47, PlayerAccount.Balance(Currency.Gem), "시작 젬 위에 쌓인다");
        Assert.AreEqual(PlayerAccount.DefaultName, PlayerAccount.Name, "이름을 정한 적이 없으면 기본 이름");
    }

    [Test]
    public void 로스터를_저장해도_재화가_남는다()
    {
        CharacterSO so = NewCharacter("Hero_Wallet");
        try
        {
            PlayerAccount.Add(Currency.Gold, 500);
            SaveSystem.Save(new List<CharacterSO> { so });
            PlayerAccount.Forget();

            Assert.AreEqual(500, PlayerAccount.Balance(Currency.Gold));
        }
        finally
        {
            Object.DestroyImmediate(so);
        }
    }

    [Test]
    public void 재화_칸이_없던_세이브는_기본_이름에_시작_젬만_받는다()
    {
        File.WriteAllText(SaveSystem.SavePath, "{\"highestClearedFloor\":3,\"characters\":[]}");
        PlayerAccount.Forget();

        Assert.AreEqual(PlayerAccount.DefaultName, PlayerAccount.Name);
        Assert.AreEqual(0, PlayerAccount.Balance(Currency.Gold));
        Assert.AreEqual(GameEconomy.StarterGems, PlayerAccount.Balance(Currency.Gem), "옛 세이브도 시작 젬을 한 번 받는다");
    }

    [Test]
    public void 시작_젬은_한_번만_받는다()
    {
        Assert.AreEqual(GameEconomy.StarterGems, PlayerAccount.Balance(Currency.Gem));

        // 다시 읽어도(게임을 다시 켜도) 또 받지 않는다.
        PlayerAccount.Forget();
        Assert.AreEqual(GameEconomy.StarterGems, PlayerAccount.Balance(Currency.Gem));
    }

    [Test]
    public void 모자라면_재화를_빼지_않는다()
    {
        PlayerAccount.Add(Currency.Gold, 10);

        Assert.IsFalse(PlayerAccount.TrySpend(Currency.Gold, 11));
        Assert.AreEqual(10, PlayerAccount.Balance(Currency.Gold));

        Assert.IsTrue(PlayerAccount.TrySpend(Currency.Gold, 10));
        Assert.AreEqual(0, PlayerAccount.Balance(Currency.Gold));
    }
}
