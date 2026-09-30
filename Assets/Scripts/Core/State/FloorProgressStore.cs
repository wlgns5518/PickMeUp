using UnityEngine;

// 어느 층까지 깼는지, 지금 어느 층에 들어가는지.
public interface IFloorProgress
{
    // 깬 층 중 가장 높은 번호. 0이면 아직 아무 층도 깨지 못한 상태.
    int HighestCleared { get; }

    // 메인 씬에서 고른 층. 전투 씬의 스포너가 이 값을 읽어 적을 배치한다.
    int SelectedFloor { get; }

    // 깬 층의 바로 다음 층까지 선택할 수 있다. 꼭대기를 넘지는 않는다.
    int HighestUnlocked { get; }

    bool IsUnlocked(int floor);
    bool TrySelect(int floor);
    void MarkCleared(int floor);
}

// 층은 자동으로 이어지지 않는다. 플레이어가 메인 씬에서 직접 고르고, 전투가 끝나면 다시 메인 씬으로 돌아온다.
// 그래서 여기 있는 값은 "진행 중인 런"이 아니라 "해금 상태"에 가깝다.
// 씬을 넘어가야 하므로 조립하는 쪽(GameServices)이 세션 내내 하나만 들고 있다.
public sealed class FloorProgressStore : IFloorProgress
{
    public int HighestCleared { get; private set; }

    public int SelectedFloor { get; private set; } = FloorProgress.FirstFloor;

    public int HighestUnlocked => Mathf.Min(HighestCleared + 1, FloorProgress.LastFloor);

    public bool IsUnlocked(int floor) => floor >= FloorProgress.FirstFloor && floor <= HighestUnlocked;

    public bool TrySelect(int floor)
    {
        if (!IsUnlocked(floor)) return false;

        SelectedFloor = floor;
        return true;
    }

    public void MarkCleared(int floor)
    {
        if (floor < FloorProgress.FirstFloor) return;
        HighestCleared = Mathf.Clamp(Mathf.Max(HighestCleared, floor), 0, FloorProgress.LastFloor);
    }

    // 세이브에서 읽어온 해금 상태를 얹는다.
    public void RestoreCleared(int highestCleared)
    {
        HighestCleared = Mathf.Clamp(highestCleared, 0, FloorProgress.LastFloor);
        if (SelectedFloor > HighestUnlocked) SelectedFloor = HighestUnlocked;
    }
}
