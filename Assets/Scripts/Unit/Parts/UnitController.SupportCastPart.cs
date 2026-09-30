using UnityEngine;

public partial class UnitController
{
    // 스스로를 살리고 동료를 돕는 부품 — 회복약, 치유 영창, 선제 보호막.
    //
    // 치유와 보호막은 둘 다 "고르고 → 영창을 시작하며 대상을 잠그고 → 마치면 건다"이다.
    // 대상을 잠그는 것이 핵심이다: 고른 후보(healTarget, shieldTarget)는 전역 전이가 매 프레임
    // 다시 묻는 값이라, 영창을 시작해 쿨다운이 걸리는 순간 null로 지워진다. 잠가 두지 않으면
    // 영창을 마쳤을 때 대상이 사라져 있어서, 마력만 나가고 아무도 회복되지 않는다(실측으로 확인한 버그다).
    private sealed class SupportCastPart
    {
        private readonly UnitController u;

        private float lastPotionTime = -999f;
        private float lastHealTime = -999f;
        private float lastShieldTime = -999f;

        // CanHealAlly / CanShieldAlly가 매 프레임 덮어쓰는 "지금 찾아본 후보".
        private UnitController healTarget;
        private UnitController shieldTarget;

        // 영창을 시작할 때 잠가 둔 대상. 완성은 이쪽만 본다.
        private UnitController castHealTarget;
        private UnitController castShieldTarget;

        public SupportCastPart(UnitController owner)
        {
            u = owner;
        }

        // ---------------------------------------------------------------- 회복약

        public bool CanUsePotion()
        {
            if (!u.CanRecoverHp) return false;
            if (u.IsDead || !u.stats.HasPotion) return false;
            if (Time.time < lastPotionTime + u.stats.potionCooldown) return false;

            float hpRatio = u.stats.HpRatio;
            if (hpRatio <= u.stats.potionHpThreshold) return true;

            // 마나가 바닥나 스킬을 못 쓰는 것도 마실 이유가 된다. 다만 HP가 거의 가득 차 있으면
            // 회복량의 대부분이 버려지므로 그때는 아낀다. 스킬 자체가 없는 유닛은 해당 없음.
            return u.skillAnimationHash != 0 &&
                   u.stats.skillManaCost > 0 &&
                   !u.stats.HasMana(u.stats.skillManaCost) &&
                   hpRatio <= u.stats.potionManaTriggerHpRatio;
        }

        public void UsePotion()
        {
            // CanUsePotion을 거치지 않고 직접 불려도 적은 회복되지 않도록 여기서도 막는다.
            if (!u.CanRecoverHp) return;
            if (!u.stats.ConsumePotion(out _, out _)) return;

            lastPotionTime = Time.time;
        }

        // ---------------------------------------------------------------- 치유

        // 회복할 아군이 있는지 확인하고, 있으면 대상까지 잡아 둔다.
        // 판단과 대상 선정을 나누면 HealBehavior가 다시 탐색해야 해서 같은 순회를 두 번 돌게 된다.
        public bool CanHealAlly()
        {
            healTarget = null;

            if (!u.stats.canHealAllies || !u.CanRecoverHp || u.IsDead) return false;
            if (Time.time < lastHealTime + u.stats.healCooldown) return false;
            if (!u.stats.HasMana(u.stats.healManaCost)) return false;

            healTarget = UnitRegistry.FindMostWoundedAlly(
                u, u.stats.healRange, u.stats.healTargetHpRatio, u.stats.dispelPriorityBonus);
            return healTarget != null;
        }

        // 영창을 시작한다. 마력은 여기서 나간다 — 끊기면 그대로 손실이다.
        //
        // 예전에는 시전과 동시에 회복이 끝났다. 그래서 "영창 중 무방비"라는 원작 설정이 성립할 수 없었다 —
        // 맞아서 끊겨도 이미 치료가 끝난 뒤라 잃는 것이 없었기 때문이다. 지금은 시작(마력 소모)과
        // 완성(실제 회복)을 나눠서, 사제가 탱커의 방어선 안에서만 영창을 끝낼 수 있게 만든다.
        public void BeginHeal()
        {
            castHealTarget = healTarget;
            u.stats.SpendMana(u.stats.healManaCost);
            lastHealTime = Time.time;
            u.BeginCast();
        }

        // 영창을 끝까지 마쳤다. 여기서야 실제로 회복되고 상태이상이 걷힌다.
        public void CompleteHeal()
        {
            u.EndCast();

            UnitController target = castHealTarget;
            castHealTarget = null;
            if (target == null || target.IsDead || !target.CanRecoverHp) return;

            target.Stats.Heal(u.stats.healAmount);

            // 디스펠. 원작에서 독·마비·출혈을 제때 걷어내지 못하면 전열이 통째로 무력화된다 —
            // 회복량보다 이쪽이 판을 가르는 경우가 많다.
            if (u.stats.dispelOnHeal && target.Emotion != null && target.Emotion.HasDispellableEffect)
            {
                target.Emotion.Dispel();
            }
        }

        // 영창이 끊겼다. 마력은 이미 나갔으므로 돌려주지 않는다 — 그것이 끊긴 대가다.
        public void CancelHeal()
        {
            if (!u.IsCasting) return;

            u.EndCast();
            castHealTarget = null;
        }

        // ---------------------------------------------------------------- 선제 보호막

        // 미리 보호막을 걸어 줄 아군이 있는가.
        //
        // 치유와 보는 것이 정반대다. 치유는 "이미 깎인 사람"을 찾지만 여기는
        // "아직 안 깎였는데 곧 깎일 사람"을 찾는다 — 몰려 있는 쪽이다. HP를 조건으로 걸지
        // 않는 것이 핵심이다. 만피인 탱커에게 미리 걸어 두는 것이 이 기술의 전부다.
        public bool CanShieldAlly()
        {
            shieldTarget = null;

            if (!u.stats.canShieldAllies || u.IsDead) return false;
            if (Time.time < lastShieldTime + u.stats.shieldCooldown) return false;
            if (!u.stats.HasMana(u.stats.shieldManaCost)) return false;

            shieldTarget = UnitRegistry.FindShieldTarget(u, u.stats.healRange, u.stats.shieldThreatCount);
            return shieldTarget != null;
        }

        public void BeginShield()
        {
            castShieldTarget = shieldTarget;
            u.stats.SpendMana(u.stats.shieldManaCost);
            lastShieldTime = Time.time;
            u.BeginCast();
        }

        public void CompleteShield()
        {
            u.EndCast();

            UnitController target = castShieldTarget;
            castShieldTarget = null;
            if (target == null || target.IsDead) return;

            target.Stats.ApplyShield(u.stats.shieldAmount, u.stats.shieldDuration);
        }

        public void CancelShield()
        {
            if (!u.IsCasting) return;

            u.EndCast();
            castShieldTarget = null;
        }

        // 죽은 유닛을 재사용할 때. 잠가 둔 치유 대상만 놓는다(예전과 같다).
        public void Reset()
        {
            castHealTarget = null;
        }
    }
}
