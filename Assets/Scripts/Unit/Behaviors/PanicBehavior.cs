// 패닉/빈사/붕괴 상태의 유닛이 머무는 동작. 원작 설정 그대로 "행동불가"다 —
// 이동도 공격도 하지 않고, 적이 때리는 동안 아무것도 못 한 채 서 있는다.
//
// 조건은 트리 위쪽 가지에 있고(Emotion.IsActionBlocked) 잠그지 않는다. 그래서 감정이
// 풀리는 순간 가지가 무너지며 교전으로 돌아간다 — 예전에는 이 동작이 매 프레임 스스로
// 그 검사를 했다.
public class PanicBehavior : UnitBehavior
{
    public PanicBehavior(UnitController context) : base(context)
    {
    }

    protected override void OnEnter()
    {
        // 굳어 버린 유닛이 뒤늦게 움찔하지 않도록 밀린 반응 요청은 버린다.
        unit.ClearPendingReactions();

        // 공격 잠금과 이동 경로를 모두 끊어야 패닉 도중에 이전 행동이 이어지지 않는다.
        unit.InterruptCurrentAction();
        unit.StopMovement();
    }

    protected override BTStatus OnTick() => BTStatus.Running;
}
