using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
// System을 열면 Random이 System.Random과 충돌한다. 이 파일의 Random은 전부 UnityEngine 쪽.
using Random = UnityEngine.Random;

[RequireComponent(typeof(TargetScanner))]
public class UnitController : MonoBehaviour
{
    [Header("Team")]
    [SerializeField] private UnitTeam team;

    [Header("Stats")]
    [SerializeField] private UnitStats stats = new UnitStats();

    [Header("Components")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Animator animator;
    [SerializeField] private TargetScanner scanner;
    [SerializeField] private Collider bodyCollider;
    [SerializeField] private UnitEmotion emotion;

[Header("Blood VFX")]
    [SerializeField] private GameObject[] bloodEffectPrefabs;
    [SerializeField] private Vector3 bloodEffectOffset = new Vector3(0f, 1f, 0f);

        [Header("Movement")]
    [SerializeField] private float runDistance = 4f;
    [SerializeField] private float destinationUpdateInterval = 0.15f;
    [SerializeField] private float chasePredictionTime = 0.25f;
    [SerializeField] private float rotationSpeed = 720f;

    [Header("Ranged")]
    [Tooltip("사거리가 이 값 이상인 유닛만 원거리로 취급해 거리를 벌린다. 근접 유닛은 해당 없음.")]
    [SerializeField] private float minKeepDistanceRange = 4f;
    [Tooltip("사거리의 이 비율 안쪽까지 적이 붙으면 물러선다.")]
    [SerializeField, Range(0f, 1f)] private float keepDistanceRatio = 0.5f;

    [Header("Targeting")]
    [SerializeField] private float targetChangeInterval = 0.5f;
    [SerializeField, Range(0f, 1f)] private float targetSwitchDistanceRatio = 0.75f;

    [Header("Roaming")]
    [SerializeField] private bool roamWhenSearching = true;
    [SerializeField] private float roamRadius = 5f;
    [SerializeField] private float roamInterval = 2f;
    [SerializeField, Range(0f, 1f)] private float roamDirectionWeight = 0.7f;

    [Header("Animator State Names")]
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string walkStateName = "Walk";
    [SerializeField] private string runStateName = "Run";
    [SerializeField] private string jumpStateName = "";
    [SerializeField] private string attackStateName = "Attack";
    [SerializeField] private string skillStateName = "Kick";
    [SerializeField] private string blockStateName = "Block";
    [Tooltip("회복약을 마시는 모션. 비워두면 Idle로 대체된다.")]
    [SerializeField] private string potionStateName = "";
    [SerializeField] private string hitStateName = "Hit";
    [SerializeField] private string deathStateName = "Death";
    [SerializeField] private float animationFadeDuration = 0.08f;
    [Tooltip("화면 밖 유닛의 애니메이션 계산량을 줄인다. AlwaysAnimate는 보이지 않아도 전부 계산한다.")]
    [SerializeField] private AnimatorCullingMode animatorCullingMode = AnimatorCullingMode.CullUpdateTransforms;
    [Tooltip("사망 애니메이션이 끝나면 Animator를 꺼서 시체가 계속 애니메이션되지 않도록 한다.")]
    [SerializeField] private bool disableAnimatorAfterDeath = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs;
#if UNITY_EDITOR
    // 인스펙터 확인용. GetType().Name / GameObject.name은 호출할 때마다 문자열을 새로 만들기 때문에
    // 상태·타깃이 바뀔 때마다 GC 쓰레기가 쌓인다. 빌드에는 포함하지 않는다.
    [SerializeField] private string currentStateName;
    [SerializeField] private string currentTargetName;
#endif

    private StateMachine<UnitController> stateMachine;

    public IdleState IdleState { get; private set; }
    public SearchState SearchState { get; private set; }
    public ChaseState ChaseState { get; private set; }
    public MoveState MoveState { get; private set; }
    public AttackState AttackState { get; private set; }
    public SkillState SkillState { get; private set; }
    public EvadeState EvadeState { get; private set; }
    public BlockState BlockState { get; private set; }
    public HitState HitState { get; private set; }
    public DeadState DeadState { get; private set; }
    public PanicState PanicState { get; private set; }
    public PotionState PotionState { get; private set; }
    public HealState HealState { get; private set; }

    public UnitTeam Team => team;
    public UnitEmotion Emotion => emotion;
    // 이 전투 유닛이 어떤 로스터 캐릭터인지. 사망 시 영구 사망 처리에 쓴다.
    public CharacterSO SourceCharacter { get; private set; }

    // 전투 기여도 — MVP 선정과 경험치 정산이 이 값을 읽는다.
    // 가한 피해는 방어로 감소된 뒤의 실제 감소량 기준이라, 방패에 막힌 공격은 기여로 잡히지 않는다.
    public int DamageDealt { get; private set; }
    public int DamageTaken { get; private set; }
    public int Kills { get; private set; }
    public UnitStats Stats => stats;
    public NavMeshAgent Agent => agent;
    public Animator Animator => animator;
    public TargetScanner Scanner => scanner;
    public UnitController CurrentTarget { get; private set; }
    public bool IsDead => stats.IsDead;
    public bool IsBlocking { get; private set; }
    public bool HasMoveDestination { get; private set; }
    public bool IsRoamingMoveDestination { get; private set; }
    public Vector3 MoveDestination { get; private set; }
    public float RunDistance => runDistance;
    public float DestinationUpdateInterval => destinationUpdateInterval;

    private float lastSkillTime = -999f;
    private float lastBlockTime = -999f;
    private float lastPotionTime = -999f;
    private float lastHealTime = -999f;
    private UnitController healTarget;
    private float attackLockedUntil;
    private float nextDestinationUpdateTime;
    private Vector3 lastAgentDestination;
    private float lastAgentStoppingDistance;
    private bool hasAgentDestination;
    private int currentAnimationHash;
    private Vector3 roamDirection;
    private Vector3 knockbackDirection;
    private bool hasPendingKnockback;
    private float nextRoamTime;
    private float lastTargetChangeTime = -999f;
    private float requestedAgentSpeed;
    private UnitController lastAttacker;

    // 전투 매니저가 유닛 하나하나를 구독하지 않아도 되도록 정적 이벤트로 알린다.
    public static event Action<UnitController> OnAnyUnitDied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticEvents()
    {
        // 도메인 리로드를 끈 에디터에서 이전 플레이의 구독자가 남지 않도록 비운다.
        OnAnyUnitDied = null;
    }

    private int idleAnimationHash;
    private int walkAnimationHash;
    private int runAnimationHash;
    private int jumpAnimationHash;
    private int attackAnimationHash;
    private int skillAnimationHash;
    private int blockAnimationHash;
    private int potionAnimationHash;
    private int hitAnimationHash;
    private int deathAnimationHash;
    private bool hasWalkAnimationState;

    private float attackAnimationDuration;
    private float skillAnimationDuration;
    private float potionAnimationDuration;
    private float hitAnimationDuration;
    private float deathAnimationDuration;

    public float AttackAnimationDuration => attackAnimationDuration;
    public float SkillAnimationDuration => skillAnimationDuration;
    public float PotionAnimationDuration => potionAnimationDuration;
    public float HitAnimationDuration => hitAnimationDuration;
    public float DeathAnimationDuration => deathAnimationDuration;

    // 스포너가 Instantiate 직후 팀/스탯을 덮어쓸 때 사용. 이미 OnEnable로 UnitRegistry에
    // 등록된 상태에서 팀이 바뀌면 기존 리스트에서 빼고 새 팀 리스트로 다시 등록해준다.
    // 로스터 캐릭터를 그대로 얹는 경로. 히든 스탯이 감정 저항으로 넘어간다.
    public void Configure(UnitTeam newTeam, UnitStats newStats, CharacterSO source)
    {
        SourceCharacter = source;
        Configure(newTeam, newStats);

        if (emotion != null && source != null)
        {
            emotion.Configure(source.hiddenStats, source.starCount);
        }
    }

    public void Configure(UnitTeam newTeam, UnitStats newStats)
    {
        bool teamChanged = newTeam != team;
        if (teamChanged && isActiveAndEnabled) UnitRegistry.Unregister(this);

        team = newTeam;
        if (newStats != null) stats = newStats;
        stats.ResetHp();
        stats.ResetMana();
        ResetCombatRecord();
        ApplyAgentSpeed(stats.runSpeed);
        // 죽은 유닛을 재사용하는 경우를 대비해 FinalizeDeath가 껐던 Animator를 되살린다.
        if (animator != null) animator.enabled = true;

        if (teamChanged && isActiveAndEnabled) UnitRegistry.Register(this);
    }

    private void Awake()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (scanner == null) scanner = GetComponent<TargetScanner>();
        if (bodyCollider == null) bodyCollider = GetComponent<Collider>();

        if (emotion == null) emotion = GetComponent<UnitEmotion>();

        if (scanner != null) scanner.Initialize(this);

        // 배회 시각도 유닛마다 흩어 놓는다. 기본값 0이면 스폰 직후 전원이
        // 같은 프레임에 NavMesh.SamplePosition을 호출한다.
        nextRoamTime = Time.time + Random.Range(0f, roamInterval);
        if (emotion != null) emotion.Initialize(this);
        ApplyAgentSpeed(stats.runSpeed);
        CacheAnimationHashes();
        ApplyAnimatorCulling();
    }

