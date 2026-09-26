using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Kind = VillageBlockout.Kind;

// 다른 테스트가 시설 레벨 잠금과 무관한 규칙(강화 확률, 편성 저장, 던전 층 조건)을 볼 때 모든 시설을 끝까지 열어 둔다.
// Restore는 저장하지 않으므로 세이브 파일은 건드리지 않는다.
public static class FacilityTesting
{
    public static void UnlockAll() => SetAll(FacilityLevels.MaxLevel);

    public static void SetAll(int level)
    {
        var levels = new List<KeyValuePair<Kind, int>>();
        foreach (Kind kind in FacilityUnlocks.Upgradeable) levels.Add(new KeyValuePair<Kind, int>(kind, level));
        FacilityLevels.Restore(levels);
    }

    public static void Set(Kind kind, int level)
    {
        var levels = new List<KeyValuePair<Kind, int>>();
        foreach (Kind other in FacilityUnlocks.Upgradeable)
            levels.Add(new KeyValuePair<Kind, int>(other, other == kind ? level : FacilityLevels.Get(other)));
        FacilityLevels.Restore(levels);
    }
}

// 시설 업그레이드 — 레벨 저장·젬 값·레벨마다 여는 기능(2026-09-26 사용자 결정).
// 레벨과 지갑은 바뀔 때마다 실제 세이브 파일에 쓰므로, 기존 파일을 들고 있다가 되돌린다.
public class FacilityUpgradeTests
{
    private string backup;
    private bool hadSave;
    private int savedFloor;

    [SetUp]
    public void SetUp()
    {
        hadSave = SaveSystem.HasSave;
        backup = hadSave ? File.ReadAllText(SaveSystem.SavePath) : null;
        savedFloor = FloorProgress.HighestCleared;
        SaveSystem.Delete();
    }

    [TearDown]
    public void TearDown()
    {
        SaveSystem.Delete();
        if (hadSave && backup != null) File.WriteAllText(SaveSystem.SavePath, backup);
        FloorProgress.RestoreCleared(savedFloor);
    }

    private static void SetGems(long amount)
    {
        long have = PlayerAccount.Balance(Currency.Gem);
        if (have > amount) PlayerAccount.TrySpend(Currency.Gem, have - amount);
        else PlayerAccount.Add(Currency.Gem, amount - have);
    }

    [Test]
    public void 세이브가_없으면_모든_시설이_1레벨이다()
    {
        foreach (Kind kind in FacilityUnlocks.Upgradeable) Assert.AreEqual(1, FacilityLevels.Get(kind), kind.ToString());
    }

    [Test]
    public void 업그레이드는_젬을_2레벨_500_3레벨_1500_내고_세이브를_왕복한다()
    {
        SetGems(2000);

        Assert.IsTrue(FacilityLevels.TryUpgrade(Kind.Summoning, out _));
        Assert.AreEqual(2, FacilityLevels.Get(Kind.Summoning));
        Assert.AreEqual(1500, PlayerAccount.Balance(Currency.Gem));

        Assert.IsTrue(FacilityLevels.TryUpgrade(Kind.Summoning, out _));
        Assert.AreEqual(3, FacilityLevels.Get(Kind.Summoning));
        Assert.AreEqual(0, PlayerAccount.Balance(Currency.Gem));

        // 메모리를 비우고 파일에서 다시 읽는다.
        FacilityLevels.Forget();
        Assert.AreEqual(3, FacilityLevels.Get(Kind.Summoning), "올린 레벨이 세이브에 남는다");
        Assert.AreEqual(1, FacilityLevels.Get(Kind.Synthesis), "건드리지 않은 시설은 그대로");
    }

    [Test]
    public void 젬이_모자라면_오르지_않고_젬도_그대로다()
    {
        SetGems(499);

        Assert.IsFalse(FacilityLevels.TryUpgrade(Kind.Training, out string reason));
        StringAssert.Contains("젬", reason);
        Assert.AreEqual(1, FacilityLevels.Get(Kind.Training));
        Assert.AreEqual(499, PlayerAccount.Balance(Currency.Gem));
    }

    [Test]
    public void 최대_3레벨에서_멈추고_비행선착장은_올릴_수_없다()
    {
        FacilityTesting.UnlockAll();
        SetGems(10000);

        Assert.IsFalse(FacilityLevels.TryUpgrade(Kind.Rift, out _));
        Assert.AreEqual(0, FacilityLevels.NextCost(Kind.Rift));
        Assert.IsFalse(FacilityLevels.TryUpgrade(Kind.Airdock, out _));
        Assert.AreEqual(10000, PlayerAccount.Balance(Currency.Gem));
    }

