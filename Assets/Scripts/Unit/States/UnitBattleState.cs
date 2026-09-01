using UnityEngine;

public abstract class UnitBattleState : State<UnitController>
{
    protected UnitBattleState(UnitController context) : base(context)
    {
    }

    // 모션 길이로 재는 타이머가 써야 하는 시간.
    //
    // 히트스톱이 걸리면 Animator의 재생 배속이 잠깐 떨어진다. 그런데 상태 타이머가
    // 실제 시간으로만 흐르면 모션은 아직 절반인데 상태가 먼저 끝나 다음 동작으로 튄다
    // (특히 피격·경직처럼 타격 직후에 시작되는 모션이 그 순간 정확히 눌린다).
    // 애니메이션 길이를 기준으로 잡은 타이머는 전부 이 값을 써야 한다.
    protected float AnimationDeltaTime => Time.deltaTime * context.AnimatorSpeed;

    // ---------------------------------------------------------------- 상태가 스스로 답하는 것들
    //
    // 아래 셋은 컨트롤러가 "지금 어느 상태인가"를 묻는 대신 상태가 직접 답한다.
    //
    // 예전에는 UnitController가 세 곳에서 구체 상태를 스무 개 남짓 늘어놓고 비교했다
    // (IsHoldingGround / IsTargetChangeLocked / ReceiveSharedTarget). 상태를 하나 추가하면
    // 그 세 목록을 전부 뒤져 "여기에도 넣어야 하나"를 판단해야 하는데, 컴파일러는 아무것도
    // 알려주지 않는다 — 실제로 FleeState를 넣을 때 한 곳만 고치면 됐고 나머지 둘은 손으로
    // 확인해야 했다. 다음 사람이 그 확인을 건너뛰면 그 상태만 조용히 다르게 움직인다.
    //
    // 기본값을 여기 두고 해당하는 상태에서만 뒤집으면, 답할 자리가 그 상태의 파일 안이 된다.
    // 상태를 새로 만드는 사람은 어차피 그 파일을 보고 있다.

    // 제자리에서 무언가를 하는 중인가. 참이면 NavMesh 지역 회피를 꺼서 떠밀리지 않는다
    // (TickAvoidance 주석 참조 — 이게 "계속 밀려나는" 그림의 원인이었다).
    public virtual bool HoldsGround => false;

    // 겨눌 상대를 바꾸면 안 되는 상태인가. 이미 나간 동작이 엉뚱한 쪽을 향하게 되는 것들이다.
    public virtual bool LocksTarget => false;

    // 팀이 공유해 온 표적을 받고, 하던 것을 끊어 교전 상태로 넘어가도 되는가.
    // 거짓이면 표적만 갈아 끼우고 지금 하던 동작은 그대로 끝낸다.
    public virtual bool AcceptsCombatRedirect => true;

    protected bool TrySwitchToIdleWhenNoEnemy()
    {
        if (UnitRegistry.HasLivingEnemy(context)) return false;
        context.ChangeState(context.IdleState);
        return true;
    }

    protected bool TryRefreshTarget()
    {
        if (context.HasUsableTarget()) return true;

        UnitController target = context.Scanner != null ? context.Scanner.FindTargetNow() : null;
        return context.TrySetTarget(target);
    }

    // 한 동작(피격, 방어, 스킬, 회복약, 치료, 패닉)을 끝내고 교전으로 돌아가는 꼬리.
    //
    // 상태 일곱 곳에 같은 세 줄이 복사돼 있었다. 새 동작 상태를 추가할 때마다 다시 적어야 했고,
    // "적이 전멸했는지 먼저 볼 것"을 빠뜨린 곳과 아닌 곳이 갈리기 시작했다(Hit/Block/Skill에는 없다).
    // 여기 한 곳으로 모으면서 그 검사도 전부에 걸리도록 통일한다 — 적이 남아 있지 않은데
    // 교전 상태로 돌아가 봐야 다음 프레임에 Search를 거쳐 Idle로 갈 뿐이다.
    protected void ReturnToCombat()
    {
        if (TrySwitchToIdleWhenNoEnemy()) return;

        if (!context.HasUsableTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        context.ChangeState(context.IsTargetInAttackRange() ? context.AttackState : context.ChaseState);
    }
}
