using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 지휘관 명령의 실제 상태와 규칙(PartyCommand 주석 참조).
//
// 명령은 전투 하나가 쓰는 값이라 플레이를 새로 시작할 때마다 GameServices가 새로 만든다.
// 파티원 명단은 생성자로 받는다 — 기본은 UnitRegistry.Allies이고, 시험에서는 임의의 목록을 줄 수 있다.
public interface IPartyCommander
{
    PartyOrder Order { get; }
    TargetRef FocusTarget { get; }
    event Action Changed;

    void Reset();
    bool FocusFire(TargetRef target);
    void ClearFocus();
    bool OrderRetreat(float distance);
    bool OrderHold();
    void Resume();
    void Tick();
}

public sealed class PartyCommander : IPartyCommander
{
    // 후퇴할 때 무리 안에서의 제자리를 얼마까지 살릴 것인가(미터). 전원이 한 점으로 달려가면
    // 도착하자마자 서로 밀치느라 진형이 아니라 덩어리가 된다. 너무 넓으면 흩어진 채로 물러난다.
    private const float MaxSpreadOnRetreat = 3f;

    // 적의 무게중심을 잴 반경. 이보다 먼 적은 "지금 물러나야 할 상대"가 아니다.
    private const float ThreatRadius = 20f;

    private readonly Func<IReadOnlyList<UnitController>> allies;
    private readonly List<UnitController> buffer = new List<UnitController>(16);

    public PartyCommander(Func<IReadOnlyList<UnitController>> allies = null)
    {
        this.allies = allies ?? (() => UnitRegistry.Allies);
    }

    public PartyOrder Order { get; private set; }

    // 지휘관이 찍은 표적. 쓰러지면 스스로 풀린다(Tick).
    public TargetRef FocusTarget { get; private set; }

    // 명령이 바뀌었다. HUD가 글자를 다시 쓰는 데만 쓴다 — 매 프레임 문자열을 만들지 않으려고.
    public event Action Changed;

    // 전투를 새로 열 때. 지난 판의 명령이 남아 있으면 첫 프레임부터 파티가 물러난다.
    public void Reset()
    {
        Order = PartyOrder.Engage;
        FocusTarget = TargetRef.None;
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- 명령

    // 집중 공격. 파티원 전원의 주 표적을 이 적으로 갈아 끼운다.
    public bool FocusFire(TargetRef target)
    {
        if (!target.IsAlive) return false;

        FocusTarget = target;

        IReadOnlyList<UnitController> party = allies();
        for (int i = 0; i < party.Count; i++)
        {
            UnitController ally = party[i];
            if (ally != null && !ally.IsDead) ally.ReceiveFocusCommand(target);
        }

        Changed?.Invoke();
        return true;
    }

    public void ClearFocus()
    {
        if (!FocusTarget.Exists) return;

        FocusTarget = TargetRef.None;

        IReadOnlyList<UnitController> party = allies();
        for (int i = 0; i < party.Count; i++)
        {
            if (party[i] != null) party[i].ClearFocusCommand();
        }

        Changed?.Invoke();
    }

    // 긴급 후퇴. 적의 무게중심 반대쪽으로 distance만큼 물러난 자리를 파티의 집결지로 삼는다.
    //
    // 각자에게는 집결지에 "지금 무리 안에서 서 있는 자리"를 더해 준다. 앞/뒤/좌/우를 새로 나눠 주는
    // 것이 아니라 이미 서 있던 간격을 그대로 들고 물러나는 것이다.
    public bool OrderRetreat(float distance)
    {
        if (!TryGetLivingAllies(out Vector3 centroid)) return false;

        Vector3 away = ResolveRetreatDirection(centroid);
        Vector3 rally = centroid + away * Mathf.Max(1f, distance);
        if (NavMesh.SamplePosition(rally, out NavMeshHit rallyHit, 6f, NavMesh.AllAreas)) rally = rallyHit.position;
        else rally = centroid;

        for (int i = 0; i < buffer.Count; i++)
        {
            UnitController ally = buffer[i];

            Vector3 offset = ally.transform.position - centroid;
            offset.y = 0f;
            offset = Vector3.ClampMagnitude(offset, MaxSpreadOnRetreat);

            Vector3 destination = rally + offset;
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 2f, NavMesh.AllAreas)) destination = hit.position;
            else destination = rally;

            ally.ReceiveRetreatCommand(destination);
        }

        buffer.Clear();
        Order = PartyOrder.Retreat;
        Changed?.Invoke();
        return true;
    }

    // 진형 유지. 각자 지금 선 자리를 지킨다.
    public bool OrderHold()
    {
        if (!TryGetLivingAllies(out _)) return false;

        for (int i = 0; i < buffer.Count; i++) buffer[i].ReceiveHoldCommand();

        buffer.Clear();
        Order = PartyOrder.Hold;
        Changed?.Invoke();
        return true;
    }

    // 명령을 거두고 각자 판단으로 돌려보낸다. 집중 표적은 그대로 둔다 — "버텨"를 거둔다고
    // "저놈부터"까지 거둔 것은 아니다.
    public void Resume()
    {
        IReadOnlyList<UnitController> party = allies();
        for (int i = 0; i < party.Count; i++)
        {
            if (party[i] != null) party[i].ClearOrder();
        }

        Order = PartyOrder.Engage;
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- 매 프레임

    // 표적이 쓰러졌으면 집중을 풀고, 후퇴가 전원 끝났으면 진형 유지로 넘긴다.
    public void Tick()
    {
        if (FocusTarget.Exists && !FocusTarget.IsAlive)
        {
            FocusTarget = TargetRef.None;
            Changed?.Invoke();
        }

        if (Order != PartyOrder.Retreat) return;

        IReadOnlyList<UnitController> party = allies();
        for (int i = 0; i < party.Count; i++)
        {
            UnitController ally = party[i];
            if (ally != null && !ally.IsDead && ally.IsRetreatOrdered) return;
        }

        Order = PartyOrder.Hold;
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- 내부

    private bool TryGetLivingAllies(out Vector3 centroid)
    {
        buffer.Clear();
        centroid = Vector3.zero;

        IReadOnlyList<UnitController> party = allies();
        for (int i = 0; i < party.Count; i++)
        {
            UnitController ally = party[i];
            if (ally == null || ally.IsDead || !ally.isActiveAndEnabled) continue;

            buffer.Add(ally);
            centroid += ally.transform.position;
        }

        if (buffer.Count == 0) return false;

        centroid /= buffer.Count;
        return true;
    }

    // 물러날 방향. 가까이 있는 적 무리의 반대쪽이다. 적이 보이지 않으면 파티가 보고 있는 쪽의 반대로 간다.
    private Vector3 ResolveRetreatDirection(Vector3 centroid)
    {
        if (UnitRegistry.TryGetEnemyCentroidAround(buffer[0], centroid, ThreatRadius, out Vector3 enemies))
        {
            Vector3 away = centroid - enemies;
            away.y = 0f;
            if (away.sqrMagnitude > 0.01f) return away.normalized;
        }

        Vector3 facing = Vector3.zero;
        for (int i = 0; i < buffer.Count; i++) facing += buffer[i].transform.forward;
        facing.y = 0f;

        return facing.sqrMagnitude > 0.01f ? -facing.normalized : Vector3.back;
    }
}
