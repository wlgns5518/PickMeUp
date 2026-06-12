public abstract class State<TContext> : IState
{
    protected TContext context;
    private StateMachine<TContext> subStateMachine;

    protected State(TContext context)
    {
        this.context = context;
    }

    protected void InitSubStateMachine(State<TContext> defaultState)
    {
        subStateMachine = new StateMachine<TContext>();
        subStateMachine.Initialize(defaultState);
    }

    public virtual void Enter()
    {
        subStateMachine?.CurrentState?.Enter();
    }

    public virtual void Update()
    {
        subStateMachine?.Update();
    }

    public virtual void Exit()
    {
        subStateMachine?.CurrentState?.Exit();
    }

    protected void ChangeSubState(State<TContext> newState)
    {
        subStateMachine?.ChangeState(newState);
    }

    public IState CurrentSubState => subStateMachine?.CurrentState;
}
