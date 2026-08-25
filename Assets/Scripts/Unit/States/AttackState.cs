using UnityEngine;

public class AttackState : UnitBattleState
{
    public AttackState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        // 옆으로 돌면서도 상대를 봐야 한다 — 진행 방향과 90도까지 어긋나므로 회전은 코드가 잡는다.
        context.SetCodeDrivenFacing(true);
        context.StopMovement();
        // StopMovement는 평소 Idle(긴장을 푼 자세)로 떨어진다. 교전에 들어선 참이므로
        // 곧바로 전투 대기 자세로 바꿔 잡는다.
        context.PlayCombatIdle();

        if (TrySwitchToBetterState()) return;
        context.FaceTarget();
        TryAttack();
    }

    public override void Update()
    {
        if (TrySwitchToBetterState()) return;

        context.FaceTarget();

        if (context.IsAttackAnimationLocked)
        {
            // 준비 동작 동안은 타깃 쪽으로 조금 파고든다. 예전에는 StopMovement로 완전히
            // 못 박고 휘둘렀기 때문에, 사거리 경계에서 시작한 스윙이 눈에 보이게 허공을 갈랐다.
            context.UpdateAttackLunge();
            return;
        }

        // 클립은 끝났지만 아직 다음 스윙의 호흡이 남은 구간. 여기가 예전에는 통째로 비어 있었다 —
        // 사거리에 들어가면 그 자리에 못 박혀 마주 보고 계속 때리기만 했다. 이제 이 시간에
        // 간격을 재고 옆으로 돈다. 칼싸움이 서로 재는 시간으로 이루어져 있는 이유가 이것이다.
        if (!context.IsSwingReady)
        {
            context.UpdateCombatFootwork();
            return;
        }

        TryAttack();
    }

    private bool TrySwitchToBetterState()
    {
        // 휘두르는 중에는 상태를 바꾸지 않는다.
        //
        // 예전에는 타깃이 쓰러진 그 프레임에 바로 Search로 빠졌다. 그런데 공격 모션은 아직
        // 재생 중이라, 칼을 반쯤 휘두르다 달리기로 튀었다(실제로 전투 중에 Move 상태인데
        // Attack2가 재생 중인 순간을 잡았다). 무기가 무거울수록 더 눈에 띈다 — 도끼는 1.67초짜리다.
        //
        // 피격은 TakeDamage가 InterruptCurrentAction으로 잠금을 풀고 들어오므로 여전히 끊긴다.
        // 사망과 패닉도 전역 전이라 여기와 무관하게 걸린다. 즉 "끊어야 하는 것"은 그대로 끊긴다.
        // 아직 내지르지 않은 스윙은 거둘 수 있다.
        //
        // 방패를 든 유닛만이다 — 그게 그들의 역할이고, 전원이 스윙을 물리면 아무도 공격을
        // 끝내지 못한다. 이미 내지른 뒤(회수 동작)에는 되돌리지 못한다. 그게 선공의 대가다.
        //
        // 이게 없으면 방패병의 방어 기회가 스윙 사이의 0.2초짜리 틈으로 쪼그라든다.
        // 적의 준비 동작이 0.4초뿐이라 그 둘이 겹칠 일이 거의 없다.
        if (context.IsTelegraphing && context.Stats.isTank && context.CanBlock())
        {
            context.ChangeState(context.BlockState);
            return true;
        }

        if (context.IsAttackAnimationLocked) return false;

        if (!context.HasUsableTarget())
        {
            context.ChangeState(context.SearchState);
            return true;
        }

        if (!context.IsTargetInAttackRange())
        {
            context.ChangeState(context.ChaseState);
            return true;
        }

        // 죽고 사는 문제는 콤보 도중이라도 항상 본다 — 콤보를 완주하는 것보다 목숨이 우선이다.
        if (context.ShouldRetreatForSurvival())
        {
            context.ChangeState(context.EvadeState);
            return true;
        }

        // 방어는 재량이 아니라 반응이다. 아래 레커버리 게이트 뒤에 두면 안 된다 —
        // 적의 준비 동작은 0.4초뿐인데 콤보 한 바퀴는 무기에 따라 수 초에서 십수 초다
        // (창은 11단이라 한 바퀴에 13초). 막을 창이 그때까지 남아 있을 리가 없어서,
        // 방패를 안 든 유닛은 사실상 평생 한 번도 막지 못했다.
        // CanBlock 자체가 "지금 누가 나를 향해 칼을 들어올렸는가"일 때만 참이므로,
        // 여기 둔다고 아무 때나 방패를 드는 것은 아니다.
        if (context.CanBlock())
        {
            context.ChangeState(context.BlockState);
            return true;
        }

        // 여기서부터가 "재량" 전환이다(카이팅/스킬). 콤보 스텝 하나가 끝날 때마다 검토하면
        // 스윙 하나 끝날 때마다 다른 상태로 튀어서 콤보가 거의 끝까지 이어지지 않는다("뚝배기 깨기").
        // 콤보가 한 바퀴 돌아 레커버리 시점에 온 경우에만 검토한다.
        if (!context.IsComboRecoveryPoint) return false;

        // 원거리 유닛의 근접 회피.
        if (context.ShouldKeepDistance())
        {
            context.ChangeState(context.EvadeState);
            return true;
        }

        // 잠금 검사는 위에서 이미 끝났다.
        if (context.CanUseSkill())
        {
            context.ChangeState(context.SkillState);
            return true;
        }

        return false;
    }

    private void TryAttack()
    {
        if (context.CanAttack())
        {
            context.TriggerAttack();
        }
    }
}
