using UnityEngine;
using UnityEngine.AI;

public partial class UnitController
{
    // 다음 스윙을 기다리는 동안의 움직임(발놀림). 간격만 맞춘다.
    //
    // NavMeshAgent의 경로 탐색을 쓰지 않고 agent.Move로 직접 미는 이유는, 이 정도의
    // 짧은 조정에 매 프레임 SetDestination을 부르면 경로 계산만 잔뜩 쌓이기 때문이다.
    private sealed class FootworkPart
    {
        private readonly UnitController u;

        private Vector3 velocity;

        // 이번 틈에 발놀림을 할지. 틈이 시작될 때(TriggerAttack) 한 번만 정한다 —
        // 매 프레임 "남은 시간"으로 판단하면 스윙 직전 minFootworkWindow 동안은 항상 멈춰 서게 되어,
        // 공격이 끝날 때마다 잠깐씩 굳는 것처럼 보인다.
        private bool thisGap;

        public FootworkPart(UnitController owner)
        {
            u = owner;
        }

        // 스윙과 스윙 사이의 틈 길이로 이번 틈에 자리를 옮길지 정한다.
        public void PlanGap(float recovery) => thisGap = recovery >= u.minFootworkWindow;

        public void Update()
        {
            if (!u.IsTargetValid()) return;
            NavMeshAgent agent = u.agent;
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

            // 이번 틈에 자리를 옮기기로 했는가. 짧은 틈(콤보 스텝 사이)에 옆으로 한 발 떼고
            // 곧바로 다시 휘두르면 발놀림이 아니라 잔떨림으로 보이므로, 긴 틈에서만 움직인다.
            //
            // 판단은 틈이 시작될 때 이미 끝나 있다(PlanGap). 예전에는 여기서 매 프레임
            // "남은 시간 < minFootworkWindow"를 다시 봤는데, 그러면 어떤 틈이든 스윙 직전
            // 0.35초는 반드시 제자리에 멈춰 서게 된다 — 공격 후 멈칫하던 것의 정체가 이거였다.
            //
            // 마법사도 예외를 두지 않는다.
            //
            // 한때 "마법사는 스윙이 없어 thisGap이 영영 거짓이니 늘 발놀림하게 하자"고
            // 예외를 뒀는데, 그 결과 영창 사이 내내 제자리에서 잔걸음을 치며 발이 미끄러졌다.
            // 마력을 모으는 유닛은 가만히 서 있는 편이 맞고, 간격이 무너지면 그때 달아난다(FleeBehavior).
            if (!thisGap)
            {
                Stop();
                return;
            }

            UnitStats stats = u.stats;
            float speed = stats.walkSpeed * stats.footworkSpeedRatio * u.MoveMultiplier;
            if (speed <= 0.01f)
            {
                Stop();
                return;
            }

            Vector3 toTarget = u.CurrentTarget.Position - u.transform.position;
            toTarget.y = 0f;

            float distance = toTarget.magnitude;
            if (distance <= 0.0001f) return;
            Vector3 forward = toTarget / distance;

            Vector3 direction = Vector3.zero;

            // 간격 조절. 너무 붙으면 물러서고 멀면 파고든다.
            // 목표 간격의 하한은 회피가 허용하는 최소 거리다(SeparationFrom 주석 참조).
            float gap = distance - u.EngageDistance;

            // 다만 근처에 위협이 남아 있으면 파고들지 않는다.
            //
            // 이 계산은 겨누는 상대와의 거리만 본다. 그런데 물러나게 만든 적은 대개 다른 놈이다 —
            // 마법사가 발밑의 고블린을 피해 물러난 직후, 8m 밖 집중 표적까지 6.4m를 맞추겠다고
            // 방금 도망친 그 고블린 쪽으로 도로 걸어 들어갔다("물러났다가 바로 돌아와 맞는다").
            //
            // 물러나는 성분(gap < 0)은 그대로 두고 파고드는 성분만 막는다. 임계에 여유(1.3배)를
            // 두는 것은 경계에서 붙었다 물러났다를 반복하지 않게 하기 위해서다.
            if (gap > 0f && u.IsThreatWithinSpacing()) gap = 0f;

            // 비례 제어. 예전에는 Mathf.Sign으로 늘 최고 속도로 밀어서, 이상 간격을 지나칠 때마다
            // 방향이 뒤집혀 그 주위를 진동했다. 가까울수록 약하게 밀어야 그 자리에서 멎는다.
            if (Mathf.Abs(gap) > 0.12f) direction += forward * Mathf.Clamp(gap / 0.7f, -1f, 1f);

            // 옆으로 도는 성분은 없앴다.
            //
            // 한때 여기서 상대 주위를 돌게 했다(직군별 교전 방위 + 좌우 흔들기). 재는 그림을
            // 만들려던 것인데, 실제 화면에서는 "공격하면서 빙글빙글 회전이동하는" 것으로 보였다.
            // 게다가 옆걸음 클립과 실제 이동 속도가 어긋나는 구간마다 발이 눈에 띄게 미끄러졌다.
            //
            // 자리를 잡는 일은 접근(ChaseBehavior가 부르는 GetEngageDestination)이 맡는다. 붙고 나서까지
            // 계속 돌 이유는 없다 — 교전 중 발놀림은 간격만 맞추면 충분하다.
            // 방위 성향(engageAngle) 자체는 접근 쪽에 그대로 살아 있다.

            // 목표 속도로 곧바로 튀지 않고 붙였다 뺀다. 정지 → 최고 속도가 한 프레임에 일어나면
            // 발이 땅을 딛기 전에 몸이 먼저 나간다.
            Vector3 desired = direction.sqrMagnitude > 0.0001f ? direction.normalized * speed : Vector3.zero;
            velocity = Vector3.MoveTowards(velocity, desired,
                Mathf.Max(0.01f, u.footworkAcceleration) * speed * Time.deltaTime);

            float currentSpeed = velocity.magnitude;
            if (currentSpeed <= 0.05f)
            {
                u.SetMoveAnimation(0f, false, false);
                return;
            }

            agent.Move(velocity * Time.deltaTime);
            // 이동 방향을 유닛 기준으로 풀어 스트레이프 클립을 고른다. 전용 클립이 없는 리그는
            // 걷기로 대신한다(발이 조금 미끄러지지만 서서 순간이동하는 것보다는 낫다).
            u.PlayMoveAnimationForDirection(velocity / currentSpeed, forward, currentSpeed, false);
        }

        // 발놀림을 멈춘다. 속도를 0으로 되돌려 두지 않으면 다음 번에 이전 방향으로 한 번 튄다.
        private void Stop()
        {
            velocity = Vector3.zero;
            u.PlayCombatIdle();
        }

        public void Reset()
        {
            thisGap = false;
            velocity = Vector3.zero;
        }
    }
}
