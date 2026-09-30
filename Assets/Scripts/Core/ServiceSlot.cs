using System;

// 씬에 놓이거나 처음 필요할 때 생기는 서비스(전투 진행, 화면 흔들기, 피 효과 …)가 앉는 자리.
//
// 예전에는 서비스마다 static instance 필드와 등록·해제·중복 검사·자동 생성을 한 벌씩 따로 들고 있었고,
// 쓰는 쪽은 BattleManager.Instance처럼 구체 클래스를 직접 불렀다. 규칙이 조금씩 달라서(누구는 Awake에서,
// 누구는 OnEnable에서 등록하고, 누구는 없으면 만든다) 한 서비스의 버릇을 알아야 다른 서비스를 쓸 수 있었다.
// 이제 쓰는 쪽은 인터페이스만 보고, 서비스는 여기에 앉았다 내려갈 뿐이다.
public sealed class ServiceSlot<T> where T : class
{
    private readonly Func<T> create;
    private readonly T fallback;
    private T current;

    /// create: 아무도 앉아 있지 않을 때 새로 만들 방법(없으면 만들지 않는다).
    /// fallback: 만들지도 않을 때 대신 돌려줄 것 — 아무 일도 하지 않는 대역(Null Object)을 두면
    /// 쓰는 쪽이 매번 null을 확인하지 않아도 된다.
    public ServiceSlot(Func<T> create = null, T fallback = null)
    {
        this.create = create;
        this.fallback = fallback;
    }

    /// 앉아 있는 서비스. 없으면 만들고, 만들 방법도 없으면 대역(없으면 null).
    public T Current
    {
        get
        {
            if (IsAlive(current)) return current;
            current = null;

            if (create != null) current = create();
            return current ?? fallback;
        }
    }

    /// 만들지 않고 들여다본다. 앉아 있는 것이 없으면 null.
    public T Peek => IsAlive(current) ? current : null;

    /// 자리가 비어 있으면 앉힌다. 이미 다른 것이 살아 있으면 false — 씬에 둘이 놓인 경우다.
    public bool TryRegister(T service)
    {
        if (service == null) return false;
        if (IsAlive(current) && !ReferenceEquals(current, service)) return false;

        current = service;
        return true;
    }

    /// 앉아 있는 것이 이 서비스일 때만 내린다. 뒤늦게 꺼지는 옛 서비스가 새 서비스를 내리지 않게.
    public void Unregister(T service)
    {
        if (ReferenceEquals(current, service)) current = null;
    }

    public void Clear() => current = null;

    // 파괴된 컴포넌트는 인터페이스로 들고 있으면 C#의 null 검사로는 걸리지 않는다(유니티의 == 재정의를 거치지 않는다).
    private static bool IsAlive(T service) =>
        service != null && !(service is UnityEngine.Object unityObject && unityObject == null);
}
