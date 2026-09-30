using System.Collections.Generic;
using UnityEngine;

public partial class UnitController
{
    // 마법을 연산하고 영창해 현상으로 구현하는 부품.
    //
    // 스킬(SkillBehavior)과 완전히 분리해 둔 것이 요점이다. 원작에서 마법은 버튼 하나로 나가는 정해진
    // 액티브가 아니라, 마법사가 마력을 연산하고 영창해 현상으로 구현하는 것이다. 그래서 이렇게 굴러간다:
    //   1) 무엇을 쓸지 연산한다          Select()        — 자기 속성 안에서만 고른다
    //   2) 마력을 모은다(무방비)          CastBehavior + BeginCast()
    //   3) 현상으로 구현한다              Execute()
    //   4) 끊기면 마력만 날아간다          Cancel()
    //
    // 단일 속성 귀속이 이 구조의 핵심 제약이다. 마법사는 평생 하나의 속성만 다루므로
    // Select는 "어느 속성이 잘 먹히나"를 묻지 않는다. 물을 수 있는 것은 "내 속성 안에서 지금 제어인가 광역인가"
    // 뿐이고, 그래서 어느 속성의 마법사를 데려갔는지가 파티 운영을 바꾼다.
    //
    // 단일/기본(Bolt)은 영창이 없는 즉발이고, 마법사의 평타가 이미 그것이다(맨손 시전 + 투사체).
    private sealed class SpellcasterPart
    {
        // 광역 마법 착탄의 흔들림. 반경 3m짜리가 0.65, 5m 넘으면 끝까지 흔든다.
        private const float AreaSpellShakeBase = 0.35f;
        private const float AreaSpellShakePerMeter = 0.1f;

        // 마법 연산을 다시 돌릴 간격. 쿨다운이 돌아왔는데 쓸 자리가 아직 안 나온 구간에서는
        // 이 판단이 매 프레임 불린다 — 그때마다 적을 훑어 착탄 지점을 다시 계산하면
        // 마법사 한 명이 난전에서 프레임당 수백 번의 거리 검사를 돌린다. 초당 몇 번이면 충분하다.
        private const float EvaluationInterval = 0.25f;

        // 광역 판정에 쓰는 공용 버퍼. 마법 한 번에 리스트를 새로 만들지 않는다.
        private static readonly List<TargetRef> Victims = new List<TargetRef>(16);

        private readonly UnitController u;

        // 흩어진 영창을 다시 모을 수 있게 되는 시각.
        private float retryTime;

        private int castAnimationHash;

        // 마법마다 따로 식는다. 큰 것을 썼다고 잔 마법까지 잠기면 "큰 것을 기다리며 작은 것으로
        // 버틴다"가 성립하지 않는다. 속성이 정해지면 마법 수도 정해지므로(SpellCatalog) 그때 잡는다.
        private float[] readyTime;

        // 지금 영창 중인 마법과 그 착탄 지점. 영창을 시작할 때 잠근다 —
        // 사제의 치유 대상과 같은 이유다(영창 중에 판단이 다시 돌아 대상이 지워지면 안 된다).
        private SpellSpec casting;
        private Vector3 castingAimPoint;
        private bool hasCasting;

        private float nextEvaluationTime;

        public SpellcasterPart(UnitController owner)
        {
            u = owner;
        }

        public void CacheAnimations()
        {
            castAnimationHash = u.ResolveStateHash(u.castStateName);
        }

        private void EnsureCooldowns()
        {
            int count = SpellCatalog.CountOf(u.stats.affinity);
            if (readyTime != null && readyTime.Length == count) return;

            readyTime = count > 0 ? new float[count] : null;
        }

        // ---------------------------------------------------------------- 연산

