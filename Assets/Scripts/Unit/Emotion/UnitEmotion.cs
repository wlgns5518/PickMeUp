using System;
using System.Collections.Generic;
using UnityEngine;

// 원작 픽미업의 핵심 기믹인 감정 상태를 실제로 동작시키는 컴포넌트.
// CharacterTypes.cs의 EmotionState는 값만 선언돼 있고 아무 데서도 쓰이지 않았다.
// 여기서 공포 게이지를 굴리고 그 결과를 UnitController(스탯 배율/행동불가)에 넘긴다.
//
// 매 프레임 Tick을 돌면서도 상태가 바뀐 순간에만 OnStateChanged를 쏘는 이유:
// 구독자(에이전트 속도 갱신, HP바 갱신)가 프레임마다 네이티브 프로퍼티를 건드리지 않게 하려는 것.
[DisallowMultipleComponent]
public class UnitEmotion : MonoBehaviour
{
    [SerializeField] private EmotionProfile profile = new EmotionProfile();

#if UNITY_EDITOR
    // 인스펙터 확인용. 빌드에는 포함하지 않는다(UnitController의 currentStateName과 같은 이유).
    [SerializeField] private string currentEmotion;
    [SerializeField] private float fearGaugeDebug;
#endif

    private UnitController owner;
    private float fearGauge;
    private float panicTimer;
    private float bleedRemaining;
    private float bleedTickTimer;
    private EmotionState state;

    // 이 유닛의 감정이 바뀔 때 발생. UnitController가 에이전트 속도를 다시 계산하는 데 쓴다.
    public event Action<UnitEmotion> OnStateChanged;

    public EmotionProfile Profile => profile;
    public UnitController Owner => owner;
    public EmotionState State => state;
    public float FearGauge => fearGauge;
    public float FearRatio => Mathf.Clamp01(fearGauge / Mathf.Max(1f, profile.panicThreshold));
    public float StressRatio => Mathf.Clamp01(profile.stress / Mathf.Max(1f, profile.stressLimit));

    public bool Has(EmotionState flag) => (state & flag) != 0;

    // 패닉/빈사/붕괴는 스스로 행동을 결정할 수 없다. UnitController가 PanicState로 밀어넣는 조건.
    public bool IsActionBlocked => (state & EmotionState.ActionBlocking) != 0;

    // Fear일 때 공격력과 이동속도에 곱해지는 배율. 원작 설정은 모든 능력치 30% 감소.
    public float StatMultiplier => Has(EmotionState.Fear) ? profile.fearStatMultiplier : 1f;

    private void Awake()
    {
        if (owner == null) owner = GetComponent<UnitController>();
    }

    public void Initialize(UnitController unitOwner)
    {
        owner = unitOwner;
    }

    // 스포너가 CharacterSO를 전투 유닛에 얹을 때 호출. 히든 스탯을 감정 저항으로 옮긴다.
    public void Configure(HiddenStats hidden, int starCount)
    {
        if (hidden != null)
        {
            profile.mental = Mathf.Max(0, hidden.mental);
            profile.stress = Mathf.Max(0, hidden.stress);
        }

        // 1~2성은 멘탈이 약해 첫 전투에서 반드시 공포에 빠진다(CharacterRules.IsFragileMental).
        profile.fragileFirstBattle = CharacterRules.IsFragileMental(starCount);
        ResetForBattle();
    }

    public void ResetForBattle()
    {
        fearGauge = profile.fragileFirstBattle ? profile.fearThreshold : 0f;
        panicTimer = 0f;
        bleedRemaining = 0f;
        bleedTickTimer = 0f;
        state = EmotionState.None;
        RecomputeState();
    }

    // UnitController.Update에서 호출한다.
    public void Tick(float deltaTime)
    {
        if (owner == null) return;

        if (owner.IsDead)
        {
            if (state == EmotionState.None) return;
            state = EmotionState.None;
            NotifyChanged();
            return;
        }

        UpdateBleeding(deltaTime);
        if (owner.IsDead) return; // 출혈로 사망하면 이번 틱은 여기서 끝난다.

        UpdateFear(deltaTime);
        UpdatePanic(deltaTime);
        RecomputeState();
    }

    public void AddFear(float amount)
    {
        if (amount <= 0f || owner == null || owner.IsDead) return;

        fearGauge = Mathf.Clamp(fearGauge + amount * FearScale, 0f, profile.panicThreshold);
    }

    public void AddStress(float amount)
    {
        if (amount <= 0f) return;
        profile.stress = Mathf.Min(profile.stress + amount, profile.stressLimit);
    }

    public void ApplyBleeding()
    {
        bleedRemaining = Mathf.Max(bleedRemaining, profile.bleedDuration);
        if (bleedTickTimer <= 0f) bleedTickTimer = profile.bleedTickInterval;
    }

    // 피해를 입었을 때 UnitController가 호출. 잃은 HP 비율만큼 공포가 오르고,
    // 강타(스킬)에 맞으면 확률적으로 출혈이 걸린다.
    public void NotifyDamaged(int damage, bool fromSkill)
    {
        if (damage <= 0 || owner == null) return;

        float maxHp = Mathf.Max(1, owner.Stats.maxHp);
        float lostPercent = damage / maxHp * 100f;
        AddFear(lostPercent * profile.fearPerHpPercentLost);

        if (fromSkill && UnityEngine.Random.value < profile.bleedChanceOnSkillHit)
        {
            ApplyBleeding();
        }
    }

