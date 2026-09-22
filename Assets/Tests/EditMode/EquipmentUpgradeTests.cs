using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

// 장비 강화·장비 합성·재화 값표.
// 창고와 지갑은 바뀔 때마다 실제 세이브 파일에 쓰므로, 기존 파일을 들고 있다가 되돌린다(EquipmentInventoryTests와 같다).
public class EquipmentUpgradeTests
{
    private string backup;
    private bool hadSave;
    private readonly List<Object> created = new List<Object>();

    private WeaponDefinition longSword;
    private WeaponDefinition shield;

    [SetUp]
    public void SetUp()
    {
        hadSave = SaveSystem.HasSave;
        backup = hadSave ? File.ReadAllText(SaveSystem.SavePath) : null;
        SaveSystem.Delete();

        longSword = Weapon("Test_LongSword", WeaponType.SwordOneHand, EquipSlot.MainHand);
        shield = Weapon("Test_Shield", WeaponType.Shield, EquipSlot.OffHand);
    }

    [TearDown]
    public void TearDown()
    {
        SaveSystem.Delete();
        if (hadSave && backup != null) File.WriteAllText(SaveSystem.SavePath, backup);

        for (int i = 0; i < created.Count; i++)
        {
            if (created[i] != null) Object.DestroyImmediate(created[i]);
        }
        created.Clear();
    }

    private WeaponDefinition Weapon(string assetName, WeaponType type, EquipSlot slot)
    {
        var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
        weapon.name = assetName;
        weapon.displayName = assetName;
        weapon.type = type;
        weapon.slot = slot;
        created.Add(weapon);
        return weapon;
    }

    private CharacterSO Hero(string assetName)
    {
        var so = ScriptableObject.CreateInstance<CharacterSO>();
        so.name = assetName;
        so.characterName = assetName;
        so.EnsureId();
        created.Add(so);
        return so;
    }

    // 주사위 0은 언제나 성공이다. 원하는 단계까지 확실히 올린다.
    private static void EnhanceTo(OwnedEquipment item, int level)
    {
        while (item.Level < level)
            Assert.IsTrue(EquipmentEnhancement.TryEnhance(item, 0f, out _, out _));
    }

    // ---- 강화 확률 --------------------------------------------------------

    [Test]
    public void 강화_성공_확률은_100퍼센트에서_단계마다_10퍼센트씩_떨어진다()
    {
        Assert.AreEqual(1.0f, EquipmentEnhancement.SuccessChance(0), 0.0001f);
        Assert.AreEqual(0.9f, EquipmentEnhancement.SuccessChance(1), 0.0001f);
        Assert.AreEqual(0.5f, EquipmentEnhancement.SuccessChance(5), 0.0001f);
        Assert.AreEqual(0.1f, EquipmentEnhancement.SuccessChance(9), 0.0001f, "+9 → +10");
        Assert.AreEqual(0f, EquipmentEnhancement.SuccessChance(EquipmentEnhancement.MaxLevel), "끝까지 올랐으면 더 못 올린다");
    }

    [Test]
    public void 파괴_확률은_5강부터_시도할_때마다_1점5퍼센트씩_오른다()
    {
        Assert.AreEqual(0f, EquipmentEnhancement.DestroyChance(4), "+4 → +5는 파괴되지 않는다");
        Assert.AreEqual(0.015f, EquipmentEnhancement.DestroyChance(5), 0.0001f);
        Assert.AreEqual(0.030f, EquipmentEnhancement.DestroyChance(6), 0.0001f);
        Assert.AreEqual(0.075f, EquipmentEnhancement.DestroyChance(9), 0.0001f, "+9 → +10");
    }

    [Test]
    public void 파괴는_실패_안에서_떼어_낸다()
    {
        // +9 → +10: 성공 10%, 파괴 7.5%, 유지 82.5%
        Assert.AreEqual(EnhanceOutcome.Success, EquipmentEnhancement.Resolve(9, 0.099f));
        Assert.AreEqual(EnhanceOutcome.Destroyed, EquipmentEnhancement.Resolve(9, 0.10f));
        Assert.AreEqual(EnhanceOutcome.Destroyed, EquipmentEnhancement.Resolve(9, 0.174f));
        Assert.AreEqual(EnhanceOutcome.Fail, EquipmentEnhancement.Resolve(9, 0.176f));
        Assert.AreEqual(EnhanceOutcome.Success, EquipmentEnhancement.Resolve(0, 0.999f), "+0 → +1은 언제나 성공");
    }

    // ---- 강화 실행 --------------------------------------------------------

