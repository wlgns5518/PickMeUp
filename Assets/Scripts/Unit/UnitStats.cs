using System;
using UnityEngine;

[Serializable]
public class UnitStats
{
    [Header("Health")]
    public int maxHp = 100;
    public int currentHp = 100;

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
    public float attackCooldown = 0.8f;
    public float attackAnimationDuration = 1.0f;
    public float skillCooldown = 5.0f;
    public float skillAnimationDuration = 1.0f;
    public float hitAnimationDuration = 0.35f;

    [Header("Defense")]
    public float evadeRange = 2.5f;
    public float blockDuration = 0.8f;
    public float blockCooldown = 1.5f;
    public float knockbackDistance = 0.6f;
    public float knockbackDuration = 0.12f;
    [Range(0f, 1f)] public float blockDamageReduction = 0.5f;

    public bool IsDead => currentHp <= 0;

    public void ResetHp()
    {
        currentHp = Mathf.Max(1, maxHp);
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

        currentHp = Mathf.Max(0, currentHp - finalDamage);
    }
}
