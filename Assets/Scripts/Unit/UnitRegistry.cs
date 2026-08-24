using System.Collections.Generic;
using UnityEngine;

public static class UnitRegistry
{
    private static readonly List<UnitController> allies = new List<UnitController>(32);
    private static readonly List<UnitController> enemies = new List<UnitController>(32);
    private static readonly List<UnitController> neutrals = new List<UnitController>(16);

    // 살아있는 유닛에 속한 콜라이더 전부. 시야 레이가 유닛 몸통을 벽으로 착각하지 않도록
    // 걸러내는 데 쓴다. 콜라이더 참조로 직접 조회하므로 계층을 거슬러 올라갈 필요가 없다.
    private static readonly HashSet<Collider> unitColliders = new HashSet<Collider>();

    // 시야 레이의 히트를 받는 공용 버퍼. 매 판정마다 배열을 새로 만들지 않기 위해 정적으로 둔다.
    // 한 직선 위에 이보다 많은 콜라이더가 겹치는 경우는 난전 한복판뿐이고,
    // 그때는 "안 보인다"로 처리해 벽 너머를 보는 쪽 실수를 피한다(아래 주석 참조).
    private static readonly RaycastHit[] lineOfSightHits = new RaycastHit[32];

    public static IReadOnlyList<UnitController> Allies => allies;
    public static IReadOnlyList<UnitController> Enemies => enemies;
    public static IReadOnlyList<UnitController> Neutrals => neutrals;

