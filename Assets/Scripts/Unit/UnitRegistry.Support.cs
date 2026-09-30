using System.Collections.Generic;
using UnityEngine;

// 지원가가 도울 우리 편을 고르는 질의 — 보호막을 미리 걸 사람, 치료할 사람.
public static partial class UnitRegistry
{
    // 같은 팀에서 가장 손이 급한 유닛을 찾는다(자기 자신 포함). 사제의 치료 대상 선정용.
    //
    // 절대 HP가 아니라 비율로 고르는 이유: HP 총량이 큰 탱커가 절반이 깎였는데도
    // 원래 체력이 적은 유닛보다 뒤로 밀리면 파티가 먼저 무너진다.
    //
    // dispelBonus는 상태이상에 걸린 아군의 비율에서 빼 주는 값이다. 원작에서 사제의 판단은
    // "누가 가장 아픈가"가 아니라 "지금 무엇이 전열을 무너뜨리는가"이므로, 덜 다쳤어도
    // 출혈이 흐르고 있으면 그쪽이 먼저다. 0이면 예전처럼 HP만 본다.
    // 보호막을 미리 걸어 줄 아군. 가장 많이 노려지고 있는 쪽을 고른다.
    //
    // 치유 대상 선정(FindMostWoundedAlly)과 정반대 기준이다. 저쪽은 HP가 낮은 순인데
    // 여기는 HP를 아예 보지 않는다 — 보호막은 맞기 전에 걸어야 값어치가 있고, 이미 깎인
    // 사람에게 거는 것은 치유가 할 일이다. 대신 "몇 마리가 붙어 있는가"를 본다.
    //
    // 같은 조건이면 HP가 낮은 쪽이 이긴다. 셋이 붙은 만피 탱커와 셋이 붙은 반피 검사가
    // 있으면 후자가 먼저 무너지기 때문이다.
    public static UnitController FindShieldTarget(UnitController caster, float range, int minAttackers)
    {
        if (caster == null) return null;

        List<UnitController> team = GetList(caster.Team);
        Vector3 origin = caster.transform.position;
        float rangeSqr = range * range;
        UnitTeam hostile = caster.Team == UnitTeam.Ally ? UnitTeam.Enemy : UnitTeam.Ally;

        UnitController best = null;
        int bestAttackers = Mathf.Max(1, minAttackers) - 1;
        float bestRatio = 0f;

        for (int i = team.Count - 1; i >= 0; i--)
        {
            UnitController candidate = team[i];
            if (candidate == null || candidate.IsDead || !candidate.isActiveAndEnabled) continue;
            // 이미 걸려 있으면 덧바르지 않는다. 마력을 그냥 버리는 셈이다.
            if (candidate.Stats.HasShield) continue;

            // 엔티티 적은 AddAttacker를 부르지 않으므로 브리지가 센 수를 더한다. 이게 빠져 있던 동안
            // 고블린에게 둘러싸인 탱커도 "붙은 적 0"으로 보여 사제의 보호막이 한 번도 나가지 않았다.
            int attackers = candidate.AttackersFrom(hostile);
            if (hostile == UnitTeam.Enemy) attackers += EnemyWorldBridge.EntityAttackersOnAlly(candidate);
            if (attackers < bestAttackers) continue;
            if (attackers == bestAttackers && best != null && candidate.Stats.HpRatio >= bestRatio) continue;
            if ((candidate.transform.position - origin).sqrMagnitude > rangeSqr) continue;

            bestAttackers = attackers;
            bestRatio = candidate.Stats.HpRatio;
            best = candidate;
        }

        return best;
    }

    public static UnitController FindMostWoundedAlly(
        UnitController healer, float range, float hpRatioThreshold, float dispelBonus = 0f)
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
            if (dispelBonus > 0f && candidate.Emotion != null && candidate.Emotion.HasDispellableEffect)
            {
                ratio -= dispelBonus;
            }

            if (ratio > bestRatio) continue;
            if ((candidate.transform.position - origin).sqrMagnitude > rangeSqr) continue;

            bestRatio = ratio;
            best = candidate;
        }

        return best;
    }
}
