using UnityEngine;

// 회복약을 마시는 동안 머무는 상태.
// HP도 마나도 저절로 차지 않기 때문에, 전투 중 자원을 되돌릴 수 있는 유일한 수단이다.
//
// 진입은 PanicState와 마찬가지로 UnitController.Update가 판단한다.
// 상태마다 조건을 넣으면 새 상태를 추가할 때 빠뜨리기 쉽기 때문.
public class PotionState : UnitBattleState
{
    private float stateTimer;

    public PotionState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();

        // 마시는 동안은 무방비 상태다. 이동과 공격 잠금을 모두 끊어야
        // 이전 행동이 회복 모션 위로 겹쳐 이어지지 않는다.
        context.InterruptCurrentAction();
        context.StopMovement();

        context.UsePotion();
        context.TriggerPotion();
        stateTimer = context.PotionAnimationDuration;
    }

    public override void Update()
    {
        stateTimer -= AnimationDeltaTime;
        if (stateTimer > 0f) return;

        ReturnToCombat();
    }
}
