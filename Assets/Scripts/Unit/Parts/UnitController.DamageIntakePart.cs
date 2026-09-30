using UnityEngine;
using Random = UnityEngine.Random;

public partial class UnitController
{
    // 맞은 한 대가 거치는 길을 맡는 부품.
    //
    //   흘려내기 → 배율(배후·회수·경직·영창·은신) → 체력 → 흔적(피·출혈·둔화) → 히트스톱 →
    //   밀림 방향 → 어그로 → 사망 → 강인도 → 동작 끊기
    //
    // 순서가 곧 규칙이라 한곳에 둔다. 각 단계가 실제로 하는 일은 그 단계의 부품(Guard, Posture,
    // HitStop, Slow, Locomotion, Targeting …)이 들고 있고, 여기서는 "어떤 순서로, 어떤 조건에서"만 정한다.
    private sealed class DamageIntakePart
    {
        private readonly UnitController u;

        public DamageIntakePart(UnitController owner)
        {
            u = owner;
        }

        public void Take(int damage, UnitController attacker, Vector3 attackerPosition, bool hasAttackerPosition,
            Unity.Entities.Entity attackerEntity, bool applyKnockback, bool fromSkill, float poiseDamage,
            float impactWeight)
        {
            UnitStats stats = u.stats;
            if (impactWeight <= 0f) impactWeight = 1f;

            // 이미 죽은 유닛에 피가 튀거나 피격 상태로 되돌아가지 않도록 여기서 끊는다.
            if (u.IsDead) return;

            // 뒤를 잡혔는가(피해 배율)와 받아낼 수 있는가(방어 각도)는 다른 질문이다.
            // 방패는 정면 반구를 통째로 가리지만 패링은 훨씬 좁으므로, 검사에게는 "막지는 못했지만
            // 등 뒤도 아닌" 구간이 생긴다 — 옆에서 들어온 칼은 정직하게 한 대 맞는다.
            bool inFrontArc = !hasAttackerPosition || u.IsWithinFrontArc(attackerPosition);
            bool wantsToBlock = u.IsBlocking && (!hasAttackerPosition || u.IsWithinGuardArc(attackerPosition));

            // 방패를 올린 직후에 들어온 공격은 막는 것이 아니라 통째로 흘려낸다.
            // 흘러가면 피해도 강인도 소모도 없으므로 아래 계산 자체를 건너뛴다.
            if (wantsToBlock && u.Guard.TryPerfect(attacker, attackerEntity)) return;

            bool wasBlocking = wantsToBlock;

            // 같은 공격이라도 어디서, 어떤 처지에서 맞았느냐로 실제 피해가 갈린다.
            // 이 세 가지가 "위치를 잡는 것"과 "먼저 내지르는 것"에 값을 매긴다.
            float damageMultiplier = 1f;
            if (hasAttackerPosition && !inFrontArc)
            {
                damageMultiplier *= stats.backstabDamageMultiplier;
                poiseDamage *= stats.backstabPoiseMultiplier;
            }
            // 내가 방금 칼을 내지르고 거두는 중이라 반응할 수 없는 상태.
            if (u.IsInAttackRecovery) damageMultiplier *= stats.recoveryVulnerabilityMultiplier;
            // 이미 자세가 무너져 있는 상태.
            if (u.IsStaggered) damageMultiplier *= stats.staggerDamageMultiplier;
            // 마력을 모으는 중이라 몸을 뺄 수도 막을 수도 없는 상태. 후방 시전자에게 가장 큰 배율이다 —
            // 원작에서 마법사·사제가 탱커의 방어선 없이는 아무것도 못 하는 이유가 이것이다.
            if (u.IsCasting) damageMultiplier *= stats.castVulnerabilityMultiplier;
            // 그림자 속에 있으면 겨눠 맞히기 어렵다. 암살자가 몸으로 버티지 않고도 사는 방식이다.
            // (이 한 대로 은신은 풀린다 — 아래에서 BreakStealth를 부른다.)
            if (u.IsStealthed) damageMultiplier *= stats.stealthDamageMultiplier;

            // 배율이 아무리 낮아도 1은 들어가던 하한을 걷어냈다(UnitStats.TakeDamage 주석 참조).
            // 그림자 속에서 겨눠 맞힌 스치는 한 대가 0이 될 수 있다 — 그게 은신이 몸으로 버티지
            // 않고도 사는 방식이고, 배율을 낮게 잡아 둔 뜻이기도 하다.
            int incoming = damage > 0 ? Mathf.RoundToInt(damage * damageMultiplier) : damage;

            int hpBefore = stats.currentHp;
            stats.TakeDamage(incoming, wasBlocking);
            int dealt = hpBefore - stats.currentHp;

            // 막았다는 것 자체가 보여야 한다. 예전에는 피가 안 튀는 것 말고는 아무 반응도 없었다.
            if (wasBlocking) u.Guard.PlayImpact();

            // 맞았으면 위치가 드러난다. 배율은 위에서 이미 적용됐으므로 이 한 대는 감면을 받는다.
            u.BreakStealth();

            u.combatRecord.RecordHit(dealt, attacker, u);
            u.HitReaction.RecordDirection(attackerPosition, hasAttackerPosition);
            if (!wasBlocking) SpawnBlood(attackerPosition, hasAttackerPosition);
            if (u.emotion != null) u.emotion.NotifyDamaged(dealt, fromSkill);

            // 때린 쪽의 직군이 남기는 흔적. 막아낸 타격은 살에 닿지 않았으므로 아무것도 남기지 않는다.
            if (!wasBlocking) ApplyOnHitDebuffs(attacker, inFrontArc);

            // 칼이 닿은 순간 양쪽의 애니메이션을 아주 짧게 눌러 붙인다. 막힌 타격은 살에 박히는
            // 것이 아니라 튕겨 나가는 것이라 더 짧게 끊는다. 무거운 한 방일수록 길다.
            u.HitStop.ApplyImpact(attacker, (wasBlocking ? 0.6f : 1f) * impactWeight);

            // 살에 닿은 한 대는 발을 무겁게 한다(피격 둔화). 막아낸 타격은 몸에 닿지 않았다.
            if (!wasBlocking && dealt > 0) u.Slow.ApplyFlinch();

            // 밀려날 방향은 때린 자리에서 나온다. 때린 쪽이 엔티티여도 위치는 온다.
            //
            // 예전에는 이 판단이 "때린 UnitController가 있는가" 안에 들어 있어서, 엔티티에게 맞으면
            // 방향이 아예 잡히지 않았다 — 강인도가 깨져 피격 리액션이 나와도 제자리에서 움찔만 했다.
            if (hasAttackerPosition && applyKnockback) u.Locomotion.SetKnockbackFrom(attackerPosition);
            else u.Locomotion.ClearKnockback();

            // 때린 쪽으로 돌아설지 본다. 엔티티에게 맞았어도 같다 — 예전에는 게임오브젝트 공격자만 봐서
            // 고블린에게 뒤를 물린 아군이 멀리 있는 처음 표적만 계속 쫓았다.
            if (attacker != null) u.Targeting.ReactToAttacker(attacker);
            else if (attackerEntity != Unity.Entities.Entity.Null) u.Targeting.ReactToAttacker(new TargetRef(attackerEntity));

            if (stats.IsDead)
            {
                u.Die();
                return;
            }

            // 강인도: 면역 중이 아니면 이번 피격으로 깎는다. 막아낸 타격도 그대로 깎는다
            // (PosturePart.TryDamagePoise 주석 참조). 깨지면 면역 시간이 함께 켜진다.
            if (u.Posture.TryDamagePoise(poiseDamage))
            {
                // 가드가 뚫려도 통째로 무너지지는 않는다.
                //
                // 한때 여기서 Stagger(staggerDuration)로 보냈다. 그런데 막는 쪽 입장에서 그 결과가
                // 가혹했다 — 방패를 들었다가 뚫리면 1.2초를 아무것도 못 하고 서 있어야 하고,
                // 그 사이 CanEverBlock이 거짓이라 다시 막지도 못한다. 여러 마리에게 둘러싸이면
                // 방패를 드는 것 자체가 손해가 됐다.
                //
                // 지금은 막다 뚫린 것도 평범한 강인도 파괴와 같은 무게로 친다: 짧은 피격 반응 한 번.
                // 가드가 열렸다는 사실은 남는다(모션이 BlockBreak으로 바뀌고, 강인도가 0에서
                // 다시 차오르며, 그 순간의 방어 자세는 InterruptCurrentAction이 내려놓는다).
                //
                // 진짜 경직(StaggerBehavior)은 이제 두 곳에서만 나온다 — 흘려내기(퍼펙트 가드)에
                // 걸린 공격자와, 붙잡아 무너뜨리는 스킬(TryForceStagger). 둘 다 "읽어냈다"의 보상이다.
                u.HitReaction.MarkGuardBreak(wasBlocking);
                u.InterruptCurrentAction();
                u.RequestHitReaction();
                return;
            }

            // 강인도가 안 깨졌으면 애니메이션은 끊지 않는다 — 슈퍼아머는 아니라서 살짝 밀리기만 하고
            // 곧장 다시 싸운다(콤보 마무리나 스킬만 진짜 경직을 유발한다).
            if (hasAttackerPosition) u.Locomotion.ApplyMicroPushback(attackerPosition, impactWeight);

            // 영창은 여기서 끊지 않는다.
            //
            // 아래 두 줄(InterruptCurrentAction + InterruptBehavior)은 근접 유닛에게는 무해하다 —
            // 어차피 공격으로 돌아갈 참이었기 때문이다. 그런데 마법사에게는 치명적이다: 동작이
            // 접히는 순간 CastBehavior가 영창을 통째로 흩어 버린다. 스치는 피해 한 번에 7.8초짜리
            // 영창이 사라지는 셈이라, 실측에서 마법사가 붙잡힌 채 단 한 번도 완주하지 못하고
            // 딜 0으로 끝났다(유성 낙하 3회, 화염구 4회 전부 중단).
            //
            // 규칙은 위 주석 그대로 가져간다: 진짜로 끊는 것은 강인도가 깨졌을 때뿐이다.
            // 그 판정은 이미 위쪽에서 피격 리액션을 요청하며 처리했으므로, 여기까지 내려온 피격은
            // "버텨 낸 것"이다. 영창 중에는 그 사이에도 무방비 배율(castVulnerabilityMultiplier)을
            // 그대로 받으므로 공짜로 버티는 것이 아니다 — 계속 맞으면 강인도가 깨져 결국 끊긴다.
            if (u.IsCasting) return;

            u.InterruptCurrentAction();
            u.InterruptBehavior();
        }

