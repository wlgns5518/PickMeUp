using System;
using System.Collections.Generic;
using UnityEngine;

// 지금 진행 중인 전투. HUD·카메라·지휘 입력이 이것으로 본다(GameServices.Battle).
public interface IBattleSession
{
    bool IsRunning { get; }

    // 전투 시작 시점의 아군 명단. 개수와 순서가 전투 내내 고정된다(BattleManager.allyRoster 주석 참조).
    IReadOnlyList<UnitController> AllyRoster { get; }

    BattleResult Result { get; }
}

// 전투가 시작되고 끝났다는 알림.
//
// 정적 이벤트인 이유: 인스턴스 이벤트로 두면 구독자가 전투 진행자를 먼저 찾아야 해서
// Awake/Start 순서에 묶이고, 스크립트를 고쳐 도메인 리로드가 일어나면(플레이 중 흔한 일)
// 구독이 통째로 끊긴 채 복구되지 않는다. 정적 이벤트 + OnEnable 구독이면 리로드 후에도 다시 붙는다.
//
// 예전에는 이 이벤트가 BattleManager에 붙어 있어서, 알림만 들으려는 HUD도 전투 진행자(구체 클래스)를 알아야 했다.
public static class BattleEvents
{
    public static event Action Started;
    public static event Action<BattleResult> Ended;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        Started = null;
        Ended = null;
    }

    internal static void RaiseStarted() => Started?.Invoke();

    internal static void RaiseEnded(BattleResult result) => Ended?.Invoke(result);
}
