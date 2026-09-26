using System;
using System.Collections.Generic;
using UnityEngine;

// 마을 시설의 레벨(1~3). 영지 관리 화면에서 젬을 내고 올린다.
//
// PlayerAccount와 같은 규칙을 따른다 — 처음 쓰일 때 세이브 파일에서 스스로 읽어 오고, 바뀔 때마다 곧바로 저장한다.
// 전투 씬이 한 번도 읽지 않은 채 로스터를 저장해도 레벨을 1로 덮어쓰지 않으려면 누가 먼저 읽든
// 파일에 있던 값이 먼저 올라와 있어야 한다.
//
// 레벨은 오르기만 한다. 레벨로 여는 기능(FacilityUnlocks)은 해금 상태를 따로 저장하지 않고 이 값만 보므로,
// 한 번 열린 기능이 다시 잠기는 일이 없다 — 층 진행도(FloorProgress)와 같은 성질이다.
//
// 건물 모양(FacilityBuilding의 레벨 파츠)은 VillageBlockout이 이 값을 읽어 맞추고, Changed를 듣고 바로 바꾼다.
public static class FacilityLevels
{
    public const int MinLevel = 1;
    public const int MaxLevel = FacilityBuilding.MaxLevel;

    private static readonly Dictionary<VillageBlockout.Kind, int> levels = new Dictionary<VillageBlockout.Kind, int>();
    private static bool loaded;

    /// 어느 시설이 몇 레벨이 되었는지.
    public static event Action<VillageBlockout.Kind, int> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 이전 플레이의 값이 남지 않도록 비운다. 실제 값은 세이브에서 다시 읽는다.
        levels.Clear();
        loaded = false;
        Changed = null;
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;

        // 읽는 도중 Restore가 다시 이리로 들어오지 않도록 먼저 세운다.
        loaded = true;
        SaveSystem.LoadFacilities();
    }

    public static int Get(VillageBlockout.Kind kind)
    {
        EnsureLoaded();
        return levels.TryGetValue(kind, out int level) ? level : MinLevel;
    }

    public static bool IsMaxed(VillageBlockout.Kind kind) => Get(kind) >= MaxLevel;

    /// 다음 레벨로 올리는 값. 끝까지 올랐으면 0.
    public static long NextCost(VillageBlockout.Kind kind) =>
        IsMaxed(kind) ? 0 : GameEconomy.FacilityUpgradeCost(Get(kind) + 1);

    /// 젬을 내고 한 레벨 올린다. 못 올리면 false와 화면에 그대로 띄울 이유 — 그때는 젬도 그대로다.
    public static bool TryUpgrade(VillageBlockout.Kind kind, out string reason)
    {
        reason = null;
        if (!FacilityUnlocks.IsUpgradeable(kind))
        {
            reason = "업그레이드할 수 없는 시설입니다.";
            return false;
        }

        int level = Get(kind);
        if (level >= MaxLevel)
        {
            reason = $"이미 Lv.{MaxLevel}입니다.";
            return false;
        }

        long cost = GameEconomy.FacilityUpgradeCost(level + 1);
        if (!PlayerAccount.TrySpend(GameEconomy.FacilityUpgradeCurrency, cost))
        {
            reason = $"젬이 부족합니다. ({UiKit.Amount(cost)} 필요)";
            return false;
        }

        levels[kind] = level + 1;
        SaveSystem.SaveFacilities();
        Changed?.Invoke(kind, level + 1);
        return true;
    }

    // SaveSystem이 파일에서 읽은 값을 얹는다. 저장은 하지 않는다(방금 읽은 것을 다시 쓸 이유가 없다).
    public static void Restore(IEnumerable<KeyValuePair<VillageBlockout.Kind, int>> saved)
    {
        loaded = true;
        levels.Clear();
        if (saved != null)
        {
            foreach (KeyValuePair<VillageBlockout.Kind, int> pair in saved)
            {
                int level = Mathf.Clamp(pair.Value, MinLevel, MaxLevel);
                // 같은 시설이 두 번 적혀 있으면 높은 쪽 — 레벨은 내려가지 않는다.
                if (levels.TryGetValue(pair.Key, out int existing) && existing >= level) continue;
                levels[pair.Key] = level;
            }
        }
        foreach (KeyValuePair<VillageBlockout.Kind, int> pair in levels) Changed?.Invoke(pair.Key, pair.Value);
    }

    // 세이브를 지웠을 때. 들고 있던 값을 버리고 다음에 쓰일 때 (빈) 파일에서 다시 읽는다.
    public static void Forget()
    {
        levels.Clear();
        loaded = false;
    }

    // 저장할 때 SaveSystem이 부른다. 1레벨은 적지 않는다(없으면 1로 읽힌다).
    public static void Snapshot(List<KeyValuePair<VillageBlockout.Kind, int>> results)
    {
        EnsureLoaded();
        results.Clear();
        foreach (KeyValuePair<VillageBlockout.Kind, int> pair in levels)
            if (pair.Value > MinLevel) results.Add(pair);
    }
}
