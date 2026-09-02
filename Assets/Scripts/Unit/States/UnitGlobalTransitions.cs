// 전투 유닛의 "어느 상태에 있든 걸리는" 전이 전부.
//
// 예전에는 이것들이 두 군데로 흩어져 있었다:
//  - 사망 검사는 상태 12개의 Update 첫 줄에 각각(하나라도 빠뜨리면 시체가 계속 싸운다)
//  - 패닉/회복약/치료는 아예 상태머신 밖 UnitController.Update에
// 후자는 "상태마다 넣으면 빠뜨리기 쉽다"는 옳은 판단이었지만, 그 대가로 전이 그래프의
// 절반이 상태머신에서 보이지 않게 됐다. 새 규칙이 생길 때마다 Update가 한 줄씩 길어지는 것도 같은 문제다.
//
// 이제 이 배열이 그 그래프다. 위에서부터 먼저 보고, 한 프레임에 하나만 걸린다.
// 전이 객체는 상태를 갖지 않으므로 모든 유닛이 이 인스턴스들을 공유한다.
public static class UnitGlobalTransitions
{
    public static readonly GlobalTransition<UnitController>[] All =
    {
        new DeadTransition(),
        new PanicTransition(),
        new PotionTransition(),
        new HealTransition(),
        new ShieldTransition(),
    };

    // 죽으면 무엇을 하고 있었든 끝이다. 가장 먼저 본다.
    private sealed class DeadTransition : GlobalTransition<UnitController>
    {
        public override IState<UnitController> Evaluate(UnitController unit, IState<UnitController> current)
        {
            return unit.IsDead ? unit.DeadState : null;
        }
    }

    // 패닉/빈사/붕괴는 스스로 행동을 결정할 수 없는 상태다. 즉시 행동을 끊는다.
    private sealed class PanicTransition : GlobalTransition<UnitController>
    {
        public override IState<UnitController> Evaluate(UnitController unit, IState<UnitController> current)
        {
            UnitEmotion emotion = unit.Emotion;
            if (emotion == null || !emotion.IsActionBlocked) return null;

            return unit.PanicState;
        }
    }

    // 회복약. 이미 휘두르고 있는 공격 모션은 끊지 않는다.
    private sealed class PotionTransition : GlobalTransition<UnitController>
    {
        public override IState<UnitController> Evaluate(UnitController unit, IState<UnitController> current)
        {
            // 행동불가 상태에서는 스스로 마실 수 없다. 이미 패닉에 들어와 있으면
            // PanicTransition은 다시 걸리지 않으므로 여기서 따로 막아야 한다.
            if (unit.Emotion != null && unit.Emotion.IsActionBlocked) return null;
            if (unit.IsAttackAnimationLocked) return null;
            // 자세가 무너져 있는 동안은 스스로 아무것도 못 한다. 그 몇 초가 상대에게 열린
            // 빈틈인데, 그 사이에 회복약을 들이켜면 무너뜨린 의미가 사라진다.
            if (unit.IsStaggered) return null;
            // 마력을 모으는 중에도 손이 비어 있지 않다. 여기서 끊으면 영창이 통째로 흩어지고
            // 마력만 날아간다 — 스스로 그럴 이유가 없다.
            if (unit.IsCasting) return null;
            if (!unit.CanUsePotion()) return null;

            return unit.PotionState;
        }
    }

    // 서포터의 아군 치료. 회복약보다 뒤에 둔다 — 제 앞가림이 먼저다.
    // 회복약을 마시는 중에는 걸리지 않는다(그 동작을 끊으면 약만 버린다).
    private sealed class HealTransition : GlobalTransition<UnitController>
    {
        public override IState<UnitController> Evaluate(UnitController unit, IState<UnitController> current)
        {
            if (current == unit.PotionState) return null;
            if (unit.Emotion != null && unit.Emotion.IsActionBlocked) return null;
            if (unit.IsAttackAnimationLocked) return null;
            // 자세가 무너져 있는 동안은 스스로 아무것도 못 한다. 그 몇 초가 상대에게 열린
            // 빈틈인데, 그 사이에 회복약을 들이켜면 무너뜨린 의미가 사라진다.
            if (unit.IsStaggered) return null;
            // 마력을 모으는 중에도 손이 비어 있지 않다. 여기서 끊으면 영창이 통째로 흩어지고
            // 마력만 날아간다 — 스스로 그럴 이유가 없다.
            if (unit.IsCasting) return null;
            if (!unit.CanHealAlly()) return null;

            return unit.HealState;
        }
    }

    // 사제의 선제 보호막. 치유보다 뒤에 둔다 — 이미 깎여 죽어 가는 아군이 있으면 그쪽이 먼저다.
    // 보호막은 "아직 안 깎였는데 곧 깎일 사람"에게 거는 것이라, 급한 불을 끈 뒤에 나가는 것이 맞다.
    private sealed class ShieldTransition : GlobalTransition<UnitController>
    {
        public override IState<UnitController> Evaluate(UnitController unit, IState<UnitController> current)
        {
            if (current == unit.PotionState || current == unit.HealState) return null;
            if (unit.Emotion != null && unit.Emotion.IsActionBlocked) return null;
            if (unit.IsAttackAnimationLocked) return null;
            if (unit.IsStaggered) return null;
            // 이미 무언가를 시전하는 중이면 끊지 않는다. 끊어 봐야 마력만 두 번 버린다.
            if (unit.IsCasting) return null;
            if (!unit.CanShieldAlly()) return null;

            return unit.ShieldState;
        }
    }
}
