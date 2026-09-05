using System;

// 조건이 성립할 때만 자식을 돌리는 데코레이터.
//
// 예전 구조에서 "이 상태로 넘어가도 되는가"를 묻던 전이 조건이 전부 여기로 온다.
// 상태 파일 안에 흩어져 있던 그 조건들이 트리의 가지에 붙으면서, 우선순위와 조건이
// 한 화면에 같이 보이게 된다(UnitBehaviorTree).
//
// latch는 "한 번 시작한 동작은 스스로 끝날 때까지 조건을 다시 묻지 않는다"는 뜻이다.
// 없으면 시작하는 순간 제 조건을 무너뜨리는 동작들이 첫 프레임에 잘려 나간다 —
// 회복약은 마시는 순간 쿨다운이 걸려 CanUsePotion이 거짓이 되고, 방어는 자세를 잡는 순간
// blockCooldown 때문에 CanBlock이 거짓이 된다. 예전 상태머신에서는 "이미 그 상태면
// 다시 걸지 않는다"가 같은 일을 했다.
//
// latch가 붙어도 더 높은 우선순위 갈래는 그대로 끊고 들어온다(사망·패닉·경직).
// 잠그는 것은 "제 조건"뿐이지 트리 전체가 아니다.
public class BTGuard<TContext> : BTNode<TContext>
{
    private readonly Func<bool> condition;
    private readonly BTNode<TContext> child;
    private readonly bool latch;

    public BTGuard(TContext context, Func<bool> condition, BTNode<TContext> child, bool latch = false)
        : base(context)
    {
        this.condition = condition;
        this.child = child;
        this.latch = latch;
    }

    public override bool CanRun() => (latch && child.IsRunning) || condition();

    // 잠글지 말지는 실제로 몸을 움직이는 쪽이 정한다. 데코레이터는 그 말을 그대로 전한다.
    public override bool AllowsReprioritize => child.AllowsReprioritize;

    public override BTNode<TContext> FindRunningLeaf() => child.FindRunningLeaf();

    protected override BTStatus OnTick() => child.Tick();

    // 조건이 무너져 부모가 이 가지를 접을 때, 자식의 뒷정리까지 함께 내려간다.
    // 자식이 스스로 끝나서 여기로 온 경우에는 이미 IsRunning이 false라 아무 일도 하지 않는다.
    protected override void OnExit() => child.Abort();
}