    [Test]
    public void 강화하면_골드가_빠지고_단계가_오르며_세이브를_왕복한다()
    {
        // 카탈로그에 있는 진짜 무기로 저장해야 불러올 때 이름으로 되찾을 수 있다.
        WeaponDefinition real = WeaponCatalog.Find("Sword_1");
        Assume.That(real, Is.Not.Null, "WeaponCatalog에 Sword_1이 없다");

        PlayerAccount.Add(Currency.Gold, 100000);
        OwnedEquipment item = EquipmentInventory.Add(real, EquipmentGrade.B);
        long cost = EquipmentEnhancement.Cost(item);

        Assert.IsTrue(EquipmentEnhancement.TryEnhance(item, 0f, out EnhanceOutcome outcome, out _));
        Assert.AreEqual(EnhanceOutcome.Success, outcome);
        Assert.AreEqual(1, item.Level);
        Assert.AreEqual(100000 - cost, PlayerAccount.Balance(Currency.Gold));

        // 창고를 파일에서 다시 읽어도 단계가 남는다.
        SaveSystem.LoadEquipment();
        Assert.AreEqual(1, EquipmentInventory.Items[0].Level);
    }

    [Test]
    public void 골드가_모자라면_시도하지_않는다()
    {
        OwnedEquipment item = EquipmentInventory.Add(longSword, EquipmentGrade.B);

        Assert.IsFalse(EquipmentEnhancement.TryEnhance(item, 0f, out _, out string reason));
        Assert.IsNotEmpty(reason);
        Assert.AreEqual(0, item.Level);
    }

    [Test]
    public void 실패해도_단계는_내려가지_않는다()
    {
        PlayerAccount.Add(Currency.Gold, 1000000);
        OwnedEquipment item = EquipmentInventory.Add(longSword, EquipmentGrade.C);
        EnhanceTo(item, 3);

        // +3 → +4: 성공 70%. 0.99는 실패(파괴 확률은 아직 없다).
        Assert.IsTrue(EquipmentEnhancement.TryEnhance(item, 0.99f, out EnhanceOutcome outcome, out _));
        Assert.AreEqual(EnhanceOutcome.Fail, outcome);
        Assert.AreEqual(3, item.Level);
    }

    [Test]
    public void 파괴되면_창고에서_사라지고_영웅은_기본_장비로_돌아간다()
    {
        PlayerAccount.Add(Currency.Gold, 1000000);
        CharacterSO hero = Hero("Hero_Destroy");
        OwnedEquipment item = EquipmentInventory.Add(longSword, EquipmentGrade.C);
        Assert.IsTrue(EquipmentInventory.Equip(hero, item, out _));
        EnhanceTo(item, 5);

        // +5 → +6: 성공 50%, 파괴 1.5% → 0.505는 파괴.
        Assert.IsTrue(EquipmentEnhancement.TryEnhance(item, 0.505f, out EnhanceOutcome outcome, out _));
        Assert.AreEqual(EnhanceOutcome.Destroyed, outcome);
        Assert.IsFalse(EquipmentInventory.Contains(item));
        Assert.IsNull(EquipmentInventory.EquippedIn(hero, EquipSlot.MainHand));
    }

    [Test]
    public void 강화_배율은_등급_배율에_곱해진다()
    {
        PlayerAccount.Add(Currency.Gold, 1000000);
        OwnedEquipment item = EquipmentInventory.Add(longSword, EquipmentGrade.B);
        EnhanceTo(item, 2);

        Assert.AreEqual(1.40f * 1.06f, item.Power, 0.0001f);
        Assert.AreEqual("+2 " + EquipmentGradeNames.ItemName(longSword, EquipmentGrade.B), item.DisplayName);
    }

    // ---- 장비 합성 --------------------------------------------------------

    [Test]
    public void 합성_결과_등급은_평균을_내린_값보다_한_단계_높다()
    {
        var items = new List<OwnedEquipment>
        {
            EquipmentInventory.Add(longSword, EquipmentGrade.E),
            EquipmentInventory.Add(longSword, EquipmentGrade.C),
            EquipmentInventory.Add(longSword, EquipmentGrade.B),
        };

        // (0 + 2 + 3) / 3 = 1(D) → C
        Assert.AreEqual(EquipmentGrade.D, EquipmentSynthesis.AverageGrade(items));
        Assert.AreEqual(EquipmentGrade.C, EquipmentSynthesis.ResultGrade(items));
    }

