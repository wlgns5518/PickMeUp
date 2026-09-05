using UnityEngine;

// 전투 유닛이 실제로 하는 동작 하나. 트리의 잎이다.
//
// 예전 UnitBattleState에서 "다음에 어디로 갈지"를 결정하던 부분(ReturnToCombat,
// TrySwitchToIdleWhenNoEnemy, TryRefreshTarget)은 전부 사라졌다. 그 판단은 이제 트리가
// 하고, 동작은 자기 일이 끝났는지만 답한다 — 그래서 동작 파일에는 조건문이 거의 남지 않는다.
//
// 남은 것은 상태가 스스로 답해야 했던 세 가지다. 컨트롤러가 "지금 어느 동작 중인가"를
// 구체 타입으로 비교하는 대신 여기에 물어보는 구조는 그대로 가져간다 — 새 동작을 만드는
// 사람이 어차피 보고 있는 파일에서 답하게 하려는 것이 원래 이유였고, 트리가 되어도 같다.
public abstract class UnitBehavior : BTNode<UnitController>
{
    protected UnitController unit => context;

    protected UnitBehavior(UnitController context) : base(context)
    {
    }

    // 모션 길이로 재는 타이머가 써야 하는 시간.
    //
    // 히트스톱이 걸리면 Animator의 재생 배속이 잠깐 떨어진다. 그런데 타이머가 실제 시간으로만
    // 흐르면 모션은 아직 절반인데 동작이 먼저 끝나 다음으로 튄다(특히 피격·경직처럼 타격
    // 직후에 시작되는 모션이 그 순간 정확히 눌린다).
    // 애니메이션 길이를 기준으로 잡은 타이머는 전부 이 값을 써야 한다.
    protected float AnimationDeltaTime => Time.deltaTime * context.AnimatorSpeed;

    // 제자리에서 무언가를 하는 중인가. 참이면 NavMesh 지역 회피를 꺼서 떠밀리지 않는다
    // (TickAvoidance 주석 참조 — 이게 "계속 밀려나는" 그림의 원인이었다).
    public virtual bool HoldsGround => false;

    // 겨눌 상대를 바꾸면 안 되는 동작인가. 이미 나간 동작이 엉뚱한 쪽을 향하게 되는 것들이다.
    public virtual bool LocksTarget => false;

    // 팀이 공유해 온 표적을 받고, 하던 것을 끊어 교전을 다시 잡아도 되는가.
    // 거짓이면 표적만 갈아 끼우고 지금 하던 동작은 그대로 끝낸다.
    public virtual bool AcceptsCombatRedirect => true;
}
