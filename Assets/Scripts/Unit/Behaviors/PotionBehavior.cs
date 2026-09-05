// 회복약을 마시는 동작.
// HP도 마나도 저절로 차지 않기 때문에, 전투 중 자원을 되돌릴 수 있는 유일한 수단이다.
public class PotionBehavior : UnitBehavior
{
    private float stateTimer;

    public PotionBehavior(UnitController context) : base(context)
    {
    }

    // 서서 들이켠다.
    public override bool HoldsGround => true;

    protected override void OnEnter()
    {
        // 마시는 동안은 무방비 상태다. 이동과 공격 잠금을 모두 끊어야
        // 이전 행동이 회복 모션 위로 겹쳐 이어지지 않는다.
        unit.InterruptCurrentAction();
        unit.StopMovement();

        unit.UsePotion();
        unit.TriggerPotion();
        stateTimer = unit.PotionAnimationDuration;
    }

    protected override BTStatus OnTick()
    {
        stateTimer -= AnimationDeltaTime;
        return stateTimer > 0f ? BTStatus.Running : BTStatus.Success;
    }
}
