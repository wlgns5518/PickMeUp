using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

// 시공의 틈에 걸리는 콘텐츠의 해금 규칙.
// 해금 상태는 따로 저장하지 않고 메인 던전 진행도(FloorProgress)에서 나온다 — 그 진행도가 줄지 않는다는 것까지 여기서 본다.
public class DungeonCatalogTests
{
    private int savedFloor;
    private string backup;
    private bool hadSave;

    [SetUp]
    public void SetUp()
    {
        savedFloor = FloorProgress.HighestCleared;
        hadSave = SaveSystem.HasSave;
        backup = hadSave ? File.ReadAllText(SaveSystem.SavePath) : null;
        FloorProgress.RestoreCleared(0);
        // 여기서는 층 조건만 본다. 시공의 틈 레벨 조건은 FacilityUpgradeTests가 본다.
        FacilityTesting.UnlockAll();
    }

    [TearDown]
    public void TearDown()
    {
        SaveSystem.Delete();
        if (hadSave && backup != null) File.WriteAllText(SaveSystem.SavePath, backup);
        FloorProgress.RestoreCleared(savedFloor);
    }

    [Test]
    public void 메인_던전은_처음부터_열려_있다()
    {
        Assert.AreEqual(0, DungeonCatalog.UnlockFloor(DungeonKind.Main));
        Assert.IsTrue(DungeonCatalog.IsUnlocked(DungeonKind.Main), "한 층도 못 깼어도 들어갈 수 있다");
        Assert.IsEmpty(DungeonCatalog.UnlockText(DungeonKind.Main));
    }

    [Test]
    public void 요일_던전은_10층_탐험_던전은_20층에_열린다()
    {
        Assert.IsFalse(DungeonCatalog.IsUnlocked(DungeonKind.Daily));
        Assert.IsFalse(DungeonCatalog.IsUnlocked(DungeonKind.Expedition));

        FloorProgress.RestoreCleared(9);
        Assert.IsFalse(DungeonCatalog.IsUnlocked(DungeonKind.Daily), "9층까지로는 아직");

        FloorProgress.RestoreCleared(10);
        Assert.IsTrue(DungeonCatalog.IsUnlocked(DungeonKind.Daily));
        Assert.IsFalse(DungeonCatalog.IsUnlocked(DungeonKind.Expedition));

        FloorProgress.RestoreCleared(20);
        Assert.IsTrue(DungeonCatalog.IsUnlocked(DungeonKind.Expedition));
    }

    [Test]
    public void 한_번_열린_던전은_낮은_층을_다시_깨도_잠기지_않는다()
    {
        FloorProgress.RestoreCleared(20);
        FloorProgress.MarkCleared(3);

        Assert.AreEqual(20, FloorProgress.HighestCleared, "진행도는 가장 높이 깬 층으로 남는다");
        Assert.IsTrue(DungeonCatalog.IsUnlocked(DungeonKind.Daily));
        Assert.IsTrue(DungeonCatalog.IsUnlocked(DungeonKind.Expedition));
    }

    [Test]
    public void 이번_판으로_새로_열린_것만_모은다()
    {
        var unlocked = new List<DungeonKind>();

        DungeonCatalog.CollectNewlyUnlocked(9, 10, unlocked);
        CollectionAssert.AreEqual(new[] { DungeonKind.Daily }, unlocked);

        unlocked.Clear();
        DungeonCatalog.CollectNewlyUnlocked(10, 10, unlocked);
        Assert.IsEmpty(unlocked, "이미 깬 층을 다시 깨면 열리는 것이 없다");

        unlocked.Clear();
        DungeonCatalog.CollectNewlyUnlocked(0, 20, unlocked);
        CollectionAssert.AreEqual(new[] { DungeonKind.Daily, DungeonKind.Expedition }, unlocked);
    }

    [Test]
    public void 해금_상태는_세이브를_왕복한다()
    {
        FloorProgress.MarkCleared(DungeonCatalog.DailyUnlockFloor);
        SaveSystem.Save(new List<CharacterSO>());

        // 게임을 다시 켠 셈 — 진행도를 잃었다가 파일에서 읽는다.
        FloorProgress.RestoreCleared(0);
        Assert.IsFalse(DungeonCatalog.IsUnlocked(DungeonKind.Daily));

        Assert.IsTrue(SaveSystem.Load());
        Assert.IsTrue(DungeonCatalog.IsUnlocked(DungeonKind.Daily));
    }
}
