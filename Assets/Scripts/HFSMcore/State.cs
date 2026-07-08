using System;
using UnityEngine;

public abstract class State<TContext> : IState<TContext>
{
    protected TContext context;

    private StateMachine<TContext> subStateMachine;
    private Func<State<TContext>> defaultSubStateFactory;

    protected State(TContext context)
    {
        this.context = context;
    }

    protected void SetDefaultSubState(Func<State<TContext>> defaultStateFactory)
    {
        this.defaultSubStateFactory = defaultStateFactory;
        subStateMachine = new StateMachine<TContext>();
    }

    public virtual void Enter()
    {
        if (subStateMachine != null && defaultSubStateFactory != null)
        {
            State<TContext> defaultSubState = defaultSubStateFactory();

            if (defaultSubState == null)
            {
                Debug.LogError($"{GetType().Name}: defaultSubStateFactory가 null 상태를 반환했습니다.");
                return;
            }

            subStateMachine.Initialize(defaultSubState);
        }
    }

    public virtual void Update()
    {
        subStateMachine?.Update();
    }

    public virtual void Exit()
    {
        subStateMachine?.Clear();
    }

    protected void ChangeSubState(State<TContext> newState)
    {
        if (subStateMachine == null)
        {
            Debug.LogError($"{GetType().Name}: 하위 상태머신이 없어 상태를 변경할 수 없습니다.");
            return;
        }

        subStateMachine.ChangeState(newState);
    }

    public IState<TContext> CurrentSubState => subStateMachine?.CurrentState;
}
