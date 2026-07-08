using UnityEngine;

public class PlayerAIAliveState : State<PlayerAIContext>
{
    private PlayerAIController runner;
    private float nextTargetRefreshTime;
    private float fleeSuppressedUntil;
    private const float TargetRefreshInterval = 0.15f;
    private const float FleeSuppressionDuration = 2f;

    public PlayerAIIdleGroupState idleGroup;
    public PlayerAIMoveGroupState moveGroup;
    public PlayerAIAttackGroupState attackGroup;
    public PlayerAIBlockGroupState blockGroup;
    public PlayerAICrouchGroupState crouchGroup;

    public PlayerAIAliveState(PlayerAIContext context, PlayerAIController runner) : base(context)
    {
        this.runner = runner;

        idleGroup = new PlayerAIIdleGroupState(context, this);
        moveGroup = new PlayerAIMoveGroupState(context, this);
        attackGroup = new PlayerAIAttackGroupState(context, this);
        blockGroup = new PlayerAIBlockGroupState(context, this);
        crouchGroup = new PlayerAICrouchGroupState(context, this);

        InitSubStateMachine(idleGroup);
    }

    public override void Enter() => base.Enter();

    public override void Update()
    {
        if (Time.time >= nextTargetRefreshTime || (context.target != null && context.target.IsDead))
        {
            nextTargetRefreshTime = Time.time + TargetRefreshInterval;
            context.RefreshTarget();
        }

        if (context.IsDead())
        {
            runner.GoToDead();
            return;
        }

        UpdateGroupTransitions();
        base.Update();
    }

    private void UpdateGroupTransitions()
    {
        if (CurrentSubState == blockGroup && blockGroup.IsLocked) return;
        if (CurrentSubState == crouchGroup && crouchGroup.IsLocked) return;
        if (CurrentSubState == attackGroup && attackGroup.IsLocked) return;

        if (ShouldFlee())
        {
            if (CurrentSubState != moveGroup)
                GoToMove();
            else if (!moveGroup.IsFleeing)
                moveGroup.GoToFlee();
            return;
        }

        if (CurrentSubState == moveGroup && moveGroup.IsFleeing)
        {
            if (context.target != null)
                GoToCombat();
            else
                GoToIdle();
            return;
        }

        if (context.target == null)
        {
            if (CurrentSubState != idleGroup)
                GoToIdle();
            return;
        }

        float distance = (context.target.Position - context.transform.position).sqrMagnitude;
        float attackRangeSqr = context.AttackRange * context.AttackRange;

        if (distance <= attackRangeSqr)
        {
            if (CurrentSubState != attackGroup)
                GoToAttack();
        }
        else
        {
            if (CurrentSubState != moveGroup)
                GoToMove();
        }
    }

    public override void Exit() => base.Exit();

    public void GoToIdle()
    {
        if (CurrentSubState != idleGroup)
            ChangeSubState(idleGroup);
    }

    public void GoToMove()
    {
        if (CurrentSubState != moveGroup)
            ChangeSubState(moveGroup);
    }

    public void GoToAttack()
    {
        if (CurrentSubState != attackGroup)
            ChangeSubState(attackGroup);
        else
            attackGroup.GoToAttack();
    }

    public void GoToBlock()
    {
        if (CurrentSubState != blockGroup)
            ChangeSubState(blockGroup);
    }

    public void GoToCrouch()
    {
        if (CurrentSubState != crouchGroup)
            ChangeSubState(crouchGroup);
    }

    public void GoToFlee()
    {
        if (CurrentSubState != moveGroup)
            ChangeSubState(moveGroup);
        else if (moveGroup.IsFleeing)
            return;

        moveGroup.GoToFlee();
    }

    public void GoToHit()
    {
        if (CurrentSubState != attackGroup)
            ChangeSubState(attackGroup);

        attackGroup.GoToHit();
    }

    public void FinishHit()
    {
        if (ShouldFlee())
        {
            GoToFlee();
            return;
        }

        if (context.target != null)
        {
            GoToCombat();
            return;
        }

        GoToIdle();
    }

    public void FinishFlee()
    {
        fleeSuppressedUntil = Time.time + FleeSuppressionDuration;

        if (context.target != null)
        {
            GoToCombat();
            return;
        }

        GoToIdle();
    }

    private void GoToCombat()
    {
        float distance = (context.target.Position - context.transform.position).sqrMagnitude;
        float attackRangeSqr = context.AttackRange * context.AttackRange;

        if (distance <= attackRangeSqr)
            GoToAttack();
        else
            GoToMove();
    }

    public bool ShouldFlee()
    {
        return CanFlee() && context.IsLowHp() && context.target != null;
    }

    private bool CanFlee()
    {
        return Time.time >= fleeSuppressedUntil;
    }
}
