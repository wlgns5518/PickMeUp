using UnityEngine;

// 암살자가 콤보를 끝내고 일단 빠져 있는 동작. 접촉이 끊겨 은신이 걸리면 다시 파고든다.
//
// 원작의 암살자는 전열에 서서 칼을 섞는 직군이 아니라 사각지대로 침투해 치고 빠지는
// 게릴라다. 그런데 그 리듬이 없으면 은신은 이름만 남는다 — 붙어 있는 한 계속 휘두르느라
// 숨을 틈이 자체가 없기 때문이다. 실측으로 확인했다: 손을 놓는 빈틈이 중앙값 0.1초,
// 최대 1.55초뿐이었고 은신 비율은 11%, 그러면서 파티에서 가장 많이 맞았다.
//
// 그래서 빠지는 것을 행동으로 만들었다. 여기서 벌어 놓은 거리가 은신을 켜고, 은신이
// 다음 접근을 지켜 준다(적이 겨누지 못하고 받는 피해도 줄어든다). 파고들 때는 배후
// 방위(engageAngle 180도)로 들어가므로 그 다음 콤보는 등 뒤에서 시작된다.
//
// 도주(FleeBehavior)와 다르다. 저쪽은 "안전해질 때까지" 달아나는 생존 행동이고,
// 이쪽은 다시 덤비기 위해 한 박자 물러나는 공격 행동이다 — 그래서 짧고, 끝나면
// 반드시 교전으로 돌아간다.
public class StalkBehavior : UnitBehavior
{
    private float elapsed;
    private Vector3 destination;
    private bool startFailed;

    public StalkBehavior(UnitController context) : base(context)
    {
    }

    // 빠지는 동안 겨눌 상대를 바꾸면 방향이 그 자리에서 뒤집힌다.
    public override bool LocksTarget => true;

    // 벌어 놓은 거리가 곧 이 동작의 값이다. 중간에 돌아서면 은신에 닿지 못한 채 그것만 버린다.
    public override bool AllowsReprioritize => false;

    protected override void OnEnter()
    {
        elapsed = 0f;
        startFailed = false;

        // 다음 빠지기는 반드시 새 콤보 뒤에만 나온다(MarkStalkStarted 주석 참조).
        unit.MarkStalkStarted();

        if (!unit.HasUsableTarget())
        {
            startFailed = true;
            return;
        }

        // 가는 쪽을 보고 달린다. 등을 보이기로 한 참이라 회전은 에이전트에게 맡긴다.
        unit.SetCodeDrivenFacing(false);

        Vector3 away = unit.transform.position - unit.CurrentTarget.Position;
        away.y = 0f;
        if (away.sqrMagnitude <= 0.0001f) away = -unit.transform.forward;
        away.Normalize();

        // 벽에 막히면 뚫린 쪽으로 튼다. 구석에 몰렸으면 빠지기를 포기하고 계속 싸운다 —
        // 벽에 붙어 굳어 있느니 그 편이 낫다.
        Vector3 resolved;
        if (!unit.TryFindRetreatSpot(away, unit.Stats.stalkDistance, out resolved, out destination))
        {
            startFailed = true;
            return;
        }

        // 출발하는 순간 몸을 돌려 둔다. 서서히 도는 동안 달리기 모션이 앞으로 재생되면
        // 그 구간에 발이 눈에 띄게 미끄러진다(FleeBehavior와 같은 이유).
        unit.SnapFacing(resolved);
        unit.MoveTo(destination, unit.Stats.runSpeed);
        unit.SetMoveAnimationFromGroundSpeed(true);
    }

    protected override BTStatus OnTick()
    {
        if (startFailed) return BTStatus.Failure;

        if (!unit.HasUsableTarget()) return BTStatus.Failure;

        unit.SetMoveAnimationFromGroundSpeed(true);
        elapsed += AnimationDeltaTime;

        // 최소 시간은 채운다. 빠지자마자 돌아서면 물러난 것으로 보이지 않고 잔떨림이 된다.
        if (elapsed < unit.Stats.stalkMinDuration) return BTStatus.Running;

        // 접촉이 끊겨 그림자에 들었다 — 이제 파고들 차례다. 여기가 이 동작의 목적이다.
        if (unit.IsStealthed) return BTStatus.Success;

        // 목적지에 닿았는데도 못 숨었거나(적이 따라붙었다) 시간이 다 됐으면 그냥 돌아간다.
        // 계속 빠져 있어 봐야 아무것도 못 한다.
        if (elapsed >= unit.Stats.stalkMaxDuration || unit.HasReachedDestination(destination))
        {
            return BTStatus.Success;
        }

        return BTStatus.Running;
    }
}
