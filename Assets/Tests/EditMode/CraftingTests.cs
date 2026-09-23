using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

// 재료로 무엇이 나올지 정하는 규칙, 재료 창고의 소모·세이브 왕복, 층별 재료 등급.
// 재료 창고는 바뀔 때마다 실제 세이브 파일에 쓰므로 SaveSystemTests와 같이 기존 파일을 들고 있다가 되돌린다.
public class CraftingTests
{
    private string backup;
    private bool hadSave;

    private static CraftMaterial M(MaterialKind kind, EquipmentGrade grade) => new CraftMaterial(kind, grade);

    [SetUp]
    public void SetUp()
    {
        hadSave = SaveSystem.HasSave;
        backup = hadSave ? File.ReadAllText(SaveSystem.SavePath) : null;
        SaveSystem.Delete();
    }

    [TearDown]
    public void TearDown()
    {
        SaveSystem.Delete();
        if (hadSave && backup != null) File.WriteAllText(SaveSystem.SavePath, backup);
    }

    // ---- 계열 ------------------------------------------------------------

    [Test]
    public void 가장_많이_넣은_재료_종류가_계열을_정한다()
    {
        var metal = new List<CraftMaterial> { M(MaterialKind.Metal, EquipmentGrade.E), M(MaterialKind.Metal, EquipmentGrade.E), M(MaterialKind.Wood, EquipmentGrade.S) };
        var wood = new List<CraftMaterial> { M(MaterialKind.Wood, EquipmentGrade.E), M(MaterialKind.Leather, EquipmentGrade.E), M(MaterialKind.Wood, EquipmentGrade.E) };
        var leather = new List<CraftMaterial> { M(MaterialKind.Leather, EquipmentGrade.E), M(MaterialKind.Leather, EquipmentGrade.E), M(MaterialKind.Leather, EquipmentGrade.E) };

        Assert.AreEqual(WeaponFamily.Metal, CraftRecipe.FamilyOf(metal));
        Assert.AreEqual(WeaponFamily.Wood, CraftRecipe.FamilyOf(wood));
        Assert.AreEqual(WeaponFamily.Shield, CraftRecipe.FamilyOf(leather));
    }

    [Test]
    public void 세_종류를_하나씩_넣으면_계열이_서지_않는다()
    {
        var mixed = new List<CraftMaterial> { M(MaterialKind.Metal, EquipmentGrade.C), M(MaterialKind.Wood, EquipmentGrade.C), M(MaterialKind.Leather, EquipmentGrade.C) };

        Assert.AreEqual(WeaponFamily.Any, CraftRecipe.FamilyOf(mixed));
    }

    [Test]
    public void 계열마다_들어가는_무기_종류()
    {
        Assert.IsTrue(CraftRecipe.Allows(WeaponFamily.Metal, WeaponType.SwordTwoHand));
        Assert.IsTrue(CraftRecipe.Allows(WeaponFamily.Metal, WeaponType.Dagger));
        Assert.IsFalse(CraftRecipe.Allows(WeaponFamily.Metal, WeaponType.Bow));
        Assert.IsFalse(CraftRecipe.Allows(WeaponFamily.Metal, WeaponType.Shield));

        Assert.IsTrue(CraftRecipe.Allows(WeaponFamily.Wood, WeaponType.Polearm));
        Assert.IsFalse(CraftRecipe.Allows(WeaponFamily.Wood, WeaponType.Axe));

        Assert.IsTrue(CraftRecipe.Allows(WeaponFamily.Shield, WeaponType.Shield));
        Assert.IsFalse(CraftRecipe.Allows(WeaponFamily.Shield, WeaponType.SwordOneHand));

        Assert.IsTrue(CraftRecipe.Allows(WeaponFamily.Any, WeaponType.Bow));
    }

