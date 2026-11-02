using UnityEngine;

// 아군이 "어디에 서서 싸울 것인가"를 땅에서 읽는다(11층부터, 건물이 선 전장).
//
// 원작의 전투는 지형을 쓴다 — 탱커는 좁은 길목을 막고, 전열은 등 뒤를 벽에 맡긴 채 앞에서 오는 칼만 받고,
// 후방은 그 뒤에서 쏜다. 이 파일은 그 판단을 점수 하나로 만든다. 자리를 옮기는 것 자체는 TerrainPart가
// 이 점수를 보고 "지킬 자리"를 정하면, 진형 유지 명령과 같은 동작(HoldPositionBehavior)이 맡는다.
//
// 자리의 값은 둘이다. 바닥이 평평하므로(BattleGround 주석) 높이는 없다.
//  1) 등 뒤 — 방어 각도(guardArcAngle) 밖에서 적이 설 수 있는 자리가 몇 곳인가. 막는 것은 앞에서 오는 칼뿐이라
//            등 뒤가 열려 있으면 아무리 막아도 옆·뒤에서 맞는다. 벽에 등을 대면 그 칸들이 닫힌다.
//  2) 둘레 — 적이 칼을 들 수 있는 자리 수. 좁을수록 한꺼번에 덤비는 수가 줄어든다(길목).
// 거기서 걸어가야 하는 거리를 뺀다 — 좋은 자리라도 가는 동안 맞으면 소용없다.
//
// 격자는 적이 딛는 땅(BattleGround)을 그대로 읽는다. 아군과 적이 서로 다른 땅을 믿으면, 아군이 기댄 벽을
// 적은 모르고 뚫고 들어온다.
public static class TerrainTactics
{
    // 둘레를 재는 고리. 적이 칼을 들고 서는 거리(멈춰 설 거리 1.0m + 몸 반지름)쯤이다.
    private const float RingRadius = 1.4f;
    private const int RingDirections = 12;

    // 적이 설 수 있으려면 벽에서 이만큼은 떨어져 있어야 한다(적 몸 반지름 0.5).
    private const float EnemyStandClearance = 0.45f;

    // 아군이 서려면 이만큼 비어 있어야 한다(NavMeshAgent 반지름 0.5 + 여유).
    private const float SpotClearance = 0.6f;

    // 후보를 찍는 간격.
    private const float SampleStep = 1.25f;

    // 파티에서 이보다 멀어지는 자리는 깎는다. 혼자 길목에 서 있으면 나머지가 둘러싸인다.
    private const float CohesionRadius = 9f;
    private const float CohesionPenaltyPerMeter = 0.35f;

    public readonly struct Weights
    {
        public readonly float RearClosed;
        public readonly float RingClosed;
        public readonly float Travel;
        public readonly float SearchRadius;

        public Weights(float rearClosed, float ringClosed, float travel, float searchRadius)
        {
            RearClosed = rearClosed;
            RingClosed = ringClosed;
            Travel = travel;
            SearchRadius = searchRadius;
        }
    }

    // 전열 — 등 뒤를 닫는 것이 가장 크고, 길목(둘레가 좁은 자리)이 그다음이다. 막는 직군이라 앞에서 오는 칼은 받아낸다.
    public static readonly Weights Melee = new Weights(rearClosed: 2f, ringClosed: 1.5f, travel: 0.12f, searchRadius: 8f);

    // 후방 — 붙기 어려운 자리(둘레가 닫힌 곳)를 더 친다. 사거리 안에서만 고른다.
    public static readonly Weights Ranged = new Weights(rearClosed: 0.8f, ringClosed: 1.6f, travel: 0.08f, searchRadius: 10f);

    public static bool IsReady => BattleGround.IsBuiltNow;

