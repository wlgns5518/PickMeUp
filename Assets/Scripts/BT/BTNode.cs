// 행동 트리의 노드 하나.
//
// 예전 IState와 하는 일이 겹쳐 보이지만 책임이 다르다. 상태는 "다음에 어디로 갈지"까지
// 스스로 정했다(Update 안에서 ChangeState를 불렀다). 그래서 전이 그래프가 열아홉 개
// 파일에 흩어졌고, "지금 여기서 스킬이 나갈 수 있나"를 알려면 그 파일들을 다 열어야 했다.
//
// 노드는 그 결정을 하지 않는다. 자기 일만 하고 끝났는지 아닌지(Success/Failure/Running)만
// 답한다. 다음에 무엇을 할지는 부모가 정하고, 그 부모들이 곧 트리다 — 전이 그래프 전체가
// UnitBehaviorTree 한 파일에 그림으로 남는다.
//
// 대신 상태가 갖고 있던 값어치 하나는 그대로 가져온다: Enter/Exit이 짝을 이루는 것.
// 방어 자세를 내리고(BlockBehavior), 도약 중이던 몸을 땅에 내려놓고(LeapAttackBehavior),
// 영창을 닫는(CastBehavior) 뒷정리는 반드시 불려야 하므로, 중간에 잘려 나가는 경우까지
// 포함해 OnExit이 한 번은 돌도록 Tick/Abort 두 곳에서만 IsRunning을 만진다.
public abstract class BTNode<TContext>
{
    protected TContext context;

    protected BTNode(TContext context)
    {
        this.context = context;
    }

    // 지난 틱에 Running을 돌려줬는가. 즉 지금 이 노드가 하던 일이 남아 있는가.
    public bool IsRunning { get; private set; }

    public BTStatus Tick()
    {
        if (!IsRunning) OnEnter();

        BTStatus status = OnTick();
        if (status == BTStatus.Running)
        {
            IsRunning = true;
            return status;
        }

        IsRunning = false;
        OnExit();
        return status;
    }

    // 더 높은 우선순위 갈래에 자리를 내주고 하던 일을 접는다.
    //
    // 셀렉터는 새 갈래를 시작하기 전에 반드시 이것을 먼저 부른다. 순서가 뒤집히면
    // (새 동작의 OnEnter가 먼저 돌면) 방어 자세를 올린 채로 영창이 시작되거나,
    // 공중에 뜬 모델을 내려놓기 전에 다음 동작이 위치를 잡는 일이 생긴다.
    public void Abort()
    {
        if (!IsRunning) return;

        IsRunning = false;
        OnExit();
    }

    // 부모가 "들어가 볼 만한가"를 묻는다. 실제로 자식을 돌려 보기 전에 답해야 하는 질문이라
    // Tick과 따로 있다 — 이게 없으면 조건을 확인하는 것만으로 OnEnter가 불려 버린다.
    public virtual bool CanRun() => true;

    // 이미 시작한 이 갈래를, 형제 우선순위를 다시 따져서 끊어도 되는가.
    //
    // 거짓이면 이 갈래를 들고 있는 셀렉터는 앞의 형제를 다시 보지 않고 그대로 이어 간다.
    // 시작하는 순간 이미 값을 치른 동작 — 스킬은 쿨다운과 마나, 도약은 이미 떠 버린 몸,
    // 방어는 잡아 놓은 자세 — 이 그 사이에 조건이 조금 바뀌었다고 잘려 나가지 않게 한다.
    // 예전 구조에서 그 상태들의 Update에 다른 전이 검사가 아예 없던 것과 같은 뜻인데,
    // 그때는 "일부러 안 넣은 것"과 "빠뜨린 것"이 구분되지 않았다.
    //
    // 잠그는 것은 자기가 속한 셀렉터 안에서뿐이다. 합성 노드는 언제나 참을 돌려주므로
    // 위쪽 계층(사망·패닉·경직·피격)은 무엇이 돌고 있든 그대로 끊고 들어온다.
    public virtual bool AllowsReprioritize => true;

    // 지금 실제로 몸을 움직이고 있는 잎 노드. 컨트롤러가 "제자리를 지키는 중인가",
    // "표적을 바꿔도 되는가"를 물을 때 답하는 자리다(예전 UnitController.CurrentBattleState).
    public virtual BTNode<TContext> FindRunningLeaf() => IsRunning ? this : null;

    protected virtual void OnEnter()
    {
    }

    protected abstract BTStatus OnTick();

    protected virtual void OnExit()
    {
    }
}
