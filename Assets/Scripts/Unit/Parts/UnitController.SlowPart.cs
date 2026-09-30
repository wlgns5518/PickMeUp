using UnityEngine;

public partial class UnitController
{
    // 발을 무겁게 하는 두 가지를 맡는 부품: 둔화(부위 억제)와 피격 둔화(움찔).
    //
    // 둘을 같은 자리에 넣지 않는다. 같은 자리에 넣으면 "더 센 쪽이 이기고 더 긴 쪽으로 늘린다"는
    // 규칙에 섞여, 창수에게 1.6초 묶인 동안 스친 한 대가 그 1.6초 전체를 더 느리게 만든다.
    private sealed class SlowPart
    {
        private readonly UnitController u;

        // 둔화. 창수의 부위 억제가 여기로 들어온다 — 다리를 찔린 쪽은 한동안 제 속도를 못 낸다.
        private float slowUntil;
        private float slowMultiplier = 1f;
        private bool slowWasActive;

        // 피격 둔화. 살에 닿은 한 대를 맞은 직후 잠깐 발이 무겁다.
        private float flinchUntil;
        private bool flinchWasActive;

        public SlowPart(UnitController owner)
        {
            u = owner;
        }

        // 지금 걸려 있는 둔화 배율. 이동 속도와 걸음 재생 배속 양쪽에 곱해진다.
        // 애니메이션에도 함께 곱해야 느려진 다리가 땅을 헛돌지 않는다.
        public float SlowMultiplier => Time.time < slowUntil ? slowMultiplier : 1f;

        public float FlinchMultiplier => Time.time < flinchUntil ? u.stats.hitFlinchMoveMultiplier : 1f;

        public void ApplyFlinch()
        {
            if (u.IsDead || u.stats.hitFlinchDuration <= 0f) return;

            flinchUntil = Mathf.Max(flinchUntil, Time.time + u.stats.hitFlinchDuration);
            if (flinchWasActive) return;

            flinchWasActive = true;
            u.RefreshAgentSpeed();
        }

        public void ApplySlow(float duration, float multiplier)
        {
            if (u.IsDead || duration <= 0f || multiplier >= 1f) return;

            // 이미 더 강한(더 느린) 둔화가 걸려 있으면 덮어쓰지 않는다. 겹쳐 걸어 0에 수렴하면
            // 창수 둘에게 찔린 적이 그 자리에 못 박히는데, 그건 억제가 아니라 속박이다.
            float current = SlowMultiplier;
            slowMultiplier = Mathf.Clamp(Mathf.Min(current, multiplier), 0.1f, 1f);
            slowUntil = Mathf.Max(slowUntil, Time.time + duration);

            // 에이전트 속도는 값이 바뀔 때만 다시 계산한다(HandleEmotionChanged와 같은 이유).
            u.RefreshAgentSpeed();
        }

        // 둔화가 풀리는 순간을 잡아 속도를 되돌린다. 만료를 감시하지 않으면 SlowMultiplier는
        // 1로 돌아가는데 NavMeshAgent.speed는 느린 값을 그대로 들고 있게 된다.
        public void Tick()
        {
            bool active = Time.time < slowUntil;
            bool flinching = Time.time < flinchUntil;
            if (active == slowWasActive && flinching == flinchWasActive) return;

            slowWasActive = active;
            flinchWasActive = flinching;
            if (!active) slowMultiplier = 1f;
            u.RefreshAgentSpeed();
        }

        public void Reset()
        {
            slowUntil = 0f;
            slowMultiplier = 1f;
            slowWasActive = false;
            flinchUntil = 0f;
            flinchWasActive = false;
        }
    }
}
