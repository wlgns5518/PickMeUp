using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

// 무기창고의 장착 규칙과 세이브 왕복.
// 창고는 바뀔 때마다 실제 세이브 파일에 쓰므로, SaveSystemTests와 같이 기존 파일을 들고 있다가 되돌린다.
public class EquipmentInventoryTests
{
    private string backup;
    private bool hadSave;
    private readonly List<Object> created = new List<Object>();

    private WeaponDefinition longSword;
    private WeaponDefinition greatSword;
    private WeaponDefinition shield;

    [SetUp]
    public void SetUp()
    {
        hadSave = SaveSystem.HasSave;
        backup = hadSave ? File.ReadAllText(SaveSystem.SavePath) : null;

        // 파일을 지우면 창고도 들고 있던 값을 버리고, 다음에 쓰일 때 빈 파일에서 다시 읽는다.
        SaveSystem.Delete();

        longSword = Weapon("Test_LongSword", WeaponType.SwordOneHand, EquipSlot.MainHand);
        greatSword = Weapon("Test_GreatSword", WeaponType.SwordTwoHand, EquipSlot.MainHand);
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

    [Test]
    public void 제작한_장비는_아무도_들지_않은_채_창고에_들어온다()
    {
        OwnedEquipment item = EquipmentInventory.Add(longSword, EquipmentGrade.B);

        Assert.AreEqual(1, EquipmentInventory.Items.Count);
        Assert.IsFalse(item.IsEquipped);
        Assert.AreEqual(EquipmentGrade.B, item.Grade);
    }

    [Test]
    public void 등급_배율은_E부터_S까지_한_단계에_0점2씩_오른다()
    {
        Assert.AreEqual(0.80f, EquipmentGradeRules.PowerOf(EquipmentGrade.E), 0.0001f);
        Assert.AreEqual(1.00f, EquipmentGradeRules.PowerOf(EquipmentGrade.D), 0.0001f, "D는 기본 장비와 같다");
        Assert.AreEqual(1.80f, EquipmentGradeRules.PowerOf(EquipmentGrade.S), 0.0001f);

        for (var grade = EquipmentGrade.E; grade < EquipmentGrade.S; grade++)
        {
            float step = EquipmentGradeRules.PowerOf(grade + 1) - EquipmentGradeRules.PowerOf(grade);
            Assert.AreEqual(0.20f, step, 0.0001f, $"{grade}→{grade + 1}");
        }
    }

    [Test]
    public void 제작_장비_이름에는_등급_수식어가_붙는다()
    {
        Assert.AreEqual("조잡한 Test_LongSword", new OwnedEquipment(longSword, EquipmentGrade.E).DisplayName);
        Assert.AreEqual("전설의 Test_LongSword", new OwnedEquipment(longSword, EquipmentGrade.S).DisplayName);
        Assert.AreEqual("명장의 Test_Shield", new CraftedEquipment(shield, EquipmentGrade.A).name, "제작소 결과도 같은 이름을 쓴다");
    }

    [Test]
    public void 들린_장비의_등급이_전투_배율로_이어진다()
    {
        CharacterSO hero = Hero("Hero_A");
        Assert.AreEqual(1f, CharacterLoadout.MainHandPower(hero), 0.0001f, "기본 장비는 배율 1");
        Assert.AreEqual(1f, CharacterLoadout.ShieldPower(hero), 0.0001f);

        EquipmentInventory.Equip(hero, EquipmentInventory.Add(longSword, EquipmentGrade.S), out _);
        EquipmentInventory.Equip(hero, EquipmentInventory.Add(shield, EquipmentGrade.E), out _);

        Assert.AreEqual(1.80f, CharacterLoadout.MainHandPower(hero), 0.0001f);
        Assert.AreEqual(0.80f, CharacterLoadout.ShieldPower(hero), 0.0001f, "조잡한 방패는 기본 방패보다 못하다");
    }

    [Test]
    public void 마법사_손에_걸린_장비의_등급은_전투에_닿지_않는다()
    {
        CharacterSO mage = Hero("Hero_Mage");
        mage.job = JobType.Mage;

        // 옛 세이브에서 마법사가 S 롱소드를 들고 있던 경우.
        WeaponDefinition real = WeaponCatalog.Find("Sword_1");
        Assume.That(real, Is.Not.Null, "WeaponCatalog에 Sword_1이 없다");
        File.WriteAllText(SaveSystem.SavePath,
            "{\"highestClearedFloor\":0,\"characters\":[],\"equipment\":[" +
            "{\"weapon\":\"Sword_1\",\"grade\":5,\"owner\":\"" + mage.Id + "\"}]}");
        SaveSystem.LoadEquipment();

        Assert.AreEqual(1f, CharacterLoadout.MainHandPower(mage), 0.0001f);
    }

    [Test]
    public void 장착하면_기본_장비_대신_제작_장비를_든다()
    {
        CharacterSO hero = Hero("Hero_A");
        hero.mainHandWeapon = Weapon("Test_Base", WeaponType.Dagger, EquipSlot.MainHand);
        hero.mainHand = WeaponType.Dagger;

        OwnedEquipment item = EquipmentInventory.Add(longSword, EquipmentGrade.S);
        Assert.IsTrue(EquipmentInventory.Equip(hero, item, out _));

        Assert.AreSame(longSword, CharacterLoadout.MainHandOf(hero));
        Assert.AreEqual(WeaponType.SwordOneHand, CharacterLoadout.MainHandTypeOf(hero));

        EquipmentInventory.Unequip(item);
        Assert.AreEqual(WeaponType.Dagger, CharacterLoadout.MainHandTypeOf(hero), "해제하면 에셋의 기본 장비로 돌아간다");
        Assert.IsFalse(item.IsEquipped, "해제한 장비는 창고로 돌아간다");
    }

    [Test]
    public void 다른_영웅이_든_장비를_장착하면_그_손에서_가져온다()
    {
        CharacterSO a = Hero("Hero_A");
        CharacterSO b = Hero("Hero_B");
        OwnedEquipment item = EquipmentInventory.Add(longSword, EquipmentGrade.C);

        EquipmentInventory.Equip(a, item, out _);
        EquipmentInventory.Equip(b, item, out _);

        Assert.IsTrue(item.IsHeldBy(b));
        Assert.IsNull(EquipmentInventory.EquippedIn(a, EquipSlot.MainHand), "한 점은 한 사람 손에만 걸린다");
    }

    [Test]
    public void 같은_자리에_새로_들면_전에_든_장비는_창고로_간다()
    {
        CharacterSO hero = Hero("Hero_A");
        OwnedEquipment first = EquipmentInventory.Add(longSword, EquipmentGrade.E);
        OwnedEquipment second = EquipmentInventory.Add(longSword, EquipmentGrade.A);

        EquipmentInventory.Equip(hero, first, out _);
        EquipmentInventory.Equip(hero, second, out _);

        Assert.IsFalse(first.IsEquipped);
        Assert.AreSame(second, EquipmentInventory.EquippedIn(hero, EquipSlot.MainHand));
    }

    [Test]
    public void 두손_무기를_들면_제작한_방패를_내려놓는다()
    {
        CharacterSO hero = Hero("Hero_A");
        OwnedEquipment board = EquipmentInventory.Add(shield, EquipmentGrade.B);
        OwnedEquipment big = EquipmentInventory.Add(greatSword, EquipmentGrade.B);

        Assert.IsTrue(EquipmentInventory.Equip(hero, board, out _));
        Assert.IsTrue(CharacterLoadout.HasShield(hero));

        Assert.IsTrue(EquipmentInventory.Equip(hero, big, out _));
        Assert.IsFalse(board.IsEquipped);
        Assert.IsFalse(CharacterLoadout.HasShield(hero));
    }

    [Test]
    public void 두손_무기를_든_영웅에게는_방패가_들리지_않는다()
    {
        CharacterSO hero = Hero("Hero_A");
        OwnedEquipment big = EquipmentInventory.Add(greatSword, EquipmentGrade.B);
        OwnedEquipment board = EquipmentInventory.Add(shield, EquipmentGrade.B);
        EquipmentInventory.Equip(hero, big, out _);

        string reason;
        Assert.IsFalse(EquipmentInventory.Equip(hero, board, out reason));
        Assert.IsFalse(string.IsNullOrEmpty(reason), "왜 안 되는지 화면에 띄울 문구가 있어야 한다");
        Assert.IsFalse(board.IsEquipped);
    }

    [Test]
    public void 제작한_두손_무기는_에셋의_기본_방패도_내려놓게_한다()
    {
        CharacterSO hero = Hero("Hero_A");
        hero.offHand = OffHandType.Shield;
        hero.offHandWeapon = shield;
        Assert.IsTrue(CharacterLoadout.HasShield(hero));

        OwnedEquipment big = EquipmentInventory.Add(greatSword, EquipmentGrade.B);
        EquipmentInventory.Equip(hero, big, out _);

        Assert.IsFalse(CharacterLoadout.HasShield(hero));
        Assert.IsNull(CharacterLoadout.OffHandOf(hero));
    }

    [Test]
    public void 마법사는_제작_장비를_들지_못한다()
    {
        CharacterSO mage = Hero("Hero_Mage");
        mage.job = JobType.Mage;
        OwnedEquipment sword = EquipmentInventory.Add(longSword, EquipmentGrade.S);
        OwnedEquipment board = EquipmentInventory.Add(shield, EquipmentGrade.S);

        string reason;
        Assert.IsFalse(EquipmentInventory.Equip(mage, sword, out reason));
        Assert.IsFalse(EquipmentInventory.Equip(mage, board, out _));
        Assert.IsFalse(sword.IsEquipped);
        Assert.IsFalse(board.IsEquipped);
        StringAssert.DoesNotContain("마법사", reason, "직업은 화면에 드러내지 않는다 — 거절 문구에도 적지 않는다");
    }

    [Test]
    public void 규칙이_생기기_전_마법사_손에_걸린_장비는_전투에_새지_않는다()
    {
        CharacterSO mage = Hero("Hero_Mage");
        mage.job = JobType.Mage;
        mage.mainHand = WeaponType.Magic;

        WeaponDefinition real = WeaponCatalog.Find("HeavySword");
        Assume.That(real, Is.Not.Null, "WeaponCatalog에 HeavySword가 없다");

        // 규칙이 생기기 전에 저장된 세이브 — 대검이 마법사 손에 걸려 있다.
        File.WriteAllText(SaveSystem.SavePath,
            "{\"highestClearedFloor\":0,\"characters\":[],\"equipment\":[" +
            "{\"weapon\":\"HeavySword\",\"grade\":4,\"owner\":\"" + mage.Id + "\"}]}");
        SaveSystem.LoadEquipment();
        Assume.That(EquipmentInventory.EquippedIn(mage, EquipSlot.MainHand), Is.Not.Null, "세이브의 주인 기록이 읽혀야 이 테스트가 뜻을 갖는다");

        Assert.AreEqual(WeaponType.Magic, CharacterLoadout.MainHandTypeOf(mage));
        Assert.IsNull(CharacterLoadout.MainHandOf(mage));
    }

    [Test]
    public void 명단에서_빠진_영웅의_장비는_창고로_돌아온다()
    {
        CharacterSO hero = Hero("Hero_A");
        OwnedEquipment item = EquipmentInventory.Add(longSword, EquipmentGrade.B);
        EquipmentInventory.Equip(hero, item, out _);

        EquipmentInventory.UnequipAll(hero);

        Assert.IsFalse(item.IsEquipped);
    }

    [Test]
    public void 창고와_장착_상태가_세이브를_왕복한다()
    {
        // 카탈로그에 있는 진짜 무기로 저장해야 불러올 때 이름으로 되찾을 수 있다.
        WeaponDefinition real = WeaponCatalog.Find("Sword_1");
        Assume.That(real, Is.Not.Null, "WeaponCatalog에 Sword_1이 없다");

        CharacterSO hero = Hero("Hero_A");
        OwnedEquipment item = EquipmentInventory.Add(real, EquipmentGrade.A);
        EquipmentInventory.Add(real, EquipmentGrade.D);
        EquipmentInventory.Equip(hero, item, out _);

        // 창고를 버리고 파일에서 다시 읽힌다.
        SaveSystem.LoadEquipment();

        Assert.AreEqual(2, EquipmentInventory.Items.Count);
        OwnedEquipment held = EquipmentInventory.EquippedIn(hero, EquipSlot.MainHand);
        Assert.IsNotNull(held, "누가 들고 있는지도 저장돼야 한다");
        Assert.AreSame(real, held.Weapon);
        Assert.AreEqual(EquipmentGrade.A, held.Grade);
    }

    [Test]
    public void 전투_정산_저장이_창고를_지우지_않는다()
    {
        WeaponDefinition real = WeaponCatalog.Find("Sword_1");
        Assume.That(real, Is.Not.Null, "WeaponCatalog에 Sword_1이 없다");

        EquipmentInventory.Add(real, EquipmentGrade.S);

        // 전투 씬은 창고를 한 번도 열지 않은 채 로스터만 저장한다.
        CharacterSO hero = Hero("Hero_A");
        SaveSystem.Save(new List<CharacterSO> { hero });
        SaveSystem.LoadEquipment();

        Assert.AreEqual(1, EquipmentInventory.Items.Count);
    }

    [Test]
    public void 창고_칸이_없던_옛_세이브는_빈_창고로_읽힌다()
    {
        File.WriteAllText(SaveSystem.SavePath, "{\"highestClearedFloor\":2,\"characters\":[]}");

        SaveSystem.LoadEquipment();

        Assert.AreEqual(0, EquipmentInventory.Items.Count);
    }
}
