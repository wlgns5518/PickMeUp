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
    [SerializeField] private WeaponEquipper equipment;

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
    [Tooltip("공격 애니메이션 상태 이름의 접두어. 콤보 길이가 1보다 크면 뒤에 1,2,3...이 붙는다(Attack1, Attack2...).")]
    [SerializeField] private string attackStateName = "Attack";
    [Tooltip("공격 콤보 단계 수. 무기별 Override Controller가 Attack1~N 상태를 모두 갖고 있어야 한다.")]
    [SerializeField, Min(1)] private int attackComboLength = 3;
    [SerializeField] private string skillStateName = "Kick";
    [SerializeField] private string blockStateName = "Block";
    [Tooltip("회복약을 마시는 모션. 비워두거나 애니메이터에 없으면 Idle로 대체된다.")]
    [SerializeField] private string potionStateName = "Potion";
    [Tooltip("아군을 치료할 때의 시전 모션. 비워두거나 애니메이터에 없으면 스킬 모션을 빌려 쓴다.")]
    [SerializeField] private string healStateName = "Cast";
    [Tooltip("거리를 벌릴 때의 회피 모션. 비워두거나 애니메이터에 없으면 달리기로 물러난다.")]
    [SerializeField] private string dodgeStateName = "Dodge";
    [SerializeField] private string hitStateName = "Hit";
    [SerializeField] private string deathStateName = "Death";
    [SerializeField] private float animationFadeDuration = 0.08f;

    [Header("Animator Move Speed")]
    [Tooltip("걷기/달리기 재생 배속을 넘길 Animator float 파라미터. 비워두면 배속을 건드리지 않는다.")]
    [SerializeField] private string moveSpeedParameterName = "MoveSpeedMultiplier";
    [Tooltip("걷기 클립이 원래 나아가는 속도(m/s). 실제 이동 속도를 이 값으로 나눠 배속을 정한다. Armed-Walk = 1.99")]
    [SerializeField, Min(0.1f)] private float walkClipSpeed = 1.99f;
    [Tooltip("달리기 클립이 원래 나아가는 속도(m/s). PlayerRun = 4.2, 고블린 Run = 2.29")]
    [SerializeField, Min(0.1f)] private float runClipSpeed = 4.2f;
    [Tooltip("배속 허용 범위. 너무 벌어지면 발이 미끄러지는 대신 다리가 헛돈다.")]
    [SerializeField] private Vector2 moveSpeedMultiplierRange = new Vector2(0.6f, 2.2f);

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
    private IState<UnitController> debugTrackedState;
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
    private Collider[] bodyColliders;

    // 이 유닛에 속한 콜라이더 전부. UnitRegistry가 시야 레이의 히트 중 유닛 몸통을 걸러내는 데 쓴다.
    public Collider[] BodyColliders => bodyColliders;

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
    private int[] attackAnimationHashes;
    private float[] attackAnimationDurationsPerStep;
    private int attackComboIndex;
    private bool hasAttackAnimation;
    private int skillAnimationHash;
    private int blockAnimationHash;
    private int potionAnimationHash;
    private int healAnimationHash;
    private int dodgeAnimationHash;
    private int hitAnimationHash;
    private int deathAnimationHash;
    private bool hasWalkAnimationState;
    private int moveSpeedParameterHash;
    private bool hasMoveSpeedParameter;

    private float attackAnimationDuration;
    private float skillAnimationDuration;
    private float potionAnimationDuration;
    private float healAnimationDuration;
    private float dodgeAnimationDuration;
    private float hitAnimationDuration;
    private float deathAnimationDuration;

    public float AttackAnimationDuration => attackAnimationDuration;
    public float SkillAnimationDuration => skillAnimationDuration;
    public float PotionAnimationDuration => potionAnimationDuration;
    public float HealAnimationDuration => healAnimationDuration;
    // 회피 모션이 없는 리그(고블린)는 0. EvadeState가 이 값으로 "구를지 달릴지"를 정한다.
    public float DodgeAnimationDuration => dodgeAnimationHash != 0 ? dodgeAnimationDuration : 0f;
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

        // 스탯은 이미 MapStats에서 장비 보정을 받았다. 여기서는 화면에 보이는 쪽만 맞춘다.
        if (equipment != null) equipment.Equip(source);
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
        // 시야 판정이 자기 몸(과 래그돌 콜라이더)을 장애물로 세지 않도록 레지스트리에 넘길 목록.
        // 무기 모델의 콜라이더는 WeaponEquipper가 꺼 두므로 여기 들어오지 않아도 된다.
        bodyColliders = GetComponentsInChildren<Collider>(true);
        UnitRegistry.RegisterColliders(this);

        if (emotion == null) emotion = GetComponent<UnitEmotion>();
        if (equipment == null) equipment = GetComponent<WeaponEquipper>();
        if (equipment != null) equipment.WeaponAnimatorChanged += RefreshAttackAnimations;

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
        runAnimationHash = ToHash(runStateName);
        CacheAttackComboAnimations();

        // 아래 상태들은 리그마다 있을 수도 없을 수도 있다(고블린 컨트롤러에는 Kick도 Block도 없다).
        // 이름만 보고 "있다"고 판단하면 존재하지 않는 상태로 CrossFade해서 유닛이 그 자리에 굳는다.
        // 실제로 애니메이터에 있는 것만 해시를 남기고, 없으면 0으로 둬서 부르는 쪽이 대체 동작을 타게 한다.
        walkAnimationHash = ResolveStateHash(walkStateName);
        jumpAnimationHash = ResolveStateHash(jumpStateName);
        skillAnimationHash = ResolveStateHash(skillStateName);
        blockAnimationHash = ResolveStateHash(blockStateName);
        potionAnimationHash = ResolveStateHash(potionStateName);
        healAnimationHash = ResolveStateHash(healStateName);
        dodgeAnimationHash = ResolveStateHash(dodgeStateName);
        hitAnimationHash = ResolveStateHash(hitStateName);
        deathAnimationHash = ResolveStateHash(deathStateName);

        hasWalkAnimationState = walkAnimationHash != 0;

        CacheMoveSpeedParameter();

        skillAnimationDuration = GetAnimationClipDuration(skillStateName, 1f);
        potionAnimationDuration = GetAnimationClipDuration(potionStateName, 0.8f);
        healAnimationDuration = healAnimationHash != 0
            ? GetAnimationClipDuration(healStateName, skillAnimationDuration)
            : skillAnimationDuration;
        dodgeAnimationDuration = GetAnimationClipDuration(dodgeStateName, 0.35f);
        hitAnimationDuration = GetAnimationClipDuration(hitStateName, 0.35f);
        deathAnimationDuration = GetAnimationClipDuration(deathStateName, 1.5f);
    }

    // 이름이 비어 있거나 애니메이터에 그런 상태가 없으면 0.
    // 애니메이터 자체가 없으면 판단할 근거가 없으므로 이름 해시를 그대로 돌려준다.
    private int ResolveStateHash(string stateName)
    {
        int hash = ToHash(stateName);
        if (hash == 0 || animator == null) return hash;

        return animator.HasState(0, hash) ? hash : 0;
    }

    // 무기를 갈아 끼우면 Attack1~N 클립이 다른 Override Controller로 바뀐다. 상태 이름(해시)은
    // 그대로지만 클립 길이는 무기마다 다르므로, WeaponEquipper.WeaponAnimatorChanged를 받을 때마다
    // 다시 재 보아야 attackLockedUntil이 실제 재생 시간과 어긋나지 않는다.
    private void CacheAttackComboAnimations()
    {
        // 무기마다 원본 팩이 주는 공격 클립 수가 다르다(단검 3 ~ 양손검 11).
        // 무기 컨트롤러가 물려 있으면 그 개수를 그대로 쓰고, 없으면(맨손이거나 이 리그가
        // 무기 컨트롤러를 안 쓰는 유닛) 프리팹에 적힌 기본값을 쓴다.
        int steps = equipment != null && equipment.WeaponAttackCount > 0
            ? equipment.WeaponAttackCount
            : Mathf.Max(1, attackComboLength);

        if (attackAnimationHashes == null || attackAnimationHashes.Length != steps)
        {
            attackAnimationHashes = new int[steps];
            attackAnimationDurationsPerStep = new float[steps];
        }

        hasAttackAnimation = false;
        for (int i = 0; i < steps; i++)
        {
            string name = steps > 1 ? attackStateName + (i + 1) : attackStateName;
            // 실제로 애니메이터에 있는 단계만 남긴다. 무기가 선언한 콤보 수보다 컨트롤러의
            // 상태가 적으면 존재하지 않는 상태로 CrossFade해서 그 단계에서 유닛이 굳는다.
            attackAnimationHashes[i] = ResolveStateHash(name);
            attackAnimationDurationsPerStep[i] = GetAnimationClipDuration(name, 1f);
            if (attackAnimationHashes[i] != 0) hasAttackAnimation = true;
        }

        attackComboIndex = 0;
    }

    // 공격 모션이 하나라도 있는가. 없으면 CanAttack이 false가 되어 공격 상태로 들어가지 않는다.
    public bool HasAttackAnimation => hasAttackAnimation;

    // WeaponEquipper가 무기를 갈아 끼운 뒤 호출. Awake는 장비가 붙기 전에 한 번 끝나므로
    // 처음 캐시된 길이는 맨손 기준이라 무기 장착 후 다시 계산해야 한다.
    public void RefreshAttackAnimations() => CacheAttackComboAnimations();

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

    // 콜라이더 등록만 오브젝트 수명에 묶여 있다(RegisterColliders 주석 참조).
    // 팀 리스트 등록/해제는 OnEnable/OnDisable 쪽이다.
    private void OnDestroy()
    {
        UnitRegistry.UnregisterColliders(this);
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
        stateMachine.Initialize(this, initialState, UnitGlobalTransitions.All);
        SyncDebugState();
    }

    private void Update()
    {
        if (scanner != null) scanner.Tick();
        if (emotion != null) emotion.Tick(Time.deltaTime);

        // 사망/패닉/회복약/치료처럼 어느 상태에서든 걸리는 전이는 상태머신이 직접 들고 있다.
        // (UnitGlobalTransitions 참조 — 예전에는 그 판단이 여기 있었다.)
        stateMachine?.Update();
        SyncDebugState();
    }

    public void ChangeState(IState<UnitController> state)
    {
        stateMachine?.ChangeState(state);
    }

    // 인스펙터 표시용. 요청한 상태가 아니라 실제로 적용된 상태를 읽어야 한다 —
    // 전이는 지연 적용되고, 같은 프레임에 덮어써져 건너뛰는 요청도 있기 때문이다.
    // GetType().Name은 호출할 때마다 문자열을 새로 만들므로 참조가 바뀐 프레임에만 읽는다.
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void SyncDebugState()
    {
#if UNITY_EDITOR
        IState<UnitController> state = stateMachine?.CurrentState;
        if (ReferenceEquals(state, debugTrackedState)) return;

        debugTrackedState = state;
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
        return HasAttackAnimation &&
               IsTargetValid() &&
               IsTargetInAttackRange() &&
               !IsAttackAnimationLocked;
    }

    public bool IsAttackAnimationLocked => Time.time < attackLockedUntil;

    public bool CanUseSkill()
    {
        return skillAnimationHash != 0 &&
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
        return skillAnimationHash != 0 &&
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
        // 시전 모션 → 없으면 스킬 모션 → 그것도 없으면 Idle.
        // 예전에는 곧바로 스킬(발차기)로 떨어져서, 서포터가 아군을 치료할 때 발길질을 했다.
        int hash = healAnimationHash != 0 ? healAnimationHash
                 : skillAnimationHash != 0 ? skillAnimationHash
                 : idleAnimationHash;
        PlayAnimation(hash, true);
    }

    // 거리를 벌릴 때의 회피 모션. 없으면 false를 돌려주고, 부르는 쪽이 달리기로 물러난다.
    public bool TriggerDodge()
    {
        if (dodgeAnimationHash == 0) return false;

        PlayAnimation(dodgeAnimationHash, true);
        return true;
    }

    public bool CanBlock()
    {
        return blockAnimationHash != 0 &&
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

    // HP가 위험 수위인데 회복 수단(회복약/치료)이 없거나 이미 바닥났을 때 거리를 벌린다.
    // 적은 CanRecoverHp가 항상 false라 회복약이 없으면 이게 유일한 생존 수단이다.
    // 쿨다운으로 한 번만 물러나게 막으면, 여전히 위험한데도 한 박자 쉬고 다시 근접전으로
    // 걸어 들어가 버린다(맞다가 죽는 원인). HP가 임계치 아래인 동안은 매 프레임 계속 true를 줘서
    // EvadeState가 안전해질 때까지 반복해서 물러나게 한다.
    private bool ShouldRetreatForSurvival()
    {
        // 적은 체력이 바닥나도 물러서지 않는다. 회복 수단이 없는 쪽(CanRecoverHp)이 도망까지 다니면
        // 죽지도 싸우지도 않고 맵을 배회하게 되어 전투가 끝나지 않는다.
        // 밸런스 수치가 아니라 설계 규칙이라 데이터가 아니라 코드에 둔다.
        if (team == UnitTeam.Enemy) return false;

        if (!IsTargetValid()) return false;
        if (stats.HpRatio > stats.retreatHpThreshold) return false;

        // 회복약으로 이번 프레임에 살아날 수 있으면 굳이 등을 보이지 않는다 — TryDrinkPotion이 먼저 처리한다.
        if (CanUsePotion()) return false;

        return true;
    }

    // 전투 중 거리를 벌려야 하는 모든 상황(원거리 유닛의 근접 회피 + 위기 후퇴)을 한데 묶는다.
    // ChaseState/AttackState/EvadeState가 매 프레임 이거 하나만 물어보면 된다.
    public bool ShouldEvade()
    {
        return ShouldKeepDistance() || ShouldRetreatForSurvival();
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
        int step = attackComboIndex % attackAnimationHashes.Length;
        attackAnimationDuration = attackAnimationDurationsPerStep[step];
        attackLockedUntil = Time.time + attackAnimationDuration;
        PlayAnimation(attackAnimationHashes[step], true);
        attackComboIndex = (step + 1) % attackAnimationHashes.Length;
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
            ApplyMoveAnimationSpeed(speed, runClipSpeed);
            PlayAnimation(runAnimationHash, false);
            return;
        }

        // 걷기 상태가 없는 리그(고블린)는 달리기 클립으로 대신하므로 기준 속도도 달리기 쪽을 쓴다.
        ApplyMoveAnimationSpeed(speed, hasWalkAnimationState ? walkClipSpeed : runClipSpeed);
        PlayAnimation(hasWalkAnimationState ? walkAnimationHash : runAnimationHash, false);
    }

    // 클립이 원래 나아가는 속도와 실제 이동 속도가 어긋나면 발이 땅에서 미끄러진다.
    // 민첩과 직업 배율로 이동 속도가 캐릭터마다 달라지므로(달리기 4~6m/s) 고정 배속으로는 맞출 수 없다.
    // 그래서 Animator의 float 파라미터로 배속을 넘기고, Run/Walk 상태가 그 값을 곱해 재생한다.
    private void ApplyMoveAnimationSpeed(float speed, float clipSpeed)
    {
        if (!hasMoveSpeedParameter || animator == null) return;

        // 공포/패닉으로 실제 이동이 느려지면 애니메이션도 같이 느려져야 한다.
        float actualSpeed = speed * EmotionMultiplier;
        float multiplier = Mathf.Clamp(actualSpeed / Mathf.Max(0.1f, clipSpeed),
            moveSpeedMultiplierRange.x, moveSpeedMultiplierRange.y);
        animator.SetFloat(moveSpeedParameterHash, multiplier);
    }

    private void CacheMoveSpeedParameter()
    {
        hasMoveSpeedParameter = false;
        if (animator == null || string.IsNullOrEmpty(moveSpeedParameterName)) return;

        moveSpeedParameterHash = Animator.StringToHash(moveSpeedParameterName);
        // 파라미터가 없는 컨트롤러에 SetFloat을 부르면 매 프레임 경고가 쌓인다. 먼저 확인한다.
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type != AnimatorControllerParameterType.Float) continue;
            if (parameter.nameHash != moveSpeedParameterHash) continue;

            hasMoveSpeedParameter = true;
            return;
        }
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
