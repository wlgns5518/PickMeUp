using UnityEngine;

public partial class UnitController
{
    // 칼이 닿은 순간 아주 짧게 애니메이션을 눌러 붙이는 부품. 공격자와 피격자 양쪽에 걸어야
    // "부딪혔다"가 되지 — 한쪽만 멈추면 그냥 렉으로 보인다.
    private sealed class HitStopPart
    {
        // 손에 쥔 것으로 직접 쳤다고 볼 거리. 이보다 멀면 때린 쪽은 멈추지 않는다.
        //
        // 화살과 마법탄은 날아가는 동안 쏜 쪽이 이미 다음 동작에 들어가 있다. 도착하는 순간 9m 밖의
        // 궁수가 시위를 당기다 말고 멈칫하면 부딪힌 것이 아니라 렉으로 보인다.
        private const float ReachMargin = 1f;

        private readonly UnitController u;

        private bool active;
        private float until;

        public HitStopPart(UnitController owner)
        {
            u = owner;
        }

        public void Apply(float duration, float scale)
        {
            if (duration <= 0f || u.IsDead) return;
            Animator animator = u.animator;
            if (animator == null || !animator.enabled) return;

            float end = Time.time + duration;
            if (active && end <= until) return;

            active = true;
            until = end;
            animator.speed = Mathf.Clamp01(scale);

            // 스윙 시계는 실제 시각 기준이라 잃어버린 만큼 뒤로 민다(SwingPart.DelayBy 주석 참조).
            u.Swing.DelayBy(duration * (1f - Mathf.Clamp01(scale)));
        }

        public void Tick()
        {
            if (!active || Time.time < until) return;

            active = false;
            if (u.animator != null) u.animator.speed = 1f;
        }

        public void Clear()
        {
            active = false;
            until = 0f;
            if (u.animator != null && u.animator.enabled) u.animator.speed = 1f;
        }

        // 타격이 닿은 순간 공격자와 피격자를 함께 눌러 붙인다.
        // 정지 시간은 맞은 쪽 기준이다 — 무거운 무기에 맞을수록 크게 흔들려야 하므로.
        public void ApplyImpact(UnitController attacker, float scale)
        {
            UnitStats stats = u.stats;
            float duration = stats.hitStopDuration * scale;
            if (duration <= 0f) return;

            Apply(duration, stats.hitStopScale);
            if (attacker != null && !attacker.IsDead && attacker.IsWithinHitStopReach(u.transform.position))
            {
                attacker.ApplyHitStop(duration, stats.hitStopScale);
            }
        }

        public bool IsWithinReach(Vector3 victimPosition)
        {
            float reach = u.Swing.Reach + ReachMargin;
            Vector3 offset = victimPosition - u.transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= reach * reach;
        }

        // 엔티티를 쳤다(TargetRef.TakeDamage). 맞은 쪽의 멈칫은 브리지가 싣고 가고, 여기서는 친 쪽만 멈춘다.
        //
        // 예전에는 이 경로에 멈칫이 통째로 없었다. 게임오브젝트끼리 싸울 때 맞은 쪽 TakeDamage가 양쪽에
        // 걸어 주던 것이라, 적이 엔티티가 된 뒤로 아군의 칼은 무엇을 베든 허공을 가르듯 지나갔다.
        public void OnStruckEntity(Vector3 victimPosition, float impactWeight)
        {
            if (!IsWithinReach(victimPosition)) return;
            Apply(u.stats.hitStopDuration * Mathf.Max(0f, impactWeight), u.stats.hitStopScale);
        }
    }
}
