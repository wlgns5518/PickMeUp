using UnityEngine;
using UnityEngine.AI;

public partial class UnitController
{
    // 몸을 어느 쪽으로 돌릴지를 맡는 부품: 표적을 향해 초당 몇 도로 돌지, 즉시 맞출지,
    // 가려는 쪽으로 먼저 돌아설지, 그리고 회전을 코드와 에이전트 중 누가 맡을지.
    private sealed class FacingPart
    {
        private readonly UnitController u;

        public FacingPart(UnitController owner)
        {
            u = owner;
        }

        // 몸을 돌리는 주체를 코드로 넘길지, NavMeshAgent에게 맡길지 정한다.
        //
        // 둘이 동시에 돌리면 유닛이 떤다. NavMeshAgent(updateRotation)는 "가고 있는 쪽"으로
        // 돌리고 FaceTarget은 "노리는 쪽"으로 돌리는데, 이 둘이 어긋나는 상황이 전투의 대부분이다:
        //  - 쫓아갈 때는 예측 위치로 달리면서 상대를 봐야 하고,
        //  - 옆으로 돌 때는 진행 방향이 아예 90도 옆이라 정반대로 당긴다.
        // 그래서 상대를 보는 상태(Chase/Attack/Block)에서는 코드가 회전을 통째로 가져오고,
        // 그 밖(이동·배회·회피)에서는 예전처럼 진행 방향을 보도록 에이전트에게 돌려준다.
        public void SetCodeDriven(bool codeDriven)
        {
            if (u.agent == null) return;
            u.agent.updateRotation = !codeDriven;
        }

        // 겨누는 상대가 엔티티일 수도 있으므로 손잡이로 받는다.
        public void Toward(TargetRef target, float turnSpeed)
        {
            if (!target.Exists) return;

            Toward(target.Position, turnSpeed);
        }

        public void Toward(Vector3 facingPosition, float turnSpeed)
        {
            Transform transform = u.transform;
            Vector3 direction = facingPosition - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f) return;

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);

            // RotateTowards는 초당 turnSpeed도씩 돌고 목표에 정확히 도달해 멈춘다.
            //
            // 예전에는 Slerp에 (turnSpeed * deltaTime / 180)을 t로 넣었다. 그건 각속도 제한이
            // 아니라 지수 감쇠라서 두 가지가 어긋났다:
            //  - 끝까지 수렴하지 않는다. 720으로 두면 프레임당 남은 각도의 6.7%만 좁히므로,
            //    90도를 도는 데 0.5초를 써도 여전히 11도가 남는다. 도착하자마자 휘두르는
            //    첫 공격이 늘 비스듬히 나가고, 스윙 도중에도 몸이 계속 돌아가던 원인이다.
            //  - 프레임레이트에 따라 실제 회전 속도가 달라진다(t가 dt에 선형인데 감쇠는 지수라서).
            // 이제 rotationSpeed / blockTurnSpeed가 이름 그대로 초당 각도를 뜻한다.
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                turnSpeed * Time.deltaTime);
        }

        // 주어진 쪽을 즉시 바라본다. 서서히 도는 것이 아니라 그 프레임에 맞춘다.
        //
        // 이동 모션과 실제 이동 방향이 어긋나면 그대로 발이 미끄러져 보인다. 도약이나 도주처럼
        // 시작하는 순간부터 방향이 정해져 있는 동작은, 돌아서는 동안의 짧은 어긋남조차 눈에 띈다.
        public void Snap(Vector3 forwardDirection)
        {
            Vector3 facing = forwardDirection;
            facing.y = 0f;
            if (facing.sqrMagnitude <= 0.0001f) return;

            u.transform.rotation = Quaternion.LookRotation(facing.normalized);
        }

        // 가려는 쪽으로 몸을 돌린다. 다 돌았으면(또는 돌 방향이 없으면) true.
        //
        // 회전 주도권을 에이전트에게 넘기기 전에 쓴다. NavMeshAgent의 updateRotation은 "지금 내는
        // 속도" 쪽으로 돌리는데, 방향을 뒤집는 구간에서는 그 속도가 0을 지나며 거의 돌지 않는다 —
        // angularSpeed를 720으로 올려 놔도 그렇다. 실측에서 암살자가 빠졌다가 돌아설 때 몸이
        // 물러나던 쪽을 본 채로 0.3초 넘게 뒷걸음질쳤다(진행·정면 내적 -0.89에서 8도/66ms로만 회복).
        //
        // 스냅이 아니라 rotationSpeed로 도는 것이 요점이다. 전속력으로 달리는 중에 몸만 홱 돌리면
        // 에이전트에는 이전 방향의 속도가 남아 그림이 통째로 역주행이 된다(ChaseBehavior 주석의 실측).
        public bool TurnTowardsMoveDirection(float toleranceDegrees)
        {
            NavMeshAgent agent = u.agent;
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return true;

            Transform transform = u.transform;

            // 가려는 쪽은 조종이 원하는 속도로 본다. 아직 경로가 안 잡혔으면 남은 목적지로 대신한다 —
            // 출발하는 첫 프레임이 정확히 그 경우다.
            Vector3 direction = agent.desiredVelocity;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f && agent.hasPath)
            {
                direction = agent.steeringTarget - transform.position;
                direction.y = 0f;
            }

            // 돌 방향을 못 찾았으면 붙들고 있을 이유가 없다. 여기서 false를 돌려주면
            // 목적지가 잡히지 않는 동안 회전이 영영 코드에 묶인다.
            if (direction.sqrMagnitude <= 0.0001f) return true;

            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f) return true;

            if (Vector3.Angle(forward, direction) <= toleranceDegrees) return true;

            Toward(transform.position + direction, u.rotationSpeed);
            return false;
        }

        // 상대를 충분히 마주 보고 있는가. 타깃이 없으면 판단할 근거가 없으니 true로 둔다.
        public bool IsFacingTarget(float toleranceDegrees)
        {
            TargetRef target = u.CurrentTarget;
            if (!target.Exists) return true;

            Transform transform = u.transform;
            Vector3 direction = target.Position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f) return true;

            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f) return true;

            return Vector3.Angle(forward, direction) <= toleranceDegrees;
        }
    }
}
