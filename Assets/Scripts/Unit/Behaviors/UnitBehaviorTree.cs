using System;

// 전투 유닛의 판단 전체. 이 파일 하나가 예전의 전이 그래프다.
//
// 예전에는 그 그래프가 스무 개 파일에 흩어져 있었다 — "지금 여기서 스킬이 나갈 수 있나"를
// 알려면 AttackState.TrySwitchToBetterState의 if 열한 개와 ChaseState의 if 넷과
// UnitGlobalTransitions 배열을 함께 읽고, 그 셋의 순서를 머릿속에서 합쳐야 했다.
// 게다가 그 셋은 조금씩 다른 조건을 보고 있었다(같은 "물러난다"가 세 곳에서 서로 달랐다).
//
// 트리에서는 위에서 아래가 곧 우선순위다. 매 틱 맨 위부터 다시 훑으므로, 아래쪽 가지가
// 돌고 있어도 위쪽 조건이 성립하는 순간 끊고 들어온다 — 예전의 "전역 전이"가 하던 일이
// 트리 구조 자체가 된다.
//
//  1) 사망        — 무엇을 하고 있었든 끝이다.
//  2) 행동불가    — 패닉/빈사/붕괴. 스스로 아무것도 결정하지 못한다.
//  3) 경직        — 자세가 무너졌다. 무너뜨린 쪽이 정한 시간만큼 열려 있다.
//  4) 피격 리액션 — 강인도가 깨진 한 대.
//  5) 회복약      — 제 앞가림이 먼저다.
//  6) 아군 치유   — 이미 깎여 죽어 가는 아군이 먼저다.
//  7) 아군 보호막 — 급한 불을 끈 뒤에 미리 걸어 둔다.
//  8) 교전/순찰   — 나머지 전부.
//
// 3·4번이 5~7번보다 위에 있는 것이 예전 배열과 다른 점이다. 예전에는 경직과 피격이
// 전역 전이가 아니라 평범한 상태였고, 대신 회복약/치유/보호막 쪽에 "경직 중에는 못 마신다"는
// 검사가 손으로 붙어 있었다(피격 쪽에는 그 검사가 빠져 있어서, 움찔하는 도중에 약을
// 들이켤 수 있었다). 우선순위로 올리면 그 검사가 구조로 표현되고 빠진 자리도 함께 메워진다.
public static class UnitBehaviorTree
{
    public static BehaviorTree<UnitController> Build(UnitController unit)
    {
        // "지금 붙어서 칼을 섞는 중인가"가 스킬의 조건에 들어가므로 먼저 만든다.
        AttackBehavior attack = new AttackBehavior(unit);

        BTSelector<UnitController> engage = new BTSelector<UnitController>(unit, true,
            // 물러난다. 위기 후퇴와 간격 후퇴가 여기 하나로 모인다.
            Guard(unit, () => WantsRetreat(unit), BuildRetreat(unit), true),

            // 방어는 재량이 아니라 반응이다 — 적의 준비 동작은 0.4초뿐인데 콤보 한 바퀴는
            // 무기에 따라 수 초에서 십수 초라(창은 11단에 13초), 뒤로 밀면 방패를 안 든 유닛은
            // 사실상 평생 한 번도 막지 못한다. 그래서 영창·스킬·빠지기보다 위에 둔다.
            // 이미 시작한 그 동작들을 방어가 끊는 일은 없다 — 그쪽이 스스로 잠근다
            // (BTNode.AllowsReprioritize).
            Guard(unit, () => WantsBlock(unit), new BlockBehavior(unit), true),

            // 마법사의 영창.
            Guard(unit, () => WantsCast(unit), new CastBehavior(unit), true),

            // 직군 스킬.
            Guard(unit, () => WantsSkill(unit, attack), new SkillBehavior(unit), true),

            // 아직 사거리 밖이지만 한 번에 붙을 수 있는 거리다 — 걸어 들어가는 대신 덤벼든다.
            Guard(unit, () => WantsLeapAttack(unit), new LeapAttackBehavior(unit), true),

            // 암살자의 치고 빠지기.
            Guard(unit, () => WantsStalk(unit), new StalkBehavior(unit), true),

            // 사거리 안이면 휘두른다. 이미 나간 스윙은 사거리를 벗어나도 끝까지 간다.
            Guard(unit, () => unit.IsAttackAnimationLocked || unit.IsTargetInAttackRange(), attack),

            // 위의 어느 것도 아니면 일단 붙는다.
            new ChaseBehavior(unit));

        BTSelector<UnitController> combat = new BTSelector<UnitController>(unit, true,
            // 겨눌 상대가 있으면 싸운다.
            Guard(unit, () => HasEngagement(unit, engage), engage),

            // 없으면 갈 곳을 정해 그리로 간다. 둘을 시퀀스로 묶은 것이 예전의 Search ↔ Move다.
            Guard(unit, () => UnitRegistry.HasLivingEnemy(unit),
                new BTSequence<UnitController>(unit, new SearchBehavior(unit), new MoveBehavior(unit))),

            // 적이 없거나 갈 곳조차 없다.
            new IdleBehavior(unit));

        BTSelector<UnitController> root = new BTSelector<UnitController>(unit, true,
            Guard(unit, () => unit.IsDead, new DeadBehavior(unit), true),
            Guard(unit, () => IsActionBlocked(unit), new PanicBehavior(unit)),
            Guard(unit, () => unit.HasPendingStagger, new StaggerBehavior(unit), true),
            Guard(unit, () => unit.HasPendingHitReaction, new HitBehavior(unit), true),
            Guard(unit, () => CanTendSelf(unit) && unit.CanUsePotion(), new PotionBehavior(unit), true),
            Guard(unit, () => CanTendSelf(unit) && unit.CanHealAlly(), new HealBehavior(unit), true),
            Guard(unit, () => CanTendSelf(unit) && unit.CanShieldAlly(), new ShieldBehavior(unit), true),
            combat);

        return new BehaviorTree<UnitController>(root);
    }

