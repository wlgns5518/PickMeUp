using UnityEngine;

public partial class UnitController
{
    // 액티브 스킬(고블린의 물어뜯기 같은 한 방)을 언제 쓸 수 있는지와, 쓰는 순간의 대가를 맡는 부품.
    // 맞는 순간(타격 판정)은 SwingPart.ResolveSkillHit, 매달리는 동작은 ClingPart가 맡는다.
    private sealed class SkillPart
    {
        private readonly UnitController u;

        // 스킬을 다시 쓸 수 있게 되는 시각.
        private float nextSkillTime;
        // 붙잡는 스킬에 당한 뒤 다시 당하지 않는 시각(무는 쪽이 아니라 물리는 쪽의 시계다).
        private float victimImmuneUntil;

        public SkillPart(UnitController owner)
        {
            u = owner;
        }

        public bool CanBeVictim => Time.time >= victimImmuneUntil;

        public bool CanUse()
        {
            UnitStats stats = u.stats;

            // 순서는 싼 것부터다. 아래 다섯은 전부 필드와 산술이라, 여기서 걸리면 표적을
            // 확인하는 비용(IsTargetValid는 표적이 게임오브젝트일 때 네이티브 호출이고,
            // 엔티티일 때는 브리지 조회다)을 통째로 건너뛴다.
            if (u.skillAnimationHash == 0) return false;

            // 이번 전투에 쓸 몫이 남았는가. 쿨다운과는 다른 질문이다 — 쿨다운은 "얼마나 자주",
            // 이쪽은 "이번 판에 몇 번이나"다(UnitStats.skillUseCount 주석 참조).
            if (!stats.HasSkillUse) return false;

            // 붙어서 겨룬 시간이 모자라면 아직 못 쓴다. 달려들자마자가 아니라 한 합
            // 주고받은 뒤에 큰 수가 나오게 하는 조건이다(EngagementPart.TickDwell 주석 참조).
            if (u.Engagement.Dwell < stats.skillEngageDelay) return false;

            // 스킬은 여는 수가 아니다. 한 번도 휘두르지 않았는데 먼저 나가면 교전의 첫 동작이
            // 늘 스킬로 똑같아진다. 원거리 유닛에서 특히 두드러졌다 — 스폰되자마자 사거리에
            // 들어서므로 "입장하자마자 스킬"이 매 전투 고정 연출이 됐다.
            // 콤보 레커버리 게이트는 이미 붙어서 칼을 섞는 중일 때만 걸리므로
            // (UnitBehaviorTree.WantsSkill), 막 사거리에 들어선 순간에는 이 검사가 전부다.
            if (!u.Swing.HasSwungAtLeastOnce) return false;

            if (!stats.HasMana(stats.skillManaCost)) return false;
            if (Time.time < nextSkillTime) return false;

            // 표적 확인은 한 번만 한다. 예전에는 이 판정이 한 식 안에서 두 번 불렸다
            // (희생자 면역 검사에서 한 번, 그 아래에서 또 한 번).
            if (!u.IsTargetValid()) return false;

            // 상대가 방금 이 스킬에 당했으면 다시 걸지 않는다. 붙잡는 스킬에 반드시 필요하다 —
            // 없으면 고블린 다섯이 같은 아군 하나의 목에 동시에 매달린다.
            // 무는 쪽이 아니라 물리는 쪽에 걸린 시간이라, 여러 마리가 각자 세어도 결과가 같다.
            if (stats.skillVictimImmunity > 0f && !u.CurrentTarget.CanBeSkillVictim) return false;

            return u.IsTargetInAttackRange();
        }

        public void Trigger()
        {
            UnitStats stats = u.stats;
            nextSkillTime = Time.time + stats.skillCooldown;
            stats.ConsumeSkillUse();
            stats.SpendMana(stats.skillManaCost);

            // 상대를 점유하는 스킬은 거는 그 순간에 상대를 잠가야 한다. 피해가 들어갈 때
            // 잠그면 늦다 — 그 사이에 다른 고블린들이 이미 같은 목을 물기 시작한 뒤다.
            if (stats.skillVictimImmunity > 0f && u.IsTargetValid())
            {
                u.CurrentTarget.MarkSkillVictim(stats.skillVictimImmunity);
            }

            u.PlayAnimation(u.skillAnimationHash, true);
        }

        // 붙잡는 스킬에 당했다고 표시한다. 시간은 건 쪽이 정한다 — 경직(Stagger)과 같은 규칙이다.
        public void MarkVictim(float duration)
        {
            if (duration <= 0f) return;
            victimImmuneUntil = Mathf.Max(victimImmuneUntil, Time.time + duration);
        }

        // 남은 스킬 사용 횟수는 stats 쪽에 있고, 전투마다 프리팹에서 복제되므로 저절로 다시 찬다.
        public void Reset()
        {
            nextSkillTime = 0f;
            victimImmuneUntil = 0f;
        }
    }
}
