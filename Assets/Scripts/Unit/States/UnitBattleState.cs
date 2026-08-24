public abstract class UnitBattleState : State<UnitController>
{
    protected UnitBattleState(UnitController context) : base(context)
    {
    }

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
