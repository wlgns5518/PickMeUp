using UnityEngine;

public class UtilityAIEvaluator
{
    private PlayerAIContext context;

    private int   lightAttackCombo;
    private float heavyAttackCooldown;
    private const float HeavyAttackCooldownMax = 6f;

    public UtilityAIEvaluator(PlayerAIContext context)
    {
        this.context = context;
    }

    public void NotifyLightAttack() => lightAttackCombo++;

    public void NotifyHeavyAttack()
    {
        lightAttackCombo    = 0;
        heavyAttackCooldown = HeavyAttackCooldownMax;
    }

    public void Tick(float deltaTime)
    {
        if (heavyAttackCooldown > 0f)
            heavyAttackCooldown -= deltaTime;
    }

    public PlayerAIAction Evaluate()
    {
        PlayerAIAction best      = PlayerAIAction.None;
        float          bestScore = float.MinValue;

        void Check(PlayerAIAction action, float score)
        {
            if (score > bestScore) { bestScore = score; best = action; }
        }

        float distance   = context.target != null
            ? Vector3.Distance(context.transform.position, context.target.Position)
            : float.MaxValue;

        float hpRatio    = context.stats != null && context.stats.MaxHp > 0
            ? (float)context.stats.Hp / context.stats.MaxHp
            : 1f;

        bool inAttackRange  = distance <= context.AttackRange;
        bool enemyAttacking = IsEnemyAttacking();

        // Flee
        {
            float score = 0f;
            if (context.IsLowHp() && context.target != null) score += 95f;
            Check(PlayerAIAction.Flee, score);
        }

        // Block
        {
            float score = 0f;
            if (enemyAttacking)  score += 90f;
            if (hpRatio >= 0.6f) score += 10f;
            Check(PlayerAIAction.Block, score);
        }

        // HeavyAttack
        {
            float score = 0f;
            if (inAttackRange && heavyAttackCooldown <= 0f)
            {
                score += 60f;
                if (lightAttackCombo >= 2) score += 20f;
            }
            Check(PlayerAIAction.HeavyAttack, score);
        }

        // Kick
        {
            float score = 0f;
            if (inAttackRange && heavyAttackCooldown > 0f) score += 50f;
            Check(PlayerAIAction.Kick, score);
        }

        // CrouchAttack
        {
            float score = 0f;
            if (inAttackRange) score += 70f;
            Check(PlayerAIAction.CrouchAttack, score);
        }

        // LightAttack
        {
            float score = 0f;
            if (inAttackRange) score += 30f;
            Check(PlayerAIAction.LightAttack, score);
        }

        return best;
    }

    private bool IsEnemyAttacking()
    {
        if (context.target == null)
            return false;

        for (int i = 0; i < context.projectiles.Count; i++)
        {
            var p = context.projectiles[i];

            if (p == null || p.TargetObject == null)
                continue;

            float dist = Vector3.Distance(
                p.Position,
                context.transform.position);

            if (dist <= context.config.dangerSkillRadius)
                return true;
        }

        return false;
    }
}
