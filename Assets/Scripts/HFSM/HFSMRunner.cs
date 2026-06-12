using UnityEngine;

public abstract class HFSMRunner<TContext> : MonoBehaviour
{
    private StateMachine<TContext> rootStateMachine;
    protected TContext context;

    protected abstract TContext CreateContext();
    protected abstract IState CreateInitialState(TContext context);

    protected virtual void Awake()
    {
        context = CreateContext();
        rootStateMachine = new StateMachine<TContext>();
    }

    protected virtual void Start()
    {
        if (context == null) return;
        IState initialState = CreateInitialState(context);
        rootStateMachine.Initialize(initialState);
    }

    protected virtual void Update()
    {
        rootStateMachine.Update();
    }

    public void ChangeRootState(IState newState)
    {
        rootStateMachine.ChangeState(newState);
    }

    public IState CurrentRootState => rootStateMachine.CurrentState;
}
