using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

// 겨눌 적 하나를 고르는 질의 — 편향을 얹은 최선의 적, 나를 노려 휘두르는 적, 스윙 궤적 안의 적, 전선.
public static partial class UnitRegistry
{
    // 대상 선정에 얹히는 편향들. 인자가 여덟 개까지 늘어나 호출부에서 어느 자리가 무엇인지
    // 읽을 수 없게 됐다(실제로 tankThreatBonus와 currentTargetBonus의 순서를 헷갈리기 쉬웠다).
    // 값 묶음으로 만들면 호출부가 필드 이름으로 채워 넣게 되어 그 실수가 사라진다.
    public readonly struct TargetBias
    {
        // 이미 아군이 붙어 있는 적을 이만큼 더 가깝게 친다(뭉치기).
        public readonly float GroupingPerAlly;
        // 후보의 위협 가중치(UnitStats.threatWeight)에 곱해 더한다. 어그로.
        public readonly float ThreatScale;
        // 후보가 적 진영의 후방(궁수/마법사/사제)이면 더한다. 암살자의 침투.
        public readonly float BacklineBonus;
        // 후보가 나보다 약한 아군을 물고 있으면 더한다. 탱커의 도발.
        public readonly float PeelBonus;
        // 후보가 파티의 집중 표적이면 더한다. 딜러가 화력을 한 곳에 모으는 힘.
        public readonly float FocusBonus;
        // 지금 싸우고 있는 타깃을 계속 유지하려는 성향.
        public readonly float Stickiness;
        // 이 수만큼 이미 물린 후보는 CrowdingPenalty만큼 멀게 쳐서 분산시킨다.
        public readonly int MaxAttackers;
        public readonly float CrowdingPenalty;

        public TargetBias(float groupingPerAlly, float threatScale, float backlineBonus, float peelBonus,
            float focusBonus, float stickiness, int maxAttackers, float crowdingPenalty)
        {
            GroupingPerAlly = groupingPerAlly;
            ThreatScale = threatScale;
            BacklineBonus = backlineBonus;
            PeelBonus = peelBonus;
            FocusBonus = focusBonus;
            Stickiness = stickiness;
            MaxAttackers = maxAttackers;
            CrowdingPenalty = crowdingPenalty;
        }
    }

    // 편향을 얹어 가장 나은 적 하나를 고른다. 게임오브젝트 적과 엔티티 적을 같은 점수로 이어서 훑는다.
    //
    // 예전에는 게임오브젝트만 훑었다(시야 레이캐스트를 1000마리에 걸 수 없어서 엔티티는 따로
    // TryAcquireEntityTarget이 거리와 붙은 수만 보고 골랐다). 그래서 적이 엔티티가 된 뒤로는 편향 전부가
    // 꺼져 있었다 — 탱커의 도발(peel)도, 딜러의 집중도, 교전 중 재평가의 유지 편향도 엔티티 앞에서는 없었다.
    // 엔티티는 콜라이더가 없어 시야각·레이캐스트 대신 탐지 거리만 본다. 편향은 똑같이 얹는다.
    public static TargetRef FindNearestVisibleEnemy(
        UnitController requester,
        float range,
        float viewAngle,
        float closeVisibleRange,
        float eyeHeight,
        LayerMask obstacleMask,
        in TargetBias bias)
    {
        if (requester == null) return TargetRef.None;

        var query = new VisionQuery(requester, range, viewAngle, closeVisibleRange);
        float bestSqrDistance = range * range;
        UnitController best = null;

        GetHostileLists(requester.Team, out List<UnitController> first, out List<UnitController> second);
        SearchNearestInList(requester, first, query, eyeHeight, obstacleMask, bias, ref bestSqrDistance, ref best);
        SearchNearestInList(requester, second, query, eyeHeight, obstacleMask, bias, ref bestSqrDistance, ref best);

        Entity bestEntity = Entity.Null;
        if (SeesEnemyEntities(requester))
        {
            SearchEntityEnemies(requester, range, bias, ref bestSqrDistance, ref bestEntity);
        }

        return bestEntity != Entity.Null ? new TargetRef(bestEntity) : best;
    }