    // 다른 정적 저장소(PartyDeck, CharacterStress...)와 같은 이유로 플레이 시작마다 비운다.
    // 여기만 빠져 있었다: 도메인 리로드를 끄면 이전 플레이에서 파괴된 유닛이 리스트에 남고,
    // HasLivingEnemy가 리스트 개수만 보기 때문에 아무도 없는 맵에서 계속 적을 찾아 헤매게 된다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        allies.Clear();
        enemies.Clear();
        neutrals.Clear();
        unitColliders.Clear();
    }

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

    public static void Register(UnitController unit)
    {
        if (unit == null) return;

        List<UnitController> list = GetList(unit.Team);
        if (list.Contains(unit)) return;

        list.Add(unit);
    }

    // 콜라이더 등록은 팀 리스트가 아니라 오브젝트 수명에 묶는다.
    // 죽어서 레지스트리에서 빠진 시체도 여전히 씬에 서 있으므로, 팀 리스트와 함께 지워버리면
    // 쌓인 시체가 갑자기 시야를 막는 벽이 된다.
    public static void RegisterColliders(UnitController unit)
    {
        if (unit == null || unit.BodyColliders == null) return;

        Collider[] colliders = unit.BodyColliders;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null) unitColliders.Add(colliders[i]);
        }
    }

    public static void UnregisterColliders(UnitController unit)
    {
        if (unit == null || unit.BodyColliders == null) return;

        Collider[] colliders = unit.BodyColliders;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null) unitColliders.Remove(colliders[i]);
        }
    }

    // 팀 리스트 직접 조회. 감정 전파처럼 "같은 팀 전원"을 훑어야 하는 곳에서 쓴다.
    public static IReadOnlyList<UnitController> GetTeam(UnitTeam team)
    {
        return GetList(team);
    }

    public static void Unregister(UnitController unit)
    {
        if (unit == null) return;

        allies.Remove(unit);
        enemies.Remove(unit);
        neutrals.Remove(unit);
    }

    public static UnitController FindNearestVisibleEnemy(
        UnitController requester,
        float range,
        float viewAngle,
        float closeVisibleRange = 0f,
        float eyeHeight = 0f,
        LayerMask obstacleMask = default)
    {
        if (requester == null) return null;

        var query = new VisionQuery(requester, range, viewAngle, closeVisibleRange);
        float bestSqrDistance = range * range;
        UnitController best = null;

        GetHostileLists(requester.Team, out List<UnitController> first, out List<UnitController> second);
        SearchNearestInList(requester, first, query, eyeHeight, obstacleMask, ref bestSqrDistance, ref best);
        SearchNearestInList(requester, second, query, eyeHeight, obstacleMask, ref bestSqrDistance, ref best);

        return best;
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

    // 같은 팀에서 가장 많이 다친 유닛을 찾는다(자기 자신 포함). 서포터의 회복 대상 선정용.
    // 절대 HP가 아니라 비율로 고르는 이유: HP 총량이 큰 탱커가 절반이 깎였는데도
    // 원래 체력이 적은 유닛보다 뒤로 밀리면 파티가 먼저 무너진다.
    public static UnitController FindMostWoundedAlly(UnitController healer, float range, float hpRatioThreshold)
    {
        if (healer == null) return null;

        List<UnitController> team = GetList(healer.Team);
        Vector3 origin = healer.transform.position;
        float rangeSqr = range * range;

        UnitController best = null;
        float bestRatio = hpRatioThreshold;

        for (int i = team.Count - 1; i >= 0; i--)
        {
            UnitController candidate = team[i];
            if (candidate == null || candidate.IsDead || !candidate.isActiveAndEnabled) continue;
            if (!candidate.CanRecoverHp) continue;

            float ratio = candidate.Stats.HpRatio;
            if (ratio > bestRatio) continue;
            if ((candidate.transform.position - origin).sqrMagnitude > rangeSqr) continue;

            bestRatio = ratio;
            best = candidate;
        }

        return best;
    }

    // 팀 전원에게 타깃을 밀어 넣던 AlertTeam은 TeamThreatBoard로 옮겼다.
    // 알림 한 번이 팀 인원 수만큼의 호출이라 난전에서 유닛 수의 제곱으로 커졌고,
    // 그 비용이 발견한 프레임에 통째로 몰렸다. 지금은 게시판에 쓰고(O(1)),
    // 각 유닛이 자기 스캔 주기에 읽어 간다.

    public static void FindEnemiesInRange(UnitController requester, float range, List<UnitController> results)
    {
        if (results == null) return;
        results.Clear();

        if (requester == null) return;

        GetHostileLists(requester.Team, out List<UnitController> first, out List<UnitController> second);
        AddEnemiesInRange(requester, first, range, results);
        AddEnemiesInRange(requester, second, range, results);
    }

    // Idle/Search/Move 상태가 매 프레임 모든 유닛에서 호출하는 핫패스.
    // allies/enemies/neutrals 리스트는 죽거나 비활성화된 유닛이 OnDisable→Unregister로
    // 즉시 제거되므로 항상 "살아있는 유닛만" 담고 있다 → 리스트 순회 없이 개수만으로 판단 가능(O(1)).
    public static bool HasLivingEnemy(UnitController requester)
    {
        if (requester == null) return false;

        switch (requester.Team)
        {
            case UnitTeam.Ally: return enemies.Count > 0;
            case UnitTeam.Enemy: return allies.Count > 0;
            default: return allies.Count > 0 || enemies.Count > 0; // Neutral의 적은 비-중립 전체
        }
    }

    public static bool AreEnemies(UnitController a, UnitController b)
    {
        if (a == null || b == null || a == b) return false;
        if (a.Team == UnitTeam.Neutral) return b.Team != UnitTeam.Neutral;
        if (b.Team == UnitTeam.Neutral) return false;
        return a.Team != b.Team;
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

    // 요청자 팀에 적대적인 리스트만 돌려준다. 예전에는 세 리스트를 모두 훑고 AreEnemies로
    // 걸러냈는데, 자기 팀 리스트(보통 가장 큰 리스트)를 통째로 헛도는 셈이었다.
    private static void GetHostileLists(UnitTeam team, out List<UnitController> first, out List<UnitController> second)
    {
        switch (team)
        {
            case UnitTeam.Ally:
                first = enemies;
                second = neutrals;
                break;
            case UnitTeam.Enemy:
                first = allies;
                second = neutrals;
                break;
            default: // Neutral은 비-중립 전체가 적
                first = allies;
                second = enemies;
                break;
        }
    }

    private static void SearchNearestInList(
        UnitController requester,
        List<UnitController> list,
        in VisionQuery query,
        float eyeHeight,
        LayerMask obstacleMask,
        ref float bestSqrDistance,
        ref UnitController best)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController candidate = list[i];
            if (!IsValidTarget(requester, candidate)) continue;
            if (!AreEnemies(requester, candidate)) continue;
            if (!query.CanSee(candidate.transform.position, out float sqrDistance)) continue;

            // Distance check before the raycast so line-of-sight (the expensive part) only
            // runs for candidates that would actually improve on the current best.
            if (sqrDistance >= bestSqrDistance) continue;

            if (!HasLineOfSight(query.Position, candidate.transform.position, eyeHeight, obstacleMask)) continue;

            bestSqrDistance = sqrDistance;
            best = candidate;
        }
    }

    private static void AddEnemiesInRange(
        UnitController requester,
        List<UnitController> list,
        float range,
        List<UnitController> results)
    {
        float rangeSqr = range * range;
        Vector3 requesterPosition = requester.transform.position;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController candidate = list[i];
            if (!IsValidTarget(requester, candidate)) continue;
            if (!AreEnemies(requester, candidate)) continue;

            float sqrDistance = (candidate.transform.position - requesterPosition).sqrMagnitude;
            if (sqrDistance <= rangeSqr)
            {
                results.Add(candidate);
            }
        }
    }

    private static bool IsValidTarget(UnitController requester, UnitController target)
    {
        return requester != null &&
               target != null &&
               requester != target &&
               target.isActiveAndEnabled &&
               !target.IsDead;
    }

    private static List<UnitController> GetList(UnitTeam team)
    {
        switch (team)
        {
            case UnitTeam.Ally:
                return allies;
            case UnitTeam.Enemy:
                return enemies;
            default:
                return neutrals;
        }
    }
}
