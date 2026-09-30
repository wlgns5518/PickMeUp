using UnityEngine;
using Random = UnityEngine.Random;

public partial class UnitController
{
    // 상대 둘레의 어디에 서서 싸울지(교전 방위)와, 지금 상대와 얼마나 오래 맞붙어 있었는지(교전 시간).
    private sealed class EngagementPart
    {
        private readonly UnitController u;

        // 이 유닛이 적의 좌우 중 어느 쪽으로 도는가. 유닛마다 스폰 시 한 번 정해진다 —
        // 전원이 같은 쪽으로 돌면 난전이 통째로 한 방향으로 흘러간다.
        private float flankSign = 1f;

        // 파고들 자리의 방위를 이번 접근이 끝날 때까지 붙들어 둔다.
        //
        // 이게 없으면 자리가 상대의 지금 정면을 따라 계속 돈다. 상대도 제 표적을 향해 도는 중이라,
        // 등 뒤(180도)를 노리는 암살자는 그 자리를 영영 따라잡지 못하고 상대 둘레를 빙글빙글 돈다.
        //
        // 시간으로 끊어 봤더니(1.2초) 도는 것이 주기적인 급회전으로 바뀌기만 했다. 실측 로그에서
        // 그 순간마다 감속 → 회전 → 재가속이 일어났고, 다 돌기 전에 가속이 시작돼 0.2초가량
        // 몸과 진행방향이 거의 직각인 채로 달리기 클립이 돌았다(dot 0.98 → 0.04).
        // 그게 화면에서 "몸과 진행방향이 반대"로 보이던 것의 정체다.
        //
        // 그래서 시간이 아니라 접근 단위로 붙든다. 한 번 고른 방위는 그 접근이 끝날 때까지 그대로고,
        // 자리는 상대를 따라 평행이동만 한다 — 표적이 움직여도 급회전이 생기지 않는다.
        // 다시 고르는 시점은 접근을 새로 시작할 때(ChaseBehavior.OnEnter)와 표적이 바뀔 때뿐이다.
        private Vector3 heldBearing;
        private TargetRef heldBearingTarget;

        // 지금 타깃과 공격 거리 안에서 맞붙어 있은 시간(초). 사거리 밖으로 떨어지거나 상대가
        // 바뀌면 0으로 돌아간다. stats.skillEngageDelay가 이 값을 본다.
        private float dwell;
        private TargetRef dwellTarget;

        public EngagementPart(UnitController owner)
        {
            u = owner;
        }

        public float Dwell => dwell;

        // 적의 어느 쪽으로 파고들지 유닛마다 갈라 놓는다. 검사 둘이 같은 측면을 물면 반대쪽이 통째로 비고,
        // 암살자 둘이 같은 방향으로 돌면 서로를 밀어낸다. 교전 내내 뒤집지 않는다 —
        // 파고드는 방향이 도중에 바뀌면 영원히 자리를 못 잡는다.
        public void RollFlankSide()
        {
            flankSign = Random.value < 0.5f ? -1f : 1f;
        }

        // 파고들 방위를 가진 직군인가. 접근 방식이 갈리는 분기점이라 부르는 쪽이 먼저 묻는다.
        //
        // 상대가 나를 노리고 있으면 파고들 사각지대라는 것이 없다. 방위는 상대의 정면을 기준으로
        // 재는데 그 정면이 나를 향해 따라오므로, 계속 밀어붙이면 둘이 영원히 맞물려 도는 그림이 된다.
        //
        // 이걸 끊는 것이 전술적으로도 맞다. 암살자는 "전면전이 벌어지는 동안 시야에서 벗어나
        // 사각지대로" 들어가는 직군이지, 자기를 노려보는 적의 등을 억지로 잡는 직군이 아니다.
        // 검사도 같다 — 적이 나를 보면 정면에서 받아치고(패링), 적이 탱커에게 시선을 돌리는
        // 순간 측면으로 미끄러진다. 그 공수 전환이 이 한 줄에서 나온다.
        public bool HasPreference =>
            u.stats.engageAngle > 0.01f &&
            (!u.CurrentTarget.Exists || !u.CurrentTarget.IsTargeting(u));

        // 다음 접근에서 방위를 새로 고르게 한다. 접근을 시작하는 쪽이 부른다.
        public void ClearBearing()
        {
            heldBearing = Vector3.zero;
            heldBearingTarget = TargetRef.None;
        }

        // 접근 중에 실제로 향할 지점. 타깃 위치가 아니라 "타깃 주위에서 내가 서고 싶은 자리"다.
        //
        // 이게 진형을 만든다. 탱커는 정면(0도)으로 곧장 들어가 어그로를 붙들고, 검사는 측면(55도)을
        // 물고, 암살자는 등 뒤(180도)로 돌아간다. 아군 탱커의 위치를 참조하지 않는데도 "탱커 옆에
        // 검사가 선다"가 되는 이유는, 적의 정면을 이미 어그로가 붙은 탱커가 차지하고 있기 때문이다.
        // 탱커가 쓰러져도 기준이 사라지지 않는다는 점에서 위치 참조보다 튼튼하다.
        //
        // "멀 때는 곧장 붙고 가까워지면 그때 파고든다"도 실측했다가 걷어냈다. 45초씩 번갈아 두 바퀴
        // 재 보니 차이가 노이즈 안이었다(미끄러짐 0.299 대 0.281, 같은 설정끼리도 0.321과 0.269).
        public Vector3 GetDestination(float standoffDistance)
        {
            Vector3 predicted = u.GetPredictedTargetPosition();
            TargetRef target = u.CurrentTarget;
            if (!target.Exists || !HasPreference) return predicted;

            bool held = heldBearingTarget == target && heldBearing.sqrMagnitude > 0.0001f;
            if (!held)
            {
                Vector3 theirForward = target.Forward;
                theirForward.y = 0f;
                if (theirForward.sqrMagnitude <= 0.0001f) return predicted;

                Quaternion rotation = Quaternion.AngleAxis(u.stats.engageAngle * flankSign, Vector3.up);
                heldBearing = rotation * theirForward.normalized;
                heldBearingTarget = target;
            }

            return predicted + heldBearing * standoffDistance;
        }

        // 지금 타깃과 얼마나 오래 맞붙어 있었는가. 붙잡는 스킬(고블린의 물어뜯기)이 이걸 본다.
        //
        // 전투 시작으로부터 재면 안 된다. 그러면 멀리서 달려오는 동안에도 시간이 흘러서,
        // 정작 도착한 그 순간에 곧바로 물어뜯는다 — 막으려던 그림이 그대로 나온다.
        // 사거리 안에 실제로 붙어 있은 시간만 센다.
        public void TickDwell()
        {
            // 상대가 바뀌면 처음부터 다시 센다. 앞사람과 겨룬 시간이 새 상대에게 넘어가면
            // 옆으로 타깃을 옮기는 것만으로 조건이 채워진다.
            if (!u.IsTargetValid() || u.CurrentTarget != dwellTarget)
            {
                dwellTarget = u.IsTargetValid() ? u.CurrentTarget : TargetRef.None;
                dwell = 0f;
                return;
            }

            if (!u.IsTargetInAttackRange())
            {
                dwell = 0f;
                return;
            }

            dwell += Time.deltaTime;
        }

        public void Reset()
        {
            ClearBearing();
            dwell = 0f;
            dwellTarget = TargetRef.None;
        }
    }
}