    private void CacheAnimationHashes()
    {
        idleAnimationHash = ToHash(idleStateName);
        walkAnimationHash = ToHash(walkStateName);
        runAnimationHash = ToHash(runStateName);
        jumpAnimationHash = ToHash(jumpStateName);
        attackAnimationHash = ToHash(attackStateName);
        skillAnimationHash = ToHash(skillStateName);
        blockAnimationHash = ToHash(blockStateName);
        potionAnimationHash = ToHash(potionStateName);
        hitAnimationHash = ToHash(hitStateName);
        deathAnimationHash = ToHash(deathStateName);

        hasWalkAnimationState = animator != null &&
                                 !string.IsNullOrEmpty(walkStateName) &&
                                 animator.HasState(0, walkAnimationHash);

        attackAnimationDuration = GetAnimationClipDuration(attackStateName, 1f);
        skillAnimationDuration = GetAnimationClipDuration(skillStateName, 1f);
        potionAnimationDuration = GetAnimationClipDuration(potionStateName, 0.8f);
        hitAnimationDuration = GetAnimationClipDuration(hitStateName, 0.35f);
        deathAnimationDuration = GetAnimationClipDuration(deathStateName, 1.5f);
    }

    // 프로파일러 확인 결과 Animators.Update가 스크립트 전체(0.08ms)의 14배인 1.1ms로,
    // 실제 CPU 비용의 대부분을 차지했다. AlwaysAnimate는 화면 밖 유닛도 리타게팅/IK/본 트랜스폼을
    // 전부 계산하므로, 화면에 보이지 않는 동안은 트랜스폼 기록을 건너뛰도록 바꾼다.
    // (상태머신은 계속 진행되므로 다시 보일 때 애니메이션이 튀지 않는다.)
    private void ApplyAnimatorCulling()
    {
        if (animator == null) return;
        animator.cullingMode = animatorCullingMode;
    }

