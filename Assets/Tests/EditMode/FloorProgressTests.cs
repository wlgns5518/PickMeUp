using NUnit.Framework;

// 층 → 전투 씬 이름. 씬 이름이 틀리면 층 선택에서 "씬을 불러올 수 없습니다"로만 드러나므로 여기서 못 박는다.
public class FloorProgressTests
{
    [TestCase(1, "Floor1~5")]
    [TestCase(4, "Floor1~5")]
    [TestCase(5, "Floor1~5")]   // 특별 미션 층도 제 구간의 맵을 쓴다.
    [TestCase(6, "Floor6~10")]
    [TestCase(10, "Floor6~10")]
    [TestCase(11, "Floor11~15")]
    [TestCase(99, "Floor96~100")]
    [TestCase(100, "Floor96~100")]
    public void BattleSceneName_GroupsFiveFloorsPerMap(int floor, string expected)
    {
        Assert.AreEqual(expected, FloorProgress.BattleSceneName(floor));
    }

    [Test]
    public void BattleSceneName_ClampsOutOfRangeFloors()
    {
        Assert.AreEqual("Floor1~5", FloorProgress.BattleSceneName(0));
        Assert.AreEqual("Floor96~100", FloorProgress.BattleSceneName(FloorProgress.LastFloor + 7));
    }
}
