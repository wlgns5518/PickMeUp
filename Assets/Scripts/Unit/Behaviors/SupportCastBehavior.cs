// 사제가 아군에게 거는 영창(치유·보호막)의 공통 뼈대.
//
// 둘 다 서서 시전하고, 마력이 먼저 나가고(Begin), 모션을 끝까지 재생해야 효과가 일어나며(Complete),
// 그 사이에 맞아 끊기면 마력만 버린다(Cancel). 다른 것은 무엇을 거느냐뿐이다.
public abstract class SupportCastBehavior : UnitBehavior
{
    private float stateTimer;

    protected SupportCastBehavior(UnitController context) : base(context)
    {
    }

    // 서서 시전한다.
    public override bool HoldsGround => true;

    // 마력을 먼저 낸다.
    protected abstract void Begin();

    // 영창을 끝까지 마쳤다. 여기서야 효과가 실제로 일어난다.
    protected abstract void Complete();

    // 끝까지 못 갔으면(피격으로 끊겼거나 죽었으면) 부른다. Complete가 이미 영창을 닫았으면 아무 일도 하지 않아야 한다.
    protected abstract void Cancel();

    protected override void OnEnter()
    {
        // 시전 중에는 무방비다. 이동과 공격 잠금을 끊어야 이전 행동이 겹쳐 이어지지 않는다.
        unit.InterruptCurrentAction();
        unit.StopMovement();

        Begin();
        unit.TriggerHeal();
        stateTimer = unit.HealAnimationDuration;
    }

    protected override BTStatus OnTick()
    {
        stateTimer -= AnimationDeltaTime;
        if (stateTimer > 0f) return BTStatus.Running;

        Complete();
        return BTStatus.Success;
    }

    protected override void OnExit() => Cancel();
}
