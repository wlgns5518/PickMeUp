using UnityEngine;

public class PlayerAIAliveState : State<PlayerAIContext>
{
    private PlayerAIController runner;

    public PlayerAIIdleState    idleState;
    public PlayerAIPatrolState  patrolState;
    public PlayerAIChaseState   chaseState;
    public PlayerAIAttackState  attackState;
    public PlayerAIFleeState    fleeState;

    public PlayerAIAliveState(PlayerAIContext context, PlayerAIController runner) : base(context)
    {
        this.runner = runner;

        idleState   = new PlayerAIIdleState(context, this);
        patrolState = new PlayerAIPatrolState(context, this);
        chaseState  = new PlayerAIChaseState(context, this);
        attackState = new PlayerAIAttackState(context, this);
        fleeState   = new PlayerAIFleeState(context, this);

        InitSubStateMachine(idleState);
    }

    public override void Enter()
    {
        base.Enter();
    }

    public override void Update()
    {
        context.RefreshTarget();

        if (context.IsDead())
        {
            runner.GoToDead();
            return;
        }

        UpdateTransitions();
        base.Update();
    }

    private void UpdateTransitions()
    {
        // 도주 중이고 아직 HP가 낮으면 유지
        if (CurrentSubState == fleeState && !context.IsSafeHp())
            return;

        // HP 낮음 + 적 존재 → 도주
        if (context.IsLowHp() && context.target != null)
        {
            if (CurrentSubState != fleeState)
                GoToFlee();
            return;
        }

        // 적 없음 → 대기/순찰 (중단하지 않음)
        if (context.target == null)
        {
            if (CurrentSubState != idleState && CurrentSubState != patrolState)
                GoToIdle();
            return;
        }

        // 거리에 따라 공격 ↔ 추격 전환
        float distance = Vector3.Distance(context.transform.position, context.target.Position);
        if (distance <= context.AttackRange)          // ← config.attackRange → context.AttackRange
        {
            if (CurrentSubState != attackState)
                GoToAttack();
        }
        else
        {
            if (CurrentSubState != chaseState)
                GoToChase();
        }
    }

    public override void Exit()
    {
        base.Exit();
    }

    public void GoToIdle()   => ChangeSubState(idleState);
    public void GoToPatrol() => ChangeSubState(patrolState);
    public void GoToChase()  => ChangeSubState(chaseState);
    public void GoToAttack() => ChangeSubState(attackState);
    public void GoToFlee()   => ChangeSubState(fleeState);
}