    // 엔티티 적에게 같은 편향을 얹어 훑는다(SearchNearestInList의 엔티티판).
    //
    // 어그로(ThreatScale)·후방 침투(Backline)·혼잡도 상한(MaxAttackers)은 적이 아군을 고를 때만 쓰이는
    // 값이라 여기서는 빠진다 — 엔티티는 늘 적이고, 고르는 쪽은 늘 아군이다.
    private static void SearchEntityEnemies(UnitController requester, float range, in TargetBias settings,
        ref float bestSqrDistance, ref Entity best)
    {
        int count = EnemyWorldBridge.EnemyCount;
        if (count == 0) return;

        bool useGrouping = settings.GroupingPerAlly > 0f;
        bool usePeel = settings.PeelBonus > 0f;
        TargetRef focus = settings.FocusBonus > 0f ? GetFocusTarget(requester.Team) : TargetRef.None;
        Entity current = settings.Stickiness > 0f && requester.IsTargetValid() ? requester.CurrentTarget.Entity : Entity.Null;

        Vector3 origin = requester.transform.position;
        float rangeSqr = range * range;

        for (int i = 0; i < count; i++)
        {
            EnemyWorldBridge.EnemyState enemy = EnemyWorldBridge.GetEnemy(i);
            if (!enemy.IsAlive) continue;

            Vector3 offset = (Vector3)enemy.position - origin;
            offset.y = 0f;
            float sqrDistance = offset.sqrMagnitude;
            if (sqrDistance > rangeSqr) continue;

            float bias = 0f;
            if (useGrouping) bias += EnemyWorldBridge.AllyAttackersOn(enemy.entity) * settings.GroupingPerAlly;
            if (usePeel) bias += PeelBiasForEntity(requester, enemy, settings.PeelBonus);
            if (focus.IsEntity && focus.Entity == enemy.entity) bias += settings.FocusBonus;
            if (current != Entity.Null && current == enemy.entity) bias += settings.Stickiness;

            float effectiveSqrDistance = sqrDistance;
            if (bias != 0f)
            {
                float biasedDistance = Mathf.Max(0f, Mathf.Sqrt(sqrDistance) - bias);
                effectiveSqrDistance = biasedDistance * biasedDistance;
            }

            if (effectiveSqrDistance >= bestSqrDistance) continue;

            bestSqrDistance = effectiveSqrDistance;
            best = enemy.entity;
        }
    }

    // PeelBiasFor의 엔티티판. 이 적이 물고 있는 아군은 스냅샷 인덱스로 온다.
    private static float PeelBiasForEntity(UnitController requester, in EnemyWorldBridge.EnemyState enemy, float peelBonus)
    {
        float best = 0f;

        UnitController victim = EnemyWorldBridge.GetAlly(enemy.targetAllyIndex);
        if (IsWorthGuarding(requester, victim))
        {
            best = victim.IsCasting ? peelBonus * CastingGuardScale : peelBonus;
        }

        if (best <= 0f && IsNearGuardedAlly(requester, (Vector3)enemy.position))
        {
            best = peelBonus * ApproachGuardScale;
        }

        return best;
    }

    // defender를 노리고 공격 모션을 휘두르는 중인 적대 유닛을 찾는다. defender의 CurrentTarget
    // 하나만 보지 않고 적대 팀 전체를 훑는다 — 여러 적에게 둘러싸이면 defender가 지금 맞서
    // 싸우는 상대가 아닌 다른 적이 휘두르는 경우가 흔한데, 그 공격도 막을 수 있어야 한다.
    public static TargetRef FindTelegraphingAttacker(UnitController defender)
    {
        if (defender == null) return TargetRef.None;

        GetHostileLists(defender.Team, out List<UnitController> first, out List<UnitController> second);
        UnitController found = FindTelegraphingAttackerInList(defender, first);
        if (found == null) found = FindTelegraphingAttackerInList(defender, second);
        if (found != null) return found;

        return FindTelegraphingEnemyEntity(defender);
    }