    [Test]
    public void 굴린_무기는_언제나_그_계열_안에서_나온다()
    {
        Assume.That(CraftRecipe.RollWeapon(WeaponFamily.Any), Is.Not.Null, "WeaponCatalog에 제작 가능한 무기가 없다");

        for (int i = 0; i < 200; i++)
        {
            Assert.AreEqual(WeaponType.Shield, CraftRecipe.RollWeapon(WeaponFamily.Shield).type);
            Assert.IsTrue(CraftRecipe.Allows(WeaponFamily.Wood, CraftRecipe.RollWeapon(WeaponFamily.Wood).type));
            Assert.IsTrue(CraftRecipe.Allows(WeaponFamily.Metal, CraftRecipe.RollWeapon(WeaponFamily.Metal).type));
        }
    }

    // ---- 등급 ------------------------------------------------------------

    [Test]
    public void 결과_등급의_밑변은_세_재료_등급의_평균을_내린_값이다()
    {
        // S(5) + B(3) + E(0) = 8 → 8/3 = 2.67 → C(2)
        var mixed = new List<CraftMaterial> { M(MaterialKind.Metal, EquipmentGrade.S), M(MaterialKind.Metal, EquipmentGrade.B), M(MaterialKind.Metal, EquipmentGrade.E) };
        Assert.AreEqual(EquipmentGrade.C, CraftRecipe.BaseGradeOf(mixed));

        var all = new List<CraftMaterial> { M(MaterialKind.Wood, EquipmentGrade.A), M(MaterialKind.Wood, EquipmentGrade.A), M(MaterialKind.Wood, EquipmentGrade.A) };
        Assert.AreEqual(EquipmentGrade.A, CraftRecipe.BaseGradeOf(all));
    }

    [Test]
    public void 세_칸이_다_차야_제작할_수_있다()
    {
        Assert.IsFalse(CraftRecipe.IsComplete(new List<CraftMaterial> { M(MaterialKind.Metal, EquipmentGrade.E) }));
        Assert.IsTrue(CraftRecipe.IsComplete(new List<CraftMaterial>
        {
            M(MaterialKind.Metal, EquipmentGrade.E), M(MaterialKind.Metal, EquipmentGrade.E), M(MaterialKind.Metal, EquipmentGrade.E),
        }));
    }

    // ---- 재료 창고 ---------------------------------------------------------

    [Test]
    public void 재료가_모자라면_하나도_빼지_않는다()
    {
        MaterialInventory.Add(M(MaterialKind.Metal, EquipmentGrade.B), 2);

        var three = new List<CraftMaterial> { M(MaterialKind.Metal, EquipmentGrade.B), M(MaterialKind.Metal, EquipmentGrade.B), M(MaterialKind.Metal, EquipmentGrade.B) };
        Assert.IsFalse(MaterialInventory.TryConsume(three));
        Assert.AreEqual(2, MaterialInventory.CountOf(MaterialKind.Metal, EquipmentGrade.B), "세 개 중 두 개만 빠진 채 멈추면 재료만 사라진다");

        MaterialInventory.Add(M(MaterialKind.Metal, EquipmentGrade.B));
        Assert.IsTrue(MaterialInventory.TryConsume(three));
        Assert.AreEqual(0, MaterialInventory.CountOf(MaterialKind.Metal, EquipmentGrade.B));
    }

    [Test]
    public void 재료가_세이브를_왕복한다()
    {
        MaterialInventory.Add(M(MaterialKind.Leather, EquipmentGrade.S), 4);
        MaterialInventory.Add(M(MaterialKind.Wood, EquipmentGrade.E));

        // 들고 있던 값을 버리고 파일에서 다시 읽힌다.
        SaveSystem.LoadMaterials();

        Assert.AreEqual(4, MaterialInventory.CountOf(MaterialKind.Leather, EquipmentGrade.S));
        Assert.AreEqual(1, MaterialInventory.CountOf(MaterialKind.Wood, EquipmentGrade.E));
        Assert.AreEqual(5, MaterialInventory.TotalCount);
    }

