using UnityEngine;
using UnityEngine.AI;

public partial class UnitController
{
    // 어떤 다리로 움직일지(보행 모션)를 고르는 부품: 대기·걷기·질주·옆걸음·뒷걸음 중 하나를,
    // 실제로 땅 위를 나아가는 속도와 방향에 맞춰 고르고 재생 배속까지 맞춘다.
    //
    // 재생 배속을 "내려던 속도"가 아니라 "지금 실제로 나아가는 속도"에 맞춘다.
    // 쫓아가는 유닛이 무리에 막히면 NavMeshAgent는 거의 제자리인데, 요청 속도로 배속을 잡으면
    // 다리만 전속력으로 돌아 땅 위를 미끄러진다 — 몰려간 고블린들이 앞에서 떠는 것처럼 보이던
    // 그림의 절반이 이것이다.
    //
    // 질주 클립 하나로 전 구간을 덮으면 느린 구간에서 다리가 헛돈다. 배속 하한이 0.6이라
    // 질주 클립(4.2m/s)은 아무리 눌러도 2.5m/s만큼 다리를 젓는데, 실제로는 그보다 훨씬
    // 느리게 기어가는 구간이 많다 — 실측으로 크게 어긋난 프레임의 73%가 이 경우였다.
    // 하한을 낮추는 것은 답이 아니다. 그러면 이번엔 질주가 슬로모션으로 돌아간다.
    // 대신 클립을 바꾼다: 빠르면 질주, 느리면 걷기, 사실상 멈췄으면 대기.
    private sealed class GaitPart
    {
        // 사실상 멈춰 있는 구간. 여기서 이동 클립을 계속 돌리면 제자리 뜀박질이 된다.
        //
        // 들어가는 문턱과 나오는 문턱을 다르게 두고 짧은 지연까지 붙인 이유는, 예전에 이 자리에서
        // 겪은 깜빡임 때문이다 — 막혔다 풀렸다 하는 유닛이 달리기와 대기 사이를 프레임마다
        // 오갔다. 그래서 예전에는 속도에 0.05 바닥을 깔아 아예 대기로 못 가게 막아 뒀는데,
        // 그게 곧 제자리 뜀박질의 원인이었다. 막는 대신 문턱을 벌린다.
        private const float IdleEnterSpeed = 0.3f;
        private const float IdleExitSpeed = 0.6f;
        private const float IdleDelay = 0.15f;

        private readonly UnitController u;

        private int strafeLeftHash;
        private int strafeRightHash;
        private int strafeBackHash;
        private int combatIdleHash;

        // 재생 배속을 실제 이동 속도에 맞춘다(MoveAnimationSpeed 주석 참조).
        private MoveAnimationSpeed speedParameter;

        private float belowIdleSince = -1f;
        private bool idleLatched;

        public GaitPart(UnitController owner)
        {
            u = owner;
        }

        public void CacheHashes()
        {
            speedParameter = new MoveAnimationSpeed(u.animator, u.moveSpeedParameterName);
            strafeLeftHash = u.ResolveStateHash(u.strafeLeftStateName);
            strafeRightHash = u.ResolveStateHash(u.strafeRightStateName);
            strafeBackHash = u.ResolveStateHash(u.strafeBackStateName);
            combatIdleHash = u.ResolveStateHash(u.combatIdleStateName);
        }

        public void SetMoveAnimation(float speed, bool isRunning, bool isJumping)
        {
            if (isJumping && !string.IsNullOrEmpty(u.jumpStateName))
            {
                u.PlayAnimation(u.jumpAnimationHash, false);
                return;
            }

            if (speed <= 0.01f)
            {
                u.PlayAnimation(u.idleAnimationHash, false);
                return;
            }

            if (isRunning)
            {
                ApplySpeed(speed, u.runClipSpeed);
                u.PlayAnimation(u.runAnimationHash, false);
                return;
            }

            // 걷기 상태가 없는 리그(고블린)는 달리기 클립으로 대신하므로 기준 속도도 달리기 쪽을 쓴다.
            bool hasWalk = u.hasWalkAnimationState;
            ApplySpeed(speed, hasWalk ? u.walkClipSpeed : u.runClipSpeed);
            u.PlayAnimation(hasWalk ? u.walkAnimationHash : u.runAnimationHash, false);
        }

        // 이동 모션을 재생하는 표준 경로. 이동 중인 상태는 전부 이것만 부르면 된다.
        public void SetFromGroundSpeed(bool isRunning)
        {
            float ground = u.CurrentMoveSpeed;
            if (TryPlayIdle(ground)) return;

            SetMoveAnimation(Mathf.Max(ground, 0.05f), isRunning && ShouldRun(ground), false);
        }