    // 이 자리에 섰을 때의 값. threat는 적이 몰려오는 쪽(무게중심)이다. travel은 걸어갈 거리.
    public static float Score(UnitController unit, Vector3 position, Vector3 threat, Weights weights, float travel,
        Vector3 partyCenter)
    {
        Vector3 facing = threat - position;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.0001f) facing = unit.transform.forward;
        facing.Normalize();

        // 막는 수단이 없는 직군(마법사)은 정면 반구만 앞으로 친다.
        float guardArc = unit.Stats.guardStyle != GuardStyle.None ? unit.Stats.guardArcAngle : 180f;
        float guardCos = Mathf.Cos(Mathf.Clamp(guardArc * 0.5f, 0f, 180f) * Mathf.Deg2Rad);

        int open = 0;
        int rear = 0;
        int rearOpen = 0;
        for (int i = 0; i < RingDirections; i++)
        {
            float angle = i * (360f / RingDirections) * Mathf.Deg2Rad;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            bool outside = Vector3.Dot(direction, facing) < guardCos;
            if (outside) rear++;

            if (BattleGround.SampleClearance(position + direction * RingRadius) <= EnemyStandClearance) continue;

            open++;
            if (outside) rearOpen++;
        }

        float rearClosed = rear > 0 ? 1f - rearOpen / (float)rear : 1f;
        float ringClosed = 1f - open / (float)RingDirections;

        float score = weights.RearClosed * rearClosed + weights.RingClosed * ringClosed - weights.Travel * travel;

        Vector3 fromParty = position - partyCenter;
        fromParty.y = 0f;
        float stray = fromParty.magnitude - CohesionRadius;
        if (stray > 0f) score -= stray * CohesionPenaltyPerMeter;

        return score;
    }

    // 지금 자리보다 minGain 이상 나은 자리가 반경 안에 있는가.
    //
    // 후보는 걸어서 닿는 곳만이다 — 이 아군의 흐름장(BattleGround.WalkDistance)이 거리를 준다.
    // 벽 너머처럼 가까워 보여도 한참 돌아가야 닿는 자리는 거기서 걸러진다.
    public static bool TryFindBetterSpot(UnitController unit, Vector3 threat, Weights weights, float minGain,
        Vector3 partyCenter, float maxThreatDistance, out Vector3 best)
    {
        Vector3 origin = unit.transform.position;
        float current = Score(unit, origin, threat, weights, 0f, partyCenter);

        best = origin;
        float bestScore = current;
        if (!IsReady) return false;

        int slot = EnemyWorldBridge.IndexOfAlly(unit);
        float radius = weights.SearchRadius;
        int steps = Mathf.CeilToInt(radius / SampleStep);

        for (int zi = -steps; zi <= steps; zi++)
        {
            for (int xi = -steps; xi <= steps; xi++)
            {
                var offset = new Vector3(xi * SampleStep, 0f, zi * SampleStep);
                float straight = offset.magnitude;
                if (straight > radius || straight < SampleStep * 0.5f) continue;

                Vector3 candidate = origin + offset;
                if (BattleGround.SampleClearance(candidate) < SpotClearance) continue;

                // 사거리를 벗어나는 자리는 쏠 수 없는 자리다(후방). 전열은 적에게서 너무 먼 자리를 고르지 않는다.
                Vector3 toThreat = threat - candidate;
                toThreat.y = 0f;
                if (toThreat.magnitude > maxThreatDistance) continue;

                float travel = BattleGround.WalkDistance(slot, candidate);
                if (travel >= BattleGround.Unreachable) continue;
                // 흐름장이 아직 없으면(스냅샷에 안 실린 유닛) 곧은 거리로 본다.
                if (travel < 0f) travel = straight;
                // 한참 돌아가야 닿는 자리는 가까워 보여도 먼 자리다.
                if (travel > radius * 1.6f) continue;

                candidate.y = origin.y;
                float score = Score(unit, candidate, threat, weights, travel, partyCenter);
                if (score <= bestScore) continue;

                bestScore = score;
                best = candidate;
            }
        }

        return bestScore - current >= minGain;
    }
}