    [Test]
    public void 합성_결과는_S에서_멈추고_평균이_S면_합성하지_않는다()
    {
        var nearTop = new List<OwnedEquipment>
        {
            EquipmentInventory.Add(longSword, EquipmentGrade.A),
            EquipmentInventory.Add(longSword, EquipmentGrade.A),
            EquipmentInventory.Add(longSword, EquipmentGrade.S),
        };
        Assert.AreEqual(EquipmentGrade.S, EquipmentSynthesis.ResultGrade(nearTop));

        var top = new List<OwnedEquipment>
        {
            EquipmentInventory.Add(longSword, EquipmentGrade.S),
            EquipmentInventory.Add(longSword, EquipmentGrade.S),
            EquipmentInventory.Add(longSword, EquipmentGrade.S),
        };
        Assert.IsFalse(EquipmentSynthesis.CanSynthesize(top, out _));
    }

    [Test]
    public void 장착_중인_장비는_합성_재료로_쓸_수_없다()
    {
        CharacterSO hero = Hero("Hero_Synth");
        OwnedEquipment held = EquipmentInventory.Add(longSword, EquipmentGrade.D);
        Assert.IsTrue(EquipmentInventory.Equip(hero, held, out _));

        Assert.IsFalse(EquipmentSynthesis.CanUse(held, out _));

        var items = new List<OwnedEquipment>
        {
            held,
            EquipmentInventory.Add(longSword, EquipmentGrade.D),
            EquipmentInventory.Add(longSword, EquipmentGrade.D),
        };
        Assert.IsFalse(EquipmentSynthesis.CanSynthesize(items, out _));
    }

    [Test]
    public void 합성_결과_계열은_과반_재료를_따른다()
    {
        var items = new List<OwnedEquipment>
        {
            EquipmentInventory.Add(shield, EquipmentGrade.D),
            EquipmentInventory.Add(shield, EquipmentGrade.D),
            EquipmentInventory.Add(longSword, EquipmentGrade.D),
        };
        Assert.AreEqual(WeaponFamily.Shield, EquipmentSynthesis.ResultFamily(items));
    }

    [Test]
    public void 합성하면_재료가_사라지고_결과_한_점과_골드_차감이_남는다()
    {
        PlayerAccount.Add(Currency.Gold, 100000);
        var items = new List<OwnedEquipment>
        {
            EquipmentInventory.Add(longSword, EquipmentGrade.D),
            EquipmentInventory.Add(longSword, EquipmentGrade.D),
            EquipmentInventory.Add(longSword, EquipmentGrade.D),
        };
        long cost = EquipmentSynthesis.Cost(items);

        Assert.IsTrue(EquipmentSynthesis.TrySynthesize(items, out OwnedEquipment result, out _));
        Assert.AreEqual(1, EquipmentInventory.Items.Count);
        Assert.AreSame(result, EquipmentInventory.Items[0]);
        Assert.AreEqual(EquipmentGrade.C, result.Grade);
        Assert.AreEqual(0, result.Level, "강화 단계는 이어지지 않는다");
        Assert.AreEqual(100000 - cost, PlayerAccount.Balance(Currency.Gold));
    }

    [Test]
    public void 골드가_모자라면_합성하지_않고_재료도_남는다()
    {
        var items = new List<OwnedEquipment>
        {
            EquipmentInventory.Add(longSword, EquipmentGrade.D),
            EquipmentInventory.Add(longSword, EquipmentGrade.D),
            EquipmentInventory.Add(longSword, EquipmentGrade.D),
        };

        Assert.IsFalse(EquipmentSynthesis.TrySynthesize(items, out _, out _));
        Assert.AreEqual(3, EquipmentInventory.Items.Count);
    }

    // ---- 값표 -----------------------------------------------------------

    [Test]
    public void 층_클리어_골드는_1층_1000에서_다섯_층마다_500씩_는다()
    {
        Assert.AreEqual(1000, GameEconomy.FloorClearGold(1));
        Assert.AreEqual(1000, GameEconomy.FloorClearGold(5), "같은 구간(1~5층)은 같은 골드");
        Assert.AreEqual(1500, GameEconomy.FloorClearGold(6));
        Assert.AreEqual(2000, GameEconomy.FloorClearGold(11));
        Assert.AreEqual(10500, GameEconomy.FloorClearGold(FloorProgress.LastFloor));
    }

    [Test]
    public void 일반_소환은_골드_고급_소환은_젬이고_10회는_1회의_열_배다()
    {
        Assert.AreEqual(Currency.Gold, GameEconomy.SummonCurrency(SummonKind.Normal));
        Assert.AreEqual(Currency.Gem, GameEconomy.SummonCurrency(SummonKind.Paid));

        Assert.AreEqual(50000, GameEconomy.SummonCost(SummonKind.Normal, 1));
        Assert.AreEqual(500000, GameEconomy.SummonCost(SummonKind.Normal, 10));
        Assert.AreEqual(300, GameEconomy.SummonCost(SummonKind.Paid, 1));
        Assert.AreEqual(3000, GameEconomy.SummonCost(SummonKind.Paid, 10));
    }
}
