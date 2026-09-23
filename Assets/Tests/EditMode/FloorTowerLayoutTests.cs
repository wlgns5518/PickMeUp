using System.Collections.Generic;
using NUnit.Framework;

// 층 선택의 탑은 1층이 맨 아래, 꼭대기가 맨 위다(위 여백에 지붕, 아래 여백에 입구). 창에 걸친 층만 풀에서 꺼내 켜므로
// "스크롤 값 → 보이는 층" 계산이 틀리면 가장자리에 빈칸이 비치거나 안 보이는 층이 켜진 채 남는다.
public class FloorTowerLayoutTests
{
    private const float Row = 100f;
    private const float Top = 300f;
    private const float Bottom = 200f;
    private static readonly FloorTowerView.TowerLayout Layout = new FloorTowerView.TowerLayout(1, 100, Row, Top, Bottom);

    [Test]
    public void TopFloorSitsUnderTheRoofAndFirstFloorAboveTheEntrance()
    {
        Assert.AreEqual(Top, Layout.TopOf(100));
        Assert.AreEqual(Top + 99 * Row, Layout.TopOf(1));
        Assert.AreEqual(Top + 100 * Row, Layout.GroundFloorBottom);
        Assert.AreEqual(Top + 100 * Row + Bottom, Layout.ContentHeight);
    }

    [Test]
    public void ScrolledToTopShowsTheHighestFloors()
    {
        // 창 높이 580: 지붕 여백 300 + 100층(300~400) + 99층 + 98층 앞 80(500~580).
        Layout.VisibleRange(0f, 580f, 0f, out int low, out int high);
        Assert.AreEqual(100, high);
        Assert.AreEqual(98, low);
    }

    [Test]
    public void ScrolledToBottomShowsTheFirstFloor()
    {
        // 맨 아래까지 내리면 창은 10150~10500: 2층 뒤 절반, 1층, 입구 여백.
        float view = 350f;
        Layout.VisibleRange(Layout.MaxScroll(view), view, 0f, out int low, out int high);
        Assert.AreEqual(1, low);
        Assert.AreEqual(2, high);
    }

    [Test]
    public void RowTouchingTheEdgeIsNotVisible()
    {
        // 창 아래 가장자리(500)가 98층 줄 윗변과 딱 맞으면 98층은 보이지 않는다.
        Layout.VisibleRange(0f, 500f, 0f, out int low, out int high);
        Assert.AreEqual(99, low);
        Assert.AreEqual(100, high);
    }

    [Test]
    public void OnlyTheRoofVisibleMeansNoFloors()
    {
        Layout.VisibleRange(0f, 250f, 0f, out int low, out int high);
        Assert.Greater(low, high);
    }

    [Test]
    public void MarginAddsOneRowOnEachSide()
    {
        Layout.VisibleRange(1000f, 400f, Row, out int low, out int high);
        Layout.VisibleRange(1000f, 400f, 0f, out int tightLow, out int tightHigh);
        Assert.AreEqual(tightLow - 1, low);
        Assert.AreEqual(tightHigh + 1, high);
    }

    [Test]
    public void EveryFloorIsVisibleSomewhereWhileScrollingTheWholeTower()
    {
        // 층을 하나도 빠뜨리지 않고, 범위가 늘 창 하나 분량을 넘지 않는지 조금씩 굴리며 본다.
        var seen = new HashSet<int>();
        float view = 820f;
        for (float scroll = 0f; scroll <= Layout.MaxScroll(view); scroll += 7f)
        {
            Layout.VisibleRange(scroll, view, 0f, out int low, out int high);
            Assert.LessOrEqual(high - low + 1, UnityEngine.Mathf.CeilToInt(view / Row) + 1);
            for (int floor = low; floor <= high; floor++) seen.Add(floor);
        }
        Assert.AreEqual(100, seen.Count);
    }

    [Test]
    public void CenteringClampsAtBothEndsOfTheTower()
    {
        float view = 800f;
        Assert.AreEqual(0f, Layout.ScrollToCenter(100, view));
        Assert.AreEqual(Layout.MaxScroll(view), Layout.ScrollToCenter(1, view));

        // 가운데 층은 줄 가운데가 창 가운데에 온다.
        float scroll = Layout.ScrollToCenter(50, view);
        Assert.AreEqual(Layout.TopOf(50) + Row * 0.5f, scroll + view * 0.5f, 0.001f);
    }

    [Test]
    public void RatioRunsFromGroundToTop()
    {
        Assert.AreEqual(1f, Layout.RatioAt(Layout.TopOf(100)), 0.0001f);
        Assert.AreEqual(0f, Layout.RatioAt(Layout.GroundFloorBottom), 0.0001f);
        Assert.AreEqual(Layout.TopOf(40), Layout.YAt(Layout.RatioAt(Layout.TopOf(40))), 0.001f);
    }

    [Test]
    public void StagesCoverTheTowerInFives()
    {
        Assert.AreEqual(20, FloorStages.Count);
        Assert.AreEqual(0, FloorStages.IndexOf(1));
        Assert.AreEqual(0, FloorStages.IndexOf(5));
        Assert.AreEqual(1, FloorStages.IndexOf(6));
        Assert.AreEqual(19, FloorStages.IndexOf(100));
        Assert.AreEqual(96, FloorStages.FirstFloorOf(19));
        Assert.AreEqual(100, FloorStages.LastFloorOf(19));
    }

    [Test]
    public void EveryStageHasItsOwnName()
    {
        var names = new HashSet<string>();
        for (int stage = 0; stage < FloorStages.Count; stage++)
        {
            string title = FloorStages.TitleOfStage(stage);
            Assert.IsFalse(string.IsNullOrEmpty(title), $"{stage}번 구간 이름이 비어 있다");
            Assert.IsTrue(names.Add(title), $"{title}이(가) 두 구간에 겹친다");
        }
    }
}
