using System.Collections.Generic;

// 시공의 틈에 걸리는 콘텐츠와 그 해금 조건.
//
//   메인 던전   — 조건 없음. 1층부터 한 층씩 오르는 탑이고, 이 게임의 기본 진행이다(FloorProgress).
//   요일 던전   — 메인 던전 10층 클리어 + 시공의 틈 Lv.2.
//   탐험 던전   — 메인 던전 20층 클리어 + 시공의 틈 Lv.3.
//
// 해금 상태를 따로 저장하지 않는다. 조건이 "메인 던전 몇 층까지 깼는가"와 "시공의 틈이 몇 레벨인가"이고, 두 값
// (FloorProgress.HighestCleared, FacilityLevels)은 세이브에 남으며 줄어들지 않는다. 그래서 한 번 열린 콘텐츠는
// 게임을 껐다 켜도, 낮은 층을 다시 깨도 잠기지 않는다 — 저장할 것이 하나 더 늘면 그 둘이 어긋날 자리만 생긴다.
public enum DungeonKind
{
    Main,       // 메인 던전 — 탑
    Daily,      // 요일 던전 — 성장 재화·재료를 얻는 반복 콘텐츠
    Expedition, // 탐험 던전 — 갈림길을 골라 나아가는 콘텐츠
}

public static class DungeonCatalog
{
    // 해금에 필요한 메인 던전 클리어 층. 0이면 조건이 없다.
    public const int DailyUnlockFloor = 10;
    public const int ExpeditionUnlockFloor = 20;

    public static readonly DungeonKind[] All = { DungeonKind.Main, DungeonKind.Daily, DungeonKind.Expedition };

    public static string Korean(DungeonKind kind)
    {
        switch (kind)
        {
            case DungeonKind.Daily:      return "요일 던전";
            case DungeonKind.Expedition: return "탐험 던전";
            default:                     return "메인 던전";
        }
    }

    // 무엇을 하는 콘텐츠인지 한 줄로. 화면(DungeonSelectUI)이 그대로 적는다.
    public static string Summary(DungeonKind kind)
    {
        switch (kind)
        {
            case DungeonKind.Daily:      return "요일마다 다른 재화·재료";
            case DungeonKind.Expedition: return "갈림길을 골라 나아가는 원정";
            default:                     return "한 층씩 오르는 탑";
        }
    }

    public static int UnlockFloor(DungeonKind kind)
    {
        switch (kind)
        {
            case DungeonKind.Daily:      return DailyUnlockFloor;
            case DungeonKind.Expedition: return ExpeditionUnlockFloor;
            default:                     return 0;
        }
    }

    // 층 조건과 시공의 틈 레벨(요일 Lv.2 · 탐험 Lv.3, FacilityUnlocks)을 둘 다 채워야 열린다.
    // 둘 다 줄지 않는 값이라 한 번 열리면 다시 잠기지 않는다.
    public static bool IsUnlocked(DungeonKind kind) => IsFloorMet(kind) && FacilityUnlocks.IsDungeonOpen(kind);

    public static bool IsFloorMet(DungeonKind kind) => FloorProgress.HighestCleared >= UnlockFloor(kind);

    // 잠긴 칸에 적는 조건. 아직 못 채운 것만 한 줄씩 적는다. 메인 던전은 조건이 없어 빈 문자열이다.
    public static string UnlockText(DungeonKind kind)
    {
        var lines = new List<string>(2);
        if (!FacilityUnlocks.IsDungeonOpen(kind))
            lines.Add(FacilityUnlocks.Requirement(VillageBlockout.Kind.Rift, FacilityUnlocks.RiftLevelFor(kind)));
        if (!IsFloorMet(kind))
            lines.Add($"{Korean(DungeonKind.Main)} {UnlockFloor(kind)}층 클리어");
        return string.Join("\n", lines);
    }

    /// before층까지 깼던 상태에서 after층까지 깨면서 새로 열린 콘텐츠를 모은다.
    /// 전투 결과창이 "새로 열렸다"고 알릴 때 쓴다 — 이미 열려 있던 층을 다시 깨면 아무것도 담기지 않는다.
    public static void CollectNewlyUnlocked(int before, int after, List<DungeonKind> results)
    {
        if (results == null) return;

        for (int i = 0; i < All.Length; i++)
        {
            int floor = UnlockFloor(All[i]);
            // 층은 채웠어도 시공의 틈 레벨이 모자라면 아직 열린 것이 아니다.
            if (floor > 0 && before < floor && after >= floor && FacilityUnlocks.IsDungeonOpen(All[i])) results.Add(All[i]);
        }
    }
}
