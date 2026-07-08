using UnityEngine;

public abstract class HFSMRunner<TContext> : MonoBehaviour
{
    private StateMachine<TContext> rootStateMachine;
    private bool hasStarted;
    protected TContext context;

    protected abstract TContext CreateContext();
    protected abstract IState<TContext> CreateInitialState(TContext context);

    protected virtual void Awake()
    {
        context = CreateContext();

        if (context == null)
        {
            Debug.LogError($"{GetType().Name}: Context 생성 실패");
            return;
        }

        rootStateMachine = new StateMachine<TContext>();
    }

    protected virtual void Start()
    {
        hasStarted = true;
        InitializeRootState();
    }

    protected virtual void Update()
    {
        rootStateMachine?.Update();
    }

    public void ChangeRootState(IState<TContext> newState)
    {
        if (rootStateMachine == null)
        {
            Debug.LogError($"{GetType().Name}: rootStateMachine이 없어 상태를 변경할 수 없습니다.");
            return;
        }

        rootStateMachine.ChangeState(newState);
    }

    public IState<TContext> CurrentRootState => rootStateMachine?.CurrentState;

    protected virtual void OnEnable()
    {
        if (hasStarted)
        {
            InitializeRootState();
        }
    }

    protected virtual void OnDisable()
    {
        rootStateMachine?.Clear();
    }

    protected virtual void OnDestroy()
    {
        rootStateMachine?.Clear();
    }

    private void InitializeRootState()
    {
        if (rootStateMachine == null)
        {
            Debug.LogError($"{GetType().Name}: rootStateMachine이 null입니다.");
            return;
        }

        IState<TContext> initialState = CreateInitialState(context);

        if (initialState == null)
        {
            Debug.LogError($"{GetType().Name}: InitialState 생성 실패");
            return;
        }

        rootStateMachine.Initialize(initialState);
    }
}
