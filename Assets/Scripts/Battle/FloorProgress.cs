using UnityEngine;

// 어느 층까지 깼는지, 그리고 지금 어느 층에 들어가는지를 들고 있다.
//
// 층은 자동으로 이어지지 않는다. 플레이어가 메인 씬에서 직접 고르고,
// 전투가 끝나면 다시 메인 씬으로 돌아온다.
// 그래서 여기 있는 값은 "진행 중인 런"이 아니라 "해금 상태"에 가깝다.
public static class FloorProgress
{
    public const int FirstFloor = 1;

    // 탑의 꼭대기. 이 층을 깨면 더 열리는 층이 없다.
    public const int LastFloor = 100;

    // 층은 다섯 개씩 한 구간으로 묶이고, 구간마다 전장 맵이 따로 있다(1~5층 평야, 6~10층 협곡 …).
    // 구간의 마지막 층(5의 배수)에는 나중에 특별 미션이 붙는다. 미션이 생기기 전까지는
    // 같은 구간의 맵에서 일반 전투로 치른다.
    public const int FloorsPerStage = 5;

    // 깬 층 중 가장 높은 번호. 0이면 아직 아무 층도 깨지 못한 상태.
    public static int HighestCleared { get; private set; }

    // 메인 씬에서 고른 층. 전투 씬의 스포너가 이 값을 읽어 적을 배치한다.
    // 씬을 넘어가야 하므로 static으로 들고 간다.
    public static int SelectedFloor { get; private set; } = FirstFloor;

    // 깬 층의 바로 다음 층까지 선택할 수 있다. 꼭대기를 넘지는 않는다.
    public static int HighestUnlocked => Mathf.Min(HighestCleared + 1, LastFloor);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 이전 플레이의 값이 남지 않도록 비운다.
        // 실제 해금 상태는 세이브에서 다시 읽어 온다.
        HighestCleared = 0;
        SelectedFloor = FirstFloor;
    }

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

    public static bool IsUnlocked(int floor)
    {
        return floor >= FirstFloor && floor <= HighestUnlocked;
    }

    public static bool TrySelect(int floor)
    {
        if (!IsUnlocked(floor)) return false;

        SelectedFloor = floor;
        return true;
    }

    public static void MarkCleared(int floor)
    {
        if (floor < FirstFloor) return;
        HighestCleared = Mathf.Clamp(Mathf.Max(HighestCleared, floor), 0, LastFloor);
    }

    // 세이브에서 읽어온 해금 상태를 얹는다.
    public static void RestoreCleared(int highestCleared)
    {
        HighestCleared = Mathf.Clamp(highestCleared, 0, LastFloor);
        if (SelectedFloor > HighestUnlocked) SelectedFloor = HighestUnlocked;
    }
}
