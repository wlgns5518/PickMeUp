// TContext는 멤버 시그니처에 쓰이지 않지만, StateMachine<TContext>가 다른 컨텍스트의
// 상태를 받아들이지 못하도록 타입으로 구분해 주는 역할을 한다.
public interface IState<TContext>
{
    void Enter();
    void Update();
    void Exit();
}
