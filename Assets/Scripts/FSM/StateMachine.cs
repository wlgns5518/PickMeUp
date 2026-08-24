using UnityEngine;

// 전이를 즉시 실행하지 않고 한 곳에서 모아 적용하는 상태머신.
//
// 예전에는 ChangeState가 그 자리에서 Exit/Enter를 불렀다. 그런데 Enter 안에서 다시
// ChangeState를 부르는 상태가 여럿이라(Idle.Enter→Search, Search.Enter→Chase, Chase.Enter→Attack)
// 전이가 그대로 재귀 호출로 쌓였다. 지금 값들로는 3~4단에서 멈추지만, 서로를 부르는 상태
// 두 개가 생기는 순간 스택이 무너지고 원인도 콜스택 수백 줄에 묻힌다.
//
// 그래서 ChangeState는 "다음 상태 요청"만 남기고, 실제 Exit/Enter는 ResolvePending이
// 루프로 처리한다. 재귀가 구조적으로 불가능해지고, 한 번에 몇 단까지 전이할지 상한을 걸 수
// 있어 무한 왕복이 스택오버플로우 대신 로그 한 줄로 잡힌다.
public class StateMachine<TContext>
{
    // 한 번의 해소에서 허용하는 전이 횟수. 진입 연쇄가 정상적으로 3~4단이라 넉넉히 잡되,
    // 두 상태가 서로를 부르는 순환은 반드시 걸리도록 유한하게 둔다.
    private const int MaxTransitionsPerResolve = 8;

    private TContext context;
    private IState<TContext> currentState;
    private IState<TContext> pendingState;
    private bool hasPending;

    // 어느 상태에 있든 먼저 검사하는 전이. 배열 순서가 곧 우선순위이고, 한 번에 하나만 걸린다.
    // 예전에는 이것들이 상태마다 복사돼 있거나(사망 검사가 12개 상태에)
    // 아예 UnitController.Update로 빠져나가 있어서, 전이 그래프 전체를 한눈에 볼 수 없었다.
    private GlobalTransition<TContext>[] globalTransitions;

    public IState<TContext> CurrentState => currentState;

    public void Initialize(TContext owner, IState<TContext> initialState,
        GlobalTransition<TContext>[] globals = null)
    {
        if (initialState == null)
        {
            Debug.LogError("StateMachine Initialize 실패: initialState가 null입니다.");
            return;
        }

        Clear();
        context = owner;
        globalTransitions = globals;
        currentState = initialState;
        currentState.Enter();
        ResolvePending();
    }

    // 즉시 전이하지 않고 요청만 남긴다. 같은 해소 구간에서 여러 번 불리면 마지막 요청이 이긴다.
    // 부르는 쪽은 예전과 똑같이 ChangeState 후 곧바로 return하면 된다.
    public void ChangeState(IState<TContext> newState)
    {
        if (newState == null)
        {
            Debug.LogError("StateMachine ChangeState 실패: newState가 null입니다.");
            return;
        }

        pendingState = newState;
        hasPending = true;
    }

    public void Update()
    {
        // 바깥에서 들어온 요청(피격, 사망)을 Update보다 먼저 반영한다.
        // 그러지 않으면 이미 끊겼어야 할 상태의 Update가 한 번 더 돌아
        // 맞고 있는 도중에 공격이 나가거나, 죽은 뒤에 한 대 더 때리는 일이 생긴다.
        ResolvePending();

        // 전역 전이도 같은 이유로 상태의 Update보다 먼저 본다.
        if (TryFireGlobalTransition()) ResolvePending();

        currentState?.Update();
        ResolvePending();
    }

    // 위에서부터 훑어 처음 성립하는 것 하나만 건다.
    // 순서가 우선순위라는 뜻이다 — 사망이 패닉보다, 패닉이 회복약보다 앞선다.
    private bool TryFireGlobalTransition()
    {
        if (globalTransitions == null) return false;

        for (int i = 0; i < globalTransitions.Length; i++)
        {
            IState<TContext> target = globalTransitions[i].Evaluate(context, currentState);
            if (target == null || target == currentState) continue;

            ChangeState(target);
            return true;
        }

        return false;
    }

    private void ResolvePending()
    {
        int applied = 0;

        while (hasPending)
        {
            IState<TContext> next = pendingState;
            hasPending = false;
            pendingState = null;

            // 같은 상태로의 재요청은 무시한다(예전 ChangeState의 동작 그대로).
            if (next == currentState) continue;

            if (++applied > MaxTransitionsPerResolve)
            {
                Debug.LogError($"StateMachine: 한 번에 {MaxTransitionsPerResolve}번을 넘겨 전이했습니다. " +
                               $"두 상태가 서로를 부르고 있는지 확인하세요. (중단 시점 요청: {next.GetType().Name})");
                return;
            }

            currentState?.Exit();
            currentState = next;
            currentState.Enter();
        }
    }

    // 재초기화 시 이전 상태의 Exit을 보장하고, 남아 있던 요청도 함께 버린다.
    private void Clear()
    {
        currentState?.Exit();
        currentState = null;
        pendingState = null;
        hasPending = false;
        globalTransitions = null;
    }
}