    // 아군이 눈앞에서 죽는 것을 목격하면 공포와 스트레스가 함께 오른다.
    // 시야 레이캐스트까지 보지 않고 거리만 보는 이유: 사망은 드문 이벤트지만 팀 전원을 훑기 때문에
    // 여기서 레이캐스트를 돌리면 전멸 구간에서 프레임이 튄다. 거리 판정으로 충분하다.
    public static void BroadcastAllyDeath(UnitController dead)
    {
        if (dead == null) return;

        IReadOnlyList<UnitController> team = UnitRegistry.GetTeam(dead.Team);
        Vector3 deathPosition = dead.transform.position;

        for (int i = team.Count - 1; i >= 0; i--)
        {
            UnitController witness = team[i];
            if (witness == null || witness == dead || witness.IsDead || !witness.isActiveAndEnabled) continue;

            UnitEmotion emotion = witness.Emotion;
            if (emotion == null) continue;

            float range = emotion.profile.allyDeathWitnessRange;
            if ((witness.transform.position - deathPosition).sqrMagnitude > range * range) continue;

            emotion.AddFear(emotion.profile.fearOnAllyDeath);
            emotion.AddStress(emotion.profile.stressPerAllyDeath);
        }
    }

    // mental이 높을수록 공포가 덜 오른다. 완전 면역이 되지 않도록 하한을 둔다.
    private float FearScale =>
        Mathf.Max(profile.minFearScale, 1f - profile.mental * profile.mentalResistPerPoint);

    private float RecoveryScale => 1f + profile.mental * profile.mentalResistPerPoint;

    private void UpdateFear(float deltaTime)
    {
        if (panicTimer > 0f) return; // 패닉 중에는 게이지를 건드리지 않는다.

        if (HpRatio <= profile.lowHpRatio)
        {
            AddFear(profile.lowHpFearPerSecond * deltaTime);
            return;
        }

        fearGauge = Mathf.Max(0f, fearGauge - profile.fearRecoveryPerSecond * RecoveryScale * deltaTime);
    }

    private void UpdatePanic(float deltaTime)
    {
        if (panicTimer > 0f)
        {
            panicTimer -= deltaTime;
            if (panicTimer > 0f) return;

            panicTimer = 0f;
            // 패닉이 풀린 직후 게이지를 임계값 아래로 내려 즉시 재패닉하지 않게 한다.
            fearGauge = Mathf.Min(profile.panicExitFear, profile.panicThreshold);
            return;
        }

        if (fearGauge < profile.panicThreshold) return;

        panicTimer = profile.panicDuration;
        AddStress(profile.stressPerPanic);
    }

    private void UpdateBleeding(float deltaTime)
    {
        if (bleedRemaining <= 0f) return;

        bleedRemaining -= deltaTime;
        bleedTickTimer -= deltaTime;
        if (bleedTickTimer > 0f) return;

        bleedTickTimer = profile.bleedTickInterval;
        int damage = Mathf.Max(1, Mathf.RoundToInt(owner.Stats.maxHp * profile.bleedDamageRatio));
        owner.TakeBleedDamage(damage);
    }

    private void RecomputeState()
    {
        EmotionState next = EmotionState.None;

        // 히스테리시스: fearThreshold를 넘으면 켜지고 fearClearThreshold 아래로 내려가야 꺼진다.
        // 임계값이 하나뿐이면 게이지가 경계에서 진동할 때 상태가 매 프레임 깜빡인다.
        bool wasFearful = (state & EmotionState.Fear) != 0;
        float threshold = wasFearful ? profile.fearClearThreshold : profile.fearThreshold;
        if (fearGauge >= threshold) next |= EmotionState.Fear;

        if (panicTimer > 0f) next |= EmotionState.Panic | EmotionState.Fear;
        if (bleedRemaining > 0f) next |= EmotionState.Bleeding;
        if (HpRatio < profile.dyingHpRatio) next |= EmotionState.Dying;
        if (profile.stress >= profile.stressLimit) next |= EmotionState.Broken;

        if (next == state) return;

        state = next;
        NotifyChanged();
    }

    private float HpRatio
    {
        get
        {
            if (owner == null) return 1f;
            return owner.Stats.currentHp / Mathf.Max(1f, owner.Stats.maxHp);
        }
    }

    private void NotifyChanged()
    {
#if UNITY_EDITOR
        currentEmotion = state.ToString();
        fearGaugeDebug = fearGauge;
#endif
        OnStateChanged?.Invoke(this);
    }

    // UI 표시용 한국어 라벨.
    public static string Korean(EmotionState flag)
    {
        switch (flag)
        {
            case EmotionState.Fear: return "공포";
            case EmotionState.Panic: return "패닉";
            case EmotionState.Bleeding: return "출혈";
            case EmotionState.Dying: return "빈사";
            case EmotionState.Broken: return "붕괴";
            default: return "";
        }
    }
}
