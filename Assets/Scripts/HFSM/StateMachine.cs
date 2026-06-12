public class StateMachine<TContext>
{
    private IState currentState;
    public IState CurrentState => currentState;

    public void Initialize(IState initialState)
    {
        currentState = initialState;
        currentState.Enter();
    }

    public void ChangeState(IState newState)
    {
        currentState?.Exit();
        currentState = newState;
        currentState?.Enter();
    }

    public void Update()
    {
        currentState?.Update();
    }
}
