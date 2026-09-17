using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 지휘관이 파티 전체에 내리는 명령.
public enum PartyOrder : byte
{
    // 명령 없음. 각자 판단대로 싸운다.
    Engage,

    // 정해 준 자리까지 물러난다. 닿으면 그 자리를 지킨다(Hold로 넘어간다).
    Retreat,

    // 지금 선 자리를 지킨다. 손 닿는 적과만 싸우고 쫓아 나가지 않는다.
    Hold,
}

// 지휘관(플레이어)의 명령을 파티원 한 명 한 명에게 나눠 주는 자리.
//
// 원작의 지휘관은 파티원을 조종하지 않는다. "저놈부터", "물러나", "버텨"를 외칠 뿐이고 칼을 어떻게
// 휘두를지는 각자가 정한다. 그래서 여기서는 동작을 지목하지 않고 값만 내려보낸다 — 집중 표적은
// 각자의 주 표적(UnitController.CurrentTarget) 자리에, 후퇴·진형은 각자의 명령 칸에 들어가고,
// 그 칸을 보고 무엇을 할지는 행동 트리의 우선순위가 정한다(UnitBehaviorTree).
//
// 정적 클래스인 이유는 UnitRegistry와 같다. 전투 하나에 파티 하나라 인스턴스가 둘일 일이 없고,
// 입력(PartyCommandInput)·HUD·유닛이 서로를 찾지 않고 같은 값을 본다.
public static class PartyCommand
{
    public static PartyOrder Order { get; private set; }

    // 지휘관이 찍은 표적. 쓰러지면 스스로 풀린다(Tick).
    public static TargetRef FocusTarget { get; private set; }

    // 명령이 바뀌었다. HUD가 글자를 다시 쓰는 데만 쓴다 — 매 프레임 문자열을 만들지 않으려고.
    public static event Action Changed;

    // 후퇴할 때 무리 안에서의 제자리를 얼마까지 살릴 것인가(미터). 전원이 한 점으로 달려가면
    // 도착하자마자 서로 밀치느라 진형이 아니라 덩어리가 된다. 너무 넓으면 흩어진 채로 물러난다.
    private const float MaxSpreadOnRetreat = 3f;

    // 적의 무게중심을 잴 반경. 이보다 먼 적은 "지금 물러나야 할 상대"가 아니다.
    private const float ThreatRadius = 20f;

