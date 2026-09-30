using UnityEngine;

// 어느 층까지 깼는지, 그리고 지금 어느 층에 들어가는지.
//
// 층의 번호 규칙(구간, 전투 씬 이름)은 여기 있고, 굴러가는 값(깬 층, 고른 층)은 FloorProgressStore가 든다.
// 생성자로 의존성을 받을 수 있는 코드는 IFloorProgress(GameServices.Floors)를 받는다.
public static class FloorProgress
{
    public const int FirstFloor = 1;

    // 탑의 꼭대기. 이 층을 깨면 더 열리는 층이 없다.
    public const int LastFloor = 100;

    // 층은 다섯 개씩 한 구간으로 묶이고, 구간마다 전장 맵이 따로 있다(1~5층 평야, 6~10층 협곡 …).
    // 구간의 마지막 층(5의 배수)에는 나중에 특별 미션이 붙는다. 미션이 생기기 전까지는
    // 같은 구간의 맵에서 일반 전투로 치른다.
    public const int FloorsPerStage = 5;

    private static FloorProgressStore State => GameServices.FloorState;

    // 깬 층 중 가장 높은 번호. 0이면 아직 아무 층도 깨지 못한 상태.
    public static int HighestCleared => State.HighestCleared;

    // 메인 씬에서 고른 층. 전투 씬의 스포너가 이 값을 읽어 적을 배치한다.
    public static int SelectedFloor => State.SelectedFloor;

    // 깬 층의 바로 다음 층까지 선택할 수 있다. 꼭대기를 넘지는 않는다.
    public static int HighestUnlocked => State.HighestUnlocked;

    // 층이 속한 구간의 첫 층. 7층이면 6.
    public static int StageFirstFloor(int floor)
    {
        int clamped = Mathf.Clamp(floor, FirstFloor, LastFloor);
        return FirstFloor + (clamped - FirstFloor) / FloorsPerStage * FloorsPerStage;
    }

    // 층이 싸우는 전투 씬. 씬 이름이 곧 구간이다(7층이면 "Floor6~10").
    // 씬은 PickMeUp/전투 맵 메뉴가 만들고, Build Settings에 등록돼 있어야 불러올 수 있다.
    public static string BattleSceneName(int floor)
    {
        int first = StageFirstFloor(floor);
        int last = Mathf.Min(first + FloorsPerStage - 1, LastFloor);
        return $"Floor{first}~{last}";
    }

    public static bool IsUnlocked(int floor) => State.IsUnlocked(floor);

    public static bool TrySelect(int floor) => State.TrySelect(floor);

    public static void MarkCleared(int floor) => State.MarkCleared(floor);

    // 세이브에서 읽어온 해금 상태를 얹는다.
    public static void RestoreCleared(int highestCleared) => State.RestoreCleared(highestCleared);
}