    private static int ToHash(string stateName)
    {
        return string.IsNullOrEmpty(stateName) ? 0 : Animator.StringToHash(stateName);
    }

    // runtimeAnimatorController.animationClips는 접근할 때마다 배열을 새로 만들어 반환한다.
    // 유닛 하나당 3번(공격/스킬/피격) 호출되므로 스폰이 많아지면 그대로 GC 부담이 된다.
    // 컨트롤러 에셋 단위로 클립 길이를 한 번만 만들어 모든 유닛이 공유한다.
    private static readonly Dictionary<RuntimeAnimatorController, Dictionary<string, float>> ClipDurationCache =
        new Dictionary<RuntimeAnimatorController, Dictionary<string, float>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetClipDurationCache()
    {
        // 도메인 리로드를 끈 에디터에서 파괴된 컨트롤러 참조가 남지 않도록 플레이 시작마다 비운다.
        ClipDurationCache.Clear();
    }

    private static Dictionary<string, float> GetClipDurations(RuntimeAnimatorController controller)
    {
        if (ClipDurationCache.TryGetValue(controller, out Dictionary<string, float> cached)) return cached;

        AnimationClip[] clips = controller.animationClips;
        var durations = new Dictionary<string, float>(clips.Length);
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip != null) durations[clip.name] = clip.length;
        }

        ClipDurationCache[controller] = durations;
        return durations;
    }

    private float GetAnimationClipDuration(string stateName, float fallback)
    {
        if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(stateName))
        {
            return fallback;
        }

        if (GetClipDurations(animator.runtimeAnimatorController).TryGetValue(stateName, out float length))
        {
            return length;
        }

        if (debugLogs) Debug.LogWarning($"[UnitController] {name} could not find animation clip named '{stateName}' to derive duration. Using fallback {fallback}s.");
        return fallback;
    }

    private void OnEnable()
    {
        UnitRegistry.Register(this);
        if (emotion != null) emotion.OnStateChanged += HandleEmotionChanged;
    }

    private void OnDisable()
    {
        UnitRegistry.Unregister(this);
        if (emotion != null) emotion.OnStateChanged -= HandleEmotionChanged;
    }

    private void Start()
    {
        CreateStates();
        stateMachine = new StateMachine<UnitController>();
        IState<UnitController> initialState = UnitRegistry.HasLivingEnemy(this) ? SearchState : IdleState;
        stateMachine.Initialize(initialState);
#if UNITY_EDITOR
        currentStateName = initialState.GetType().Name;
#endif
    }

    private void Update()
    {
        if (scanner != null) scanner.Tick();

        if (emotion != null)
        {
            emotion.Tick(Time.deltaTime);

            // 패닉/빈사/붕괴는 어느 상태에 있든 즉시 행동을 끊는다. 개별 상태마다 검사를 넣으면
            // 상태가 늘어날 때 빠뜨리기 쉬워서, 상태머신을 돌리기 직전 한 곳에서만 판단한다.
            if (!IsDead && emotion.IsActionBlocked && stateMachine != null && stateMachine.CurrentState != PanicState)
            {
                ChangeState(PanicState);
            }
        }

        TryDrinkPotion();
        TryHealAlly();

        stateMachine?.Update();
    }

    // 회복약도 패닉과 같은 이유로 상태머신 바깥 한 곳에서만 판단한다.
    // 패닉/빈사/붕괴 중에는 스스로 마실 수 없고, 이미 휘두르고 있는 공격 모션은 끊지 않는다.
    private void TryDrinkPotion()
    {
        if (IsDead || stateMachine == null) return;
        if (emotion != null && emotion.IsActionBlocked) return;
        if (stateMachine.CurrentState == PotionState || stateMachine.CurrentState == DeadState) return;
        if (IsAttackAnimationLocked) return;
        if (!CanUsePotion()) return;

        ChangeState(PotionState);
    }

    // 회복도 회복약과 같은 자리에서 판단한다. 서포터가 아니면 첫 줄에서 바로 빠진다.
    private void TryHealAlly()
    {
        if (!stats.canHealAllies) return;
        if (IsDead || stateMachine == null) return;
        if (emotion != null && emotion.IsActionBlocked) return;
        if (stateMachine.CurrentState == HealState ||
            stateMachine.CurrentState == PotionState ||
            stateMachine.CurrentState == DeadState) return;
        if (IsAttackAnimationLocked) return;
        if (!CanHealAlly()) return;

        ChangeState(HealState);
    }

    public void ChangeState(IState<UnitController> state)
    {
        stateMachine?.ChangeState(state);
#if UNITY_EDITOR
        currentStateName = state != null ? state.GetType().Name : "";
#endif
    }

    public void SetTarget(UnitController target)
    {
        TrySetTarget(target);
    }

    public void ClearTarget()
    {
        CurrentTarget = null;
#if UNITY_EDITOR
        currentTargetName = "";
#endif
    }

    public bool TrySetTarget(UnitController target)
    {
        if (target == CurrentTarget && IsTargetValid()) return true;
        if (target == null || target.IsDead || !target.isActiveAndEnabled || !UnitRegistry.AreEnemies(this, target)) return false;
        if (IsTargetChangeLocked()) return false;

        if (IsTargetValid())
        {
            if (!ShouldReleaseCurrentTarget() && !IsNewTargetClearlyBetter(target)) return false;
            if (Time.time < lastTargetChangeTime + targetChangeInterval) return false;
        }

        AssignTarget(target);
        return true;
    }

    public bool ShouldReleaseCurrentTarget()
    {
        if (CurrentTarget == null) return true;
        if (CurrentTarget.IsDead || !CurrentTarget.isActiveAndEnabled) return true;
        return false;
    }

    public bool HasUsableTarget()
    {
        if (IsTargetValid() && !ShouldReleaseCurrentTarget()) return true;

        ClearTarget();
        return false;
    }

    public void ReceiveSharedTarget(UnitController target)
    {
        if (IsDead || target == null || target.IsDead || !UnitRegistry.AreEnemies(this, target)) return;
        if (IsTargetChangeLocked()) return;

        bool acceptedTarget = HasUsableTarget() ? TrySetTarget(target) : ForceSetSharedTarget(target);
        if (!acceptedTarget) return;

        ClearMoveDestination();
        if (stateMachine == null) return;
        if (stateMachine.CurrentState == DeadState ||
            stateMachine.CurrentState == HitState ||
            stateMachine.CurrentState == AttackState ||
            stateMachine.CurrentState == SkillState ||
            stateMachine.CurrentState == BlockState)
        {
            return;
        }

        ChangeState(IsTargetInAttackRange() ? AttackState : ChaseState);
    }

    public void SetMoveDestination(Vector3 destination)
    {
        SetMoveDestination(destination, false);
    }

    private void SetMoveDestination(Vector3 destination, bool isRoaming)
    {
        MoveDestination = destination;
        HasMoveDestination = true;
        IsRoamingMoveDestination = isRoaming;
    }

    public void ClearMoveDestination()
    {
        HasMoveDestination = false;
        IsRoamingMoveDestination = false;
    }

    public bool TrySetRoamDestination()
    {
        if (!roamWhenSearching || Time.time < nextRoamTime) return false;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return false;

        nextRoamTime = Time.time + roamInterval;

        if (roamDirection.sqrMagnitude <= 0.0001f)
        {
            Vector2 initialDirection = Random.insideUnitCircle.normalized;
            roamDirection = new Vector3(initialDirection.x, 0f, initialDirection.y);
        }

        Vector2 randomCircle = Random.insideUnitCircle.normalized;
        Vector3 randomDirection = new Vector3(randomCircle.x, 0f, randomCircle.y);
        Vector3 weightedDirection = Vector3.Slerp(randomDirection, roamDirection.normalized, roamDirectionWeight);
        if (weightedDirection.sqrMagnitude <= 0.0001f)
        {
            weightedDirection = transform.forward;
            weightedDirection.y = 0f;
        }

        NavMeshHit hit;
        if (!NavMesh.SamplePosition(transform.position + weightedDirection.normalized * roamRadius, out hit, roamRadius, NavMesh.AllAreas))
        {
            return false;
        }

        roamDirection = hit.position - transform.position;
        roamDirection.y = 0f;
        SetMoveDestination(hit.position, true);
        return true;
    }

    public bool IsTargetValid()
    {
        return CurrentTarget != null && !CurrentTarget.IsDead && CurrentTarget.isActiveAndEnabled;
    }

    public bool IsTargetVisible()
    {
        return scanner != null && CurrentTarget != null && scanner.IsVisible(CurrentTarget);
    }

    public float SqrDistanceToTarget()
    {
        if (CurrentTarget == null) return float.MaxValue;
        return (CurrentTarget.transform.position - transform.position).sqrMagnitude;
    }

    public bool IsTargetInAttackRange()
    {
        float range = stats.attackRange + stats.moveStopDistance;
        return SqrDistanceToTarget() <= range * range;
    }

    public Vector3 GetPredictedTargetPosition()
    {
        if (CurrentTarget == null) return transform.position;

        Vector3 targetPosition = CurrentTarget.transform.position;
        NavMeshAgent targetAgent = CurrentTarget.Agent;
        if (targetAgent != null && targetAgent.enabled)
        {
            targetPosition += targetAgent.velocity * chasePredictionTime;
        }

        return targetPosition;
    }

    public bool CanAttack()
    {
        return !string.IsNullOrEmpty(attackStateName) &&
               IsTargetValid() &&
               IsTargetInAttackRange() &&
               !IsAttackAnimationLocked;
    }

    public bool IsAttackAnimationLocked => Time.time < attackLockedUntil;

    public bool CanUseSkill()
    {
        return !string.IsNullOrEmpty(skillStateName) &&
               IsTargetValid() &&
               IsTargetInAttackRange() &&
               stats.HasMana(stats.skillManaCost) &&
               Time.time >= lastSkillTime + stats.skillCooldown;
    }

    // 적은 전투 중 HP를 되돌릴 수단이 없다 — 인스펙터에서 회복약 개수를 넣어도 마시지 않는다.
    // 밸런스 수치가 아니라 설계 규칙이라 데이터가 아니라 코드에 둔다.
    // 앞으로 회복 스킬이나 아이템을 추가할 때도 이 프로퍼티를 먼저 확인하면 규칙이 유지된다.
    public bool CanRecoverHp => team != UnitTeam.Enemy;

    public bool CanUsePotion()
    {
        if (!CanRecoverHp) return false;
        if (IsDead || !stats.HasPotion) return false;
        if (Time.time < lastPotionTime + stats.potionCooldown) return false;

        float hpRatio = stats.HpRatio;
        if (hpRatio <= stats.potionHpThreshold) return true;

        // 마나가 바닥나 스킬을 못 쓰는 것도 마실 이유가 된다. 다만 HP가 거의 가득 차 있으면
        // 회복량의 대부분이 버려지므로 그때는 아낀다. 스킬 자체가 없는 유닛은 해당 없음.
        return !string.IsNullOrEmpty(skillStateName) &&
               stats.skillManaCost > 0 &&
               !stats.HasMana(stats.skillManaCost) &&
               hpRatio <= stats.potionManaTriggerHpRatio;
    }

    public void UsePotion()
    {
        // CanUsePotion을 거치지 않고 직접 불려도 적은 회복되지 않도록 여기서도 막는다.
        if (!CanRecoverHp) return;
        if (!stats.ConsumePotion(out int healedHp, out int healedMana)) return;

        lastPotionTime = Time.time;
        if (debugLogs) Debug.Log($"[UnitController] {name} 회복약 사용: HP +{healedHp}, MP +{healedMana} (남은 개수 {stats.potionCount})");
    }

    public void TriggerPotion()
    {
        // 전용 모션이 없으면 Idle로 대체한다. 마시는 동안 멈춰 서 있는 것만으로도 충분히 읽힌다.
        PlayAnimation(potionAnimationHash != 0 ? potionAnimationHash : idleAnimationHash, true);
    }

    // 서포터가 회복할 아군이 있는지 확인하고, 있으면 대상까지 잡아 둔다.
    // 판단과 대상 선정을 나누면 HealState가 다시 탐색해야 해서 같은 순회를 두 번 돌게 된다.
    public bool CanHealAlly()
    {
        healTarget = null;

        if (!stats.canHealAllies || !CanRecoverHp || IsDead) return false;
        if (Time.time < lastHealTime + stats.healCooldown) return false;
        if (!stats.HasMana(stats.healManaCost)) return false;

        healTarget = UnitRegistry.FindMostWoundedAlly(this, stats.healRange, stats.healTargetHpRatio);
        return healTarget != null;
    }

    public void PerformHeal()
    {
        if (healTarget == null || healTarget.IsDead || !healTarget.CanRecoverHp) return;

        int healed = healTarget.Stats.Heal(stats.healAmount);
        stats.SpendMana(stats.healManaCost);
        lastHealTime = Time.time;

        if (debugLogs) Debug.Log($"[UnitController] {name} 회복 시전: {healTarget.name} HP +{healed}");
        healTarget = null;
    }

    public void TriggerHeal()
    {
        // 전용 시전 모션이 없으면 스킬 모션을 빌려 쓴다.
        PlayAnimation(skillAnimationHash != 0 ? skillAnimationHash : idleAnimationHash, true);
    }

    public bool CanBlock()
    {
        return !string.IsNullOrEmpty(blockStateName) &&
               IsTargetValid() &&
               Time.time >= lastBlockTime + stats.blockCooldown &&
               IsTargetTelegraphingAttack();
    }

    private bool IsTargetTelegraphingAttack()
    {
        return CurrentTarget != null &&
               CurrentTarget.IsAttackAnimationLocked &&
               CurrentTarget.CurrentTarget == this;
    }

    // 원거리 유닛이 적에게 붙잡혔는지 판단한다.
    // attackRange만 늘리면 "더 멀리서 공격을 시작"할 뿐, 이미 붙은 적에게서 물러나지는 않는다.
    // 실제로 돌려보니 사거리 9짜리가 1.4m에서 근접 유닛과 나란히 싸우고 있었다 —
    // 원거리 역할이 성립하려면 교전 중에도 거리를 다시 벌리는 판단이 따로 필요하다.
    public bool ShouldKeepDistance()
    {
        if (stats.attackRange < minKeepDistanceRange) return false;
        if (!IsTargetValid()) return false;

        float threshold = stats.attackRange * keepDistanceRatio;
        return SqrDistanceToTarget() < threshold * threshold;
    }

    public bool CanEvade()
    {
        return IsTargetValid() && SqrDistanceToTarget() < stats.attackRange * stats.attackRange * 0.5f;
    }

    public void TakeDamage(int damage)
    {
        TakeDamage(damage, null, false);
    }

    public void TakeDamage(int damage, UnitController attacker)
    {
        TakeDamage(damage, attacker, false);
    }

    public void TakeDamage(int damage, UnitController attacker, bool applyKnockback)
    {
        TakeDamage(damage, attacker, applyKnockback, false);
    }

    // fromSkill: 강타(스킬)에 맞았는지 여부. 출혈 발생 판정에만 쓰인다.
    public void TakeDamage(int damage, UnitController attacker, bool applyKnockback, bool fromSkill)
    {
        // 이미 죽은 유닛에 피가 튀거나 피격 상태로 되돌아가지 않도록 여기서 끊는다.
        if (IsDead) return;

        bool wasBlocking = IsBlocking;
        int hpBefore = stats.currentHp;
        stats.TakeDamage(damage, IsBlocking);
        int dealt = hpBefore - stats.currentHp;

        RecordDamage(dealt, attacker);
        if (!wasBlocking) SpawnBloodEffect(attacker);
        if (emotion != null) emotion.NotifyDamaged(dealt, fromSkill);

        if (attacker != null && !attacker.IsDead && attacker.isActiveAndEnabled && UnitRegistry.AreEnemies(this, attacker))
        {
            ForceSetAttackTarget(attacker);
            if (applyKnockback)
            {
                SetKnockbackDirection(attacker.transform.position);
            }
            else
            {
                ClearKnockback();
            }
        }

        if (stats.IsDead)
        {
            Die();
            return;
        }

        InterruptCurrentAction();

        if (wasBlocking)
        {
            ChangeState(AttackState);
            return;
        }

        ChangeState(HitState);
    }

    private void SpawnBloodEffect(UnitController attacker)
    {
        if (bloodEffectPrefabs == null || bloodEffectPrefabs.Length == 0) return;

        GameObject prefab = bloodEffectPrefabs[Random.Range(0, bloodEffectPrefabs.Length)];
        if (prefab == null) return;

        Vector3 spawnPosition = (bodyCollider != null ? bodyCollider.bounds.center : transform.position) + bloodEffectOffset;
        Vector3 lookDirection = attacker != null ? transform.position - attacker.transform.position : transform.forward;
        lookDirection.y = 0f;
        Quaternion rotation = lookDirection.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(lookDirection.normalized) : transform.rotation;

        BloodEffectPool.Instance.Spawn(prefab, spawnPosition, rotation);
    }

    public void Die()
    {
        if (stateMachine != null && stateMachine.CurrentState != DeadState)
        {
            ChangeState(DeadState);
        }
    }

    // 출혈처럼 시간에 따라 들어오는 피해. 피격 리액션(HitState)을 일으키지 않아야
    // 출혈이 공격 모션을 매초 끊어먹는 일이 없다.
    public void TakeBleedDamage(int damage)
    {
        if (IsDead) return;

        int hpBefore = stats.currentHp;
        stats.TakeDamage(damage);
        DamageTaken += hpBefore - stats.currentHp;

        if (stats.IsDead) Die();
    }

    // DeadState 진입 시 호출. 같은 팀에 죽음을 알려 공포를 전파하고(원작의 핵심 기믹)
    // 전투 매니저/UI 같은 외부 구독자에게 통지한다.
    public void NotifyDeath()
    {
        // 처치는 마지막으로 피해를 준 유닛에게 귀속시킨다. 출혈로 쓰러진 경우에도
        // 출혈을 걸어둔 공격자가 lastAttacker로 남아 있어 기여가 사라지지 않는다.
        if (lastAttacker != null && lastAttacker != this) lastAttacker.Kills++;

        UnitEmotion.BroadcastAllyDeath(this);
        OnAnyUnitDied?.Invoke(this);
    }

    public void ApplyAttackDamage()
    {
        if (!IsTargetValid()) return;
        CurrentTarget.TakeDamage(ScaleDamage(stats.attackDamage), this);
    }

    public void ApplySkillDamage()
    {
        if (!IsTargetValid()) return;
        CurrentTarget.TakeDamage(ScaleDamage(stats.skillDamage), this, true, true);
    }

    // 공포에 빠진 유닛은 원작 설정대로 능력치가 깎인다. 0이 되어 무해해지지는 않도록 최소 1.
    private int ScaleDamage(int damage)
    {
        return Mathf.Max(1, Mathf.RoundToInt(damage * EmotionMultiplier));
    }

    public void TriggerAttack()
    {
        attackLockedUntil = Time.time + attackAnimationDuration;
        PlayAnimation(attackAnimationHash, true);
    }

    public void TriggerSkill()
    {
        lastSkillTime = Time.time;
        stats.SpendMana(stats.skillManaCost);
        PlayAnimation(skillAnimationHash, true);
    }

    public void SetBlocking(bool isBlocking)
    {
        IsBlocking = isBlocking;
        if (isBlocking)
        {
            lastBlockTime = Time.time;
            PlayAnimation(blockAnimationHash, true);
        }
        else
        {
            PlayAnimation(idleAnimationHash, false);
        }
    }

    public void StopMovement()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        hasAgentDestination = false;
        SetMoveAnimation(0f, false, false);
    }

    public void MoveTo(Vector3 destination, float speed)
    {
        MoveTo(destination, speed, stats.moveStopDistance);
    }

    public void MoveTo(Vector3 destination, float speed, float stoppingDistance)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Vector3 destinationDelta = destination - lastAgentDestination;
        float stoppingDistanceDelta = Mathf.Abs(stoppingDistance - lastAgentStoppingDistance);
        if (hasAgentDestination &&
            Time.time < nextDestinationUpdateTime &&
            destinationDelta.sqrMagnitude < 0.04f &&
            stoppingDistanceDelta < 0.01f)
        {
            return;
        }

        hasAgentDestination = true;
        lastAgentDestination = destination;
        lastAgentStoppingDistance = stoppingDistance;
        nextDestinationUpdateTime = Time.time + destinationUpdateInterval;

        agent.isStopped = false;
        ApplyAgentSpeed(speed);
        agent.stoppingDistance = stoppingDistance;
        agent.SetDestination(destination);
    }

    public bool HasReachedDestination(Vector3 destination)
    {
        float stopDistance = stats.moveStopDistance;
        Vector3 toDestination = destination - transform.position;
        toDestination.y = 0f;
        return toDestination.sqrMagnitude <= stopDistance * stopDistance;
    }

    public void SetMoveAnimation(float speed, bool isRunning, bool isJumping)
    {
        if (isJumping && !string.IsNullOrEmpty(jumpStateName))
        {
            PlayAnimation(jumpAnimationHash, false);
            return;
        }

        if (speed <= 0.01f)
        {
            PlayAnimation(idleAnimationHash, false);
            return;
        }

        if (isRunning)
        {
            PlayAnimation(runAnimationHash, false);
            return;
        }

        PlayAnimation(hasWalkAnimationState ? walkAnimationHash : runAnimationHash, false);
    }

    public void FaceTarget()
    {
        if (CurrentTarget == null) return;

        Vector3 direction = CurrentTarget.transform.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Mathf.Clamp01(rotationSpeed * Time.deltaTime / 180f));
    }

    public void TriggerDead()
    {
        PlayAnimation(deathAnimationHash, true);
    }

    // 실제로 재생 중인 사망 상태의 길이를 읽는다.
    // deathStateName(상태 이름)과 클립 이름이 다른 경우가 있어서(예: 상태 "Death" / 클립 "PlayerDeath")
    // 이름 매칭 기반인 deathAnimationDuration은 fallback으로 떨어질 수 있다. 그대로 쓰면
    // 사망 애니메이션 도중에 Animator를 꺼서 시체가 넘어지다 만 자세로 굳는다.
    // 전이가 끝나고 사망 상태에 실제로 진입한 뒤에만 true를 돌려준다.
    public bool TryGetDeathStateLength(out float length)
    {
        length = 0f;
        if (animator == null || !animator.enabled || !animator.isActiveAndEnabled) return false;
        if (deathAnimationHash == 0 || animator.IsInTransition(0)) return false;

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        if (info.shortNameHash != deathAnimationHash || info.length <= 0f) return false;

        length = info.length;
        return true;
    }

    public void TriggerHit()
    {
        PlayAnimation(hitAnimationHash, true);
    }

    public void InterruptCurrentAction()
    {
        attackLockedUntil = 0f;
        IsBlocking = false;
        hasAgentDestination = false;

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    public void ApplyKnockback(float progress)
    {
        if (!hasPendingKnockback) return;
        if (knockbackDirection.sqrMagnitude <= 0.0001f) return;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        float distance = stats.knockbackDistance * progress;
        Vector3 destination = transform.position + knockbackDirection.normalized * distance;

        agent.Move(destination - transform.position);
    }

    public void DisableCollider()
    {
        if (bodyCollider != null) bodyCollider.enabled = false;
    }

    // 사망 즉시 처리 — 이동 관련만 끈다. 사망 애니메이션은 아직 재생 중이어야 하므로
    // Animator와 이 컴포넌트는 여기서 끄지 않는다(FinalizeDeath에서 처리).
    public void DisableAgentAfterDeath()
    {
        if (agent == null) return;

        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        agent.enabled = false;
    }

    // 사망 애니메이션이 끝난 뒤 호출. Animator를 끄지 않으면 시체가 늘어날수록
    // Animators.Update 비용이 그대로 쌓인다(프로파일러에서 시체 128구가 살아있는 유닛과
    // 동일한 1.0ms를 계속 소비하는 것을 확인). 마지막 프레임 포즈는 그대로 유지된다.
    public void FinalizeDeath()
    {
        if (disableAnimatorAfterDeath && animator != null) animator.enabled = false;
        enabled = false;
    }

    private void CreateStates()
    {
        IdleState = new IdleState(this);
        SearchState = new SearchState(this);
        ChaseState = new ChaseState(this);
        MoveState = new MoveState(this);
        AttackState = new AttackState(this);
        SkillState = new SkillState(this);
        EvadeState = new EvadeState(this);
        BlockState = new BlockState(this);
        HitState = new HitState(this);
        DeadState = new DeadState(this);
        PanicState = new PanicState(this);
        PotionState = new PotionState(this);
        HealState = new HealState(this);
    }

    // 공포 상태에서는 이동속도도 함께 깎인다. 요청 속도를 따로 들고 있는 이유는
    // 감정이 바뀌었을 때 MoveTo를 기다리지 않고 바로 다시 계산하기 위해서다.
    private void ResetCombatRecord()
    {
        DamageDealt = 0;
        DamageTaken = 0;
        Kills = 0;
        lastAttacker = null;
    }

    private void RecordDamage(int dealt, UnitController attacker)
    {
        if (dealt <= 0) return;

        DamageTaken += dealt;
        if (attacker == null || attacker == this) return;

        lastAttacker = attacker;
        attacker.DamageDealt += dealt;
    }

    private void ApplyAgentSpeed(float speed)
    {
        requestedAgentSpeed = speed;
        if (agent != null) agent.speed = speed * EmotionMultiplier;
    }

    private float EmotionMultiplier => emotion != null ? emotion.StatMultiplier : 1f;

    private void HandleEmotionChanged(UnitEmotion changed)
    {
        ApplyAgentSpeed(requestedAgentSpeed);
    }

    private void AssignTarget(UnitController target)
    {
        CurrentTarget = target;
        lastTargetChangeTime = Time.time;
#if UNITY_EDITOR
        currentTargetName = CurrentTarget != null ? CurrentTarget.name : "";
#endif
    }

    private bool ForceSetSharedTarget(UnitController target)
    {
        if (target == null || target.IsDead || !target.isActiveAndEnabled || !UnitRegistry.AreEnemies(this, target)) return false;

        AssignTarget(target);
        return true;
    }

    private void ForceSetAttackTarget(UnitController attacker)
    {
        if (attacker == CurrentTarget && IsTargetValid()) return;

        AssignTarget(attacker);
        ClearMoveDestination();
    }

    private void SetKnockbackDirection(Vector3 attackerPosition)
    {
        knockbackDirection = transform.position - attackerPosition;
        knockbackDirection.y = 0f;
        if (knockbackDirection.sqrMagnitude <= 0.0001f)
        {
            knockbackDirection = -transform.forward;
        }

        hasPendingKnockback = true;
    }

    private void ClearKnockback()
    {
        knockbackDirection = Vector3.zero;
        hasPendingKnockback = false;
    }

    private bool IsTargetChangeLocked()
    {
        if (stateMachine == null) return false;

        return stateMachine.CurrentState == AttackState ||
               stateMachine.CurrentState == SkillState ||
               stateMachine.CurrentState == EvadeState;
    }

    private bool IsNewTargetClearlyBetter(UnitController target)
    {
        if (CurrentTarget == null || target == null) return true;

        float currentSqrDistance = SqrDistanceToTarget();
        float newSqrDistance = (target.transform.position - transform.position).sqrMagnitude;
        float requiredSqrDistance = currentSqrDistance * targetSwitchDistanceRatio * targetSwitchDistanceRatio;

        return newSqrDistance < requiredSqrDistance;
    }

    private void PlayAnimation(int stateHash, bool forceRestart)
    {
        if (animator == null)
        {
            if (debugLogs) Debug.LogWarning($"[UnitController] {name} cannot play animation: Animator is null.");
            return;
        }

        if (stateHash == 0)
        {
            if (debugLogs) Debug.LogWarning($"[UnitController] {name} cannot play animation: state name is empty.");
            return;
        }

        if (!forceRestart && currentAnimationHash == stateHash) return;

        currentAnimationHash = stateHash;

        animator.CrossFadeInFixedTime(stateHash, animationFadeDuration);
    }
}