    [Test]
    public void 전체_저장은_시설_레벨을_지우지_않는다()
    {
        SetGems(500);
        Assert.IsTrue(FacilityLevels.TryUpgrade(Kind.Armory, out _));

        SaveSystem.Save(new List<CharacterSO>());
        FacilityLevels.Forget();
        Assert.AreEqual(2, FacilityLevels.Get(Kind.Armory));
    }

    [Test]
    public void 소환소는_2레벨에_10회_3레벨에_고급_10회_4성_확정을_연다()
    {
        FacilityTesting.Set(Kind.Summoning, 1);
        Assert.IsFalse(FacilityUnlocks.CanSummonTen);
        Assert.AreEqual(0, FacilityUnlocks.GuaranteeFor(SummonKind.Paid, 10));

        FacilityTesting.Set(Kind.Summoning, 2);
        Assert.IsTrue(FacilityUnlocks.CanSummonTen);
        Assert.AreEqual(0, FacilityUnlocks.GuaranteeFor(SummonKind.Paid, 10));

        FacilityTesting.Set(Kind.Summoning, 3);
        Assert.AreEqual(4, FacilityUnlocks.GuaranteeFor(SummonKind.Paid, 10));
        Assert.AreEqual(0, FacilityUnlocks.GuaranteeFor(SummonKind.Paid, 1), "1회 소환에는 확정이 없다");
        Assert.AreEqual(0, FacilityUnlocks.GuaranteeFor(SummonKind.Normal, 10), "일반 소환은 2성까지라 확정을 걸지 않는다");
    }

    [Test]
    public void 확정_굴림은_그_별_아래로_나오지_않는다()
    {
        for (int i = 0; i < 500; i++)
            Assert.GreaterOrEqual(SummonTable.RollStarsAtLeast(SummonKind.Paid, 4), 4);
    }

    [Test]
    public void 합성소_레벨이_합성으로_배울_스킬_수를_정한다()
    {
        FacilityTesting.Set(Kind.Synthesis, 1);
        Assert.AreEqual(2, FacilityUnlocks.SynthesisSkillCap);
        FacilityTesting.Set(Kind.Synthesis, 2);
        Assert.AreEqual(3, FacilityUnlocks.SynthesisSkillCap);
        FacilityTesting.Set(Kind.Synthesis, 3);
        Assert.AreEqual(SkillCatalog.MaxSkillsPerCharacter, FacilityUnlocks.SynthesisSkillCap);
    }

    [Test]
    public void 장비제작소는_2레벨에_수동_제작_3레벨에_장비_합성을_연다()
    {
        FacilityTesting.Set(Kind.EquipmentWorkshop, 1);
        Assert.IsFalse(FacilityUnlocks.CanCraftManually);
        Assert.IsFalse(FacilityUnlocks.CanSynthesizeEquipment);

        FacilityTesting.Set(Kind.EquipmentWorkshop, 2);
        Assert.IsTrue(FacilityUnlocks.CanCraftManually);
        Assert.IsFalse(FacilityUnlocks.CanSynthesizeEquipment);

        FacilityTesting.Set(Kind.EquipmentWorkshop, 3);
        Assert.IsTrue(FacilityUnlocks.CanSynthesizeEquipment);
    }

    [Test]
    public void 무기창고_레벨이_강화_상한을_정한다()
    {
        FacilityTesting.Set(Kind.Armory, 1);
        Assert.AreEqual(0, FacilityUnlocks.EnhanceCap);
        FacilityTesting.Set(Kind.Armory, 2);
        Assert.AreEqual(5, FacilityUnlocks.EnhanceCap);
        FacilityTesting.Set(Kind.Armory, 3);
        Assert.AreEqual(EquipmentEnhancement.MaxLevel, FacilityUnlocks.EnhanceCap);

        Assert.AreEqual(2, FacilityUnlocks.EnhanceLevelFor(0));
        Assert.AreEqual(2, FacilityUnlocks.EnhanceLevelFor(4));
        Assert.AreEqual(3, FacilityUnlocks.EnhanceLevelFor(5));
    }

