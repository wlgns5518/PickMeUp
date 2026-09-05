using UnityEngine;

// 트리 하나를 들고 매 프레임 뿌리부터 돌린다.
//
// 예전 StateMachine이 하던 일 중 남은 것은 "매 프레임 한 번 돌린다"뿐이다. 전이를 모아
// 적용하던 ResolvePending도, 재귀를 막던 전이 횟수 상한도 필요 없어졌다 — 트리는 한 틱에
// 위에서 아래로 한 번만 내려가므로 전이가 전이를 부르는 구조 자체가 없다.
public class BehaviorTree<TContext>
{
    private readonly BTNode<TContext> root;

    // 지금 실제로 몸을 움직이고 있는 잎. 바깥에서 "지금 무엇을 하는 중인가"를 물을 때 쓴다.
    public BTNode<TContext> RunningLeaf { get; private set; }

    public BehaviorTree(BTNode<TContext> root)
    {
        if (root == null) Debug.LogError("BehaviorTree 생성 실패: root가 null입니다.");
        this.root = root;
    }

    public void Tick()
    {
        if (root == null) return;

        root.Tick();
        RunningLeaf = root.FindRunningLeaf();
    }

    // 하던 일을 그 자리에서 접는다. 다음 틱은 아무것도 진행 중이 아닌 상태에서 다시 고른다.
    //
    // 바깥에서 들어오는 개입(피격으로 하던 동작을 끊기, 팀이 표적을 넘겨줘 교전을 다시 잡기)이
    // 여기로 온다. 예전에는 그것들이 ChangeState(AttackState)로 특정 상태를 지목했는데,
    // 지목한 상태가 진입하자마자 다시 판단해서 다른 곳으로 튀는 일이 잦았다. 트리에서는
    // 지목할 필요가 없다 — 접어 두면 다음 틱에 뿌리부터 다시 고른다.
    public void Abort()
    {
        root?.Abort();
        RunningLeaf = null;
    }
}
