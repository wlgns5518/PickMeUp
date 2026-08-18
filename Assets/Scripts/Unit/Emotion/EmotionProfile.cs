using System;
using UnityEngine;

// 감정 시스템 튜닝값. UnitStats와 같은 자리(유닛 인스펙터)에서 조정하고,
// 스포너가 CharacterSO의 히든 스탯으로 덮어쓴다.
[Serializable]
public class EmotionProfile
{
    [Header("Mental (저항)")]
    [Tooltip("높을수록 공포가 느리게 쌓이고 빠르게 회복된다. 히든 스탯 mental에서 주입된다.")]
    [Min(0)] public int mental = 10;
    [Tooltip("mental 1당 공포 상승률이 줄어드는 비율. 0.04면 mental 10에서 상승량 -40%.")]
    [Range(0f, 0.1f)] public float mentalResistPerPoint = 0.04f;
    [Tooltip("저항으로 줄일 수 있는 공포 상승량의 하한(0.3 = 최대 70%까지만 경감).")]
    [Range(0.05f, 1f)] public float minFearScale = 0.3f;

    [Header("Fear / Panic 임계값")]
    [Tooltip("공포 게이지가 이 값을 넘으면 Fear — 모든 능력치가 감소한다.")]
    public float fearThreshold = 40f;
    [Tooltip("이 값을 넘으면 Panic — 행동불가.")]
    public float panicThreshold = 85f;
    [Tooltip("Fear 상태에서 공격력/이동속도에 곱해지는 배율(원작 설정: 30% 감소).")]
    [Range(0.1f, 1f)] public float fearStatMultiplier = 0.7f;
    [Tooltip("Fear에서 벗어나는 게이지. 임계값 근처에서 상태가 떨리는 것을 막는 히스테리시스.")]
    public float fearClearThreshold = 25f;
    [Tooltip("초당 자연 회복량. mental이 높을수록 빨라진다.")]
    public float fearRecoveryPerSecond = 6f;

    [Header("Fear 유발량")]
    [Tooltip("최대 HP의 1%를 잃을 때마다 오르는 공포량.")]
    public float fearPerHpPercentLost = 0.8f;
    [Tooltip("시야 안에서 아군이 죽는 것을 목격했을 때 오르는 공포량.")]
    public float fearOnAllyDeath = 25f;
    [Tooltip("아군의 죽음을 목격했다고 판정하는 거리.")]
    public float allyDeathWitnessRange = 12f;
    [Tooltip("HP가 낮게 유지되는 동안 초당 오르는 공포량.")]
    public float lowHpFearPerSecond = 10f;
    [Range(0f, 1f)] public float lowHpRatio = 0.3f;

    [Header("Panic")]
    public float panicDuration = 2.5f;
    [Tooltip("패닉이 풀린 직후 남는 공포 게이지. panicThreshold보다 낮아야 즉시 재패닉하지 않는다.")]
    public float panicExitFear = 55f;

    [Header("Bleeding")]
    public float bleedTickInterval = 1f;
    [Tooltip("출혈 틱마다 잃는 최대 HP 비율.")]
    [Range(0f, 0.5f)] public float bleedDamageRatio = 0.03f;
    public float bleedDuration = 6f;
    [Tooltip("스킬(강타)에 맞았을 때 출혈이 걸릴 확률.")]
    [Range(0f, 1f)] public float bleedChanceOnSkillHit = 0.5f;

    [Header("Dying")]
    [Tooltip("HP가 이 비율 미만이면 빈사 — 행동불가(원작 설정: 3%).")]
    [Range(0f, 0.5f)] public float dyingHpRatio = 0.03f;

    [Header("Stress")]
    [Tooltip("누적 스트레스. 히든 스탯 stress에서 주입된다.")]
    public float stress;
    [Tooltip("이 값을 넘으면 정신이 붕괴(Broken)해 전투를 지속할 수 없다.")]
    public float stressLimit = 100f;
    [Tooltip("붕괴가 지속되는 시간. 스트레스는 전투 중 줄어들지 않으므로 이 시간이 없으면 " +
             "한 번 한계치에 닿은 캐릭터가 남은 전투 내내 행동불능으로 굳는다.")]
    public float brokenDuration = 8f;
    [Tooltip("붕괴에서 벗어난 직후 남는 스트레스. stressLimit보다 낮아야 즉시 다시 무너지지 않는다.")]
    public float brokenExitStress = 70f;
    // 붕괴는 회복되지 않는 영구 행동불능이라 한 전투에서 여러 명이 무너지면 게임이 성립하지 않는다.
    // 실플레이 검증에서 25초 전투에 5명 중 2명이 붕괴해 값을 낮췄다 — 붕괴는 소모전 끝에만 나와야 한다.
    public float stressPerPanic = 5f;
    [Tooltip("아군 사망 목격 시 누적되는 스트레스.")]
    public float stressPerAllyDeath = 6f;

    [Header("First Battle")]
    [Tooltip("1~2성처럼 멘탈이 약한 캐릭터는 첫 전투에서 반드시 공포에 빠진다(원작 설정).")]
    public bool fragileFirstBattle;
}
