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
