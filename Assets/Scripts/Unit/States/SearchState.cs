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

            // 물러날지는 여기서 보지 않는다.
            //
            // 한때 여기에 CanEvade()라는 검사가 따로 있었다. 표적이 사거리의 70% 안에 있는가만
            // 보는 것이라 직군도 HP도 보지 않았고, 나머지 진입 지점 넷이 전부 역할 기반으로
            // 옮겨간 뒤에도 이 자리만 남아 있었다. 그 결과 근접 유닛(간격 임계가 0이라
            // ShouldKeepDistance가 늘 거짓)이 표적을 잃고 코앞의 다음 적을 잡으면 만피인 채로
            // 위기 도주 갈래를 타서, 등을 돌리고 달아났다가 0.35초 뒤 돌아왔다.
            //
            // ChaseState가 들어서는 순간 같은 것을 제대로 판단하므로(TrySwitchToActionState)
            // 여기서는 그냥 넘기면 된다.
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