    // 준비 동작 중인 적은 이미 제 사거리 안까지 들어와 있다 — 엔티티 쪽은 사거리 밖에서 아예
    // 칼을 들지 않는다(EnemySimulationSystems.CombatJob). 그래서 이 거리는 막을 상대를 고르는
    // 조건이 아니라, 스냅샷이 한 프레임 낡았을 때를 위한 상한이다. 고블린의 사거리(1.2m)와
    // 타격 여유(0.4m)를 넉넉히 덮는다.
    private const float TelegraphReach = 4f;

    private static TargetRef FindTelegraphingEnemyEntity(UnitController defender)
    {
        // 브리지가 겨눔을 아군 인덱스로 기록하므로, 스냅샷에 실리는 유닛만 물어볼 수 있다.
        // 그 밖(적 팀, 중립)은 여기서 -1로 떨어져 게임오브젝트 쪽 결과만 쓰게 된다.
        int allyIndex = EnemyWorldBridge.IndexOfAlly(defender);
        if (allyIndex < 0) return TargetRef.None;

        if (!EnemyWorldBridge.TryFindTelegraphingAttacker(allyIndex, defender.transform.position,
                TelegraphReach, out int index))
        {
            return TargetRef.None;
        }

        return new TargetRef(EnemyWorldBridge.GetEnemy(index).entity);
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
    public static TargetRef FindEnemyInArc(UnitController attacker, float reach, float arcAngle)
    {
        if (attacker == null) return TargetRef.None;

        GetHostileLists(attacker.Team, out List<UnitController> first, out List<UnitController> second);

        // 최단거리를 하나만 들고 양쪽 세계를 이어서 훑는다. 세계마다 따로 고르면
        // "궤적 안에 여럿이 있으면 가장 가까운 하나만 벤다"는 규칙이 둘로 쪼개진다.
        float bestSqr = reach * reach;
        UnitController best = null;
        AccumulateEnemyInArc(attacker, first, arcAngle, ref best, ref bestSqr);
        AccumulateEnemyInArc(attacker, second, arcAngle, ref best, ref bestSqr);

        if (SeesEnemyEntities(attacker))
        {
            Entity entity = Entity.Null;
            Transform t = attacker.transform;
            EnemyWorldBridge.AccumulateEnemyInArc(t.position, t.forward, arcAngle, ref entity, ref bestSqr);

            // 엔티티가 잡혔다면 위에서 고른 것보다 반드시 가깝다 — 같은 bestSqr를 이어받아
            // 그보다 가까울 때만 덮었기 때문이다.
            if (entity != Entity.Null) return new TargetRef(entity);
        }

        return best != null ? new TargetRef(best) : TargetRef.None;
    }

    private static void AccumulateEnemyInArc(UnitController attacker, List<UnitController> list, float arcAngle,
        ref UnitController best, ref float bestSqrDistance)
    {
        Vector3 origin = attacker.transform.position;
        Vector3 forward = attacker.transform.forward;
        forward.y = 0f;
        bool hasForward = forward.sqrMagnitude > 0.0001f;
        if (hasForward) forward.Normalize();

        float minDot = Mathf.Cos(Mathf.Clamp(arcAngle * 0.5f, 0f, 180f) * Mathf.Deg2Rad);

        // 궤적 안에 여럿이 있으면 가장 가까운 하나만 벤다. 광역기가 아니라 스윙이므로
        // 전부에게 피해가 들어가면 난전에서 근접 유닛이 지나치게 강해진다.
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
    }

    // 시야에 적이 없을 때 걸어갈 곳 — "지금 싸움이 벌어지고 있는 자리".
    //
    // 적을 잡고 나면 다음 적이 시야 밖인 경우가 흔하다(탐지 범위가 8~14m뿐이다).
    // 그때 Search가 제자리 근처를 배회하면, 아군은 아직 싸우고 있는데 유닛 하나가
    // 전투에서 조용히 빠져 버린다. 그래서 배회 대신 이쪽으로 걸어가게 한다.
    //
    // 아군이 이미 붙어 있는 적을 먼저 고른다 — 그 자리가 곧 전선이다.
    // 아무도 교전 중이 아니면(첫 진입, 또는 모두 놓친 뒤) 가장 가까운 적으로 떨어진다.
    public static TargetRef FindRallyEnemy(UnitController seeker)
    {
        if (seeker == null) return TargetRef.None;

        GetHostileLists(seeker.Team, out List<UnitController> first, out List<UnitController> second);

        UnitController engaged = null;
        UnitController nearest = null;
        float engagedSqr = float.MaxValue;
        float nearestSqr = float.MaxValue;

        AccumulateRallyCandidates(seeker, first, ref engaged, ref engagedSqr, ref nearest, ref nearestSqr);
        AccumulateRallyCandidates(seeker, second, ref engaged, ref engagedSqr, ref nearest, ref nearestSqr);

        // 거리도 같은 값을 이어받으므로, 엔티티가 잡혔다면 그쪽이 더 가까운 것이다.
        Entity engagedEntity = Entity.Null;
        Entity nearestEntity = Entity.Null;
        if (SeesEnemyEntities(seeker))
        {
            EnemyWorldBridge.AccumulateRallyCandidates(seeker.transform.position,
                ref engagedEntity, ref engagedSqr, ref nearestEntity, ref nearestSqr);
        }

        // 전선(이미 붙어 있는 적)이 먼저다. 두 세계를 통틀어 그런 적이 없을 때에만
        // 가장 가까운 적으로 떨어진다 — 세계마다 따로 고르면 엔티티 쪽 전선이 있는데도
        // 게임오브젝트 쪽 "가장 가까운 적"이 이겨 버린다.
        if (engagedEntity != Entity.Null) return new TargetRef(engagedEntity);
        if (engaged != null) return engaged;
        if (nearestEntity != Entity.Null) return new TargetRef(nearestEntity);
        return nearest != null ? new TargetRef(nearest) : TargetRef.None;
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

    private static void SearchNearestInList(
        UnitController requester,
        List<UnitController> list,
        in VisionQuery query,
        float eyeHeight,
        LayerMask obstacleMask,
        in TargetBias settings,
        ref float bestSqrDistance,
        ref UnitController best)
    {
        bool useGrouping = settings.GroupingPerAlly > 0f;
        bool useThreat = settings.ThreatScale > 0f;
        bool useBackline = settings.BacklineBonus > 0f;
        bool usePeel = settings.PeelBonus > 0f;
        bool useFocus = settings.FocusBonus > 0f;
        UnitController focus = useFocus ? GetFocusTarget(requester.Team).Unit : null;
        if (focus == null) useFocus = false;
        bool useStickiness = settings.Stickiness > 0f && requester.IsTargetValid();
        bool useCrowdCap = settings.MaxAttackers > 0 && settings.CrowdingPenalty > 0f;
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

            // 이미 붙어 있는 적(아군 뭉침), 어그로가 높은 아군, 지금 싸우고 있는 기존 타깃은
            // 그만큼 더 가깝게 쳐서 우선시킨다. 반대로 이미 상한만큼 붙잡힌 아군은 그만큼
            // 더 멀게 쳐서(빼는 게 아니라 더해서) 몬스터가 자연히 덜 붙잡힌 아군에게 가도록 한다 —
            // 아군 한 명당 1~2마리로 붙는 교전을 유도한다. 실제 사거리/시야 판정은 위 query.CanSee가
            // 이미 원래 거리로 끝냈으므로 여기서는 "누가 이기는지"만 바뀐다.
            float bias = 0f;
            if (useGrouping)
            {
                bias += attackerCount * settings.GroupingPerAlly;
            }

            // 어그로. 예전에는 "탱커인가"라는 참/거짓 하나였다 — 탱커가 아니면 전부 똑같이
            // 노려져서, 사제가 최전선의 검사와 같은 확률로 물렸다. 지금은 직군마다 다른
            // 가중치를 곱한다(탱커 3.2 / 검사 1.0 / 창수 0.85 / 암살자 0.55 / 궁수·마법사 0.4 / 사제 0.3).
            // 그래서 방어선이 서면 후방이 실제로 안전해지고, 방어선이 무너지면 그 순간 후방이 노출된다.
            if (useThreat)
            {
                bias += candidate.Stats.threatWeight * settings.ThreatScale;
            }

            // 후방 침투. 암살자만 이 값을 갖는다 — 눈앞의 전열이 아니라 그 너머의 궁수·마법사·사제를
            // 찾아 들어간다. 어그로가 만든 편향(탱커가 가장 당겨진다)을 정확히 거스르는 자리라,
            // 파티에 암살자가 있고 없고가 적 후방의 안전을 가른다.
            if (useBackline && JobProfile.IsBacklineRole(candidate.Stats.role))
            {
                bias += settings.BacklineBonus;
            }

            // 도발. 나보다 약한 아군을 물고 있는 적을 우선 노린다 — 탱커만 이 값을 갖는다.
            //
            // 어그로를 "적이 나를 고르게 하는 힘"으로만 두면 탱커는 수동적이다. 이미 사제를
            // 물고 있는 적은 사제가 죽을 때까지 그대로 붙어 있다(실측: 탱커 1마리 / 검사 3마리).
            // 여기서 탱커가 그쪽을 고르면, 걸어가 한 대 치는 순간 위협 비교가 적을 넘겨받는다.
            if (usePeel)
            {
                bias += PeelBiasFor(requester, candidate, settings.PeelBonus);
            }

            // 파티 집중 표적. 딜러가 화력을 한 곳에 모으는 힘이다(GetFocusTarget 주석 참조).
            // 기존 타깃 유지 편향(Stickiness)보다 크게 잡아야 실제로 옮겨 간다 —
            // 작으면 각자 처음 문 적을 죽을 때까지 놓지 않아 파티 전투가 1대1 여러 개가 된다.
            if (useFocus && candidate == focus)
            {
                bias += settings.FocusBonus;
            }

            if (useStickiness && candidate == requester.CurrentTarget)
            {
                bias += settings.Stickiness;
            }
            // 혼잡도 상한. 이미 충분히 붙잡힌 아군은 멀게 쳐서 다음 몬스터가 다른 곳으로 가게 한다.
            //
            // 상한을 전원 똑같이 두면 어그로와 정면으로 싸운다. 실측에서 탱커(위협 3.2)에 둘이
            // 붙는 순간 상한에 걸려 다음 몬스터들이 곧바로 궁수·마법사에게 갔다 — 방어선이
            // 두 마리까지만 유효했다는 뜻이다. 원작의 탱커는 그러라고 있는 직군이 아니다.
            //
            // 그래서 "몇을 붙들 수 있는가"도 직군이 정한다. 다만 어그로(선형)보다 완만하게 늘린다 —
            // 위협이 3배라고 셋을 동시에 막아낼 수 있는 것은 아니기 때문이다(제곱근).
            // 기본 상한 2 기준으로 탱커 4 / 검사·창수 2 / 궁수·마법사·사제 1이 된다.
            if (useCrowdCap && attackerCount >= EffectiveAttackerCap(candidate, settings.MaxAttackers))
            {
                bias -= settings.CrowdingPenalty;
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

    // 지켜야 할 아군을 위협하는 적일수록 크게 당긴다. 전열이 후방을 지키는 판단 전부가 여기 있다.
    //
    // 세 갈래로 나뉜다:
    //   1) 약한 아군을 물고 있다        — 떼어내야 한다(도발)
    //   2) 그 아군이 지금 영창 중이다   — 가장 급하다. 영창은 붙잡히는 순간 접히므로
    //                                    (ShouldAbandonCast) 지금 떼어내지 못하면 그 한 방은 사라진다
    //   3) 아직 물지는 않았지만 약한 아군 곁에 있다 — 파고드는 중이다. 물기를 기다릴 이유가 없다
    //
    // 3이 없으면 전열은 "이미 맞고 있는 아군"만 구하러 간다. 그건 늦다 —
    // 마법사를 노리고 달려가는 적은 아직 탱커를 타깃으로 들고 있을 수 있기 때문이다.
    private static float PeelBiasFor(UnitController requester, UnitController candidate, float peelBonus)
    {
        float best = 0f;

        // 1·2) 무언가를 물고 있는 경우.
        UnitController victim = candidate.CurrentTarget.Unit;
        if (IsWorthGuarding(requester, victim))
        {
            best = victim.IsCasting ? peelBonus * CastingGuardScale : peelBonus;
        }

        // 3) 물지 않았어도 지켜야 할 아군 곁에 있으면 그것만으로 이유가 된다.
        if (best <= 0f && IsNearGuardedAlly(requester, candidate))
        {
            best = peelBonus * ApproachGuardScale;
        }

        return best;
    }

    // 영창 중인 아군을 물고 있는 적은 이만큼 더 급하게 친다.
    private const float CastingGuardScale = 1.6f;
    // 아직 물지는 않았지만 지켜야 할 아군 곁까지 온 적. 이미 문 적보다는 덜 급하다.
    private const float ApproachGuardScale = 0.7f;
    // "곁에 있다"로 볼 거리(미터).
    private const float GuardProximity = 3.5f;

    // 내가 지켜야 할 아군인가. 나보다 약한(위협 가중치가 낮은) 우리 편이 그 대상이다.
    // 나 자신은 해당 없다 — 이미 내가 붙들고 있는 것이라 떼어낼 것이 없다.
    private static bool IsWorthGuarding(UnitController requester, UnitController ally)
    {
        if (ally == null || ally == requester) return false;
        if (ally.Team != requester.Team || ally.IsDead) return false;

        return ally.Stats.threatWeight < requester.Stats.threatWeight;
    }

    // 이 적이 지켜야 할 아군 바로 곁에 와 있는가.
    private static bool IsNearGuardedAlly(UnitController requester, UnitController candidate)
    {
        return IsNearGuardedAlly(requester, candidate.transform.position);
    }

    private static bool IsNearGuardedAlly(UnitController requester, Vector3 enemyPosition)
    {
        List<UnitController> team = GetList(requester.Team);
        float rangeSqr = GuardProximity * GuardProximity;

        for (int i = team.Count - 1; i >= 0; i--)
        {
            UnitController ally = team[i];
            if (!IsWorthGuarding(requester, ally)) continue;

            Vector3 offset = ally.transform.position - enemyPosition;
            offset.y = 0f;
            if (offset.sqrMagnitude <= rangeSqr) return true;
        }

        return false;
    }

    // 이 후보가 동시에 몇에게 붙잡혀도 "아직 여유 있다"고 볼 것인가.
    // 위협 가중치의 제곱근으로 늘린다(위 주석 참조). 최소 1 — 아무도 못 붙는 아군은 없어야 한다.
    // 스폰 시점의 초기 배정(CharacterBattleSpawner)도 같은 정의를 써야 어긋나지 않는다.
    public static int EffectiveAttackerCap(UnitController candidate, int baseCap)
    {
        float weight = candidate.Stats.threatWeight;
        if (weight <= 0f) return Mathf.Max(1, baseCap);

        return Mathf.Max(1, Mathf.RoundToInt(baseCap * Mathf.Sqrt(weight)));
    }
}