    // ---------------------------------------------------------------- 반응 계층의 조건

    private static bool IsActionBlocked(UnitController unit)
    {
        UnitEmotion emotion = unit.Emotion;
        return emotion != null && emotion.IsActionBlocked;
    }

    // 회복약·치유·보호막이 함께 쓰는 검사. 셋 다 "손이 비어 있고 자세가 살아 있을 때만
    // 스스로 시작할 수 있는 것"이라 조건이 같다.
    //
    // 셋 사이의 우선순위(제 앞가림 → 죽어 가는 아군 → 미리 거는 보호막)는 조건이 아니라
    // 트리에서의 자리로 표현된다. 예전에는 그것도 조건이었다 — HealTransition 안에
    // "지금 회복약을 마시는 중이면 걸지 않는다"가, ShieldTransition 안에 그 둘이 손으로
    // 들어 있었다. 위에 있는 가지가 잠겨 있으면 아래는 애초에 검토되지 않으므로 사라진다.
    private static bool CanTendSelf(UnitController unit)
    {
        // 행동불가 상태에서는 스스로 마시거나 걸지 못한다.
        if (IsActionBlocked(unit)) return false;

        // 이미 휘두르고 있는 공격 모션은 끊지 않는다.
        if (unit.IsAttackAnimationLocked) return false;

        // 자세가 무너져 있는 동안은 스스로 아무것도 못 한다. 그 몇 초가 상대에게 열린
        // 빈틈인데, 그 사이에 회복약을 들이켜면 무너뜨린 의미가 사라진다.
        if (unit.IsStaggered) return false;

        // 마력을 모으는 중에도 손이 비어 있지 않다. 여기서 끊으면 영창이 통째로 흩어지고
        // 마력만 날아간다 — 스스로 그럴 이유가 없다.
        if (unit.IsCasting) return false;

        return true;
    }

    // ---------------------------------------------------------------- 교전 계층의 조건

