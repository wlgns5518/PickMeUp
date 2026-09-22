using UnityEngine;

public enum EnhanceOutcome
{
    Success,   // 한 단계 올랐다
    Fail,      // 단계는 그대로다
    Destroyed, // 장비가 사라졌다
}

// 장비 강화 — 골드를 내고 강화 단계를 한 단계씩 올린다.
//
// 2026-09-22 사용자 결정:
//   - +10까지. 성공 확률은 +0→+1이 100%이고 단계마다 10%씩 떨어진다(+9→+10은 10%).
//   - +5 이상에서 시도하면 파괴 확률이 붙는다. +5→+6이 1.5%이고 단계마다 1.5%씩 오른다(+9→+10은 7.5%).
//   - 파괴되지 않은 실패는 단계를 그대로 둔다(내려가지 않는다).
// 파괴 확률은 실패 안에서 떼어 낸다. +9→+10이면 성공 10%, 파괴 7.5%, 유지 82.5%다.
//
// 한 단계에 능력치 +3%(제안값, +10이면 x1.30). 등급 배율(E 0.80 ~ S 1.80)에 곱한다 — 강화는 등급 차이를
// 뒤집지 못하고 같은 등급 안에서 더 좋게 만드는 정도다. 조정은 PowerPerLevel 한 곳에서.
public static class EquipmentEnhancement
{
    public const int MaxLevel = 10;
    public const float PowerPerLevel = 0.03f;

    private const float SuccessDropPerLevel = 0.10f;
    public const int DestroyFromLevel = 5;
    private const float DestroyPerLevel = 0.015f;

    public static int ClampLevel(int level) => Mathf.Clamp(level, 0, MaxLevel);

    public static float Multiplier(int level) => 1f + PowerPerLevel * ClampLevel(level);

    public static bool IsMaxed(OwnedEquipment item) => item != null && item.Level >= MaxLevel;

    // level에서 한 단계 올릴 확률(0~1). 이미 끝까지 올랐으면 0.
    public static float SuccessChance(int level)
    {
        if (level >= MaxLevel) return 0f;
        return Mathf.Clamp01(1f - SuccessDropPerLevel * Mathf.Max(0, level));
    }

    // level에서 시도했다가 장비를 잃을 확률(0~1).
    public static float DestroyChance(int level)
    {
        if (level < DestroyFromLevel || level >= MaxLevel) return 0f;
        return DestroyPerLevel * (level - DestroyFromLevel + 1);
    }

    public static long Cost(OwnedEquipment item) =>
        item == null ? 0 : GameEconomy.EnhanceGold(item.Grade, item.Level);

    // roll은 0~1의 주사위. 테스트가 확률 경계를 직접 찌를 수 있게 밖에서 받는다.
    public static EnhanceOutcome Resolve(int level, float roll)
    {
        float success = SuccessChance(level);
        if (roll < success) return EnhanceOutcome.Success;
        if (roll < success + DestroyChance(level)) return EnhanceOutcome.Destroyed;
        return EnhanceOutcome.Fail;
    }

    /// 골드를 내고 한 번 강화한다. 시도하지 못했으면(끝까지 올랐거나 골드가 모자라면) false와 이유를 돌려준다 —
    /// 그때는 골드도 빠지지 않는다. 시도했으면 결과가 무엇이든 true다. 파괴되면 장비는 창고에서 사라진다.
    public static bool TryEnhance(OwnedEquipment item, out EnhanceOutcome outcome, out string reason) =>
        TryEnhance(item, Random.value, out outcome, out reason);

    public static bool TryEnhance(OwnedEquipment item, float roll, out EnhanceOutcome outcome, out string reason)
    {
        outcome = EnhanceOutcome.Fail;
        reason = null;

        if (item == null || !EquipmentInventory.Contains(item))
        {
            reason = "무기창고에 없는 장비입니다.";
            return false;
        }

        if (IsMaxed(item))
        {
            reason = $"이미 +{MaxLevel}까지 강화했습니다.";
            return false;
        }

        if (!PlayerAccount.TrySpend(Currency.Gold, Cost(item)))
        {
            reason = "골드가 부족합니다.";
            return false;
        }

        outcome = Resolve(item.Level, roll);
        switch (outcome)
        {
            case EnhanceOutcome.Success:
                EquipmentInventory.SetLevel(item, item.Level + 1);
                break;
            case EnhanceOutcome.Destroyed:
                EquipmentInventory.Remove(item);
                break;
        }
        return true;
    }
}
