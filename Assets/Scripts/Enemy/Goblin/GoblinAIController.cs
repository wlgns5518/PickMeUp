using UnityEngine;
using UnityEngine.Events;

public class GoblinAIController : EnemyAIController
{
    private GoblinUtilityEvaluator goblinEvaluator;

    protected override IState CreateInitialState(EnemyAIContext context)
    {
        goblinEvaluator = new GoblinUtilityEvaluator(context);
        return new GoblinAliveState(context, this, goblinEvaluator);
    }

    protected override IState CreateDeadState()
        => new GoblinDeadState(context);

    protected override void OnTakeDamage()
    {
        goblinEvaluator?.NotifyHit();
        context.Play("Hit", true);
    }
}
