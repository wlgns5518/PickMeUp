using UnityEngine;

public partial class UnitController
{
    // 맞았을 때 어떤 모션을 낼지 고르는 부품.
    //
    // 공격자가 내 어느 쪽에 있었는지(HitFront/Back/Left/Right), 막다가 뚫렸는지(BlockBreak),
    // 자세가 통째로 무너졌는지(Stagger)에 따라 모션이 갈린다. 하나라도 없으면 그 경우만
    // 기본 Hit으로 떨어진다 — 전부 갖추지 않아도 동작한다.
    private sealed class HitReactionPart
    {
        private readonly UnitController u;

        private int frontHash;
        private int backHash;
        private int leftHash;
        private int rightHash;
        private int blockBreakHash;
        private int staggerHash;

        // 마지막으로 맞은 방향. TakeDamage가 기록하고 HitBehavior가 모션을 고를 때 읽는다.
        // 상태에 인자를 넘기지 않고 여기 두는 이유는, 상태 객체가 유닛마다 하나씩 재사용되기 때문이다 —
        // Enter에 값을 실어 보낼 통로가 없다.
        private Vector3 lastAttackerPosition;
        private bool hasLastAttacker;

        // 이번 피격이 "막다가 뚫린 것"인가. TakeDamage가 세우고 PlayHit이 한 번 쓰고 내린다.
        private bool fromGuardBreak;

        public HitReactionPart(UnitController owner)
        {
            u = owner;
        }

        public void CacheHashes()
        {
            frontHash = u.ResolveStateHash(u.hitFrontStateName);
            backHash = u.ResolveStateHash(u.hitBackStateName);
            leftHash = u.ResolveStateHash(u.hitLeftStateName);
            rightHash = u.ResolveStateHash(u.hitRightStateName);
            blockBreakHash = u.ResolveStateHash(u.blockBreakStateName);
            staggerHash = u.ResolveStateHash(u.staggerStateName);
        }

        // 때린 쪽이 엔티티면 위치만 온다. 피격 모션을 고르는 데 필요한 것은 어차피 방향뿐이다.
        public void RecordDirection(Vector3 attackerPosition, bool hasAttacker)
        {
            hasLastAttacker = hasAttacker;
            if (hasAttacker) lastAttackerPosition = attackerPosition;
        }

        public void MarkGuardBreak(bool value) => fromGuardBreak = value;

        // 가드가 뚫린 그 한 대만은 방향과 무관하게 BlockBreak으로 낸다. 경직을 없앤 뒤에도
        // "방패가 젖혀졌다"는 것은 보여야 하기 때문이다 — 그러지 않으면 막다 뚫린 것과
        // 그냥 한 대 맞은 것이 화면에서 구분되지 않는다.
        public void PlayHit()
        {
            bool guardBreak = fromGuardBreak;
            fromGuardBreak = false;

            if (guardBreak && blockBreakHash != 0)
            {
                u.PlayAnimation(blockBreakHash, true);
                return;
            }

            // 어디서 맞았는지 모르는 경우(출혈 등)는 기본 피격 모션으로 친다.
            int hash = hasLastAttacker ? ResolveDirectional(lastAttackerPosition) : u.hitAnimationHash;
            u.PlayAnimation(hash != 0 ? hash : u.hitAnimationHash, true);
        }

        public void PlayStagger(bool guardBreak)
        {
            int hash = guardBreak && blockBreakHash != 0 ? blockBreakHash : staggerHash;
            u.PlayAnimation(hash != 0 ? hash : u.hitAnimationHash, true);
        }

        public void Reset()
        {
            hasLastAttacker = false;
            fromGuardBreak = false;
        }

        private int ResolveDirectional(Vector3 attackerPosition)
        {
            int fallback = u.hitAnimationHash;
            if (frontHash == 0 && backHash == 0 && leftHash == 0 && rightHash == 0) return fallback;

            Transform transform = u.transform;
            Vector3 toAttacker = attackerPosition - transform.position;
            toAttacker.y = 0f;

            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (toAttacker.sqrMagnitude <= 0.0001f || forward.sqrMagnitude <= 0.0001f)
            {
                return frontHash != 0 ? frontHash : fallback;
            }

            float angle = Vector3.SignedAngle(forward, toAttacker, Vector3.up);
            float absAngle = Mathf.Abs(angle);

            int hash;
            if (absAngle <= 50f) hash = frontHash;
            else if (absAngle >= 130f) hash = backHash;
            else hash = angle > 0f ? rightHash : leftHash;

            return hash != 0 ? hash : fallback;
        }
    }
}
