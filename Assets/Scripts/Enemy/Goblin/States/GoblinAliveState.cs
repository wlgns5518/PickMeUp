using UnityEngine;

public class GoblinAliveState : State<EnemyAIContext>
{
    private GoblinAIController runner;
    private GoblinUtilityEvaluator evaluator;

    private GoblinIdleState idleState;
    private GoblinPatrolState patrolState;
    private GoblinChaseState chaseState;
    private GoblinAttackState attackState;
    private GoblinHitState hitState;

    public GoblinAliveState(
        EnemyAIContext context,
        GoblinAIController runner,
        GoblinUtilityEvaluator evaluator) : base(context)
    {
        this.runner = runner;
        this.evaluator = evaluator;

        idleState = new GoblinIdleState(context, this);
        patrolState = new GoblinPatrolState(context, this);
        chaseState = new GoblinChaseState(context, this);
        attackState = new GoblinAttackState(context, this);
        hitState = new GoblinHitState(context, this);

        InitSubStateMachine(idleState);
    }

    public override void Enter() => base.Enter();

    public override void Update()
    {
        evaluator.Tick(Time.deltaTime);

        if (context.IsDead)
        {
            runner.GoToDead();
            return;
        }

        if (CurrentSubState == hitState)
        {
            base.Update();
            return;
        }

        if (CurrentSubState == attackState && attackState.IsMotionPlaying)
        {
            base.Update();
            return;
        }

        UpdateTransitions();
        base.Update();
    }

    private void UpdateTransitions()
    {
        GoblinAction action = evaluator.Evaluate();

        switch (action)
        {
            case GoblinAction.Hit:
                if (CurrentSubState != hitState) GoToHit();
                break;
            case GoblinAction.Attack:
                if (CurrentSubState != attackState) GoToAttack();
                break;
            case GoblinAction.Chase:
                if (CurrentSubState != chaseState) GoToChase();
                break;
            case GoblinAction.Patrol:
                if (CurrentSubState != patrolState) GoToPatrol();
                break;
            default:
                if (CurrentSubState != idleState) GoToIdle();
                break;
        }
    }

    public override void Exit() => base.Exit();

    public void GoToIdle()
    {
        if (CurrentSubState != idleState)
            ChangeSubState(idleState);
    }

    public void GoToPatrol()
    {
        if (CurrentSubState != patrolState)
            ChangeSubState(patrolState);
    }

    public void GoToChase()
    {
        if (CurrentSubState != chaseState)
            ChangeSubState(chaseState);
    }

    public void GoToAttack()
    {
        if (CurrentSubState != attackState)
            ChangeSubState(attackState);
    }

    public void GoToHit()
    {
        if (CurrentSubState != hitState)
            ChangeSubState(hitState);
    }

    public void FinishHit()
    {
        GoToIdle();
    }
}
