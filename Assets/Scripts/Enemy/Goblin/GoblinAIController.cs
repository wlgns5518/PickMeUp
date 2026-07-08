using UnityEngine;
using UnityEngine.Events;

public class GoblinAIController : EnemyAIController
{
    private GoblinUtilityEvaluator goblinEvaluator;
    private GoblinAliveState aliveState;

    protected override IState CreateInitialState(EnemyAIContext context)
    {
        goblinEvaluator = new GoblinUtilityEvaluator(context);
        aliveState = new GoblinAliveState(context, this, goblinEvaluator);
        return aliveState;
    }

    protected override IState CreateDeadState()
        => new GoblinDeadState(context);

    protected override void OnTakeDamage()
    {
        goblinEvaluator?.NotifyHit();
        aliveState?.GoToHit();
    }
}
