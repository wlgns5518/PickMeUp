public interface IState
{
    void Enter();
    void Update();
    void Exit();
}

public interface IState<TContext> : IState
{
}
