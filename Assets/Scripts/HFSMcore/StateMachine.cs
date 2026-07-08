using UnityEngine;

public class StateMachine<TContext>
{
    private IState<TContext> currentState;

    public IState<TContext> CurrentState => currentState;

    public void Initialize(IState<TContext> initialState)
    {
        if (initialState == null)
        {
            Debug.LogError("StateMachine Initialize 실패: initialState가 null입니다.");
            return;
        }

        Clear();
        currentState = initialState;
        currentState.Enter();
    }

    public void ChangeState(IState<TContext> newState)
    {
        if (newState == null)
        {
            Debug.LogError("StateMachine ChangeState 실패: newState가 null입니다.");
            return;
        }

        if (currentState == newState)
            return;

        currentState?.Exit();
        currentState = newState;
        currentState.Enter();
    }

    public void Update()
    {
        currentState?.Update();
    }

    public void Clear()
    {
        currentState?.Exit();
        currentState = null;
    }
}
