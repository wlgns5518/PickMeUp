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
        destinationTimer = 0f;
    }

    public override void Update()
    {
        if (TrySwitchToDead()) return;

        if (!TryRefreshTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        if (context.CanUseSkill())
        {
            context.ChangeState(context.SkillState);
            return;
        }

        if (context.IsTargetInAttackRange())
        {
            context.ChangeState(context.AttackState);
            return;
        }

        destinationTimer -= Time.deltaTime;
        if (destinationTimer <= 0f)
        {
            destinationTimer = context.DestinationUpdateInterval;
            float stoppingDistance = Mathf.Max(context.Stats.moveStopDistance, context.Stats.attackRange * 0.85f);
            context.MoveTo(context.GetPredictedTargetPosition(), context.Stats.runSpeed, stoppingDistance);
            context.SetMoveAnimation(context.Stats.runSpeed, true, false);
        }

        context.FaceTarget();
    }
}