    // 겨눌 상대가 있는가. 없으면 스캐너가 잡아 둔 것을 받아 본다.
    //
    // 교전 갈래가 이미 돌고 있었다면 한 발 더 나간다 — 겨누던 상대가 쓰러진 그 프레임에
    // 곧바로 다시 훑는다(예전 ChaseState.TryRefreshTarget). 순찰 중에는 이 즉시 탐색을
    // 하지 않는다. 시야·거리·레이캐스트를 전부 도는 전면 탐색이라, 상대가 없는 유닛 전원이
    // 매 프레임 돌리면 유닛 수의 제곱으로 비용이 커진다(TargetScanner 주석).
    private static bool HasEngagement(UnitController unit, BTNode<UnitController> engage)
    {
        // 이미 나간 스윙은 상대가 쓰러져도 끝까지 휘두른다. 여기서 끊으면 칼을 반쯤 휘두르다
        // 달리기로 튄다 — 무기가 무거울수록 더 눈에 띈다(도끼는 1.67초짜리다).
        if (unit.IsAttackAnimationLocked) return true;

        // 이미 시작해서 값을 치른 동작도 마찬가지다(스킬, 도약, 방어 자세).
        // 겨눌 상대가 사라졌다고 그 모션을 중간에 끊으면 그 값만 버린다.
        // 상대를 잃었을 때 접어야 하는 동작은 스스로 그 검사를 한다(빠지기, 후퇴).
        BTNode<UnitController> leaf = engage.FindRunningLeaf();
        if (leaf != null && !leaf.AllowsReprioritize) return true;

        if (unit.HasUsableTarget()) return true;

        if (unit.Scanner == null) return false;

        UnitController target = unit.Scanner.Target;
        if (target == null && engage.IsRunning) target = unit.Scanner.FindTargetNow();

        return target != null && unit.TrySetTarget(target);
    }

    // 물러날 것인가.
    //
    // 예전에는 이 판단이 세 곳에 흩어져 서로 다른 것을 보고 있었다 — ChaseState는
    // ShouldEvade() 하나, AttackState는 위기 후퇴와 간격 후퇴를 따로 두 줄, CastState는
    // ShouldAbandonCast(). 하나로 모으면 그 셋의 차이가 마지막 한 줄로 남는다.
    private static bool WantsRetreat(UnitController unit)
    {
        // 이미 나간 스윙은 거두지 않는다. 원거리 유닛의 근접 회피가 "쏘자마자 물러나는" 것이
        // 되는 이유가 이 한 줄이다 — 여기를 지나온 시점이면 이번 발은 이미 시위를 떠났다.
        if (unit.IsAttackAnimationLocked) return false;

        // 죽고 사는 문제는 콤보 도중이라도 항상 본다 — 완주보다 목숨이 우선이다.
        if (unit.ShouldRetreatForSurvival()) return true;

        // 여기서부터는 "붙잡혀서 제 간격을 잃었는가"다.
        if (!unit.ShouldKeepDistance()) return false;

        // 아직 사거리 밖이면 곧바로 물러난다(예전 ChaseState — 사거리까지 들어갈 때까지
        // 기다리지 않는다). 마력을 모으는 중이어도 곧바로 접는다 — 영창은 방어선 안에서만
        // 성립하고, 제 간격을 잃은 순간 그 자리에 서 있을 이유가 없다(예전 CastState).
        //
        // 사거리 안이라면 한 발은 쏘고 나서야 다시 물러난다. 이게 없으면 무한 왕복이 생긴다 —
        // 한 번 물러나 봐야 실제로 벌어지는 거리는 0.5m 남짓이라, 복귀하자마자 다시 임계
        // 안이면 한 발도 쏘지 못한 채 또 물러난다(HasAttackedSinceEvade 주석 참조).
        return !unit.IsTargetInAttackRange() || unit.IsCasting || unit.HasAttackedSinceEvade;
    }

    // 어떻게 물러날 것인가. 뒷걸음으로 사거리를 되찾을 것인가, 등을 보이고 달아날 것인가.
    //
    // 기억형 셀렉터라 한 번 고르면 그 후퇴가 끝날 때까지 바뀌지 않는다. 매 틱 다시 고르면
    // 뒷걸음으로 거리가 벌어지는 순간 조건이 뒤집혀 도주로 갈아타고, 그 그림이 곧 잔떨림이 된다.
    private static BTNode<UnitController> BuildRetreat(UnitController unit)
    {
        return new BTSelector<UnitController>(unit, false,
            Guard(unit, () => RunsAway(unit), new FleeBehavior(unit)),
            new EvadeBehavior(unit));
    }

    // 마법사는 붙잡히면 뒷걸음으로 재지 않고 등을 보이고 달린다(FleeBehavior 주석 참조).
    // 간격 문제가 아니면 물러나는 이유는 HP뿐이고, 그때 필요한 것은 전열이 아니라 거리다.
    private static bool RunsAway(UnitController unit)
    {
        return !unit.ShouldKeepDistance() || unit.Stats.fleeByRunning;
    }

