using UnityEngine;

// 사제가 부상당한 아군을 회복시키는 동안 머무는 상태.
//
// 회복약과 함께 전투 중 HP를 되돌리는 유이한 수단이다.
// 적은 UnitController.CanRecoverHp에서 막히므로 이 경로도 아군 전용으로 남는다.
//
// 이 상태의 핵심은 "시간이 걸린다"는 것이다. 예전에는 Enter에서 곧바로 회복을 끝내고
// 모션만 재생했다 — 맞아서 끊겨도 이미 치료가 끝난 뒤라 잃는 것이 없었고, 그래서
// 원작의 "영창 중 완전 무방비"가 성립할 수 없었다. 지금은 마력이 먼저 나가고(BeginHealCast)
// 모션을 끝까지 재생해야 실제로 회복된다(CompleteHeal). 그 사이에 맞으면 마력만 버린다.
public class HealState : UnitBattleState
{
    private float stateTimer;

    public HealState(UnitController context) : base(context)
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

        context.BeginHealCast();
        context.TriggerHeal();
        stateTimer = context.HealAnimationDuration;
    }

    public override void Update()
    {
        stateTimer -= AnimationDeltaTime;
        if (stateTimer > 0f) return;

        // 영창을 끝까지 마쳤다. 여기서야 회복과 디스펠이 일어난다.
        context.CompleteHeal();
        ReturnToCombat();
    }

    public override void Exit()
    {
        // 끝까지 못 갔으면(피격으로 끊겼거나 죽었으면) 여기로 온다.
        // CompleteHeal이 이미 영창을 닫았으면 아무 일도 하지 않는다.
        context.CancelHealCast();
        base.Exit();
    }
}
