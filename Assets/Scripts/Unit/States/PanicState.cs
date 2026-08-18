using UnityEngine;

// 패닉/빈사/붕괴 상태의 유닛이 머무는 상태. 원작 설정 그대로 "행동불가"다 —
// 이동도 공격도 하지 않고, 적이 때리는 동안 아무것도 못 한 채 서 있는다.
//
// 진입은 개별 상태가 아니라 UnitController.Update가 감정 상태를 보고 직접 밀어넣는다.
// (모든 상태의 Update마다 패닉 체크를 넣으면 상태가 늘어날 때마다 빠뜨리기 쉽다)
public class PanicState : UnitBattleState
{
    public PanicState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        // 공격 잠금과 이동 경로를 모두 끊어야 패닉 도중에 이전 행동이 이어지지 않는다.
        context.InterruptCurrentAction();
        context.StopMovement();
    }

    public override void Update()
    {
        if (TrySwitchToDead()) return;

        // 붕괴는 회복되지 않으므로 여기서 영원히 머문다. 패닉/빈사는 풀릴 수 있다.
        if (context.Emotion != null && context.Emotion.IsActionBlocked) return;

        if (TrySwitchToIdleWhenNoEnemy()) return;

        if (!context.HasUsableTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        context.ChangeState(context.IsTargetInAttackRange() ? context.AttackState : context.ChaseState);
    }
}
