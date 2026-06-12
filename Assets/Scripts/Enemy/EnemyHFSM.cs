using System;
using System.Collections.Generic;
public class EnemyHFSM
{
    private readonly Dictionary<string, EnemyStateBase> _states = new();
    private EnemyStateBase _current;

    public string CurrentStateName => _current?.GetType().Name ?? "None";

    public event Action<string, string> OnStateChanged;

    public void Register(string key, EnemyStateBase state, EnemyBlackboard bb)
    {
        state.Init(bb);
        _states[key] = state;
    }

    public void Start(string initialKey)
    {
        if (!_states.TryGetValue(initialKey, out var s)) return;
        _current = s;
        _current.OnEnter();
    }

    public void Transition(string targetKey)
    {
        if (!_states.TryGetValue(targetKey, out var next)) return;

        string fromName = _current?.GetType().Name;
        _current?.OnExit();
        _current = next;
        _current.OnEnter();
        OnStateChanged?.Invoke(fromName, targetKey);
    }

    public void Update()
    {
        if (_current == null) return;
        _current.OnUpdate();
        string next = _current.OnTransition();
        if (next != null) Transition(next);
    }
}
