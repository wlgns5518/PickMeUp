using System.Collections.Generic;
using UnityEngine;

// 둘레를 훑는 질의 — 가장 가까운 우리 편, 어느 원 안의 적(세기·모으기·무게중심), 나를 쫓는 적.
public static partial class UnitRegistry
{
    // 가장 가까운 우리 편. "전선에서 떨어져 나왔는가"를 재는 기준이다.
    //
    // 팀의 무게중심으로 재 봤더니 쓸 수 없었다. 실측에서 정상적으로 대형을 이룬 탱커가 중심에서
    // 6.4m, 마법사가 9.5m로 나와, 혼자 도망친 궁수(16.4m)와 구분되지 않았다 — 파티가 넓게
    // 퍼져 싸우는 것이 정상이라 중심까지의 거리는 이탈을 뜻하지 않는다. 게다가 파티가 두 무리로
    // 갈리면 중심이 그 사이 빈 공간에 놓여 전원이 이탈로 잡힌다.
    //
    // 최근접 아군까지의 거리는 그 둘을 깨끗하게 가른다: 같은 실측에서 대형 안의 유닛은 1.1~3.3m,
    // 도망친 궁수만 14.7m였다. "옆에 아무도 없다"가 곧 떨어져 나왔다는 뜻이기 때문이다.
    public static UnitController FindNearestAlly(UnitController self)
    {
        if (self == null) return null;

        List<UnitController> list = GetList(self.Team);
        Vector3 origin = self.transform.position;

        UnitController best = null;
        float bestSqr = float.MaxValue;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController candidate = list[i];
            if (candidate == null || candidate == self || candidate.IsDead || !candidate.isActiveAndEnabled) continue;

            float sqr = (candidate.transform.position - origin).sqrMagnitude;
            if (sqr >= bestSqr) continue;

            bestSqr = sqr;
            best = candidate;
        }

