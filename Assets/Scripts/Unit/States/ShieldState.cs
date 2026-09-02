using UnityEngine;

// 사제가 아군에게 미리 보호막을 걸어 주는 동안 머무는 상태.
//
// 치유(HealState)와 나눠 둔 이유는 두 기술이 쓰이는 시점이 정반대이기 때문이다.
// 치유는 이미 깎인 것을 되돌리는 일이라 늦으면 죽고, 보호막은 아직 오지 않은 결정타를
// 미리 받아 두는 일이라 늦으면 아무 의미가 없다. 대상을 고르는 기준도 반대다 —
// 치유는 HP가 낮은 쪽, 보호막은 적이 많이 붙은 쪽이다(UnitRegistry.FindShieldTarget).
//
// 이 상태의 성질은 치유와 같다: 시전에 시간이 걸리고, 그동안 무방비이며,
// 끊기면 마력만 버린다. 사제가 전열 뒤에 서 있어야 하는 이유가 여기서도 같다.
public class ShieldState : UnitBattleState
{
    private float stateTimer;

    public ShieldState(UnitController context) : base(context)
    {
    }

    // 서서 시전한다.
    public override bool HoldsGround => true;

    public override void Enter()
    {
        base.Enter();

        // 시전 중에는 무방비다. 이동과 공격 잠금을 끊어야 이전 행동이 겹쳐 이어지지 않는다.
        context.InterruptCurrentAction();
        context.StopMovement();

        context.BeginShieldCast();
        context.TriggerHeal();
        stateTimer = context.HealAnimationDuration;
    }

    public override void Update()
    {
        stateTimer -= AnimationDeltaTime;
        if (stateTimer > 0f) return;

        // 끝까지 마쳤다. 여기서야 보호막이 실제로 걸린다.
        context.CompleteShield();
        ReturnToCombat();
    }

    public override void Exit()
    {
        // 끊겼으면(피격, 사망) 마력만 나간다. CompleteShield가 이미 닫았으면 아무 일도 하지 않는다.
        context.CancelShieldCast();
        base.Exit();
    }
}