    private static readonly List<UnitController> Buffer = new List<UnitController>(16);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        Order = PartyOrder.Engage;
        FocusTarget = TargetRef.None;
        Changed = null;
    }

    // 전투를 새로 열 때. 지난 판의 명령이 남아 있으면 첫 프레임부터 파티가 물러난다.
    public static void Reset()
    {
        Order = PartyOrder.Engage;
        FocusTarget = TargetRef.None;
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- 명령

    // 집중 공격. 파티원 전원의 주 표적을 이 적으로 갈아 끼운다.
    public static bool FocusFire(TargetRef target)
    {
        if (!target.IsAlive) return false;

        FocusTarget = target;

        IReadOnlyList<UnitController> allies = UnitRegistry.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            UnitController ally = allies[i];
            if (ally != null && !ally.IsDead) ally.ReceiveFocusCommand(target);
        }

        Changed?.Invoke();
        return true;
    }

    public static void ClearFocus()
    {
        if (!FocusTarget.Exists) return;

        FocusTarget = TargetRef.None;

        IReadOnlyList<UnitController> allies = UnitRegistry.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] != null) allies[i].ClearFocusCommand();
        }

        Changed?.Invoke();
    }

    // 긴급 후퇴. 적의 무게중심 반대쪽으로 distance만큼 물러난 자리를 파티의 집결지로 삼는다.
    //
    // 각자에게는 집결지에 "지금 무리 안에서 서 있는 자리"를 더해 준다. 앞/뒤/좌/우를 새로 나눠 주는
    // 것이 아니라 이미 서 있던 간격을 그대로 들고 물러나는 것이다.
    public static bool OrderRetreat(float distance)
    {
        if (!TryGetLivingAllies(out Vector3 centroid)) return false;

        Vector3 away = ResolveRetreatDirection(centroid);
        Vector3 rally = centroid + away * Mathf.Max(1f, distance);
        if (NavMesh.SamplePosition(rally, out NavMeshHit rallyHit, 6f, NavMesh.AllAreas)) rally = rallyHit.position;
        else rally = centroid;

        for (int i = 0; i < Buffer.Count; i++)
        {
            UnitController ally = Buffer[i];

            Vector3 offset = ally.transform.position - centroid;
            offset.y = 0f;
            offset = Vector3.ClampMagnitude(offset, MaxSpreadOnRetreat);

            Vector3 destination = rally + offset;
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 2f, NavMesh.AllAreas)) destination = hit.position;
            else destination = rally;

            ally.ReceiveRetreatCommand(destination);
        }

        Buffer.Clear();
        Order = PartyOrder.Retreat;
        Changed?.Invoke();
        return true;
    }

    // 진형 유지. 각자 지금 선 자리를 지킨다.
    public static bool OrderHold()
    {
        if (!TryGetLivingAllies(out _)) return false;

        for (int i = 0; i < Buffer.Count; i++) Buffer[i].ReceiveHoldCommand();

        Buffer.Clear();
        Order = PartyOrder.Hold;
        Changed?.Invoke();
        return true;
    }

    // 명령을 거두고 각자 판단으로 돌려보낸다. 집중 표적은 그대로 둔다 — "버텨"를 거둔다고
    // "저놈부터"까지 거둔 것은 아니다.
    public static void Resume()
    {
        IReadOnlyList<UnitController> allies = UnitRegistry.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] != null) allies[i].ClearOrder();
        }

        Order = PartyOrder.Engage;
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- 매 프레임

    // 표적이 쓰러졌으면 집중을 풀고, 후퇴가 전원 끝났으면 진형 유지로 넘긴다.
    public static void Tick()
    {
        if (FocusTarget.Exists && !FocusTarget.IsAlive)
        {
            FocusTarget = TargetRef.None;
            Changed?.Invoke();
        }

        if (Order != PartyOrder.Retreat) return;

        IReadOnlyList<UnitController> allies = UnitRegistry.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            UnitController ally = allies[i];
            if (ally != null && !ally.IsDead && ally.IsRetreatOrdered) return;
        }

        Order = PartyOrder.Hold;
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- 내부

    private static bool TryGetLivingAllies(out Vector3 centroid)
    {
        Buffer.Clear();
        centroid = Vector3.zero;

        IReadOnlyList<UnitController> allies = UnitRegistry.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            UnitController ally = allies[i];
            if (ally == null || ally.IsDead || !ally.isActiveAndEnabled) continue;

            Buffer.Add(ally);
            centroid += ally.transform.position;
        }

        if (Buffer.Count == 0) return false;

        centroid /= Buffer.Count;
        return true;
    }

    // 물러날 방향. 가까이 있는 적 무리의 반대쪽이다. 적이 보이지 않으면 파티가 보고 있는 쪽의 반대로 간다.
    private static Vector3 ResolveRetreatDirection(Vector3 centroid)
    {
        if (UnitRegistry.TryGetEnemyCentroidAround(Buffer[0], centroid, ThreatRadius, out Vector3 enemies))
        {
            Vector3 away = centroid - enemies;
            away.y = 0f;
            if (away.sqrMagnitude > 0.01f) return away.normalized;
        }

        Vector3 facing = Vector3.zero;
        for (int i = 0; i < Buffer.Count; i++) facing += Buffer[i].transform.forward;
        facing.y = 0f;

        return facing.sqrMagnitude > 0.01f ? -facing.normalized : Vector3.back;
    }
}
