public class GoblinUtilityEvaluator : EnemyUtilityEvaluator
{
    public GoblinUtilityEvaluator(EnemyAIContext context) : base(context) { }

    public override void Tick(float deltaTime)
    {
        base.Tick(deltaTime);
    }

    public GoblinAction Evaluate()
    {
        if (IsHit) return GoblinAction.Hit;

        bool inAttack  = context.InAttackRange;
        bool inDetect  = context.InDetectRange;
        bool hasTarget = context.HasTarget;
        bool canAttack = context.CanAttack;

        GoblinAction best      = GoblinAction.None;
        float        bestScore = float.MinValue;

        void Check(GoblinAction action, float score)
        {
            if (score > bestScore)
            {
                bestScore = score;
                best = action;
            }
        }

        float attackScore = 0f;
        if (inAttack && canAttack)
            attackScore += 70f;
        else if (inAttack)
            attackScore += 20f;
        Check(GoblinAction.Attack, attackScore);

        float chaseScore = 0f;
        if (hasTarget && inDetect && !inAttack)
            chaseScore += 75f;
        Check(GoblinAction.Chase, chaseScore);

        float patrolScore = 0f;
        if (!hasTarget || !inDetect)
            patrolScore += 30f;
        Check(GoblinAction.Patrol, patrolScore);

        float idleScore = 0f;
        if (!hasTarget)
            idleScore += 10f;
        Check(GoblinAction.Idle, idleScore);

        return best;
    }
}
