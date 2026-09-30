using UnityEngine;

// 누가 무엇을 볼 수 있는가 — 시야각·거리(VisionQuery)와 벽에 가리는지(HasLineOfSight).
public static partial class UnitRegistry
{
    // 시야 레이의 히트를 받는 공용 버퍼. 매 판정마다 배열을 새로 만들지 않기 위해 정적으로 둔다.
    // 한 직선 위에 이보다 많은 콜라이더가 겹치는 경우는 난전 한복판뿐이고,
    // 그때는 "안 보인다"로 처리해 벽 너머를 보는 쪽 실수를 피한다(아래 주석 참조).
    private static readonly RaycastHit[] lineOfSightHits = new RaycastHit[32];

    // 시야 판정에 필요한 값(시야각 cos, 제곱 거리, 정규화된 전방 벡터)을 스캔 1회당 한 번만
    // 계산해두는 구조체. 후보마다 Cos/normalize를 다시 돌던 비용을 없앤다.
    private readonly struct VisionQuery
    {
        private readonly Vector3 position;
        private readonly Vector3 forward; // XZ 평면으로 눕힌 정규화 전방
        private readonly float rangeSqr;
        private readonly float closeRangeSqr;
        private readonly float minDot;
        private readonly bool hasForward;
        private readonly bool ignoreAngle;

        public Vector3 Position => position;

        public VisionQuery(UnitController requester, float range, float viewAngle, float closeVisibleRange)
        {
            position = requester.transform.position;
            rangeSqr = range * range;
            closeRangeSqr = closeVisibleRange * closeVisibleRange;
            ignoreAngle = viewAngle >= 359f;

            Vector3 flatForward = requester.transform.forward;
            flatForward.y = 0f;
            hasForward = flatForward.sqrMagnitude > 0.0001f;
            forward = hasForward ? flatForward.normalized : Vector3.zero;

            float halfAngle = Mathf.Clamp(viewAngle * 0.5f, 0f, 180f);
            minDot = Mathf.Cos(halfAngle * Mathf.Deg2Rad);
        }

        // 보이면 true, 그리고 판정에 쓴 제곱 거리를 함께 돌려준다(호출자가 거리 비교에 재사용).
        public bool CanSee(Vector3 targetPosition, out float sqrDistance)
        {
            Vector3 toTarget = targetPosition - position;
            toTarget.y = 0f;

            sqrDistance = toTarget.sqrMagnitude;
            if (sqrDistance > rangeSqr || sqrDistance <= 0.0001f) return false;
            if (closeRangeSqr > 0f && sqrDistance <= closeRangeSqr) return true;
            if (ignoreAngle || !hasForward) return true;

            return Vector3.Dot(forward, toTarget / Mathf.Sqrt(sqrDistance)) >= minDot;
        }
    }

    public static bool HasLineOfSight(Vector3 from, Vector3 to, float eyeHeight, LayerMask obstacleMask)
    {
        if (obstacleMask == 0) return true;

        Vector3 origin = from + Vector3.up * eyeHeight;
        Vector3 destination = to + Vector3.up * eyeHeight;
        Vector3 offset = destination - origin;
        float distance = offset.magnitude;
        if (distance <= 0.01f) return true;

        // Physics.Raycast는 가장 가까운 히트 하나만 돌려준다. 유닛 몸통도 마스크에 들어 있으므로
        // (TargetScanner의 기본 obstacleMask는 ~0이다) 앞에 다른 유닛이 한 명이라도 서 있으면
        // 그 뒤의 벽이 통째로 무시돼 "보인다"가 나왔다 — 난전에서는 거의 항상 이 경로를 탄다.
        // 직선 위의 히트를 전부 받아서 유닛이 아닌 것이 하나라도 있으면 막힌 것으로 본다.
        int hitCount = Physics.RaycastNonAlloc(
            origin, offset / distance, lineOfSightHits, distance - 0.05f, obstacleMask, QueryTriggerInteraction.Ignore);

        // 버퍼가 꽉 찼다면 그 너머에 벽이 더 있었는지 알 수 없다.
        // 벽 너머를 보게 두는 쪽이 더 큰 실수이므로 막힌 것으로 처리한다.
        if (hitCount >= lineOfSightHits.Length) return false;

        for (int i = 0; i < hitCount; i++)
        {
            // 유닛 몸은 시야를 막지 않는다. 정적 레벨 지오메트리(벽)만 막는다.
            if (unitColliders.Contains(lineOfSightHits[i].collider)) continue;
            return false;
        }

        return true;
    }

    public static bool IsVisibleTo(
        UnitController requester,
        UnitController target,
        float range,
        float viewAngle,
        float closeVisibleRange = 0f)
    {
        if (!IsValidTarget(requester, target)) return false;

        var query = new VisionQuery(requester, range, viewAngle, closeVisibleRange);
        return query.CanSee(target.transform.position, out _);
    }
}
