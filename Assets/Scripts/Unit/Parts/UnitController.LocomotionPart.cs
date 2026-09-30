using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

public partial class UnitController
{
    // 몸을 실제로 옮기는 부품: NavMeshAgent에 목적지와 속도를 주고, 세우고, 밀고(넉백·밀림),
    // 회피 도약 동안 이동을 코드가 가져온다. 배회 목적지도 여기서 고른다.
    //
    // 어떤 다리로 걷는지(보행 모션)는 GaitPart가, 어디로 갈지(판단)는 행동 트리가 정한다.
    private sealed class LocomotionPart
    {
        private readonly UnitController u;

        private float nextDestinationUpdateTime;
        private Vector3 lastDestination;
        private float lastStoppingDistance;
        private bool hasDestination;

        private Vector3 roamDirection;
        private float nextRoamTime;

        // 감정·둔화를 곱하기 전의 속도. 둘 중 하나가 바뀌면 MoveTo를 기다리지 않고 바로 다시 계산한다.
        private float requestedSpeed;

        private Vector3 knockbackDirection;
        private bool hasPendingKnockback;

        public LocomotionPart(UnitController owner)
        {
            u = owner;
        }

        // 도약 중인가. 켜져 있는 동안은 다른 이동 수단이 이 유닛을 건드리지 않는다.
        public bool IsLeapingDodge { get; private set; }

        // 지금 실제로 땅 위를 나아가는 속도.
        // 재생 배속은 "요청한 속도"가 아니라 이 값에 맞춰야 한다 — NavMeshAgent는 가속 중이거나
        // 코너를 돌 때, 다른 유닛을 피할 때 요청보다 느리게 간다. 그 구간마다 다리가 헛돈다.
        public float CurrentSpeed
        {
            get
            {
                NavMeshAgent agent = u.agent;
                if (agent == null || !agent.enabled) return 0f;
                Vector3 v = agent.velocity;
                v.y = 0f;
                return v.magnitude;
            }
        }

        private bool AgentReady
        {
            get
            {
                NavMeshAgent agent = u.agent;
                return agent != null && agent.enabled && agent.isOnNavMesh;
            }
        }

        // 배회 시각도 유닛마다 흩어 놓는다. 기본값 0이면 스폰 직후 전원이
        // 같은 프레임에 NavMesh.SamplePosition을 호출한다.
        public void ScatterRoamClock() => nextRoamTime = Time.time + Random.Range(0f, u.roamInterval);

        // ------------------------------------------------------------ 목적지

        public void MoveTo(Vector3 destination, float speed, float stoppingDistance)
        {
            if (!AgentReady) return;
            NavMeshAgent agent = u.agent;

            Vector3 destinationDelta = destination - lastDestination;
            float stoppingDistanceDelta = Mathf.Abs(stoppingDistance - lastStoppingDistance);
            if (hasDestination &&
                Time.time < nextDestinationUpdateTime &&
                destinationDelta.sqrMagnitude < 0.04f &&
                stoppingDistanceDelta < 0.01f)
            {
                return;
            }

            hasDestination = true;
            lastDestination = destination;
            lastStoppingDistance = stoppingDistance;
            nextDestinationUpdateTime = Time.time + u.destinationUpdateInterval;

            agent.isStopped = false;
            ApplySpeed(speed);
            agent.stoppingDistance = stoppingDistance;
            agent.SetDestination(destination);
        }

        public bool HasReached(Vector3 destination)
        {
            float stopDistance = u.stats.moveStopDistance;
            Vector3 toDestination = destination - u.transform.position;
            toDestination.y = 0f;
            return toDestination.sqrMagnitude <= stopDistance * stopDistance;
        }

        // 남은 속도까지 지운다.
        //
        // 감속하며 서는 편이 자연스럽다고 보고 한동안 velocity를 남겨 뒀는데, 그 판단이 틀렸다.
        // 이 함수를 부르는 쪽(공격·방어·영창 진입)은 곧바로 애니메이션을 대기 자세로 바꾼다.
        // 그림은 이미 멈췄는데 몸만 계속 미끄러지는 셈이라, 실측에서 달아나기를 마친 마법사가
        // 대기 자세로 3.69m/s를 미끄러졌다. 그림과 몸은 같이 멈춰야 한다.
        public void Stop()
        {
            Halt(true);
            hasDestination = false;
        }

