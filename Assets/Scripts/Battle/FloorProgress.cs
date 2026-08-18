using UnityEngine;

// 어느 층까지 깼는지, 그리고 지금 어느 층에 들어가는지를 들고 있다.
//
// 층은 자동으로 이어지지 않는다. 플레이어가 메인 씬에서 직접 고르고,
// 전투가 끝나면 다시 메인 씬으로 돌아온다.
// 그래서 여기 있는 값은 "진행 중인 런"이 아니라 "해금 상태"에 가깝다.
public static class FloorProgress
{
    public const int FirstFloor = 1;

    // 깬 층 중 가장 높은 번호. 0이면 아직 아무 층도 깨지 못한 상태.
    public static int HighestCleared { get; private set; }

    // 메인 씬에서 고른 층. 전투 씬의 스포너가 이 값을 읽어 적을 배치한다.
    // 씬을 넘어가야 하므로 static으로 들고 간다.
    public static int SelectedFloor { get; private set; } = FirstFloor;

    // 깬 층의 바로 다음 층까지 선택할 수 있다.
    public static int HighestUnlocked => HighestCleared + 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 이전 플레이의 값이 남지 않도록 비운다.
        // 실제 해금 상태는 세이브에서 다시 읽어 온다.
        HighestCleared = 0;
        SelectedFloor = FirstFloor;
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
        HighestCleared = Mathf.Max(HighestCleared, floor);
    }

    // 세이브에서 읽어온 해금 상태를 얹는다.
    public static void RestoreCleared(int highestCleared)
    {
        HighestCleared = Mathf.Max(0, highestCleared);
        if (SelectedFloor > HighestUnlocked) SelectedFloor = HighestUnlocked;
    }
}