        // 지금 쓸 마법을 고른다. 없으면 false.
        //
        // 고르는 순서가 곧 우선순위다: 판을 끝낼 수 있으면 끝내고(광역), 아니면 판을 만들고(제어),
        // 그것도 아니면 기본 마법을 쏜다(단일). 마지막 갈래가 반드시 있어야 한다 —
        // 마법사에게는 평타가 없으므로, 아무 마법도 고르지 못하면 그 유닛은 아무것도 못 한다.
        public bool Select(out SpellSpec spell, out Vector3 aimPoint)
        {
            spell = default;
            aimPoint = Vector3.zero;

            if (u.stats.affinity == MagicAffinity.None) return false;
            if (!u.IsTargetValid()) return false;
            // 마법은 겨눌 수 있어야 쏜다. 사거리 밖이면 먼저 붙어야 한다.
            if (!u.IsTargetInAttackRange()) return false;

            EnsureCooldowns();
            SpellSpec[] spells = SpellCatalog.SpellsOf(u.stats.affinity);
            if (spells.Length == 0) return false;

            // 적이 품 안에 들어와 있으면 긴 영창은 시작하지 않는다.
            //
            // 영창은 한 대만 맞으면 통째로 흩어진다. 실측에서 붙잡힌 화염 마법사가 유성 낙하(2.6초)를
            // 연달아 시작했다가 번번이 끊겨 시간만 버렸다. 그렇다고 아무것도 못 하게 두면 마법사는
            // 붙잡힌 순간 무력해진다 — 평타가 없기 때문이다. 그래서 긴 것만 접고 잔 마법은 계속 쏜다.
            bool pressured = u.ShouldKeepDistance();

            // 위에서부터 훑어 지금 쓸 수 있는 첫 번째를 고른다. 표의 순서가 곧 우선순위이므로
            // (SpellCatalog 주석) 파괴력이 큰 것부터 검토하고, 마지막의 기본 마법이 언제나 받쳐 준다.
            for (int i = 0; i < spells.Length; i++)
            {
                SpellSpec candidate = spells[i];
                if (pressured && candidate.Role != SpellRole.Bolt) continue;
                if (!IsReady(candidate)) continue;
                if (!TryFindAimPoint(candidate, out aimPoint)) continue;

                spell = candidate;
                return true;
            }

            return false;
        }

        private bool IsReady(in SpellSpec spell)
        {
            if (readyTime != null && spell.Index < readyTime.Length && Time.time < readyTime[spell.Index])
            {
                return false;
            }

            return u.stats.HasMana(spell.ManaCost);
        }

        // 마법이 떨어질 자리를 고른다.
        //
        // 노리던 상대의 발밑이 기본이지만, 그 자리가 최선이라는 보장은 없다. 적이 몰려 있는 쪽에
        // 떨어뜨리는 것이 광역 마법의 값어치이므로, 사거리 안의 적들을 후보로 두고 각자의 자리에
        // 떨어뜨렸을 때 몇을 덮는지 세어 가장 많이 덮는 자리를 고른다.
        //
        // 후보를 적의 위치로만 잡는 것은 근사다. 정확한 최적해(원 덮기)를 구하려면 비용이 크고,
        // 실제 난전에서는 "가장 뭉친 놈 발밑"이 거의 언제나 답이다.
        private bool TryFindAimPoint(in SpellSpec spell, out Vector3 aimPoint)
        {
            aimPoint = u.CurrentTarget.Position;

            // 반경이 없는 마법은 노리는 상대에게 그대로 간다.
            if (spell.Radius <= 0.01f) return spell.MinTargets <= 1;

            int bestCount = UnitRegistry.CountEnemiesAround(u, aimPoint, spell.Radius);

            UnitRegistry.FindEnemiesInRange(u, u.stats.attackRange + u.stats.moveStopDistance, Victims);
            int sampled = 0;
            for (int i = 0; i < Victims.Count && sampled < u.spellAimSampleLimit; i++, sampled++)
            {
                TargetRef candidate = Victims[i];
                if (!candidate.Exists) continue;

                Vector3 point = candidate.Position;
                int count = UnitRegistry.CountEnemiesAround(u, point, spell.Radius);
                if (count <= bestCount) continue;

                bestCount = count;
                aimPoint = point;
            }

            Victims.Clear();

            // 광역기가 하나 잡자고 나가면 마력만 버린다. 값어치가 설 때만 쓴다.
            return bestCount >= spell.MinTargets;
        }