        // 하던 동작이 끊겼다(InterruptCurrentAction). 목적지를 버리고 에이전트를 세운다 — 속도는 남긴다.
        public void Interrupt()
        {
            hasDestination = false;

            NavMeshAgent agent = u.agent;
            if (AgentReady)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
        }

        // 에이전트를 실제로 멈춘다.
        //
        // 순서가 중요하다. 예전에는 isStopped를 세운 뒤 ResetPath를 불렀는데, ResetPath가
        // isStopped를 도로 false로 되돌린다 — 실측으로 StopMovement 직후 isStopped가 False였다.
        // 즉 "멈췄다"고 부른 뒤에도 에이전트는 멈춰 있지 않았다.
        //
        // clearVelocity: 남은 속도까지 지울지. 도약처럼 이동을 코드가 통째로 가져가는 경우에도 지운다.
        private void Halt(bool clearVelocity)
        {
            if (!AgentReady) return;
            NavMeshAgent agent = u.agent;

            agent.ResetPath();
            agent.isStopped = true;
            if (clearVelocity) agent.velocity = Vector3.zero;
        }

        public bool TrySetRoamDestination()
        {
            if (!u.roamWhenSearching || Time.time < nextRoamTime) return false;
            if (!AgentReady) return false;

            nextRoamTime = Time.time + u.roamInterval;

            if (roamDirection.sqrMagnitude <= 0.0001f)
            {
                Vector2 initialDirection = Random.insideUnitCircle.normalized;
                roamDirection = new Vector3(initialDirection.x, 0f, initialDirection.y);
            }

            Transform transform = u.transform;
            Vector2 randomCircle = Random.insideUnitCircle.normalized;
            Vector3 randomDirection = new Vector3(randomCircle.x, 0f, randomCircle.y);
            Vector3 weightedDirection = Vector3.Slerp(randomDirection, roamDirection.normalized, u.roamDirectionWeight);
            if (weightedDirection.sqrMagnitude <= 0.0001f)
            {
                weightedDirection = transform.forward;
                weightedDirection.y = 0f;
            }

            float radius = u.roamRadius;
            if (!NavMesh.SamplePosition(transform.position + weightedDirection.normalized * radius,
                    out NavMeshHit hit, radius, NavMesh.AllAreas))
            {
                return false;
            }

            roamDirection = hit.position - transform.position;
            roamDirection.y = 0f;
            u.SetMoveDestination(hit.position, true);
            return true;
        }

        // ------------------------------------------------------------ 속도

        public void ApplySpeed(float speed)
        {
            requestedSpeed = speed;
            if (u.agent != null) u.agent.speed = speed * u.MoveMultiplier;
        }

        // 감정과 둔화가 걸린 값을 다시 계산해 에이전트에 밀어 넣는다.
        // 둘 중 하나가 바뀌는 순간에만 부르면 되므로 매 프레임 계산하지 않는다.
        //
        // 회전이 따라잡을 때까지 속도를 눌러 보는 안을 실측했다가 걷어냈다. 도움이 되지 않는다 —
        // 에이전트의 회전 속도는 이동 속도와 무관해서(angularSpeed), 느리게 가면 회전이 빨리
        // 끝나는 것이 아니라 어긋난 채로 더 오래 갈 뿐이다.
        // (실측: 미끄러짐 지수가 0.218에서 0.259로 오히려 나빠졌다.)
        public void RefreshSpeed() => ApplySpeed(requestedSpeed);

        // ------------------------------------------------------------ 회피 도약