    [Test]
    public void 훈련소_레벨만큼_파티를_쓴다()
    {
        FacilityTesting.Set(Kind.Training, 1);
        Assert.AreEqual(1, FacilityUnlocks.PartySlots);
        Assert.IsFalse(FacilityUnlocks.IsPartyUsable(1));

        FacilityTesting.Set(Kind.Training, 3);
        Assert.AreEqual(3, FacilityUnlocks.PartySlots);
        Assert.IsTrue(FacilityUnlocks.IsPartyUsable(2));
    }

    [Test]
    public void 요일_던전은_시공의_틈_2레벨_탐험_던전은_3레벨이_있어야_열린다()
    {
        FloorProgress.RestoreCleared(20);

        FacilityTesting.Set(Kind.Rift, 1);
        Assert.IsTrue(DungeonCatalog.IsUnlocked(DungeonKind.Main));
        Assert.IsFalse(DungeonCatalog.IsUnlocked(DungeonKind.Daily), "층은 채웠어도 시공의 틈이 1레벨이면 잠겨 있다");
        StringAssert.Contains("Lv.2", DungeonCatalog.UnlockText(DungeonKind.Daily));

        FacilityTesting.Set(Kind.Rift, 2);
        Assert.IsTrue(DungeonCatalog.IsUnlocked(DungeonKind.Daily));
        Assert.IsFalse(DungeonCatalog.IsUnlocked(DungeonKind.Expedition));

        FacilityTesting.Set(Kind.Rift, 3);
        Assert.IsTrue(DungeonCatalog.IsUnlocked(DungeonKind.Expedition));
    }

    [Test]
    public void 레벨이_모자라면_층을_깨도_새로_열렸다고_알리지_않는다()
    {
        FacilityTesting.Set(Kind.Rift, 1);
        var unlocked = new List<DungeonKind>();
        DungeonCatalog.CollectNewlyUnlocked(9, 10, unlocked);
        Assert.IsEmpty(unlocked);
    }
}

// 훈련소 레벨로 잠긴 파티. 편성은 지우지 않고, 거기 든 영웅은 지금 파티에 넣으면 옮겨 온다.
public class LockedPartyTests
{
    private string backup;
    private bool hadSave;
    private readonly List<CharacterSO> heroes = new List<CharacterSO>();

    [SetUp]
    public void SetUp()
    {
        hadSave = SaveSystem.HasSave;
        backup = hadSave ? File.ReadAllText(SaveSystem.SavePath) : null;
        SaveSystem.Delete();

        for (int i = 0; i < 2; i++)
        {
            CharacterSO hero = ScriptableObject.CreateInstance<CharacterSO>();
            hero.name = "LockedPartyTests_Hero" + i;
            hero.EnsureId();
            heroes.Add(hero);
        }
    }

    [TearDown]
    public void TearDown()
    {
        SaveSystem.Delete();
        SaveSystem.LoadParty(heroes);
        if (hadSave && backup != null) File.WriteAllText(SaveSystem.SavePath, backup);

        foreach (CharacterSO hero in heroes) Object.DestroyImmediate(hero);
        heroes.Clear();
    }

    [Test]
    public void 잠긴_파티는_고를_수_없고_그_영웅은_지금_파티로_옮겨_온다()
    {
        FacilityTesting.Set(Kind.Training, 1);

        // 3파티에 한 명이 들어 있고 3파티를 고른 채 저장된 세이브(훈련소가 3레벨이던 시절이라고 치자).
        PartyDeck.Restore(new List<IReadOnlyList<CharacterSO>>
        {
            new List<CharacterSO>(), new List<CharacterSO>(), new List<CharacterSO> { heroes[0] },
        }, 2);

        Assert.AreEqual(0, PartyDeck.ActiveIndex, "잠긴 파티를 골라 두었으면 1파티로 나간다");
        Assert.AreEqual(1, PartyDeck.CountOf(2), "잠긴 파티의 편성은 지우지 않는다");

        PartyDeck.SetActive(1);
        Assert.AreEqual(0, PartyDeck.ActiveIndex, "잠긴 파티로는 바꿀 수 없다");

        Assert.IsFalse(PartyDeck.IsInOtherParty(heroes[0]), "잠긴 파티의 영웅은 막지 않는다");
        Assert.IsTrue(PartyDeck.Add(heroes[0]));
        Assert.AreEqual(0, PartyDeck.CountOf(2), "옮겨 왔으므로 잠긴 파티에서는 빠진다");
        Assert.IsTrue(PartyDeck.Contains(heroes[0]));
    }
}
