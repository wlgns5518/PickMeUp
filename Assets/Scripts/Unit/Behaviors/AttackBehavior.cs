// 사거리 안에서 칼을 섞는다. 콤보를 돌리고, 스윙 사이에는 간격을 재며 옆으로 돈다.
//
// 예전 AttackState의 절반은 TrySwitchToBetterState — if 열한 개짜리 전이 판단이었다.
// 그게 전부 트리로 올라가고(UnitBehaviorTree.Engage) 여기에는 실제 동작만 남았다.
//
// 이 동작은 스스로 끝나지 않는다(항상 Running). 사거리 밖으로 나가면 가지의 조건이
// 무너져 추격으로 넘어가고, 방어·후퇴·영창·스킬은 위쪽 형제가 끊고 들어간다.
public class AttackBehavior : UnitBehavior
{
    public AttackBehavior(UnitController context) : base(context)
    {
    }

    // 제자리에서 휘두른다. 그 사이 밀려나면 발이 땅을 딛지 않은 채 미끄러진다.
    public override bool HoldsGround => true;

    // 이미 나간 칼은 되돌리지 못한다. 스윙 사이의 틈에서만 갈아탄다.
    public override bool LocksTarget => true;

    public override bool AcceptsCombatRedirect => false;

    protected override void OnEnter()
    {
        // 옆으로 돌면서도 상대를 봐야 한다 — 진행 방향과 90도까지 어긋나므로 회전은 코드가 잡는다.
        unit.SetCodeDrivenFacing(true);
        unit.StopMovement();
        // StopMovement는 평소 Idle(긴장을 푼 자세)로 떨어진다. 교전에 들어선 참이므로
        // 곧바로 전투 대기 자세로 바꿔 잡는다.
        unit.PlayCombatIdle();
    }

    protected override BTStatus OnTick()
    {
        if (unit.IsAttackAnimationLocked)
        {
            // 휘두르는 중에는 몸을 거의 돌리지 않는다.
            //
            // 예전에는 여기서도 평소 회전 속도(720도/초)로 돌렸다. 공격 클립은 제자리에서
            // 베는 동작인데 그 위에서 몸이 홱 돌아가니, 발이 땅에 붙지 않고 미끄러지는 것이
            // 그대로 보였다. 겨누는 일은 휘두르기 전에 끝나 있어야 하고(attackFacingTolerance),
            // 내지른 뒤에는 상대가 움직인 만큼만 조금 따라간다.
            unit.FaceTargetWhileAttacking();

            // 준비 동작 동안은 타깃 쪽으로 조금 파고든다. 예전에는 StopMovement로 완전히
            // 못 박고 휘둘렀기 때문에, 사거리 경계에서 시작한 스윙이 눈에 보이게 허공을 갈랐다.
            // 이미 교전 간격에 서 있으면 한 발도 움직이지 않는다(UpdateAttackLunge 참조).
            unit.UpdateAttackLunge();
            return BTStatus.Running;
        }

        unit.FaceTarget();

        // 클립은 끝났지만 아직 다음 스윙의 호흡이 남은 구간. 여기가 예전에는 통째로 비어 있었다 —
        // 사거리에 들어가면 그 자리에 못 박혀 마주 보고 계속 때리기만 했다. 이제 이 시간에
        // 간격을 재고 옆으로 돈다. 칼싸움이 서로 재는 시간으로 이루어져 있는 이유가 이것이다.
        if (!unit.IsSwingReady)
        {
            unit.UpdateCombatFootwork();
            return BTStatus.Running;
        }

        // 휘두르지 못하는 동안은 자리를 잡는다.
        //
        // 마법사가 여기로 온다. 평타가 없으니 CanAttack이 늘 거짓이고, 예전 코드는 그때 아무것도
        // 하지 않아서 마법 쿨다운을 기다리는 내내 못 박힌 듯 서 있었다. 마주 보고 간격을 재는
        // 편이 낫다 — 마법사에게도 거리는 목숨이다.
        // (아직 몸을 다 돌리지 못한 근접 유닛도 이 갈래로 오는데, 그쪽도 서 있는 것보다 낫다.)
        if (!TryAttack()) unit.UpdateCombatFootwork();
        return BTStatus.Running;
    }

    // 지금 실제로 휘둘렀는가. 휘두르지 못했으면 부르는 쪽이 그 시간에 자리를 잡는다.
    private bool TryAttack()
    {
        if (!unit.CanAttack()) return false;

        unit.TriggerAttack();
        return true;
    }
}
