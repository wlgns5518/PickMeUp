using UnityEngine;

// 서포터가 부상당한 아군을 회복시키는 동안 머무는 상태.
//
// 회복약과 함께 전투 중 HP를 되돌리는 유이한 수단이다.
// 적은 UnitController.CanRecoverHp에서 막히므로 이 경로도 아군 전용으로 남는다.
public class HealState : UnitBattleState
{
    private float stateTimer;

    public HealState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();

        // 시전 중에는 무방비다. 이동과 공격 잠금을 끊어야 이전 행동이 겹쳐 이어지지 않는다.
        context.InterruptCurrentAction();
        context.StopMovement();

        context.PerformHeal();
        context.TriggerHeal();
        stateTimer = context.SkillAnimationDuration;
    }

    public override void Update()
    {
        if (TrySwitchToDead()) return;

        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f) return;

        if (TrySwitchToIdleWhenNoEnemy()) return;

        if (!context.HasUsableTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        context.ChangeState(context.IsTargetInAttackRange() ? context.AttackState : context.ChaseState);
    }
}
