using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

public partial class UnitController
{
    // 지역 회피(RVO)와 회피 우선순위를 상황에 맞게 켜고 끄는 부품.
    //
    // 이게 "계속 밀려나는" 문제의 진짜 원인이었다. NavMeshAgent의 지역 회피는 반지름이 겹치는
    // 에이전트를 매 프레임 서로 밀어내 떼어 놓는데, 근접전은 반지름 합(0.5+0.5=1.0m) 바로
    // 언저리에서 벌어지므로 이 밀어냄이 교전 내내 상시로 걸린다. 밀려난 만큼 transform은
    // 움직이지만 애니메이션은 그대로라, 발이 땅을 딛지 않은 채 미끄러지는 그림이 나온다.
    // 목표 거리를 회피 하한 위로 올려도 이건 사라지지 않는다 — 서로 조금만 다가서면
    // 다시 겹침 판정에 걸리기 때문이다.
    //
    // 교전 중에는 간격을 발놀림(FootworkPart)이 직접 잡으므로 회피에게 맡길 이유가 없다.
    // 회피를 끈 유닛도 "다른 에이전트가 피해야 할 장애물"로는 그대로 남으므로, 달려오는 쪽이
    // 알아서 돌아간다 — 자리를 지키는 쪽만 밀리지 않게 된다.
    private sealed class AvoidancePart
    {
        private readonly UnitController u;

        private int priorityOffset;
        private bool priorityResolved;

        public AvoidancePart(UnitController owner)
        {
            u = owner;
        }

        public void Tick()
        {
            NavMeshAgent agent = u.agent;
            if (agent == null || !agent.enabled) return;

            // 도약 중에는 회피도 끈다. 도약은 이동을 코드가 통째로 가져가는 동작인데
            // 지역 회피가 옆에서 밀면 그만큼 궤적이 휘어 "뒤로 뛰는데 옆으로 흐르는" 그림이 된다.
            bool holdingGround = IsHoldingGround() || u.IsLeapingDodge;

            ObstacleAvoidanceType desiredType = holdingGround
                ? ObstacleAvoidanceType.NoObstacleAvoidance
                : u.movingAvoidanceQuality;
            if (agent.obstacleAvoidanceType != desiredType) agent.obstacleAvoidanceType = desiredType;

            // 이동 중인 유닛끼리도 우선순위는 남는다(숫자가 작을수록 덜 밀린다).
            // 여기에 유닛마다 다른 오프셋을 얹어 같은 값이 겹치지 않게 한다(avoidancePrioritySpread 주석 참조).
            int desiredPriority = Mathf.Clamp(
                (holdingGround ? u.engagedAvoidancePriority : u.movingAvoidancePriority) + PriorityOffset,
                0, 99);
            if (agent.avoidancePriority != desiredPriority) agent.avoidancePriority = desiredPriority;
        }

        // 제자리에서 무언가를 하는 중인가. 이 상태들은 위치를 스스로 정하므로 NavMesh 쪽에 자리를 맡기지 않는다.
        //
        // 답은 동작이 들고 있다(UnitBehavior.HoldsGround). 예전에는 여기서 구체 상태를
        // 일곱 개 늘어놓고 비교했는데, 상태를 추가할 때 이 목록을 뒤져야 한다는 것을 아무것도 알려주지 않았다.
        private bool IsHoldingGround()
        {
            UnitBehavior current = u.RunningBehavior;
            return current != null && current.HoldsGround;
        }

        // 이 유닛만의 회피 우선순위 오프셋.
        //
        // 한 번만 뽑고 그대로 들고 간다는 점이 중요하다. 매 프레임 다시 굴리면 누가 양보할지가
        // 계속 뒤바뀌어 교착이 그대로 남는다 — 배회 시각이나 옆걸음 방향을 유닛마다 한 번씩
        // 흩어 놓는 것과 같은 이유다.
        private int PriorityOffset
        {
            get
            {
                if (u.avoidancePrioritySpread <= 0) return 0;
                if (!priorityResolved)
                {
                    priorityOffset = Random.Range(0, u.avoidancePrioritySpread + 1);
                    priorityResolved = true;
                }
                return priorityOffset;
            }
        }
    }
}
