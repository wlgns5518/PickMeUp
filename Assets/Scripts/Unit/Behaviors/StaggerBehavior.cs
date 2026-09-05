using UnityEngine;

// 자세가 통째로 무너져 아무것도 못 하는 동작.
//
// 피격 리액션(HitBehavior)과 나눈 이유는 성격이 다르기 때문이다:
//  - 피격은 "한 대 맞아 잠깐 끊긴 것"이다. 피격 모션 하나 길이(0.3초 안팎)로 끝나고
//    곧바로 교전으로 돌아간다.
//  - 이쪽은 "무너진 것"이다. 방패가 젖혀졌거나(가드 브레이크) 내지른 공격을 통째로 흘려
//    맞았을 때(퍼펙트 가드에 당한 쪽) 들어오고, 그 몇 초가 상대에게 열린 진짜 빈틈이 된다.
//    이 동안 받는 피해는 staggerDamageMultiplier만큼 커진다.
//
// 시간은 무너뜨린 쪽이 정한다(UnitController.Stagger의 인자). 동작 객체는 유닛마다
// 하나씩 재사용되므로 진입에 인자를 넘길 수 없어, 컨텍스트에 실어 두고 여기서 꺼내 쓴다.
public class StaggerBehavior : UnitBehavior
{
    private float stateTimer;

    public StaggerBehavior(UnitController context) : base(context)
    {
    }

    // 자세가 무너져 스스로 움직이지 못한다.
    public override bool HoldsGround => true;

    public override bool AcceptsCombatRedirect => false;

    protected override void OnEnter()
    {
        unit.ConsumeStagger();

        unit.InterruptCurrentAction();
        unit.StopMovement();
        // 방패가 젖혀진 것과 그냥 무너진 것은 모션이 다르다.
        unit.TriggerStagger(unit.StaggerFromGuardBreak);

        // 무너져 있는 시간은 무너뜨린 쪽이 정한 값 그대로 쓴다.
        //
        // 모션 길이(Stunned 2초, BlockBreak 0.97초)와 max를 잡고 싶은 유혹이 있는데, 그러면
        // 클립을 갈아 끼울 때마다 밸런스가 따라 흔들린다. 대신 stats.staggerDuration의 기본값을
        // 가장 긴 리액션 클립(BlockBreak)보다 넉넉하게 잡아 두고, 남는 시간은 마지막 프레임이
        // 유지되게 둔다 — 무너진 자세로 서 있는 그림이라 오히려 맞다.
        stateTimer = Mathf.Max(0.3f, unit.PendingStaggerDuration);
    }

    protected override BTStatus OnTick()
    {
        // 히트스톱으로 애니메이션이 눌려 있는 동안은 이 타이머도 같이 느려져야 한다.
        stateTimer -= AnimationDeltaTime;
        return stateTimer > 0f ? BTStatus.Running : BTStatus.Success;
    }
}
