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
        // 들고 있던 타깃이 있으면 그 대상의 "붙어 있는 적 수"에 다시 포함된다.
        unit.HoldTargetCount();
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
        // 죽거나 꺼진 유닛은 더 이상 누구를 물고 있는 것으로 세지 않는다.
        unit.ReleaseTargetCount();
    }

    public static UnitController FindNearestVisibleEnemy(
        UnitController requester,
        float range,
        float viewAngle,
        float closeVisibleRange = 0f,
        float eyeHeight = 0f,
        LayerMask obstacleMask = default,
        float groupingBonusPerAlly = 0f,
        float tankThreatBonus = 0f,
        float currentTargetBonus = 0f,
        int maxAttackersPerTarget = 0,
        float crowdingPenalty = 0f)
    {
        if (requester == null) return null;

        var query = new VisionQuery(requester, range, viewAngle, closeVisibleRange);
        float bestSqrDistance = range * range;
        UnitController best = null;

        GetHostileLists(requester.Team, out List<UnitController> first, out List<UnitController> second);
        SearchNearestInList(requester, first, query, eyeHeight, obstacleMask, groupingBonusPerAlly, tankThreatBonus, currentTargetBonus, maxAttackersPerTarget, crowdingPenalty, ref bestSqrDistance, ref best);
        SearchNearestInList(requester, second, query, eyeHeight, obstacleMask, groupingBonusPerAlly, tankThreatBonus, currentTargetBonus, maxAttackersPerTarget, crowdingPenalty, ref bestSqrDistance, ref best);

        return best;
    }

    // defender를 노리고 공격 모션을 휘두르는 중인 적대 유닛을 찾는다. defender의 CurrentTarget
    // 하나만 보지 않고 적대 팀 전체를 훑는다 — 여러 적에게 둘러싸이면 defender가 지금 맞서
    // 싸우는 상대가 아닌 다른 적이 휘두르는 경우가 흔한데, 그 공격도 막을 수 있어야 한다.
    public static UnitController FindTelegraphingAttacker(UnitController defender)
    {
        if (defender == null) return null;

        GetHostileLists(defender.Team, out List<UnitController> first, out List<UnitController> second);
        UnitController found = FindTelegraphingAttackerInList(defender, first);
        return found != null ? found : FindTelegraphingAttackerInList(defender, second);
    }

    private static UnitController FindTelegraphingAttackerInList(UnitController defender, List<UnitController> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController candidate = list[i];
            if (candidate == null || candidate.IsDead || !candidate.isActiveAndEnabled) continue;
            // 아직 내지르기 전(준비 동작)인 공격만 막을 수 있다. 예전에는 공격 애니메이션이
            // 재생 중이기만 하면 전부 걸렸는데, 도끼처럼 1.7초짜리 클립은 절반 이상이 칼을
            // 거두는 동작이라 이미 지나간 공격에 대고 방패를 드는 일이 잦았다.
            if (!candidate.IsTelegraphing) continue;
            if (candidate.CurrentTarget != defender) continue;

            return candidate;
        }

        return null;
    }

    // 공격자의 스윙 궤적(사거리 + 정면 부채꼴) 안에 있는 적을 하나 찾는다.
    // 노리던 상대가 스윙 도중 빠져나갔을 때 "그럼 눈앞에 있는 놈이 맞는다"를 위한 것 —
    // 실제로 칼을 휘두르면 표적으로 삼지 않은 상대도 베인다.
    public static UnitController FindEnemyInArc(UnitController attacker, float reach, float arcAngle)
    {
        if (attacker == null) return null;

        GetHostileLists(attacker.Team, out List<UnitController> first, out List<UnitController> second);
        UnitController found = FindEnemyInArcInList(attacker, first, reach, arcAngle);
        return found != null ? found : FindEnemyInArcInList(attacker, second, reach, arcAngle);
    }

    private static UnitController FindEnemyInArcInList(UnitController attacker, List<UnitController> list, float reach, float arcAngle)
    {
        Vector3 origin = attacker.transform.position;
        Vector3 forward = attacker.transform.forward;
        forward.y = 0f;
        bool hasForward = forward.sqrMagnitude > 0.0001f;
        if (hasForward) forward.Normalize();

        float minDot = Mathf.Cos(Mathf.Clamp(arcAngle * 0.5f, 0f, 180f) * Mathf.Deg2Rad);
        float reachSqr = reach * reach;

        // 궤적 안에 여럿이 있으면 가장 가까운 하나만 벤다. 광역기가 아니라 스윙이므로
        // 전부에게 피해가 들어가면 난전에서 근접 유닛이 지나치게 강해진다.
        float bestSqrDistance = reachSqr;
        UnitController best = null;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController candidate = list[i];
            if (candidate == null || candidate.IsDead || !candidate.isActiveAndEnabled) continue;

            Vector3 toCandidate = candidate.transform.position - origin;
            toCandidate.y = 0f;

            float sqrDistance = toCandidate.sqrMagnitude;
            if (sqrDistance > bestSqrDistance || sqrDistance <= 0.0001f) continue;
            if (hasForward && Vector3.Dot(forward, toCandidate / Mathf.Sqrt(sqrDistance)) < minDot) continue;

            bestSqrDistance = sqrDistance;
            best = candidate;
        }

        return best;
    }

    // 이미 그 적을 타깃으로 삼고 있는 attackerTeam 소속 유닛 수. 대상 선정에 편향을 줘서
    // 아군이 각자 다른 적으로 흩어지지 않고 같은 적에게 모이도록 만드는 데 쓴다.
    // 이 대상을 노리고 있는 attackerTeam 유닛 수.
    //
    // 예전에는 부를 때마다 팀 전체를 훑었다. 타깃을 고를 때 후보마다 부르는 값이라
    // 유닛 수의 세제곱으로 커졌다(67유닛 기준 스캔 한 주기에 약 7만 회, 두 배면 여덟 배).
    // 지금은 타깃을 잡고 놓는 순간에 대상이 직접 세어 두므로 조회는 배열 한 번이다.
    public static int CountAlliesTargeting(UnitTeam attackerTeam, UnitController target)
    {
        return target != null ? target.AttackersFrom(attackerTeam) : 0;
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

    // 시야에 적이 없을 때 걸어갈 곳 — "지금 싸움이 벌어지고 있는 자리".
    //
    // 적을 잡고 나면 다음 적이 시야 밖인 경우가 흔하다(탐지 범위가 8~14m뿐이다).
    // 그때 Search가 제자리 근처를 배회하면, 아군은 아직 싸우고 있는데 유닛 하나가
    // 전투에서 조용히 빠져 버린다. 그래서 배회 대신 이쪽으로 걸어가게 한다.
    //
    // 아군이 이미 붙어 있는 적을 먼저 고른다 — 그 자리가 곧 전선이다.
    // 아무도 교전 중이 아니면(첫 진입, 또는 모두 놓친 뒤) 가장 가까운 적으로 떨어진다.
    public static UnitController FindRallyEnemy(UnitController seeker)
    {
        if (seeker == null) return null;

        GetHostileLists(seeker.Team, out List<UnitController> first, out List<UnitController> second);

        UnitController engaged = null;
        UnitController nearest = null;
        float engagedSqr = float.MaxValue;
        float nearestSqr = float.MaxValue;

        AccumulateRallyCandidates(seeker, first, ref engaged, ref engagedSqr, ref nearest, ref nearestSqr);
        AccumulateRallyCandidates(seeker, second, ref engaged, ref engagedSqr, ref nearest, ref nearestSqr);

        return engaged != null ? engaged : nearest;
    }

    private static void AccumulateRallyCandidates(UnitController seeker, List<UnitController> list,
        ref UnitController engaged, ref float engagedSqr, ref UnitController nearest, ref float nearestSqr)
    {
        if (list == null) return;

        Vector3 from = seeker.transform.position;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController candidate = list[i];
            if (candidate == null || candidate.IsDead || !candidate.isActiveAndEnabled) continue;

            float sqr = (candidate.transform.position - from).sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = candidate;
            }

            // 우리 편 누군가가 이미 이 적을 노리고 있는가.
            if (CountAlliesTargeting(seeker.Team, candidate) <= 0) continue;
            if (sqr >= engagedSqr) continue;

            engagedSqr = sqr;
            engaged = candidate;
        }
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
        float groupingBonusPerAlly,
        float tankThreatBonus,
        float currentTargetBonus,
        int maxAttackersPerTarget,
        float crowdingPenalty,
        ref float bestSqrDistance,
        ref UnitController best)
    {
        bool useGrouping = groupingBonusPerAlly > 0f;
        bool useTankThreat = tankThreatBonus > 0f;
        bool useStickiness = currentTargetBonus > 0f && requester.IsTargetValid();
        bool useCrowdCap = maxAttackersPerTarget > 0 && crowdingPenalty > 0f;
        bool needsAttackerCount = useGrouping || useCrowdCap;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController candidate = list[i];
            if (!IsValidTarget(requester, candidate)) continue;
            if (!AreEnemies(requester, candidate)) continue;
            if (!query.CanSee(candidate.transform.position, out float sqrDistance)) continue;

            // 이미 같은 편이 붙어 있는 후보 수. 아군 입장에서는 "뭉치는" 데, 적 입장에서는
            // "이미 몇 마리가 이 아군을 물고 있는지"(혼잡도 판정)에 재사용한다.
            int attackerCount = needsAttackerCount ? CountAlliesTargeting(requester.Team, candidate) : 0;

            // 이미 붙어 있는 적(아군 뭉침), (적 입장에서는) 탱커인 아군, 지금 싸우고 있는 기존
            // 타깃은 그만큼 더 가깝게 쳐서 우선시킨다. 반대로 이미 상한만큼 붙잡힌 아군은 그만큼
            // 더 멀게 쳐서(빼는 게 아니라 더해서) 몬스터가 자연히 덜 붙잡힌 아군에게 가도록 한다 —
            // 아군 한 명당 1~2마리로 붙는 교전을 유도한다. 실제 사거리/시야 판정은 위 query.CanSee가
            // 이미 원래 거리로 끝냈으므로 여기서는 "누가 이기는지"만 바뀐다.
            float bias = 0f;
            if (useGrouping)
            {
                bias += attackerCount * groupingBonusPerAlly;
            }
            if (useTankThreat && candidate.Stats.isTank)
            {
                bias += tankThreatBonus;
            }
            if (useStickiness && candidate == requester.CurrentTarget)
            {
                bias += currentTargetBonus;
            }
            if (useCrowdCap && attackerCount >= maxAttackersPerTarget)
            {
                bias -= crowdingPenalty;
            }

            float effectiveSqrDistance = sqrDistance;
            if (bias != 0f)
            {
                float biasedDistance = Mathf.Max(0f, Mathf.Sqrt(sqrDistance) - bias);
                effectiveSqrDistance = biasedDistance * biasedDistance;
            }

            // Distance check before the raycast so line-of-sight (the expensive part) only
            // runs for candidates that would actually improve on the current best.
            if (effectiveSqrDistance >= bestSqrDistance) continue;

            if (!HasLineOfSight(query.Position, candidate.transform.position, eyeHeight, obstacleMask)) continue;

            bestSqrDistance = effectiveSqrDistance;
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
