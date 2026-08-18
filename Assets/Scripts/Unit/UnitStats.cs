using System;
using UnityEngine;

[Serializable]
public class UnitStats
{
    [Header("Health")]
    public int maxHp = 100;
    public int currentHp = 100;

    [Header("Mana")]
    // 전투 중에는 회복되지 않는 고정 자원. 전투를 시작할 때 가득 채워지고 그걸로 끝이라,
    // 한 판에 쓸 수 있는 스킬 횟수가 maxMana / skillManaCost로 정해진다.
    [Tooltip("스킬 자원. 전투 중 회복되지 않는다. 0이면 스킬을 아예 쓸 수 없으니 주의.")]
    public int maxMana = 50;
    public int currentMana = 50;
    [Tooltip("스킬 1회 소모량. 쿨다운과 별개로 이 값이 모자라면 그 전투에서는 더 쓸 수 없다.")]
    public int skillManaCost = 20;

    [Header("Potion")]
    // HP도 마나도 저절로 차지 않는다. 전투 중 회복 수단은 회복약뿐이다.
    // 기본값은 0 — 회복약은 공짜로 주어지지 않는다. 인벤토리나 인스펙터에서 명시적으로 넣어야 한다.
    [Tooltip("전투 시작 시 들고 있는 회복약 개수. 전투 중에는 보충되지 않는다.")]
    [Min(0)] public int potionCount;
    [Tooltip("회복약 1개가 회복시키는 최대 HP 비율.")]
    [Range(0f, 1f)] public float potionHealHpRatio = 0.4f;
    [Tooltip("회복약 1개가 회복시키는 최대 마나 비율.")]
    [Range(0f, 1f)] public float potionHealManaRatio = 0.5f;
    [Tooltip("HP가 이 비율 아래로 떨어지면 회복약을 마신다.")]
    [Range(0f, 1f)] public float potionHpThreshold = 0.35f;
    [Tooltip("마나가 스킬 1회분에 못 미칠 때, HP가 이 비율 아래면 회복약을 마신다. 회복량을 통째로 버리지 않기 위한 조건.")]
    [Range(0f, 1f)] public float potionManaTriggerHpRatio = 0.8f;
    [Tooltip("회복약을 연달아 들이켜지 않도록 하는 간격.")]
    public float potionCooldown = 6f;

    [Header("Movement")]
    public float walkSpeed = 2f;
    public float runSpeed = 4f;
    public float jumpPower = 5f;
    public float moveStopDistance = 0.25f;

    [Header("Detection")]
    public float attackRange = 1.6f;
    public float detectRange = 8f;

    [Header("Damage")]
    public int attackDamage = 8;
    public int skillDamage = 24;

    [Header("Cooldowns")]
    public float skillCooldown = 5.0f;

    [Header("Defense")]
    public float evadeRange = 2.5f;
    public float blockDuration = 0.8f;
    public float blockCooldown = 1.5f;
    public float knockbackDistance = 0.6f;
    public float knockbackDuration = 0.12f;
    [Range(0f, 1f)] public float blockDamageReduction = 0.5f;
    [Tooltip("상시 피해 경감. 방어 자세와 별개로 항상 적용된다. 탱커와 방패가 올려준다.")]
    [Range(0f, 0.9f)] public float damageReduction;

    [Header("Heal (Support)")]
    [Tooltip("서포터 직업만 켜진다. 부상당한 아군을 회복시킬 수 있는지 여부.")]
    public bool canHealAllies;
    public int healAmount = 25;
    public float healRange = 8f;
    public float healCooldown = 6f;
    public int healManaCost = 15;
    [Tooltip("HP가 이 비율 이하인 아군을 치료 대상으로 본다.")]
    [Range(0f, 1f)] public float healTargetHpRatio = 0.7f;

    public bool IsDead => currentHp <= 0;
    public bool HasPotion => potionCount > 0;

    // 최대치를 넘지 않게 회복하고 실제로 회복된 양을 돌려준다.
    public int Heal(int amount)
    {
        if (amount <= 0 || IsDead) return 0;

        int before = currentHp;
        currentHp = Mathf.Min(maxHp, currentHp + amount);
        return currentHp - before;
    }
    public float HpRatio => currentHp / Mathf.Max(1f, maxHp);

    // 프리팹의 stats는 모든 인스턴스가 공유하는 하나의 객체다.
    // 층별로 값을 키우려면 반드시 복사본을 만들어 써야 원본이 오염되지 않는다.
    public UnitStats Clone()
    {
        return (UnitStats)MemberwiseClone();
    }

    public void ResetHp()
    {
        currentHp = Mathf.Max(1, maxHp);
    }

    // 전투 시작 시점에만 채워진다.
    public void ResetMana()
    {
        currentMana = Mathf.Max(0, maxMana);
    }

    public bool HasMana(int cost) => currentMana >= cost;

    public void SpendMana(int cost)
    {
        currentMana = Mathf.Clamp(currentMana - Mathf.Max(0, cost), 0, maxMana);
    }

    // 회복약 1개를 소모하고 실제로 회복된 양을 돌려준다.
    // 최대치를 넘는 분은 그냥 버려진다 - 가득 찬 상태에서 마시면 낭비라는 뜻이라,
    // 언제 마실지 판단하는 쪽(UnitController.CanUsePotion)이 그걸 피하도록 되어 있다.
    public bool ConsumePotion(out int healedHp, out int healedMana)
    {
        healedHp = 0;
        healedMana = 0;
        if (potionCount <= 0 || IsDead) return false;

        potionCount--;

        int hpBefore = currentHp;
        currentHp = Mathf.Min(maxHp, currentHp + Mathf.RoundToInt(maxHp * potionHealHpRatio));
        healedHp = currentHp - hpBefore;

        int manaBefore = currentMana;
        currentMana = Mathf.Min(maxMana, currentMana + Mathf.RoundToInt(maxMana * potionHealManaRatio));
        healedMana = currentMana - manaBefore;
        return true;
    }

    public void TakeDamage(int damage)
    {
        TakeDamage(damage, false);
    }

    public void TakeDamage(int damage, bool isBlocking)
    {
        if (IsDead) return;

        int finalDamage = Mathf.Max(0, damage);
        if (isBlocking)
        {
            finalDamage = Mathf.RoundToInt(finalDamage * (1f - blockDamageReduction));
        }

        if (damageReduction > 0f)
        {
            finalDamage = Mathf.RoundToInt(finalDamage * (1f - damageReduction));
        }

        // 경감이 겹쳐 0이 되면 단단한 유닛끼리 만났을 때 전투가 영원히 끝나지 않는다.
        // 유효타는 최소 1은 들어가게 한다.
        if (damage > 0) finalDamage = Mathf.Max(1, finalDamage);

        currentHp = Mathf.Max(0, currentHp - finalDamage);
    }
}
