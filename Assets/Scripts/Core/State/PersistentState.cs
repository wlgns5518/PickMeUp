using System;

// 세이브 파일과 이어진 게임 상태의 공통 규칙 — 처음 쓰일 때 한 번 채워지고, 바뀔 때마다 저장을 부탁한다.
//
// 상태를 든 쪽은 세이브 파일을 모른다. 무엇으로 채우고 언제 저장하는지는 조립하는 쪽(GameServices)이
// BindLoader와 Committed 구독으로 이어 준다. 예전에는 저장소가 SaveSystem을 직접 부르고 SaveSystem이 다시
// 저장소를 불러서(서로를 부르는 고리), 지갑 하나를 시험하려 해도 세이브 파일이 함께 끌려 왔다.
//
// 처음 쓰일 때 채우는 이유: 전투 씬처럼 창고 창이 없는 곳에서도 전투 정산 저장이 창고를 빈 채로
// 덮어쓰지 않으려면, 누가 먼저 읽든 파일에 있던 값이 먼저 올라와 있어야 한다.
public abstract class PersistentState
{
    private Action loader;
    private bool loaded;

    /// 사용자가 무언가를 바꿨다 — 저장할 때다. 파일에서 되살릴 때(Restore)는 울리지 않는다.
    public event Action Committed;

    /// 처음 쓰일 때 부를 채우기. 비워 두면 채우지 않는다(테스트, 세이브와 무관한 상태).
    public void BindLoader(Action load) => loader = load;

    protected void EnsureLoaded()
    {
        if (loaded) return;

        // 읽는 도중 Restore가 다시 이리로 들어오지 않도록 먼저 세운다.
        loaded = true;
        loader?.Invoke();
        OnLoaded();
    }

    /// 파일에서 채운 직후에 한 번. 채운 값에 규칙을 얹어야 하는 상태가 쓴다(시작 젬 지급 등).
    protected virtual void OnLoaded() { }

    /// 파일에서 방금 되살렸다. 다시 채우지 않는다.
    protected void MarkLoaded() => loaded = true;

    /// 세이브가 지워졌다. 다음에 쓰일 때 (빈) 파일에서 다시 채운다.
    protected void MarkUnloaded() => loaded = false;

    protected void RaiseCommitted() => Committed?.Invoke();
}
