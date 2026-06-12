using UnityEngine;

public class PlayerAIAliveState : State<PlayerAIContext>
{
    private PlayerAIController runner;

    // ── 그룹 스테이트 ──────────────────────────────────────
    public PlayerAIIdleGroupState   idleGroup;
    public PlayerAIMoveGroupState   moveGroup;
    public PlayerAIAttackGroupState attackGroup;
    public PlayerAIBlockGroupState  blockGroup;
    public PlayerAICrouchGroupState crouchGroup;

    public PlayerAIAliveState(PlayerAIContext context, PlayerAIController runner) : base(context)
    {
        this.runner = runner;

        idleGroup   = new PlayerAIIdleGroupState(context, this);
        moveGroup   = new PlayerAIMoveGroupState(context, this);
        attackGroup = new PlayerAIAttackGroupState(context, this);
        blockGroup  = new PlayerAIBlockGroupState(context, this);
        crouchGroup = new PlayerAICrouchGroupState(context, this);

        InitSubStateMachine(idleGroup);
    }

    public override void Enter() => base.Enter();

    public override void Update()
    {
        // 공통: 매 프레임 타겟 갱신
        context.RefreshTarget();

        // 공통: 사망 체크
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
        // 모션 중인 그룹은 외부 전환 차단
        if (CurrentSubState == blockGroup  && blockGroup.IsLocked)  return;
        if (CurrentSubState == crouchGroup && crouchGroup.IsLocked) return;
        if (CurrentSubState == attackGroup && attackGroup.IsLocked) return;

        // HP 낮음 + 적 존재 → Move (도주)
        if (context.IsLowHp() && context.target != null)
        {
            if (CurrentSubState != moveGroup)
                GoToMove();
            return;
        }

        // 도주 중 → HP 회복될 때까지 유지
        if (CurrentSubState == moveGroup && moveGroup.IsFleeing && !context.IsSafeHp())
            return;

        // 적 없음 → Idle
        if (context.target == null)
        {
            if (CurrentSubState != idleGroup)
                GoToIdle();
            return;
        }

        float distance = Vector3.Distance(context.transform.position, context.target.Position);

        // 공격 범위 내 → Attack
        if (distance <= context.AttackRange)
        {
            if (CurrentSubState != attackGroup)
                GoToAttack();
        }
        // 공격 범위 밖 → Move (추격)
        else
        {
            if (CurrentSubState != moveGroup)
                GoToMove();
        }
    }

    public override void Exit() => base.Exit();

    // ── 그룹 전환 메서드 ───────────────────────────────────
    public void GoToIdle()   => ChangeSubState(idleGroup);
    public void GoToMove()   => ChangeSubState(moveGroup);
    public void GoToAttack() => ChangeSubState(attackGroup);
    public void GoToBlock()  => ChangeSubState(blockGroup);
    public void GoToCrouch() => ChangeSubState(crouchGroup);
}
