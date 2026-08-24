// 상태의 공통 베이스. context 보관과 no-op 기본 구현만 제공하므로
// 파생 상태는 실제로 필요한 콜백만 override하면 된다.
public abstract class State<TContext> : IState<TContext>
{
    protected TContext context;

    protected State(TContext context)
    {
        this.context = context;
    }

    public virtual void Enter()
    {
    }

    public virtual void Update()
    {
    }

    public virtual void Exit()
    {
    }
}
