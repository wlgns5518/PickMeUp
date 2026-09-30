using UnityEngine;
using UnityEngine.AI;

public partial class UnitController
{
    // 쏘기 좋은 높은 자리를 찾는 부품. 궁수(Marksman)만 쓴다.
    //
    // 원작의 궁수는 "고지 선점 → 시야 확보 → 정찰"이 한 묶음이다. 높은 곳은 그 자체로
    // 사거리를 벌어 주고, 난전 위로 시선이 통해 후방까지 보인다. 지금 구조에서는 높이가
    // 직접 이득을 주지는 않지만, 적어도 근접 유닛이 붙기 어려운 자리에 서게 된다.
    private sealed class HighGroundPart
    {
        private const float Hold = 3f;
        // 이보다 덜 높으면 고지가 아니다. 평지의 NavMesh 잡음에 끌려다니지 않게 하는 하한.
        private const float MinGain = 0.8f;
        private static readonly float[] Angles = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };

        private readonly UnitController u;

        // 마지막으로 잡은 고지와 그 시각. 매 프레임 다시 훑으면 비싸고, 그때마다 답이 조금씩
        // 달라져 궁수가 언덕 위에서 잔걸음을 친다. 한 번 고른 자리를 잠시 붙들고 간다.
        private Vector3 spot;
        private float time = -999f;

        public HighGroundPart(UnitController owner)
        {
            u = owner;
        }

        // 표적에서 멀어지는 쪽으로는 가지 않는다 — 고지를 찾다 사거리 밖으로 나가면 정찰이
        // 아니라 이탈이다(예전에 궁수가 45m 밖으로 나가 파티가 쪼개진 적이 있다).
        public bool TryFind(Vector3 desiredSpot, out Vector3 highGround)
        {
            highGround = desiredSpot;
            if (u.stats.role != JobRole.Marksman || !u.IsTargetValid()) return false;

            // 아직 유지 시간이 남았으면 지난번 자리를 그대로 쓴다.
            if (Time.time < time + Hold && spot.sqrMagnitude > 0.0001f)
            {
                highGround = spot;
                return true;
            }

            Vector3 origin = u.transform.position;
            Vector3 targetPos = u.CurrentTarget.Position;
            float searchRadius = Mathf.Max(2f, u.stats.attackRange * 0.5f);
            float maxRangeSqr = u.stats.attackRange * u.stats.attackRange;

            Vector3 best = desiredSpot;
            float bestHeight = origin.y + MinGain;
            bool found = false;

            for (int i = 0; i < Angles.Length; i++)
            {
                Vector3 dir = Quaternion.AngleAxis(Angles[i], Vector3.up) * Vector3.forward;
                Vector3 probe = origin + dir * searchRadius;
                if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, searchRadius * 0.6f, NavMesh.AllAreas)) continue;

                if (hit.position.y <= bestHeight) continue;
                // 그 자리에서 표적이 사거리 안에 들어와야 의미가 있다.
                if ((hit.position - targetPos).sqrMagnitude > maxRangeSqr) continue;

                bestHeight = hit.position.y;
                best = hit.position;
                found = true;
            }

            spot = found ? best : Vector3.zero;
            time = Time.time;
            highGround = best;
            return found;
        }
    }
}
