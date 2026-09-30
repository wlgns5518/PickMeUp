using System;
using System.Collections.Generic;
using UnityEngine;

// 마을 시설의 레벨(1~3). 영지 관리 화면과 기능 해금(FacilityUnlocks), 건물 모양(VillageBlockout)이 이것으로 본다.
public interface IFacilityLevels
{
    int Get(VillageBlockout.Kind kind);
    bool IsMaxed(VillageBlockout.Kind kind);

    /// 다음 레벨로 올리는 값. 끝까지 올랐으면 0.
    long NextCost(VillageBlockout.Kind kind);

    /// 값을 내고 한 레벨 올린다. 못 올리면 false와 화면에 그대로 띄울 이유 — 그때는 값도 그대로다.
    bool TryUpgrade(VillageBlockout.Kind kind, out string reason);

    /// 어느 시설이 몇 레벨이 되었는지.
    event Action<VillageBlockout.Kind, int> Changed;
}

// 레벨은 오르기만 한다. 레벨로 여는 기능(FacilityUnlocks)은 해금 상태를 따로 저장하지 않고 이 값만 보므로,
// 한 번 열린 기능이 다시 잠기는 일이 없다 — 층 진행도(FloorProgress)와 같은 성질이다.
//
// 값을 내는 쪽은 지갑(IWallet)이다. 예전에는 PlayerAccount(정적 클래스)를 직접 불러서, 레벨 규칙만
// 시험하려 해도 실제 지갑과 세이브 파일이 함께 움직였다.
public sealed class FacilityLevelStore : PersistentState, IFacilityLevels
{
    public const int MinLevel = 1;
    public const int MaxLevel = FacilityBuilding.MaxLevel;

    private readonly IWallet wallet;
    private readonly Dictionary<VillageBlockout.Kind, int> levels = new Dictionary<VillageBlockout.Kind, int>();

    public event Action<VillageBlockout.Kind, int> Changed;

    public FacilityLevelStore(IWallet wallet)
    {
        this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
    }

    public int Get(VillageBlockout.Kind kind)
    {
        EnsureLoaded();
        return levels.TryGetValue(kind, out int level) ? level : MinLevel;
    }

    public bool IsMaxed(VillageBlockout.Kind kind) => Get(kind) >= MaxLevel;

    public long NextCost(VillageBlockout.Kind kind) =>
        IsMaxed(kind) ? 0 : GameEconomy.FacilityUpgradeCost(Get(kind) + 1);

    public bool TryUpgrade(VillageBlockout.Kind kind, out string reason)
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
        if (!wallet.TrySpend(GameEconomy.FacilityUpgradeCurrency, cost))
        {
            reason = $"젬이 부족합니다. ({TextFormat.Amount(cost)} 필요)";
            return false;
        }

        levels[kind] = level + 1;
        RaiseCommitted();
        Changed?.Invoke(kind, level + 1);
        return true;
    }

    // ---- 세이브 연동 ------------------------------------------------------

    // 파일에서 읽은 값을 얹는다. 저장은 하지 않는다(방금 읽은 것을 다시 쓸 이유가 없다).
    public void Restore(IEnumerable<KeyValuePair<VillageBlockout.Kind, int>> saved)
    {
        MarkLoaded();
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
    public void Forget()
    {
        levels.Clear();
        MarkUnloaded();
    }

    // 저장할 때 부른다. 1레벨은 적지 않는다(없으면 1로 읽힌다).
    public void Snapshot(List<KeyValuePair<VillageBlockout.Kind, int>> results)
    {
        EnsureLoaded();
        results.Clear();
        foreach (KeyValuePair<VillageBlockout.Kind, int> pair in levels)
            if (pair.Value > MinLevel) results.Add(pair);
    }
}
