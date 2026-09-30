using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public static partial class UnitRegistry
{
    // ------------------------------------------------------------------
    // 파티 집중 표적
    //
    // 파티 전투인데 각자 다른 적을 때리면 아무도 죽지 않는다. 실측에서 아군 6명이 표적 5개로
    // 갈라져 적 8마리가 전부 HP 100%로 남았다 — 피해가 여덟 갈래로 흩어져 어느 하나도
    // 처치선에 닿지 못한 것이다.
    //
    // 흩어지는 이유는 편향의 순서였다. 스폰 직후에는 전원이 같은 프레임에 표적을 고르므로
    // "이미 아군이 붙은 적"(groupingBonus)이 읽을 정보가 없어 전부 0으로 보이고, 그 뒤로는
    // 기존 타깃 유지 편향(stickiness 5m)이 그룹핑(아군 1명당 3m)보다 커서 아무도 옮기지 않는다.
    //
    // 그래서 "누구를 먼저 죽일 것인가"를 파티 차원에서 하나 정해 둔다. 고르는 기준은
    // 가장 많이 깎인 적이다 — 이미 들인 피해를 버리지 않고 처치까지 밀어붙이는 쪽이
    // 파티 전체의 화력을 가장 크게 만든다(적 하나가 죽으면 그만큼 들어오는 공격도 준다).
    //
    // 모두가 여기 모이지는 않는다. 탱커는 어그로를 붙들어야 하고 암살자는 후방을 파고들어야
    // 하므로 그쪽은 이 편향을 받지 않는다(JobProfile.FocusBonus). 집중은 딜러의 몫이다.
    // ------------------------------------------------------------------

    //
    // 표적은 손잡이(TargetRef)다. 게임오브젝트만 담던 시절에는 적이 엔티티가 되자 집중 표적이 영영 비어,
    // 딜러의 집중 편향·집중 표적 우선 선택·"집중 중이면 맞아도 안 흔들린다"가 전부 조용히 꺼져 있었다.
    private static readonly TargetRef[] focusTargets = new TargetRef[3];

    // 한 번 정한 표적은 쓰러질 때까지 바꾸지 않는다.
    //
    // 처음에는 "가장 많이 깎인 적"을 주기적으로 다시 골랐는데, 그게 오히려 흩어지게 만들었다:
    // 피해가 퍼질수록 최저 HP가 계속 바뀌어 표적이 매 초 옮겨 다녔고, 아무도 따라잡지 못했다
    // (실측에서 집중 표적이 G4 → G7로 흔들리는 동안 딜러 셋이 서로 다른 적을 때리고 있었다).
    //
    // 집중 사격은 "지금 가장 약한 놈"을 쫓는 것이 아니라 "하나를 정해 끝까지 미는 것"이다.
    // 그래서 표적은 죽어야만 바뀐다. 하나가 쓰러지면 그만큼 들어오는 공격도 줄어드는데,
    // 표적을 계속 갈아타면 그 이득을 영영 얻지 못한다.
    public static TargetRef GetFocusTarget(UnitTeam team)
    {
        int index = TeamIndex(team);

        TargetRef current = focusTargets[index];
        if (current.Exists && current.IsAlive) return current;

        focusTargets[index] = PickFocusTarget(team);
        return focusTargets[index];
    }

    // 다음에 잡을 하나를 고른다.
    //
    // 이미 교전 중인 적을 먼저 본다 — 아무도 손대지 않은 적을 고르면 파티가 전선을 버리고
    // 그쪽으로 끌려간다. 그 안에서는 가장 많이 깎인 쪽을 골라 들인 피해를 버리지 않는다.
    // 교전 중인 적이 하나도 없으면(전투 시작 직후) 전선에서 가장 가까운 적으로 떨어진다.
    //
    // 게임오브젝트 적과 엔티티 적을 같은 누적값으로 이어서 훑는다 — 세계마다 따로 고른 뒤 합치면
    // "가장 많이 깎인 하나"가 세계마다 하나씩 둘이 나온다.
    private static TargetRef PickFocusTarget(UnitTeam team)
    {
        GetHostileLists(team, out List<UnitController> first, out List<UnitController> second);

        UnitController engagedUnit = null;
        float bestRatio = float.MaxValue;
        UnitController nearestUnit = null;
        float nearestSqr = float.MaxValue;

        Vector3 origin = TeamOrigin(team);

        AccumulateFocusCandidate(team, first, origin, ref engagedUnit, ref bestRatio, ref nearestUnit, ref nearestSqr);
        AccumulateFocusCandidate(team, second, origin, ref engagedUnit, ref bestRatio, ref nearestUnit, ref nearestSqr);

        // 엔티티는 전부 적 팀이다. 적 팀이 고르는 집중 표적에는 들어가지 않는다.
        Entity engagedEntity = Entity.Null;
        Entity nearestEntity = Entity.Null;
        if (team != UnitTeam.Enemy)
        {
            EnemyWorldBridge.AccumulateFocusCandidates(origin, ref engagedEntity, ref bestRatio,
                ref nearestEntity, ref nearestSqr);
        }

        // 엔티티 쪽이 값을 덮었으면 그쪽이 더 나은 후보다(같은 누적값에서 더 작을 때만 덮는다).
        TargetRef engaged = engagedEntity != Entity.Null ? new TargetRef(engagedEntity) : engagedUnit;
        TargetRef nearest = nearestEntity != Entity.Null ? new TargetRef(nearestEntity) : nearestUnit;

        return engaged.Exists ? engaged : nearest;
    }

    private static void AccumulateFocusCandidate(UnitTeam team, List<UnitController> list, Vector3 origin,
        ref UnitController engaged, ref float bestRatio, ref UnitController nearest, ref float nearestSqr)
    {
        if (list == null) return;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController candidate = list[i];
            if (candidate == null || candidate.IsDead || !candidate.isActiveAndEnabled) continue;

            float sqr = (candidate.transform.position - origin).sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = candidate;
            }

            if (CountAlliesTargeting(team, candidate) <= 0) continue;

            float ratio = candidate.Stats.HpRatio;
            if (ratio >= bestRatio) continue;

            bestRatio = ratio;
            engaged = candidate;
        }
    }

    // 이 팀이 지금 서 있는 자리의 대표값. 집중 표적을 처음 고를 때 "가장 가까운"의 기준이 된다.
    private static Vector3 TeamOrigin(UnitTeam team)
    {
        List<UnitController> list = GetList(team);
        Vector3 sum = Vector3.zero;
        int count = 0;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController unit = list[i];
            if (unit == null || unit.IsDead) continue;

            sum += unit.transform.position;
            count++;
        }

        return count > 0 ? sum / count : Vector3.zero;
    }

    private static int TeamIndex(UnitTeam team)
    {
        int index = (int)team;
        return index >= 0 && index < focusTargets.Length ? index : 0;
    }
}