        return best;
    }

    // 어느 지점을 중심으로 한 원 안의 적 전부. 광역 마법의 착탄 판정이 이걸 쓴다.
    //
    // FindEnemiesInRange와 나눠 둔 이유는 중심이 다르기 때문이다. 저쪽은 "내 주위"를 재고
    // 이쪽은 "마법이 떨어지는 자리"를 잰다 — 마법사는 7.5m 밖에서 쏘므로 그 둘은 전혀 다른 원이다.
    public static void FindEnemiesAround(UnitController requester, Vector3 center, float radius, List<UnitController> results)
    {
        if (results == null) return;
        results.Clear();
        if (requester == null) return;

        GetHostileLists(requester.Team, out List<UnitController> first, out List<UnitController> second);
        AddEnemiesAround(requester, first, center, radius, results);
        AddEnemiesAround(requester, second, center, radius, results);
    }

    // 어느 지점 주위에 있는 적들의 무게중심. 없으면 false.
    //
    // "무엇으로부터 물러날 것인가"를 정하는 데 쓴다. 겨누고 있는 상대 하나가 아니라 실제로
    // 품 안에 들어온 적들을 기준으로 잡아야 도망 방향이 안정된다 — 표적이 바뀔 때마다
    // 방향이 홱 도는 것을 막는 것이 목적이다.
    public static bool TryGetEnemyCentroidAround(UnitController requester, Vector3 center, float radius,
        out Vector3 centroid)
    {
        centroid = Vector3.zero;
        if (requester == null) return false;

        GetHostileLists(requester.Team, out List<UnitController> first, out List<UnitController> second);

        Vector3 sum = Vector3.zero;
        int count = 0;
        AccumulateCentroid(requester, first, center, radius, ref sum, ref count);
        AccumulateCentroid(requester, second, center, radius, ref sum, ref count);

        // 엔티티가 된 적도 같은 합에 더한다. 세계마다 무게중심을 따로 내서 둘을 다시 평균 내면
        // 마리 수가 적은 쪽이 과대평가돼, 고블린 스무 마리를 등지고 게임오브젝트 하나 쪽으로
        // 물러나는 그림이 나온다.
        if (SeesEnemyEntities(requester)) EnemyWorldBridge.AccumulateCentroidAround(center, radius, ref sum, ref count);

        if (count == 0) return false;

        centroid = sum / count;
        return true;
    }

    private static void AccumulateCentroid(UnitController requester, List<UnitController> list,
        Vector3 center, float radius, ref Vector3 sum, ref int count)
    {
        float radiusSqr = radius * radius;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController candidate = list[i];
            if (!IsValidTarget(requester, candidate)) continue;
            if (!AreEnemies(requester, candidate)) continue;

            Vector3 offset = candidate.transform.position - center;
            offset.y = 0f;
            if (offset.sqrMagnitude > radiusSqr) continue;

            sum += candidate.transform.position;
            count++;
        }
    }

    // 나를 노리고 쫓아오는 적이 이 거리 안에 있는가.
    //
    // "간격 안에 들어왔는가"(CountEnemiesAround)와는 다른 질문이다. 그쪽은 이미 붙은 상태를 재고,
    // 이쪽은 아직 멀어도 나를 쫓고 있는 중인지를 본다 — 달아나기를 언제 멈출지 정하는 기준이다.
    public static bool HasEnemyChasing(UnitController self, float range)
    {
        if (self == null) return false;

        GetHostileLists(self.Team, out List<UnitController> first, out List<UnitController> second);
        if (HasChaserInList(self, first, range) || HasChaserInList(self, second, range)) return true;

        // 엔티티가 된 적도 함께 본다. 이게 없으면 원거리 유닛이 고블린 무리에게 쫓기는 동안
        // "아무도 나를 안 쫓는다"고 읽고 달아나기를 멈춘다 — 그대로 붙잡힌다.
        int allyIndex = EnemyWorldBridge.IndexOfAlly(self);
        return allyIndex >= 0 && EnemyWorldBridge.HasEnemyChasing(allyIndex, self.transform.position, range);
    }

    private static bool HasChaserInList(UnitController self, List<UnitController> list, float range)
    {
        float rangeSqr = range * range;
        Vector3 origin = self.transform.position;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController candidate = list[i];
            if (candidate == null || candidate.IsDead || !candidate.isActiveAndEnabled) continue;
            if (candidate.CurrentTarget != self) continue;

            Vector3 offset = candidate.transform.position - origin;
            offset.y = 0f;
            if (offset.sqrMagnitude <= rangeSqr) return true;
        }

        return false;
    }

    // 세지만 담지는 않는다. "이 자리에 광역기를 쓸 만한가"를 판단할 때 쓰는 값이라
    // 목록까지 만들면 매 판단마다 리스트가 채워졌다 비워진다.
    public static int CountEnemiesAround(UnitController requester, Vector3 center, float radius)
    {
        if (requester == null) return 0;

        GetHostileLists(requester.Team, out List<UnitController> first, out List<UnitController> second);
        int count = CountEnemiesAroundInList(requester, first, center, radius)
                  + CountEnemiesAroundInList(requester, second, center, radius);

        // 엔티티가 된 적도 센다. 이 한 줄에 걸려 있는 것이 많다 — 마법사의 "붙잡혔는가"
        // (ShouldKeepDistance), 암살자의 빠지기 조건과 은신, 광역 마법의 착탄 지점,
        // 발놀림이 파고들지 말지까지 전부 이 값을 읽는다.
        //
        // 은신이 특히 위험했다. IsStealthed가 "둘레에 적이 없는가"인데 엔티티를 세지 않으면
        // 언제나 참이 되어, 고블린 무리 한복판에서도 암살자가 계속 그림자에 들어 있었다.
        if (SeesEnemyEntities(requester)) count += EnemyWorldBridge.CountEnemiesAround(center, radius);

        return count;
    }

    // 어느 지점 둘레에 있는 적을 모은다. 손잡이로 받으므로 엔티티도 함께 담긴다 —
    // 광역 마법이 실제로 때릴 상대를 고르는 자리다(UnitController.Magic).
    public static void FindEnemiesAround(UnitController requester, Vector3 center, float radius,
        List<TargetRef> results)
    {
        if (results == null) return;
        results.Clear();

        if (requester == null) return;

        GetHostileLists(requester.Team, out List<UnitController> first, out List<UnitController> second);
        AddEnemiesAround(requester, first, center, radius, results);
        AddEnemiesAround(requester, second, center, radius, results);
        AppendEnemyEntities(requester, center, radius, results);
    }

    // 사거리 안의 적을 모은다. 위와 같은 이유로 손잡이로 받는다.
    public static void FindEnemiesInRange(UnitController requester, float range, List<TargetRef> results)
    {
        if (results == null) return;
        results.Clear();

        if (requester == null) return;

        FindEnemiesAround(requester, requester.transform.position, range, results);
    }

    // 엔티티를 손잡이로 감싸 목록에 더한다. 브리지는 ECS 용어(Entity)로만 답하므로
    // 여기서 한 번 갈아 끼운다. 버퍼를 정적으로 두는 것은 매 호출 할당을 피하려는 것이고,
    // 이 경로는 전부 메인 스레드에서만 불린다.
    private static readonly List<Unity.Entities.Entity> entityBuffer = new List<Unity.Entities.Entity>(64);

    private static void AppendEnemyEntities(UnitController requester, Vector3 center, float radius,
        List<TargetRef> results)
    {
        if (!SeesEnemyEntities(requester)) return;

        entityBuffer.Clear();
        EnemyWorldBridge.AppendEnemiesAround(center, radius, entityBuffer);
        for (int i = 0; i < entityBuffer.Count; i++) results.Add(new TargetRef(entityBuffer[i]));
        entityBuffer.Clear();
    }

    private static void AddEnemiesAround(UnitController requester, List<UnitController> list,
        Vector3 center, float radius, List<UnitController> results)
    {
        float radiusSqr = radius * radius;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController candidate = list[i];
            if (!IsValidTarget(requester, candidate)) continue;
            if (!AreEnemies(requester, candidate)) continue;

            Vector3 offset = candidate.transform.position - center;
            offset.y = 0f;
            if (offset.sqrMagnitude <= radiusSqr) results.Add(candidate);
        }
    }

    private static void AddEnemiesAround(UnitController requester, List<UnitController> list,
        Vector3 center, float radius, List<TargetRef> results)
    {
        float radiusSqr = radius * radius;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController candidate = list[i];
            if (!IsValidTarget(requester, candidate)) continue;
            if (!AreEnemies(requester, candidate)) continue;

            Vector3 offset = candidate.transform.position - center;
            offset.y = 0f;
            if (offset.sqrMagnitude <= radiusSqr) results.Add(candidate);
        }
    }

    private static int CountEnemiesAroundInList(UnitController requester, List<UnitController> list,
        Vector3 center, float radius)
    {
        float radiusSqr = radius * radius;
        int count = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController candidate = list[i];
            if (!IsValidTarget(requester, candidate)) continue;
            if (!AreEnemies(requester, candidate)) continue;

            Vector3 offset = candidate.transform.position - center;
            offset.y = 0f;
            if (offset.sqrMagnitude <= radiusSqr) count++;
        }

        return count;
    }

    public static void FindEnemiesInRange(UnitController requester, float range, List<UnitController> results)
    {
        if (results == null) return;
        results.Clear();

        if (requester == null) return;

        GetHostileLists(requester.Team, out List<UnitController> first, out List<UnitController> second);
        AddEnemiesInRange(requester, first, range, results);
        AddEnemiesInRange(requester, second, range, results);
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
}
