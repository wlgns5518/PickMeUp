using UnityEngine;

// 아직 칼이 닿지 않는 거리에서 몸을 던져 붙는 한 수.
//
// 공격과 나눈 이유는 이동이 딸려 있기 때문이다. 공격은 제자리에서 휘두르고 준비 동작 동안
// 반 발 파고드는 것이 전부라, 접근은 전적으로 추격의 몫이다. 도약은 그 접근을 통째로
// 건너뛴다 — 그래서 "쫓아가는 중"과 "휘두르는 중" 어느 쪽에도 얹을 수 없고, 클립 진행도에
// 맞춰 몸을 미는 짧은 동작 하나가 따로 필요하다.
//
// 짐승처럼 싸우는 적만 쓴다(UnitStats.leapAttackRange 주석 참조). 사람 직군은 사거리까지
// 걸어 들어가 방위를 잡고 겨루는 쪽이라 기본값 0으로 꺼져 있다.
public class LeapAttackBehavior : UnitBehavior
{
    private float elapsed;
    private float duration;

    public LeapAttackBehavior(UnitController context) : base(context)
    {
    }

    // 뛰어오른 방향은 이미 정해졌다. 공중에서 타깃을 바꿔 봐야 엉뚱한 쪽으로 착지한다.
    public override bool LocksTarget => true;

    public override bool AcceptsCombatRedirect => false;

    // 이미 뜬 몸은 도로 내려놓을 수 없다. 착지까지는 다른 판단을 하지 않는다.
    public override bool AllowsReprioritize => false;

    protected override void OnEnter()
    {
        // 도약 방향은 뛰어오르는 순간에 정해진다. 뜬 뒤에는 상대를 따라 휘지 않는다 —
        // 공중에서 방향을 바꾸는 도약은 피할 수 없는 도약이라, 옆으로 빠지면 헛뛰어야 한다.
        // 그래서 회전 주도권만 코드가 쥐고, 실제로 돌리는 것은 발이 땅에 있는 동안뿐이다.
        unit.SetCodeDrivenFacing(true);
        unit.StopMovement();
        unit.FaceTarget();

        elapsed = 0f;
        duration = Mathf.Max(0.05f, unit.LeapAttackAnimationDuration);
        unit.TriggerLeapAttack();
    }

    protected override BTStatus OnTick()
    {
        // 히트스톱으로 클립이 눌리는 동안은 몸도 같이 멈춰야 모션과 위치가 어긋나지 않는다.
        elapsed += AnimationDeltaTime;
        float normalized = Mathf.Clamp01(elapsed / duration);

        // 아직 발이 땅에 있는 동안(웅크림)에만 조준을 다듬는다.
        if (normalized < unit.LeapLaunchRatio) unit.FaceTarget();

        unit.UpdateLeap(normalized);

        return elapsed < duration ? BTStatus.Running : BTStatus.Success;
    }

    protected override void OnExit()
    {
        // 공중에서 끊겨도(피격·사망·패닉) 모델은 반드시 땅으로 내려놓는다.
        unit.EndLeap();
    }
}