    [Test]
    public void 전투_정산_저장이_재료를_지우지_않는다()
    {
        MaterialInventory.Add(M(MaterialKind.Metal, EquipmentGrade.A), 3);

        // 전투 씬은 제작소를 한 번도 열지 않은 채 로스터만 저장한다.
        SaveSystem.Save(new List<CharacterSO>());
        SaveSystem.LoadMaterials();

        Assert.AreEqual(3, MaterialInventory.CountOf(MaterialKind.Metal, EquipmentGrade.A));
    }

    [Test]
    public void 재료_칸이_없던_옛_세이브는_재료_없이_읽힌다()
    {
        File.WriteAllText(SaveSystem.SavePath, "{\"highestClearedFloor\":2,\"characters\":[]}");

        SaveSystem.LoadMaterials();

        Assert.AreEqual(0, MaterialInventory.TotalCount);
    }

    // ---- 전투 보상 ---------------------------------------------------------

    [TestCase(1, EquipmentGrade.E)]
    [TestCase(10, EquipmentGrade.E)]
    [TestCase(11, EquipmentGrade.D)]
    [TestCase(30, EquipmentGrade.D)]
    [TestCase(31, EquipmentGrade.C)]
    [TestCase(50, EquipmentGrade.C)]
    [TestCase(51, EquipmentGrade.B)]
    [TestCase(70, EquipmentGrade.B)]
    [TestCase(71, EquipmentGrade.A)]
    [TestCase(90, EquipmentGrade.A)]
    [TestCase(91, EquipmentGrade.S)]
    [TestCase(100, EquipmentGrade.S)]
    public void 재료_등급은_층_구간으로_정해진다(int floor, EquipmentGrade expected)
    {
        Assert.AreEqual(expected, MaterialDrops.BaseGrade(floor));
    }

    [Test]
    public void 확률_안쪽이면_한_단계_높고_밖이면_구간_등급_그대로다()
    {
        Assert.AreEqual(EquipmentGrade.D, MaterialDrops.RollGrade(EquipmentGrade.E, 0f));
        Assert.AreEqual(EquipmentGrade.D, MaterialDrops.RollGrade(EquipmentGrade.E, MaterialDrops.UpgradeChance * 0.99f));
        Assert.AreEqual(EquipmentGrade.E, MaterialDrops.RollGrade(EquipmentGrade.E, MaterialDrops.UpgradeChance));
        Assert.AreEqual(EquipmentGrade.E, MaterialDrops.RollGrade(EquipmentGrade.E, 0.99f));
        // S 위로는 오르지 않는다.
        Assert.AreEqual(EquipmentGrade.S, MaterialDrops.RollGrade(EquipmentGrade.S, 0f));
    }

    // 층 선택 화면이 "E ~ D등급"처럼 보여 주는 윗끝. 주사위가 확률 안쪽일 때 나오는 등급과 같아야 한다.
    [TestCase(1, EquipmentGrade.D)]
    [TestCase(30, EquipmentGrade.C)]
    [TestCase(90, EquipmentGrade.S)]
    [TestCase(91, EquipmentGrade.S)]
    public void 나올_수_있는_가장_좋은_재료는_구간_등급의_한_단계_위다(int floor, EquipmentGrade expected)
    {
        Assert.AreEqual(expected, MaterialDrops.HighestGrade(floor));
        Assert.AreEqual(expected, MaterialDrops.RollGrade(MaterialDrops.BaseGrade(floor), 0f));
    }

    [Test]
    public void 보상_재료는_정한_개수_안에서_구간_등급_또는_한_단계_위로만_나온다()
    {
        var settings = new BattleRewardSettings();
        var drops = new List<CraftMaterial>();

        for (int i = 0; i < 200; i++)
        {
            MaterialDrops.Roll(40, settings, drops); // 31~50층 C

            Assert.GreaterOrEqual(drops.Count, settings.materialsMin);
            Assert.LessOrEqual(drops.Count, settings.materialsMax);
            foreach (CraftMaterial drop in drops)
            {
                Assert.GreaterOrEqual((int)drop.Grade, (int)EquipmentGrade.C);
                Assert.LessOrEqual((int)drop.Grade, (int)EquipmentGrade.B);
            }
        }
    }
}
