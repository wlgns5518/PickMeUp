using UnityEngine;

public class AttackState : UnitBattleState
{
    public AttackState(UnitController context) : base(context)
    {
    }

    // 제자리에서 휘두른다. 그 사이 밀려나면 발이 땅을 딛지 않은 채 미끄러진다.
    public override bool HoldsGround => true;

    // 이미 나간 칼은 되돌리지 못한다. 스윙 사이의 틈에서만 갈아탄다.
    public override bool LocksTarget => true;

    public override bool AcceptsCombatRedirect => false;

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

        if (context.IsAttackAnimationLocked)
        {
            // 휘두르는 중에는 몸을 거의 돌리지 않는다.
            //
            // 예전에는 여기서도 평소 회전 속도(720도/초)로 돌렸다. 공격 클립은 제자리에서
            // 베는 동작인데 그 위에서 몸이 홱 돌아가니, 발이 땅에 붙지 않고 미끄러지는 것이
            // 그대로 보였다. 겨누는 일은 휘두르기 전에 끝나 있어야 하고(attackFacingTolerance),
            // 내지른 뒤에는 상대가 움직인 만큼만 조금 따라간다.
            context.FaceTargetWhileAttacking();

            // 준비 동작 동안은 타깃 쪽으로 조금 파고든다. 예전에는 StopMovement로 완전히
            // 못 박고 휘둘렀기 때문에, 사거리 경계에서 시작한 스윙이 눈에 보이게 허공을 갈랐다.
            // 이미 교전 간격에 서 있으면 한 발도 움직이지 않는다(UpdateAttackLunge 참조).
            context.UpdateAttackLunge();
            return;
        }

        context.FaceTarget();

        // 클립은 끝났지만 아직 다음 스윙의 호흡이 남은 구간. 여기가 예전에는 통째로 비어 있었다 —
        // 사거리에 들어가면 그 자리에 못 박혀 마주 보고 계속 때리기만 했다. 이제 이 시간에
        // 간격을 재고 옆으로 돈다. 칼싸움이 서로 재는 시간으로 이루어져 있는 이유가 이것이다.
        if (!context.IsSwingReady)
        {
            context.UpdateCombatFootwork();
            return;
        }

        // 휘두르지 못하는 동안은 자리를 잡는다.
        //
        // 마법사가 여기로 온다. 평타가 없으니 CanAttack이 늘 거짓이고, 예전 코드는 그때 아무것도
        // 하지 않아서 마법 쿨다운을 기다리는 내내 못 박힌 듯 서 있었다. 마주 보고 간격을 재는
        // 편이 낫다 — 마법사에게도 거리는 목숨이다.
        // (아직 몸을 다 돌리지 못한 근접 유닛도 이 갈래로 오는데, 그쪽도 서 있는 것보다 낫다.)
        if (!TryAttack()) context.UpdateCombatFootwork();
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
        // 받아내는 방식을 가진 직군만이다 — 그게 그들의 역할이고, 전원이 스윙을 물리면
        // 아무도 공격을 끝내지 못한다. 일곱 직군 중 탱커(방패)와 검사(패링) 둘뿐이다.
        // 이미 내지른 뒤(회수 동작)에는 되돌리지 못한다. 그게 선공의 대가다.
        //
        // 이게 없으면 방어 기회가 스윙 사이의 0.2초짜리 틈으로 쪼그라든다.
        // 적의 준비 동작이 0.4초뿐이라 그 둘이 겹칠 일이 거의 없다.
        //
        // 검사에게 특히 중요하다. 패링은 "버티는 자세"가 아니라 날아오는 궤적에 맞춰 내미는
        // 한 동작이라, 휘두르던 칼을 거두고 들어가지 못하면 성립 자체가 안 된다 —
        // 원작의 공수 전환(파고들다가 반격이 오면 즉시 받아친다)이 이 한 줄에 걸려 있다.
        if (context.IsTelegraphing && context.Stats.guardStyle != GuardStyle.None && context.CanBlock())
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
            context.ChangeState(context.RetreatState);
            return true;
        }

        // 원거리 유닛의 근접 회피. 콤보를 완주할 때까지 기다리지 않는다 —
        // 활은 6단이라 레커버리까지 여섯 발이 걸리고, 그동안 적은 코앞에서 계속 때린다.
        // 위 잠금 검사를 지나온 시점이라 이번 발은 이미 시위를 떠났다. 쏘자마자 물러나는 셈이다.
        //
        // HasAttackedSinceEvade가 없으면 여기서 무한 왕복이 생긴다. 한 번 물러나 봐야
        // 실제로 벌어지는 거리는 0.5m 남짓이라, 복귀하자마자 다시 임계 안이면 한 발도
        // 쏘지 못한 채 Evade로 되돌아간다(EvadeState 안에서 반복하던 문제가 자리만 옮긴 꼴).
        if (context.HasAttackedSinceEvade && context.ShouldKeepDistance())
        {
            context.ChangeState(context.RetreatState);
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

        // 마법사의 영창. 콤보 레커버리 게이트보다 앞에 둔다.
        //
        // 그 게이트는 "콤보를 끝까지 이어붙이게 하라"는 것이고, 판단 근거가 hasSwungAtLeastOnce다.
        // 그런데 마법사는 평생 한 번도 휘두르지 않으므로(CanAttack이 false) 그 값이 영영 거짓이고,
        // 아래에 두면 마법사는 영창을 한 번도 시작하지 못한 채 제자리에 선다.
        //
        // CanCastSpell 안에서 이미 "쓸 만한 마법이 있는가"까지 연산이 끝나고(SelectSpell),
        // 마법사가 아닌 유닛은 속성이 없어 항상 false다. 그래서 앞으로 옮겨도 다른 직군에는
        // 아무 영향이 없다.
        if (context.CanCastSpell())
        {
            context.ChangeState(context.CastState);
            return true;
        }

        // 여기서부터가 "재량" 전환이다(스킬). 콤보 스텝 하나가 끝날 때마다 검토하면
        // 스윙 하나 끝날 때마다 다른 상태로 튀어서 콤보가 거의 끝까지 이어지지 않는다("뚝배기 깨기").
        // 콤보가 한 바퀴 돌아 레커버리 시점에 온 경우에만 검토한다.
        if (!context.IsComboRecoveryPoint) return false;

        // 잠금 검사는 위에서 이미 끝났다.
        if (context.CanUseSkill())
        {
            context.ChangeState(context.SkillState);
            return true;
        }

        // 암살자는 한 바퀴 돌리고 나면 일단 빠진다. 그 사이에 은신이 걸리고,
        // 다음 접근은 그림자 속에서 배후로 들어간다(StalkState 주석 참조).
        // 스킬보다 뒤에 두는 이유는 쓸 수 있는 한 방이 있으면 그것부터 꽂는 편이 낫기 때문이다.
        if (context.ShouldStalk())
        {
            context.ChangeState(context.StalkState);
            return true;
        }

        return false;
    }

    // 지금 실제로 휘둘렀는가. 휘두르지 못했으면 부르는 쪽이 그 시간에 자리를 잡는다.
    private bool TryAttack()
    {
        if (!context.CanAttack()) return false;

        context.TriggerAttack();
        return true;
    }
}
