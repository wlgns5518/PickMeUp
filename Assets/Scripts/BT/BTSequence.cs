// 자식을 앞에서부터 차례로 끝내는 노드. 하나라도 실패하면 거기서 접는다.
//
// 기억을 갖는다 — 자식 하나가 Running을 돌려주면 다음 틱에는 그 자식부터 이어 간다.
// 이게 없으면 앞 자식이 매 틱 다시 돌아, "갈 곳을 정한다 → 그리로 간다"가 프레임마다
// 목적지를 새로 뽑는 동작이 되어 버린다(예전 SearchState → MoveState의 관계가 그것이다).
//
// 위쪽 갈래에 자리를 내주고 접힐 때는 진행 위치도 함께 버린다. 다시 돌아오면
// 처음부터 — 다시 갈 곳을 정하는 것부터 — 시작하는 것이 맞다.
public class BTSequence<TContext> : BTNode<TContext>
{
    private readonly BTNode<TContext>[] children;
    private int index;

    public BTSequence(TContext context, params BTNode<TContext>[] children) : base(context)
    {
        this.children = children;
    }

    public override BTNode<TContext> FindRunningLeaf()
    {
        if (!IsRunning || index >= children.Length) return null;
        return children[index].FindRunningLeaf();
    }

    protected override void OnEnter() => index = 0;

    protected override BTStatus OnTick()
    {
        while (index < children.Length)
        {
            BTStatus status = children[index].Tick();
            if (status == BTStatus.Running) return BTStatus.Running;

            if (status == BTStatus.Failure)
            {
                index = 0;
                return BTStatus.Failure;
            }

            index++;
        }

        index = 0;
        return BTStatus.Success;
    }

    protected override void OnExit()
    {
        for (int i = 0; i < children.Length; i++) children[i].Abort();
        index = 0;
    }
}