        // 실제로 나아가는 쪽에 맞는 다리를 고른다. 앞이면 달리기, 옆이나 뒤면 그 방향 클립.
        //
        // 몸과 진행방향은 완전히 맞출 수 없다. NavMeshAgent는 회전과 이동을 따로 돌리고
        // (angularSpeed와 speed가 서로 무관하다), 목적지는 상대가 움직이고 지역 회피가 밀 때마다
        // 방향이 바뀐다. 실측으로 추격 중 이동 프레임의 13%가 진행방향과 45도 이상 어긋났고,
        // 몸을 즉시 돌리는 것도(SnapFacing) 속도를 누르는 것도(회전이 그만큼 빨라지지 않는다)
        // 그 수치를 낮추지 못했다.
        //
        // 그래서 몸을 속도에 맞추는 대신 클립을 속도에 맞춘다. 어긋난 그 구간이 옆걸음·뒷걸음으로
        // 재생되면 발이 땅을 딛고, 몸이 조금 어긋나 있는 것은 오히려 자연스러운 그림이 된다.
        // 교전 발놀림이 이미 같은 방식으로 클립을 고른다(FootworkPart.Update).
        public void SetDirectionalFromGroundSpeed()
        {
            float speed = u.CurrentMoveSpeed;

            // 사실상 멈췄으면 방향을 물어봐야 의미가 없다.
            if (TryPlayIdle(speed)) return;

            NavMeshAgent agent = u.agent;
            if (agent == null || !agent.enabled)
            {
                SetFromGroundSpeed(true);
                return;
            }

            Vector3 move = agent.velocity;
            move.y = 0f;
            Vector3 forward = u.transform.forward;
            forward.y = 0f;
            if (move.sqrMagnitude <= 0.0001f || forward.sqrMagnitude <= 0.0001f)
            {
                SetFromGroundSpeed(true);
                return;
            }

            PlayForDirection(move.normalized, forward.normalized, speed, ShouldRun(speed));
        }

        // 나아가는 쪽에 맞는 다리를 고른다. 발놀림과 접근이 같이 쓴다.
        //
        // runWhenForward: 앞으로 가는 구간을 달리기로 칠지. 발놀림은 걷기고(제자리에서 재는
        // 동작이라 달리면 안 된다), 접근은 달리기다.
        public void PlayForDirection(Vector3 move, Vector3 forward, float speed, bool runWhenForward)
        {
            float forwardDot = Vector3.Dot(move, forward);
            float rightDot = Vector3.Dot(move, Vector3.Cross(Vector3.up, forward));

            int hash = 0;
            float clipSpeed = u.strafeClipSpeed;
            if (forwardDot < -0.5f)
            {
                hash = strafeBackHash;
                clipSpeed = u.strafeBackClipSpeed;
            }
            else if (Mathf.Abs(rightDot) > 0.4f) hash = rightDot > 0f ? strafeRightHash : strafeLeftHash;

            if (hash == 0)
            {
                SetMoveAnimation(speed, runWhenForward, false);
                return;
            }

            // 클립마다 원래 나아가는 속도가 다르다. 실제 이동 속도를 그 값으로 나눠 배속을 준다.
            // 이 배속은 StrafeLeft/Right/Back 상태의 Speed Multiplier가 MoveSpeedMultiplier에 묶여
            // 있어야 실제로 먹는다 — 안 묶여 있으면 값만 넘어가고 클립은 제 속도로 재생된다.
            ApplySpeed(speed, clipSpeed);
            u.PlayAnimation(hash, false);
        }

        // 물러날 때의 다리. 뒷걸음 클립이 있으면 그것으로, 없는 리그는 예전처럼 달리기로 물러난다.
        // 회피 모션(Dodge)은 한 번 재생되고 끝나므로, 남은 거리는 이쪽이 이어받는다.
        public void PlayRetreat(float speed)
        {
            if (strafeBackHash == 0)
            {
                SetMoveAnimation(speed, true, false);
                return;
            }

            ApplySpeed(speed, u.strafeBackClipSpeed);
            u.PlayAnimation(strafeBackHash, false);
        }

        // 교전 중 제자리에 설 때의 자세.
        //
        // 평소 Idle을 쓰면 안 된다. 그건 칼을 내리고 긴장을 푼 자세라, 스윙과 스윙 사이의
        // 0.2초짜리 틈마다 "공격 → 긴장 풀림 → 공격"이 반복되어 매번 멈칫하는 것처럼 보인다.
        // 전용 자세가 없는 리그(고블린)는 예전처럼 Idle로 떨어진다.
        public void PlayCombatIdle()
        {
            u.PlayAnimation(combatIdleHash != 0 ? combatIdleHash : u.idleAnimationHash, false);
        }

        // speed는 "지금 실제로 땅 위를 나아가는 속도"다. 요청 속도가 아니다.
        private void ApplySpeed(float groundSpeed, float clipSpeed) =>
            speedParameter?.Apply(groundSpeed, clipSpeed, u.moveSpeedMultiplierRange, u.moveSpeedDampTime);

        // 질주와 걷기가 갈리는 속도. 두 클립의 배속이 똑같이 1에서 멀어지는 지점이라
        // 어느 쪽으로 가도 손해가 같다(기하평균).
        private bool ShouldRun(float groundSpeed)
        {
            float crossover = u.hasWalkAnimationState
                ? Mathf.Sqrt(Mathf.Max(0.01f, u.walkClipSpeed * u.runClipSpeed))
                : 0f;
            return groundSpeed >= crossover;
        }

        private bool TryPlayIdle(float groundSpeed)
        {
            if (groundSpeed >= IdleExitSpeed)
            {
                idleLatched = false;
                belowIdleSince = -1f;
                return false;
            }

            if (!idleLatched)
            {
                if (groundSpeed > IdleEnterSpeed)
                {
                    belowIdleSince = -1f;
                    return false;
                }

                if (belowIdleSince < 0f) belowIdleSince = Time.time;
                if (Time.time - belowIdleSince < IdleDelay) return false;

                idleLatched = true;
            }

            // 교전 중이면 칼을 든 자세로 선다. 순찰 중에 그 자세로 서 있으면 어색하므로
            // 겨누는 상대가 있을 때만이다(PlayCombatIdle 주석과 같은 이유).
            if (u.CurrentTarget.Exists) PlayCombatIdle();
            else u.PlayAnimation(u.idleAnimationHash, false);
            return true;
        }
    }
}
