using UnityEngine;

public class ChaseState : UnitBattleState
{
    private float destinationTimer;

    public ChaseState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        destinationTimer = context.DestinationUpdateInterval;

        if (!TryRefreshTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        if (TrySwitchToActionState()) return;
        UpdateDestination();
        context.FaceTarget();
    }

    public override void Update()
    {
        if (TrySwitchToDead()) return;

        if (!TryRefreshTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        if (TrySwitchToActionState()) return;

        destinationTimer -= Time.deltaTime;
        if (destinationTimer <= 0f)
        {
            destinationTimer = context.DestinationUpdateInterval;
            UpdateDestination();
        }

        context.FaceTarget();
    }

    private bool TrySwitchToActionState()
    {
        if (context.CanUseSkill())
        {
            context.ChangeState(context.SkillState);
            return true;
        }

        if (context.IsTargetInAttackRange())
        {
            context.ChangeState(context.AttackState);
            return true;
        }

        return false;
    }

    private void UpdateDestination()
    {
        float stoppingDistance = Mathf.Max(context.Stats.moveStopDistance, context.Stats.attackRange * 0.85f);
        context.MoveTo(context.GetPredictedTargetPosition(), context.Stats.runSpeed, stoppingDistance);
        context.SetMoveAnimation(context.Stats.runSpeed, true, false);
    }
}