        // 회피 도약을 시작하기 전에 부른다.
        //
        // 도약은 agent.Move로 직접 미는 동작이라(MoveDodge) 에이전트가 스스로 움직이면
        // 그 둘이 겹친다. 물러나기 직전의 유닛은 대개 적 쪽으로 다가가던 참이라 남은 속도가
        // 앞을 향하고 있고, 그게 뒤로 미는 도약과 상쇄되어 "뒷점프를 하는데 몸은 앞으로 가는"
        // 그림이 된다. 도약이 시작되는 순간 에이전트의 속도를 통째로 지워 그 겹침을 없앤다.
        public void BeginDodge()
        {
            Halt(true);
            hasDestination = false;
            IsLeapingDodge = true;
        }

        // 도약이 끝났다. 이동 권한을 에이전트에게 돌려준다.
        public void EndDodge() => IsLeapingDodge = false;

        // 회피 도약을 실제 이동으로 옮긴다.
        //
        // NavMeshAgent에 목적지를 주지 않고 직접 미는 이유: 에이전트는 가속(8m/s^2)을 거치므로
        // 4m/s에 닿는 데만 0.5초가 걸리는데 도약 자체가 0.67초다. 목적지를 주면 클립은 뛰는데
        // 몸은 기어간다. 발놀림(FootworkPart)이 같은 이유로 agent.Move를 쓴다.
        // 뛰는 동안은 발이 땅에 닿아 있지 않으므로 등속으로 밀어도 미끄러져 보이지 않는다.
        public void MoveDodge(Vector3 direction, float speed, float deltaTime)
        {
            if (!AgentReady) return;
            NavMeshAgent agent = u.agent;

            // 도약하는 동안은 이 호출이 이 유닛을 움직이는 유일한 수단이어야 한다.
            // 에이전트가 스스로 남긴 속도로 흘러가면 도약과 겹쳐 엉뚱한 쪽으로 간다.
            agent.velocity = Vector3.zero;
            agent.Move(direction * (speed * deltaTime));
        }

        // ------------------------------------------------------------ 밀림

        public void SetKnockbackFrom(Vector3 attackerPosition)
        {
            Transform transform = u.transform;
            knockbackDirection = transform.position - attackerPosition;
            knockbackDirection.y = 0f;
            if (knockbackDirection.sqrMagnitude <= 0.0001f)
            {
                knockbackDirection = -transform.forward;
            }

            hasPendingKnockback = true;
        }

        public void ClearKnockback()
        {
            knockbackDirection = Vector3.zero;
            hasPendingKnockback = false;
        }

        // 피격 리액션 동안 시간에 나눠 민다. progress는 이번 프레임에 밀 몫(0~1).
        public void ApplyKnockback(float progress)
        {
            if (!hasPendingKnockback) return;
            if (knockbackDirection.sqrMagnitude <= 0.0001f) return;
            if (!AgentReady) return;

            float distance = u.stats.knockbackDistance * progress;
            u.agent.Move(knockbackDirection.normalized * distance);
        }

        // 강인도가 깨지지 않은 일반 피격의 즉각적인 밀림. 피격 리액션의 시간 분산 넉백과 달리
        // 하던 동작을 바꾸지 않고 그 자리에서 한 번에 살짝 밀어서 타격감만 준다.
        //
        // agent.Move로 민다. 에이전트가 NavMesh 경계를 넘지 않게 잘라 주므로 벽이나 낭떠러지 밖으로
        // 밀려 나가지 않는다 — transform을 직접 옮기면 다음 프레임에 에이전트가 도로 끌어오거나 떨어진다.
        public void ApplyMicroPushback(Vector3 attackerPosition, float weight)
        {
            float pushback = u.stats.poiseHitPushback;
            if (pushback <= 0f) return;
            if (!AgentReady) return;

            Vector3 direction = u.transform.position - attackerPosition;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f) return;

            u.agent.Move(direction.normalized * (pushback * Mathf.Max(0f, weight)));
        }

        // 사망 즉시 처리 — 이동 관련만 끈다. 사망 애니메이션은 아직 재생 중이어야 하므로
        // Animator와 UnitController는 여기서 끄지 않는다(FinalizeDeath에서 처리).
        public void DisableAfterDeath()
        {
            NavMeshAgent agent = u.agent;
            if (agent == null) return;

            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }

            agent.enabled = false;
        }
    }
}
