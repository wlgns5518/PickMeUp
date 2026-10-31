// 사제가 아군에게 미리 보호막을 걸어 주는 동작.
//
// 치유(HealBehavior)와 나눠 둔 이유는 두 기술이 쓰이는 시점이 정반대이기 때문이다.
// 치유는 이미 깎인 것을 되돌리는 일이라 늦으면 죽고, 보호막은 아직 오지 않은 결정타를
// 미리 받아 두는 일이라 늦으면 아무 의미가 없다. 대상을 고르는 기준도 반대다 —
// 치유는 HP가 낮은 쪽, 보호막은 적이 많이 붙은 쪽이다(UnitRegistry.FindShieldTarget).
//
// 이 동작의 성질은 치유와 같다: 시전에 시간이 걸리고, 그동안 무방비이며,
// 끊기면 마력만 버린다. 사제가 전열 뒤에 서 있어야 하는 이유가 여기서도 같다.
public class ShieldBehavior : SupportCastBehavior
{
    public ShieldBehavior(UnitController context) : base(context)
    {
    }

    protected override void Begin() => unit.BeginShieldCast();
    protected override void Complete() => unit.CompleteShield();
    protected override void Cancel() => unit.CancelShieldCast();
}
