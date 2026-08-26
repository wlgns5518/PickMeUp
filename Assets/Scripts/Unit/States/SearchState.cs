public class SearchState : UnitBattleState
{
    public SearchState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        // 여기서는 가는 쪽을 보고 달린다. 회전은 NavMeshAgent에게 돌려준다.
        context.SetCodeDrivenFacing(false);
        context.StopMovement();
        if (TrySwitchToIdleWhenNoEnemy()) return;

        if (!context.HasUsableTarget())
        {
            context.ClearTarget();
        }

        if (TrySwitchToTarget()) return;
        if (TryJoinFight()) return;
        TrySwitchToMove();
    }

    public override void Update()
    {
        if (TrySwitchToIdleWhenNoEnemy()) return;

        if (TrySwitchToTarget()) return;
        if (TryJoinFight()) return;
        TrySwitchToMove();
    }

    // 시야에 다음 적이 없으면 싸움이 벌어지고 있는 자리로 걸어간다.
    //
    // 예전에는 곧바로 배회(TrySetRoamDestination)로 떨어졌다. 적을 잡은 유닛은 다음 적이
    // 탐지 범위(8~14m) 밖인 경우가 흔해서, 아군이 아직 싸우고 있는데 혼자 제자리를 맴돌았다.
    //
    // 목적지에 닿기 전에 적이 시야에 들어오면 MoveState가 스캐너로 잡아 평소대로 교전에 넘긴다.
    // 닿은 뒤에도 못 찾으면 다시 이 상태로 돌아와 그때의 전선으로 다시 잡는다 —
    // 목적지를 한 번 찍고 마는 것이라 상대가 움직여도 따라붙는 비용이 들지 않는다.
    private bool TryJoinFight()
    {
        UnitController rally = UnitRegistry.FindRallyEnemy(context);
        if (rally == null) return false;

        context.SetMoveDestination(rally.transform.position);
        context.ChangeState(context.MoveState);
        return true;
    }

    private bool TrySwitchToTarget()
    {
        UnitController target = context.HasUsableTarget() ? context.CurrentTarget : null;
        if (target == null && context.Scanner != null)
        {
            target = context.Scanner.Target;
        }

        if (target != null)
        {
            if (!context.TrySetTarget(target)) return false;

            if (context.CanEvade())
            {
                context.ChangeState(context.EvadeState);
                return true;
            }

            if (context.CanBlock())
            {
                context.ChangeState(context.BlockState);
                return true;
            }

            context.ChangeState(context.ChaseState);
            return true;
        }

        return false;
    }

    private bool TrySwitchToMove()
    {
        if (context.HasMoveDestination)
        {
            context.ChangeState(context.MoveState);
            return true;
        }

        if (context.TrySetRoamDestination())
        {
            context.ChangeState(context.MoveState);
            return true;
        }

        return false;
    }
}
