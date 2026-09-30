using System;
using System.Collections.Generic;

// 마을 시설의 레벨(1~3). 영지 관리 화면에서 젬을 내고 올린다.
//
// 입구일 뿐이다. 레벨과 올리는 규칙은 FacilityLevelStore가, 세이브와의 연결은 GameServices가 든다.
// 건물 모양(FacilityBuilding의 레벨 파츠)은 VillageBlockout이 이 값을 읽어 맞추고, Changed를 듣고 바로 바꾼다.
public static class FacilityLevels
{
    public const int MinLevel = FacilityLevelStore.MinLevel;
    public const int MaxLevel = FacilityLevelStore.MaxLevel;

    private static FacilityLevelStore State => GameServices.FacilityState;

    /// 어느 시설이 몇 레벨이 되었는지.
    public static event Action<VillageBlockout.Kind, int> Changed
    {
        add => State.Changed += value;
        remove => State.Changed -= value;
    }

    public static int Get(VillageBlockout.Kind kind) => State.Get(kind);

    public static bool IsMaxed(VillageBlockout.Kind kind) => State.IsMaxed(kind);

    /// 다음 레벨로 올리는 값. 끝까지 올랐으면 0.
    public static long NextCost(VillageBlockout.Kind kind) => State.NextCost(kind);

    /// 젬을 내고 한 레벨 올린다. 못 올리면 false와 화면에 그대로 띄울 이유 — 그때는 젬도 그대로다.
    public static bool TryUpgrade(VillageBlockout.Kind kind, out string reason) => State.TryUpgrade(kind, out reason);

    public static void Restore(IEnumerable<KeyValuePair<VillageBlockout.Kind, int>> saved) => State.Restore(saved);

    public static void Forget() => State.Forget();

    public static void Snapshot(List<KeyValuePair<VillageBlockout.Kind, int>> results) => State.Snapshot(results);
}