        // ---------------------------------------------------------------- 영창

        // 지금 영창을 시작할 수 있는가. CastBehavior 가지로 들어가는 조건이다.
        public bool CanCast()
        {
            if (u.IsDead || u.stats.affinity == MagicAffinity.None) return false;
            if (u.IsCasting) return false;
            // 휘두르는 중에는 영창을 시작하지 않는다. 이미 나간 마력탄이 있다.
            if (u.IsAttackAnimationLocked) return false;
            if (u.IsStaggered) return false;

            // 영창이 흩어진 직후에는 곧바로 다시 모으지 못한다.
            //
            // 이게 없으면 붙잡힌 마법사가 "시작 → 맞아서 중단 → 즉시 시작 → 다시 중단"을 초당 서너 번
            // 반복한다. 실측 로그가 통째로 그 왕복이었고(완주는 넷 중 하나), 영창 모션이 0.3초마다
            // 처음부터 되감기니 화면에서는 짧은 평타를 계속 내지르는 것처럼 보였다.
            // 흩어진 마력을 다시 모으는 데는 시간이 걸린다 — 한 박자 쉬어야 동작도 판단도 성립한다.
            if (Time.time < retryTime) return false;

            if (Time.time < nextEvaluationTime) return false;

            nextEvaluationTime = Time.time + EvaluationInterval;
            return Select(out _, out _);
        }

        // 영창 시작. 마력도 쿨다운도 여기서는 건드리지 않는다.
        //
        // 영창이 끊기면 아무것도 소모하지 않는다 — 모으던 마력은 아직 현상이 되지 않았으므로
        // 흩어질 뿐 쓰인 것이 아니다. 그래서 대가는 오직 시간이다: 그 자리에 무방비로 서 있던
        // 시간과, 다시 모으기까지의 한 박자(spellRetryDelay).
        // 실제 소모와 쿨다운은 마법이 현상으로 구현되는 순간에 일어난다(Execute).
        public void BeginCast(in SpellSpec spell, Vector3 aimPoint)
        {
            casting = spell;
            castingAimPoint = aimPoint;
            hasCasting = true;

            // 물러난 뒤 한 수는 냈다는 표시.
            //
            // 이 플래그는 원거리 유닛이 Attack↔Evade만 오가는 것을 막으려고 평타가 세우는 값인데,
            // 마법사는 평타가 없어 그 자리를 영영 지나지 않는다. 그래서 첫 회피 이후로는
            // 계속 거짓이었고, 공격 중의 거리 벌리기 조건이 다시는 성립하지 않았다 —
            // 실측에서 마법사가 1.8m(유지 거리 2.6m 안쪽)에 붙잡힌 채 빠져나오지 못했다.
            // 마법사에게는 영창이 곧 "한 수 냈다"이므로 여기서 세운다.
            u.MarkAttackedSinceEvade();

            u.BeginCast();
            // 전용 영창 모션이 없으면 대기 자세로 선다. 서서 마력을 모으는 그림으로 읽히므로
            // 아무것도 안 하는 것보다 낫고, 마법 자체는 그대로 나간다.
            u.PlayAnimation(castAnimationHash != 0 ? castAnimationHash : u.idleAnimationHash, true);
        }

        // 이번 영창이 실제로 걸리는 시간. 성장(castSpeedMultiplier)이 여기에 곱해진다.
        public float CurrentCastDuration =>
            hasCasting ? casting.CastTime * Mathf.Max(0.1f, u.stats.castSpeedMultiplier) : 0f;