        // 출혈처럼 시간에 따라 들어오는 피해. 피격 리액션을 일으키지 않아야
        // 출혈이 공격 모션을 매초 끊어먹는 일이 없다.
        public void TakeBleed(int damage)
        {
            if (u.IsDead) return;

            UnitStats stats = u.stats;
            int hpBefore = stats.currentHp;
            stats.TakeDamage(damage);
            u.combatRecord.AddTaken(hpBefore - stats.currentHp);

            if (stats.IsDead) u.Die();
        }

        // 때린 쪽의 직군이 상대에게 남기는 것. 막아낸 타격은 이 경로로 오지 않는다.
        //
        // 이 한 함수가 "같은 평타인데 직군마다 다른 일이 일어난다"를 만든다. 암살자는 급소를 그어
        // 피를 내고(등을 잡으면 두 배), 창수는 다리를 찔러 발을 묶는다. 나머지 직군은 값이 0이라
        // 아무 일도 일어나지 않으므로, 이 호출이 늘 있어도 예전 동작 그대로다.
        private void ApplyOnHitDebuffs(UnitController attacker, bool inFrontArc)
        {
            if (attacker == null || attacker == u || u.IsDead) return;

            UnitStats source = attacker.Stats;

            // 급소 타격 — 정면에서 그은 것보다 뒤를 잡고 그은 쪽이 확실히 깊다.
            if (source.bleedChanceOnHit > 0f && u.emotion != null)
            {
                float chance = inFrontArc ? source.bleedChanceOnHit : source.bleedChanceOnHit * 2f;
                if (Random.value < chance) u.emotion.ApplyBleeding();
            }

            // 부위 억제 — 발이 묶이면 리치 안으로 파고들지 못한다. 창수가 거리를 유지하는 수단이다.
            if (source.slowOnHitDuration > 0f)
            {
                u.Slow.ApplySlow(source.slowOnHitDuration, source.slowOnHitMultiplier);
            }
        }

        private void SpawnBlood(Vector3 attackerPosition, bool hasAttacker)
        {
            GameObject[] prefabs = u.bloodEffectPrefabs;
            if (prefabs == null || prefabs.Length == 0) return;

            GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];
            if (prefab == null) return;

            Transform transform = u.transform;
            Collider body = u.bodyCollider;
            Vector3 spawnPosition = (body != null ? body.bounds.center : transform.position) + u.bloodEffectOffset;
            Vector3 lookDirection = hasAttacker ? transform.position - attackerPosition : transform.forward;
            lookDirection.y = 0f;
            Quaternion rotation = lookDirection.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(lookDirection.normalized)
                : transform.rotation;

            GameServices.BloodEffects.Current.Spawn(prefab, spawnPosition, rotation, u.bloodColor);
        }
    }
}
