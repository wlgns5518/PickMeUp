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

    // 이번 빠지기가 스스로 끝났는가. 중간에 잘린 것과 구분해야 한다 — 아래 OnExit 참조.
    private bool completed;

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
        completed = false;

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
        if (startFailed)
        {
            completed = true;
            return BTStatus.Failure;
        }

        if (!unit.HasUsableTarget())
        {
            completed = true;
            return BTStatus.Failure;
        }

        unit.SetMoveAnimationFromGroundSpeed(true);
        elapsed += AnimationDeltaTime;

        // 최소 시간은 채운다. 빠지자마자 돌아서면 물러난 것으로 보이지 않고 잔떨림이 된다.
        if (elapsed < unit.Stats.stalkMinDuration) return BTStatus.Running;

        // 접촉이 끊겨 그림자에 들었다 — 이제 파고들 차례다. 여기가 이 동작의 목적이다.
        if (unit.IsStealthed)
        {
            completed = true;
            return BTStatus.Success;
        }

        // 목적지에 닿았는데도 못 숨었거나(적이 따라붙었다) 시간이 다 됐으면 그냥 돌아간다.
        // 계속 빠져 있어 봐야 아무것도 못 한다.
        if (elapsed >= unit.Stats.stalkMaxDuration || unit.HasReachedDestination(destination))
        {
            completed = true;
            return BTStatus.Success;
        }

        return BTStatus.Running;
    }

    // 이번 콤보의 빠지기를 소진 처리한다 — 스스로 끝났을 때만이다.
    //
    // 예전에는 이걸 OnEnter에서 했다. 그래서 빠지는 도중에 한 대 맞으면(피격 리액션은 트리
    // 위쪽 가지라 무엇이든 끊고 들어온다) 그 자리에서 빠지기 자격이 사라졌고, 리액션이 끝난
    // 뒤 트리가 다시 고를 때 ShouldStalk이 거짓이라 곧바로 공격 가지로 떨어졌다 —
    // 등을 보이고 물러나던 암살자가 반쯤 물러난 자리에서 홱 돌아 다시 덤벼드는 그림이 그것이다.
    //
    // 암살자는 파티에서 가장 많이 맞는 유닛이라(이 파일 첫 주석의 실측) 사실상 매번 그랬다.
    // 잘린 빠지기는 아직 끝나지 않은 것으로 두면 리액션 뒤에 이어서 물러난다.
    protected override void OnExit()
    {
        if (completed) unit.MarkStalkStarted();
    }
}
