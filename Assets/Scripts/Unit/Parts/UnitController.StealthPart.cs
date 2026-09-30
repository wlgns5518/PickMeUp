using UnityEngine;

public partial class UnitController
{
    // 손을 놓고 있으면 모습을 감추는 부품. 암살자(stats.canStealth)만 쓴다.
    //
    // 원작의 암살자는 몸으로 버티는 직군이 아니라 사각지대로 침투해 후방을 치는 게릴라라,
    // 살아남는 방식이 방어가 아니라 "거기 없는 것"이다. 은신 중에는 적이 겨누지 못하고
    // (UnitRegistry.IsHiddenFrom) 받는 피해도 줄어든다. 때리거나 맞으면 그 자리에서 풀린다 —
    // 치고 빠지는 리듬이 여기서 나온다.
    private sealed class StealthPart
    {
        private readonly UnitController u;

        // 마지막으로 때리거나 맞은 시각. 이때부터 stealthDelay가 흐른다.
        private float lastBreakTime = -999f;

        public StealthPart(UnitController owner)
        {
            u = owner;
        }

        public bool IsStealthed { get; private set; }

        public void Tick()
        {
            UnitStats stats = u.stats;
            if (!stats.canStealth || u.IsDead)
            {
                IsStealthed = false;
                return;
            }

            // 자세가 무너져 있으면 숨을 수 없다. 넘어져 있는 사람은 그림자에 못 든다.
            if (u.IsStaggered || (u.emotion != null && u.emotion.IsActionBlocked))
            {
                IsStealthed = false;
                return;
            }

            // 아직 드러나 있는 시간이면 숨을 수 없다.
            if (Time.time < lastBreakTime + stats.stealthDelay)
            {
                IsStealthed = false;
                return;
            }

            // 여기가 핵심이다: 은신은 시간이 아니라 거리로 갈린다.
            //
            // 처음에는 "일정 시간 손을 놓고 있으면 숨는다"로 만들었는데 전투 내내 한 번도 걸리지
            // 않았다. 실측해 보니 난전 중 암살자가 손을 놓는 빈틈이 중앙값 0.1초, 최대 1.55초라
            // 어떤 값을 넣어도 성립하지 않았다 — 칼이 닿는 자리에 서 있는 한 계속 휘두르기 때문이다.
            //
            // 그래서 조건을 바꿨다. 붙어서 칼을 섞는 동안은 숨지 못하고, 그 자리에서 떨어져 나온
            // 순간(다음 표적으로 파고드는 중, 전선을 가로지르는 중) 그림자로 든다. 원작의 암살자가
            // 위험한 순간이 바로 그 이동 구간이고, 은신이 지켜 주어야 할 것도 정확히 그 구간이다.
            float contact = Mathf.Max(1f, stats.attackRange * 1.2f);
            IsStealthed = UnitRegistry.CountEnemiesAround(u, u.transform.position, contact) == 0;
        }

        // 은신이 풀리는 두 순간: 내가 때렸을 때와 내가 맞았을 때.
        public void Break()
        {
            IsStealthed = false;
            lastBreakTime = Time.time;
        }
    }
}
