using UnityEngine;

// 지휘관의 진형 유지 명령. 지킬 자리에 서서 손 닿는 적만 상대한다.
//
// 이 동작은 싸우지 않는다. 칼이 닿는 적이 생기면 트리의 가지 조건이 뒤집혀 평소의 교전
// (방어·공격·영창)이 그 자리에서 받는다(UnitBehaviorTree.ShouldKeepHoldPosition). 여기서 하는 것은
// 싸울 상대가 없거나 자리에서 밀려났을 때 자리를 지키는 것 — 돌아오고, 손 닿는 적을 찾아 두는 것이다.
//
// 자리는 좌표 하나다. 파티원끼리 앞/뒤/좌/우를 나눠 주는 진형 슬롯이 아니라, 명령을 받은 순간
// 각자 서 있던 곳을 그대로 붙든다.
public class HoldPositionBehavior : UnitBehavior
{
    // 싸울 상대가 없는 동안 이보다 멀리 벗어나 있으면 제자리로 돌아간다.
    private const float ReturnDistance = 1.2f;

    // 돌아가는 중에는 이만큼 가까워져야 멈춘다. 떠날 때와 같은 값이면 경계에서 떨린다.
    private const float SettleDistance = 0.35f;

    public HoldPositionBehavior(UnitController context) : base(context)
    {
    }

    // 서 있는 동안은 지역 회피를 꺼서 밀리지 않는다. 돌아가는 중에는 켠다.
    public override bool HoldsGround => !unit.IsReturningToHoldAnchor;

    protected override void OnEnter()
    {
        if (unit.IsReturningToHoldAnchor) return;

        unit.SetCodeDrivenFacing(true);
        unit.StopMovement();
        unit.PlayCombatIdle();
    }

    protected override BTStatus OnTick()
    {
        float distance = unit.HoldAnchorDistance;
        bool returning = unit.IsReturningToHoldAnchor;

        if (returning ? distance > SettleDistance : distance > ReturnDistance)
        {
            if (!returning)
            {
                unit.SetReturningToHoldAnchor(true);
                unit.SetCodeDrivenFacing(false);
            }

            bool run = distance > unit.RunDistance;
            unit.MoveTo(unit.CommandAnchor, run ? unit.Stats.runSpeed : unit.Stats.walkSpeed, 0.1f);
            unit.SetMoveAnimationFromGroundSpeed(run);
            return BTStatus.Running;
        }

        if (returning)
        {
            unit.SetReturningToHoldAnchor(false);
            unit.StopMovement();
            unit.SetCodeDrivenFacing(true);
            unit.PlayCombatIdle();
        }

        // 칼이 닿는 적을 찾아 둔다. 찾으면 다음 틱에 교전 가지가 이 동작을 대신한다.
        // 자리에 돌아온 뒤에만 찾는다 — 돌아가는 길에 표적을 바꿔 봐야 그 자리에서 싸우지 않는다.
        unit.TryAcquireTargetInReach();

        // 겨눈 상대가 있으면(손은 안 닿지만) 그쪽을 본다. 등을 돌린 채 진형을 지키는 사람은 없다.
        if (unit.IsTargetValid()) unit.FaceTarget();
        return BTStatus.Running;
    }
}