        // 영창을 끝까지 마쳤다. 여기서 마력이 현상이 된다.
        public void Execute()
        {
            u.EndCast();

            if (!hasCasting) return;

            SpellSpec spell = casting;
            Vector3 aimPoint = castingAimPoint;
            hasCasting = false;

            if (u.IsDead) return;

            // 마력은 여기서 나간다 — 영창 도중에 끊겼다면 이 자리에 오지 않으므로 아무것도 소모되지 않는다.
            u.stats.SpendMana(spell.ManaCost);

            EnsureCooldowns();
            if (readyTime != null && spell.Index < readyTime.Length)
            {
                readyTime[spell.Index] = Time.time + spell.Cooldown;
            }

            int damage = u.ScaleDamage(Mathf.RoundToInt(u.stats.attackDamage * spell.DamageMultiplier));

            if (spell.Radius <= 0.01f)
            {
                // 반경이 없는 마법은 겨눈 하나에게만 간다.
                if (!u.IsTargetValid()) return;

                // 손을 떠나는 것이 보여야 한다. 마법사가 든 것은 무기가 아니라 맨손이지만
                // (Casting.asset — 모델 없음, 투사체만 있음) 탄환은 활의 화살과 같은 길을 간다.
                // 예전에는 공격 애니메이션의 타격 이벤트가 탄환을 쐈는데, 마법사에게 평타가 없어진 뒤로는
                // 영창을 마친 이 자리에서 직접 쏘지 않으면 아무것도 날아가지 않는다.
                TargetRef target = u.CurrentTarget;
                bool fired = u.TryFireProjectile(target, damage, spell.PoiseDamage, true);
                if (!fired) target.TakeDamage(damage, u, true, true, spell.PoiseDamage, SkillImpactWeight);

                // 둔화는 탄환에 실어 보낼 수 없어(WeaponProjectile은 피해만 옮긴다) 여기서 건다.
                // 탄환이 닿기 직전에 걸리는 셈이지만 사거리 7.5m를 0.2초에 지나가므로 눈에 띄지 않는다.
                if (spell.SlowDuration > 0f && spell.SlowMultiplier < 1f)
                {
                    target.ApplySlow(spell.SlowDuration, spell.SlowMultiplier);
                }
                return;
            }

            UnitRegistry.FindEnemiesAround(u, aimPoint, spell.Radius, Victims);
            for (int i = 0; i < Victims.Count; i++) ApplyTo(Victims[i], spell, damage);
            Victims.Clear();

            // 영창을 대가로 치른 한 방이 떨어지는 자리. 반경이 클수록 크게 흔든다 —
            // 마법사의 "압도적인 한 방"이 화면에 남는 것은 이 순간뿐이다. 빗맞아도 땅은 울린다.
            GameServices.Shake.Current.Emit(u, Mathf.Clamp01(AreaSpellShakeBase + spell.Radius * AreaSpellShakePerMeter));
        }

        private void ApplyTo(TargetRef victim, in SpellSpec spell, int damage)
        {
            if (!victim.IsAlive) return;

            // 마법은 밀쳐낸다. fromSkill로 넘겨 평타와 다른 취급을 받게 한다(출혈 판정 등).
            victim.TakeDamage(damage, u, true, true, spell.PoiseDamage, SkillImpactWeight);

            // 속성이 남기는 것. 빙결은 묶고, 화염은 밀어내며 태우고, 전격은 무너뜨린다
            // (전격의 몫은 위 PoiseDamage가 이미 크게 잡혀 있다).
            if (spell.SlowDuration > 0f && spell.SlowMultiplier < 1f)
            {
                victim.ApplySlow(spell.SlowDuration, spell.SlowMultiplier);
            }
        }

        // 영창이 끊겼다. 모으던 마력은 흩어질 뿐 쓰이지 않는다 — 대가는 다시 모으기까지의 한 박자다.
        public void Cancel()
        {
            if (!hasCasting && !u.IsCasting) return;

            hasCasting = false;
            u.EndCast();
            retryTime = Time.time + u.spellRetryDelay;
        }

        // 죽은 유닛을 재사용하는 경로(Configure)를 위한 초기화.
        public void Reset()
        {
            hasCasting = false;
            if (readyTime != null)
            {
                for (int i = 0; i < readyTime.Length; i++) readyTime[i] = 0f;
            }
            nextEvaluationTime = 0f;
            retryTime = 0f;
        }
    }
}