    // 방패/무기를 들 것인가.
    //
    // 예전 AttackState에는 방어 검사가 두 곳에 있었다. 맨 위 — 휘두르던 칼을 거두고 받아치는
    // 패링 — 과, 후퇴 판단 뒤의 평범한 방어다. 트리에서는 가지 하나로 합치되 그 자리 차이를
    // 조건에 남긴다.
    private static bool WantsBlock(UnitController unit)
    {
        // "지금 누가 나를 향해 칼을 들어올렸는가"일 때만 참이다. 아무 때나 방패를 들지는 않는다.
        if (!unit.CanBlock()) return false;

        if (!unit.IsAttackAnimationLocked) return true;

        // 휘두르는 중이라면, 아직 내지르지 않은 스윙만 거둘 수 있다(IsTelegraphing).
        // 이미 내지른 뒤에는 되돌리지 못한다 — 그게 선공의 대가다.
        //
        // 받아내는 방식을 가진 직군만이다 — 그게 그들의 역할이고, 전원이 스윙을 물리면
        // 아무도 공격을 끝내지 못한다. 일곱 직군 중 탱커(방패)와 검사(패링) 둘뿐이다.
        // 검사에게 특히 중요하다: 패링은 버티는 자세가 아니라 날아오는 궤적에 맞춰 내미는
        // 한 동작이라, 휘두르던 칼을 거두고 들어가지 못하면 성립 자체가 안 된다 —
        // 원작의 공수 전환(파고들다가 반격이 오면 즉시 받아친다)이 이 한 줄에 걸려 있다.
        return unit.IsTelegraphing && unit.Stats.guardStyle != GuardStyle.None;
    }

    // 마법사의 영창. 사거리 안에서만 검토한다.
    //
    // CanCastSpell은 안에서 쓸 마법까지 고르고(SelectSpell) 그 연산을 주기로 제한하는데
    // (nextSpellEvaluationTime), 쏘지도 못할 거리에서 그 주기를 태우면 정작 붙었을 때
    // 한 박자 늦는다. 예전에도 이 검사는 AttackState — 즉 사거리 안 — 에서만 돌았다.
    private static bool WantsCast(UnitController unit)
    {
        return unit.IsTargetInAttackRange() && unit.CanCastSpell();
    }

    private static bool WantsSkill(UnitController unit, AttackBehavior attack)
    {
        if (unit.IsAttackAnimationLocked) return false;
        if (!unit.CanUseSkill()) return false;

        // 콤보 레커버리 게이트. 스윙 하나 끝날 때마다 재량 전환을 검토하면 콤보가 거의 끝까지
        // 이어지지 않는다("뚝배기 깨기") — 콤보가 한 바퀴 돌았을 때만 검토한다.
        //
        // 다만 이 게이트는 이미 붙어서 칼을 섞는 중일 때만 건다. 쫓아가다 사거리에 막 들어선
        // 순간에는 게이트 없이 한 번 검토한다 — 예전 ChaseState가 그랬고, 그쪽은 애초에
        // 콤보를 돌리는 중이 아니라 게이트가 걸 대상이 없다.
        return !attack.IsRunning || unit.IsComboRecoveryPoint;
    }

    // CanLeapAttack이 "사거리 안이면 거짓"이므로 아래 공격 가지와 겹치지 않는다.
    private static bool WantsLeapAttack(UnitController unit)
    {
        return !unit.IsAttackAnimationLocked && unit.CanLeapAttack();
    }

    // 암살자는 콤보를 한 바퀴 돌리고 나면 일단 빠진다. 그 사이에 은신이 걸리고,
    // 다음 접근은 그림자 속에서 배후로 들어간다(StalkBehavior 주석 참조).
    // 스킬보다 뒤에 둔 이유는 쓸 수 있는 한 방이 있으면 그것부터 꽂는 편이 낫기 때문이다.
    private static bool WantsStalk(UnitController unit)
    {
        return !unit.IsAttackAnimationLocked && unit.IsComboRecoveryPoint && unit.ShouldStalk();
    }

    private static BTGuard<UnitController> Guard(UnitController unit, Func<bool> condition,
        BTNode<UnitController> child, bool latch = false)
    {
        return new BTGuard<UnitController>(unit, condition, child, latch);
    }
}
