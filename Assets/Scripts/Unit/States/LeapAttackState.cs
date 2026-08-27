using UnityEngine;

// 아직 칼이 닿지 않는 거리에서 몸을 던져 붙는 한 수.
//
// AttackState와 나눈 이유는 이동이 딸려 있기 때문이다. AttackState는 제자리에서 휘두르고
// 준비 동작 동안 반 발 파고드는 것이 전부라, 접근은 전적으로 ChaseState의 몫이다.
// 도약은 그 접근을 통째로 건너뛴다 — 그래서 "쫓아가는 중"과 "휘두르는 중" 어느 쪽에도
// 얹을 수 없고, 클립 진행도에 맞춰 몸을 미는 짧은 상태 하나가 따로 필요하다.
//
// 짐승처럼 싸우는 적만 쓴다(UnitStats.leapAttackRange 주석 참조). 사람 직군은 사거리까지
// 걸어 들어가 방위를 잡고 겨루는 쪽이라 기본값 0으로 꺼져 있다.
public class LeapAttackState : UnitBattleState
{
    private float elapsed;
    private float duration;

    public LeapAttackState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();

        if (!context.HasUsableTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        // 도약 방향은 뛰어오르는 순간에 정해진다. 뜬 뒤에는 상대를 따라 휘지 않는다 —
        // 공중에서 방향을 바꾸는 도약은 피할 수 없는 도약이라, 옆으로 빠지면 헛뛰어야 한다.
        // 그래서 회전 주도권만 코드가 쥐고, 실제로 돌리는 것은 발이 땅에 있는 동안뿐이다.
        context.SetCodeDrivenFacing(true);
        context.StopMovement();
        context.FaceTarget();

        elapsed = 0f;
        duration = Mathf.Max(0.05f, context.LeapAttackAnimationDuration);
        context.TriggerLeapAttack();
    }

    public override void Update()
    {
        // 히트스톱으로 클립이 눌리는 동안은 몸도 같이 멈춰야 모션과 위치가 어긋나지 않는다.
        elapsed += AnimationDeltaTime;
        float normalized = Mathf.Clamp01(elapsed / duration);

        // 아직 발이 땅에 있는 동안(웅크림)에만 조준을 다듬는다.
        if (normalized < context.LeapLaunchRatio) context.FaceTarget();

        context.UpdateLeap(normalized);

        if (elapsed < duration) return;

        ReturnToCombat();
    }

    public override void Exit()
    {
        // 공중에서 끊겨도(피격·사망·패닉) 모델은 반드시 땅으로 내려놓는다.
        context.EndLeap();
        base.Exit();
    }
}
