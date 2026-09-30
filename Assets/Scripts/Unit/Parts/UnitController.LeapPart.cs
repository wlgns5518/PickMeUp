using UnityEngine;
using UnityEngine.AI;

public partial class UnitController
{
    // 아직 칼이 닿지 않는 거리에서 몸을 던져 붙는 한 수(도약 공격). 파고들기와는 다르다 —
    // 저쪽은 이미 사거리 근처에서 스윙과 함께 반 발 들어가는 것이고, 이쪽은 접근 자체를
    // 건너뛴다. 짐승처럼 싸우는 적에게만 열어 둔다(stats.leapAttackRange가 0이면 꺼짐).
    //
    // 도약을 NavMeshAgent의 목적지로 옮기지 않는 이유는 회피 도약과 같다(MoveDodge 주석 참조):
    // 에이전트는 가속을 거치므로 목적지를 주면 클립은 뛰는데 몸은 기어간다.
    //
    // 높이는 에이전트에게 맡길 수 없다. NavMeshAgent는 매 프레임 transform을 자기 위치
    // (NavMesh 표면)로 되돌려 놓기 때문에, 그냥 올려 봐야 다음 프레임에 도로 붙는다.
    // 그래서 뜨는 동안만 updatePosition을 꺼서 위치의 주도권을 가져오고, 수평은 그대로
    // 에이전트에게 물어(nextPosition) 경로와 회피가 계속 살아 있게 둔다.
    //
    // 판정은 높이를 보지 않는다(스윙 판정은 전부 XZ 평면이다). 공중에 있는 동안만
    // 맞지 않는다든가 하는 규칙은 만들지 않았다 — 그런 무적 구간은 이 전투의 규칙이 아니다.
    private sealed class LeapPart
    {
        private readonly UnitController u;

        private int hash;
        private float lastLeapTime = -999f;

        private Vector3 direction;
        private float distance;
        private float travelled;
        // 지금 위치를 에이전트 대신 이쪽이 쓰고 있는가(Update 주석 참조).
        private bool holdingPosition;

        public LeapPart(UnitController owner)
        {
            u = owner;
        }

        public float Duration { get; private set; }

        public void CacheHashes()
        {
            hash = u.ResolveStateHash(u.leapAttackStateName);
            Duration = hash != 0 ? u.GetAnimationClipDuration(u.leapAttackStateName, 0.8f) : 0f;
        }

        public bool CanLeap()
        {
            UnitStats stats = u.stats;
            if (hash == 0) return false;
            if (stats.leapAttackRange <= 0f) return false;
            if (Time.time < lastLeapTime + stats.leapAttackCooldown) return false;
            if (!u.IsTargetValid()) return false;
            // 이미 칼이 닿는 거리면 그냥 휘두르면 된다. 붙어 있는데 뛰어오르면 제자리에서 뛴다.
            if (u.IsTargetInAttackRange()) return false;

            Vector3 from = u.transform.position;
            Vector3 to = u.CurrentTarget.Position;
            float gap = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(to.x, 0f, to.z));
            return gap <= stats.leapAttackRange;
        }

        public void Trigger()
        {
            lastLeapTime = Time.time;

            // 도약도 스윙이다. 평타와 똑같이 스윙 시계를 건다(SwingPart.BeginLeap 주석 참조).
            u.Swing.BeginLeap(Duration);

            travelled = 0f;
            direction = Vector3.zero;
            distance = 0f;

            if (u.IsTargetValid())
            {
                Vector3 toTarget = u.CurrentTarget.Position - u.transform.position;
                toTarget.y = 0f;
                float gap = toTarget.magnitude;
                if (gap > 0.0001f)
                {
                    direction = toTarget / gap;
                    // 착지 지점도 교전 간격이다(EngageDistance). 그보다 안쪽에 내려앉으면
                    // 착지하자마자 회피가 도로 밀어내서, 뛰어든 보람 없이 뒷걸음질부터 하게 된다.
                    distance = Mathf.Max(0f, gap - u.EngageDistance);
                }
            }

            u.PlayAnimation(hash, true);
        }

        // LeapAttackBehavior가 매 프레임 부른다. 받는 값은 클립의 진행도(0~1).
        public void Update(float normalizedTime)
        {
            NavMeshAgent agent = u.agent;
            float airborne = Mathf.Clamp01(Mathf.InverseLerp(u.leapLaunchRatio, u.leapLandRatio, normalizedTime));

            // 수평 이동. 남은 거리를 진행도에 맞춰 따라가게 두면, 히트스톱으로 클립이 눌리는
            // 동안 몸도 같이 멈춰서 모션과 위치가 어긋나지 않는다.
            if (distance > 0f && agent != null && agent.enabled && agent.isOnNavMesh)
            {
                float target = distance * airborne;
                float step = target - travelled;
                if (step > 0f)
                {
                    travelled = target;
                    agent.Move(direction * step);
                }
            }

            // 포물선. 이 한 줄이 "달려든다"와 "뛰어서 덤벼든다"를 가른다.
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

            if (!holdingPosition)
            {
                agent.updatePosition = false;
                holdingPosition = true;
            }

            u.transform.position = agent.nextPosition +
                                   Vector3.up * (Mathf.Sin(airborne * Mathf.PI) * u.leapAttackHeight);
        }

        // 도약이 끝났거나 도중에 끊겼다. 위치의 주도권을 반드시 에이전트에게 돌려줘야 한다 —
        // 공중에서 피격당해 피격 리액션으로 빠지면 그대로 떠 있는 채로 싸우게 된다.
        public void End()
        {
            distance = 0f;
            travelled = 0f;

            if (!holdingPosition) return;
            holdingPosition = false;

            NavMeshAgent agent = u.agent;
            if (agent == null) return;
            // 먼저 땅에 내려놓고 나서 주도권을 넘긴다. 순서가 반대면 뜬 좌표가 한 프레임 남는다.
            if (agent.enabled && agent.isOnNavMesh) u.transform.position = agent.nextPosition;
            agent.updatePosition = true;
        }

        // 재사용되는 유닛이 도약 도중에 회수됐다면 모델이 떠 있는 채로 남는다.
        public void Reset()
        {
            lastLeapTime = -999f;
            End();
        }
    }
}
