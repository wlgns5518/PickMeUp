// 위에서부터 훑어 처음으로 성립하는 갈래 하나를 고르는 노드. 트리의 우선순위가 여기서 나온다.
//
// 예전 상태머신의 GlobalTransition 배열이 하던 일과 같다 — "위에서부터 보고 한 번에 하나만"이
// 그대로 남았다. 다른 점은 그 배열이 전역 전이 다섯 개만 담을 수 있었던 반면, 여기서는
// 교전 판단 전체(방어/후퇴/영창/스킬/도약/빠지기/공격/추격)가 같은 규칙으로 줄지어 선다는 것이다.
// 예전에는 그 순서가 AttackState.TrySwitchToBetterState 안의 if 열한 개였다.
//
// reactive가 참이면 매 틱 맨 위부터 다시 본다. 그래서 아래쪽 갈래가 돌고 있어도 위쪽 조건이
// 성립하는 순간 끊고 들어온다(사망·패닉·경직이 무엇을 하고 있든 걸리는 이유).
// 거짓이면 이미 고른 갈래를 그대로 이어 간다 — 한 번 정하면 끝날 때까지 바꾸지 않아야 하는
// 선택(뒷걸음이냐 도주냐)에만 쓴다.
public class BTSelector<TContext> : BTNode<TContext>
{
    private readonly BTNode<TContext>[] children;
    private readonly bool reactive;
    private BTNode<TContext> running;

    public BTSelector(TContext context, bool reactive, params BTNode<TContext>[] children)
        : base(context)
    {
        this.reactive = reactive;
        this.children = children;
    }

    public override BTNode<TContext> FindRunningLeaf() => running != null ? running.FindRunningLeaf() : null;

    protected override BTStatus OnTick()
    {
        int start = 0;

        // 기억형이거나, 돌고 있는 갈래가 스스로 잠갔으면 앞의 형제를 다시 보지 않고 이어 간다
        // (AllowsReprioritize 주석 참조).
        if (running != null && (!reactive || !running.AllowsReprioritize))
        {
            BTNode<TContext> resumedChild = running;
            BTStatus resumed = resumedChild.Tick();
            if (resumed != BTStatus.Failure)
            {
                if (resumed != BTStatus.Running) running = null;
                return resumed;
            }

            // 이어 가던 갈래가 "할 수 없다"고 답했다(구석에 몰려 물러날 곳이 없다, 겨누던
            // 상대가 사라졌다). 여기서 통째로 실패하면 안 된다 — 그 아래 형제부터 다시 고른다.
            // 예전 상태들이 하던 일을 마치지 못했을 때 ReturnToCombat을 부르던 자리다.
            running = null;
            start = IndexOf(resumedChild) + 1;
        }

        for (int i = start; i < children.Length; i++)
        {
            BTNode<TContext> child = children[i];

            if (!child.CanRun())
            {
                // 돌고 있었는데 조건이 사라진 갈래는 여기서 접힌다(뒷정리가 불린다).
                if (child == running) running = null;
                child.Abort();
                continue;
            }

            // 새 갈래를 시작하기 전에 하던 갈래를 먼저 끝낸다. Exit이 Enter보다 앞이어야 한다.
            if (running != null && running != child)
            {
                running.Abort();
                running = null;
            }

            BTStatus status = child.Tick();
            if (status == BTStatus.Failure)
            {
                if (child == running) running = null;
                continue;
            }

            running = status == BTStatus.Running ? child : null;
            return status;
        }

        AbortRunning();
        return BTStatus.Failure;
    }

    protected override void OnExit() => AbortRunning();

    private int IndexOf(BTNode<TContext> child)
    {
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] == child) return i;
        }

        return -1;
    }

    private void AbortRunning()
    {
        if (running == null) return;

        BTNode<TContext> node = running;
        running = null;
        node.Abort();
    }
}
