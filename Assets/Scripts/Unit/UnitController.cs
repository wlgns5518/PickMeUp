using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
// System을 열면 Random이 System.Random과 충돌한다. 이 파일의 Random은 전부 UnityEngine 쪽.
using Random = UnityEngine.Random;

[RequireComponent(typeof(TargetScanner))]
public partial class UnitController : MonoBehaviour
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
    [Tooltip("방어 중 위협 쪽으로 도는 속도. 일반 회전보다 느려야 등 뒤에서 들어오는 공격에 허점이 생긴다.")]
    [SerializeField] private float blockTurnSpeed = 240f;

    [Header("Ranged")]
    [Tooltip("사거리가 이 값 이상인 유닛만 원거리로 취급해 거리를 벌린다. 근접 유닛은 해당 없음.")]
    [SerializeField] private float minKeepDistanceRange = 4f;
    [Tooltip("사거리의 이 비율 안쪽까지 적이 붙으면 물러선다. " +
             "쫓아오는 근접 유닛과 이동 속도가 비슷해서 물러나 봐야 거의 못 벌리므로, " +
             "\"적이 실제로 때릴 수 있게 되는 순간\"에 맞춰 잡는다 — 사거리 9짜리에 0.3이면 2.7m로, " +
             "고블린의 타격 도달(1.85m) 바로 바깥이다. 더 올리면 사정권 밖의 적에게도 물러나느라 " +
             "쏘는 시간이 사라진다.")]
    [SerializeField, Range(0f, 1f)] private float keepDistanceRatio = 0.3f;

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
    [Tooltip("액티브 스킬 모션. 지금은 비워 둔다 — 기획상 공격 스킬이 없고, 발차기는 스킬이 아니라 " +
             "기본공격으로 옮겼다(kickStateName). 진짜 액티브 스킬이 생기면 그때 채운다.")]
    [SerializeField] private string skillStateName = "";
    [Tooltip("발차기 모션. 기본공격의 한 갈래로, 상대가 방패를 올렸을 때 골라 쓴다. " +
             "이 상태가 없는 리그는 발차기를 쓰지 않고 무기 콤보만 돈다.")]
    [SerializeField] private string kickStateName = "Kick";
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
    [Tooltip("옆걸음 클립이 원래 나아가는 속도(m/s). StrafeLeft = 1.94, StrafeRight = 1.90")]
    [SerializeField, Min(0.1f)] private float strafeClipSpeed = 1.92f;
    [Tooltip("뒷걸음 클립이 원래 나아가는 속도(m/s). StrafeBack = 2.19 — 옆걸음보다 빨라서 " +
             "같은 값으로 계산하면 뒤로 물러설 때만 발이 밀린다.")]
    [SerializeField, Min(0.1f)] private float strafeBackClipSpeed = 2.19f;
    [Tooltip("거리를 벌릴 때 뒤로 빠지는 속도. 달리기(4)로 물러나면 뒷걸음 클립이 2배속으로 " +
             "돌아 다리가 눈에 띄게 헛돈다. 클립이 원래 나아가는 속도(2.19)의 1.5배쯤이 한계다.")]
    [SerializeField, Min(0.1f)] private float backpedalSpeed = 3.3f;
    [Tooltip("회피 도약 클립이 원래 나아가는 속도(m/s). Dodge = 4.08(0.67초에 2.72m). " +
             "도약은 이 속도를 밑으로 두고 회피 거리에 맞춰 최대 1.8배까지 올려 민다.")]
    [SerializeField, Min(0.1f)] private float dodgeClipSpeed = 4.08f;
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
    public StaggerState StaggerState { get; private set; }
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
    private UnitController blockThreat;
    private float attackLockedUntil;
    private float poiseImmuneUntil;
    private bool pendingIsComboFinisher;
    // 이번 스윙이 발차기인가. 피해와 강인도 계산이 갈린다.
    private bool pendingIsKick;
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

    // 원거리 공격이 겨누는 지점. 발밑이 아니라 몸통 한가운데다 —
    // transform.position으로 쏘면 화살이 발등에 꽂힌다.
    public Vector3 AimPoint => bodyCollider != null ? bodyCollider.bounds.center : transform.position + Vector3.up;

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
    private int kickAnimationHash;
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
    private float kickAnimationDuration;
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

    public float BackpedalSpeed => backpedalSpeed;

    // 도약이 실제로 나아갈 속도. 클립이 원래 나아가는 속도를 밑으로 두고, 회피 거리를
    // 클립 길이 안에 소화할 만큼 올린다. 위로는 1.8배까지만 — 그 이상은 뛰는 중에도 눈에 띄게 밀린다.
    public float DodgeMoveSpeed(float distance)
    {
        float duration = Mathf.Max(0.05f, dodgeAnimationDuration);
        return Mathf.Clamp(distance / duration, dodgeClipSpeed, dodgeClipSpeed * 1.8f);
    }

    // 회피 도약을 실제 이동으로 옮긴다.
    //
    // NavMeshAgent에 목적지를 주지 않고 직접 미는 이유: 에이전트는 가속(8m/s^2)을 거치므로
    // 4m/s에 닿는 데만 0.5초가 걸리는데 도약 자체가 0.67초다. 목적지를 주면 클립은 뛰는데
    // 몸은 기어간다. 발놀림(UpdateCombatFootwork)이 같은 이유로 agent.Move를 쓴다.
    // 뛰는 동안은 발이 땅에 닿아 있지 않으므로 등속으로 밀어도 미끄러져 보이지 않는다.
    public void MoveDodge(Vector3 direction, float speed, float deltaTime)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        agent.Move(direction * (speed * deltaTime));
    }

    // 지금 실제로 땅 위를 나아가는 속도.
    // 재생 배속은 "요청한 속도"가 아니라 이 값에 맞춰야 한다 — NavMeshAgent는 가속 중이거나
    // 코너를 돌 때, 다른 유닛을 피할 때 요청보다 느리게 간다. 그 구간마다 다리가 헛돈다.
    public float CurrentMoveSpeed
    {
        get
        {
            if (agent == null || !agent.enabled) return 0f;
            Vector3 v = agent.velocity;
            v.y = 0f;
            return v.magnitude;
        }
    }

    // 마지막으로 물러난 뒤에 한 번이라도 공격했는가.
    //
    // 원거리 유닛의 "가까워지면 즉시 물러난다"를 조건 없이 지키면, 한 번 물러나서 벌어지는
    // 거리가 임계보다 작을 때 Attack에 들어서자마자 다시 Evade로 나가 한 발도 쏘지 못한다
    // (물러나는 0.9초 동안 적도 따라오므로 실제로 벌어지는 건 0.5m 남짓이다).
    // 물러난 직후 한 발은 반드시 쏘게 해서 그 왕복을 끊는다.
    public bool HasAttackedSinceEvade { get; private set; }

    // EvadeState가 물러나기 시작할 때 부른다.
    public void MarkEvadeStarted()
    {
        HasAttackedSinceEvade = false;
    }
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
        stats.ResetPoise();
        poiseImmuneUntil = 0f;
        ResetCombatRecord();
        ResetCombatRuntime();
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
        kickAnimationHash = ResolveStateHash(kickStateName);
        blockAnimationHash = ResolveStateHash(blockStateName);
        potionAnimationHash = ResolveStateHash(potionStateName);
        healAnimationHash = ResolveStateHash(healStateName);
        dodgeAnimationHash = ResolveStateHash(dodgeStateName);
        hitAnimationHash = ResolveStateHash(hitStateName);
        deathAnimationHash = ResolveStateHash(deathStateName);

        hasWalkAnimationState = walkAnimationHash != 0;

        CacheMoveSpeedParameter();

        skillAnimationDuration = GetAnimationClipDuration(skillStateName, 1f);
        kickAnimationDuration = GetAnimationClipDuration(kickStateName, 0.8f);
        potionAnimationDuration = GetAnimationClipDuration(potionStateName, 0.8f);
        healAnimationDuration = healAnimationHash != 0
            ? GetAnimationClipDuration(healStateName, skillAnimationDuration)
            : skillAnimationDuration;
        dodgeAnimationDuration = GetAnimationClipDuration(dodgeStateName, 0.35f);
        hitAnimationDuration = GetAnimationClipDuration(hitStateName, 0.35f);
        deathAnimationDuration = GetAnimationClipDuration(deathStateName, 1.5f);

        // 피격 방향·방어 반동·경직·스트레이프처럼 "있으면 쓰고 없으면 대체"인 리액션 모션들.
        // 기본 모션 길이가 먼저 잡혀 있어야 대체값을 정할 수 있어서 마지막에 부른다.
        CacheCombatAnimationHashes();
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
        hasSwungAtLeastOnce = false;
    }

    // 공격 모션이 하나라도 있는가. 없으면 CanAttack이 false가 되어 공격 상태로 들어가지 않는다.
    public bool HasAttackAnimation => hasAttackAnimation;

    // 콤보가 한 바퀴를 다 돌아 다음 스윙이 다시 1번부터 시작하는 시점. AttackState가 이때만
    // 스킬 같은 재량 전환을 검토한다 — 콤보 스텝 사이마다 검토하면 스윙 하나 끝날 때마다
    // 다른 상태로 튀어서 콤보가 거의 끝까지 이어지지 않는다.
    //
    // 한 번도 휘두르지 않은 상태를 레커버리로 세면 안 된다. attackComboIndex는 처음부터 0이라
    // 교전에 들어선 첫 프레임에 이 값이 참이 되고, 유닛이 공격을 하기도 전에 스킬부터 쓴다.
    // 원거리 유닛에서 특히 드러났다 — 스폰되자마자 사거리 안이라 첫 행동이 곧바로 스킬이 되는데,
    // 스킬 모션은 근접 동작이라 7m 밖에서 발차기를 하며 피해를 넣었다.
    public bool IsComboRecoveryPoint => hasSwungAtLeastOnce && attackComboIndex == 0;

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
        // 히트스톱 해제와 방어 자세 복귀는 상태머신보다 먼저 처리한다 — 상태의 Update가
        // AnimatorSpeed로 타이머를 재기 때문에, 그 전에 이번 프레임의 배속이 확정돼 있어야 한다.
        TickCombat();

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
        ReleaseTargetCount();
        CurrentTarget = null;
#if UNITY_EDITOR
        currentTargetName = "";
#endif
    }

    // ------------------------------------------------------------------
    // 나를 노리고 있는 적 수
    //
    // "이 유닛에게 몇 명이 붙어 있는가"는 타깃을 고를 때마다 후보마다 필요하다(뭉치기·혼잡도).
    // 그때그때 팀 전체를 훑으면 유닛 수의 세제곱으로 커진다 — 67유닛이면 스캔 한 주기에 7만 번,
    // 유닛이 두 배면 여덟 배가 된다. 그래서 세지 않고 들고 있는다.
    //
    // 더하고 빼는 자리는 네 곳뿐이다: 타깃을 잡을 때(AssignTarget), 놓을 때(ClearTarget),
    // 전장에 들어올 때(Register), 나갈 때(Unregister — 죽거나 꺼지면 여기로 온다).
    // 그 넷 밖에서 CurrentTarget을 건드리면 숫자가 어긋나므로 대입은 AssignTarget 한 곳으로 모아 둔다.
    // ------------------------------------------------------------------

    private readonly int[] attackersByTeam = new int[3];
    private bool countedOnTarget;

    // 지금 이 유닛을 노리고 있는 team 소속 유닛 수.
    public int AttackersFrom(UnitTeam team) => attackersByTeam[(int)team];

    private void AddAttacker(UnitTeam team, int delta)
    {
        int index = (int)team;
        attackersByTeam[index] = Mathf.Max(0, attackersByTeam[index] + delta);
    }

    // 레지스트리에 들고 날 때 호출된다. 죽은 유닛은 레지스트리에서 빠지므로 자연히 숫자에서도 빠진다.
    internal void HoldTargetCount()
    {
        if (countedOnTarget || CurrentTarget == null) return;

        countedOnTarget = true;
        CurrentTarget.AddAttacker(team, 1);
    }

    internal void ReleaseTargetCount()
    {
        if (!countedOnTarget) return;

        countedOnTarget = false;
        // 대상이 이미 파괴됐으면 숫자도 그와 함께 사라진 것이라 뺄 곳이 없다.
        if (CurrentTarget != null) CurrentTarget.AddAttacker(team, -1);
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
            stateMachine.CurrentState == StaggerState ||
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
               !IsAttackAnimationLocked &&
               // 스윙과 스윙 사이의 호흡. 이게 없으면 클립이 끝난 프레임에 곧바로 다음 스윙이
               // 나가 쉼 없이 칼을 돌린다. 그 사이 시간에 AttackState가 발놀림을 한다.
               IsSwingReady &&
               // 마주 보기 전에는 휘두르지 않는다. 도착하자마자 등을 진 채 스윙을 시작하면
               // 모션이 비스듬히 나갈 뿐 아니라, 타격 판정(attackArcAngle)에서 그대로 빗나간다.
               // 몸을 돌리는 것도 전투의 일부다 — 그동안 상대는 먼저 칠 기회를 얻는다.
               IsFacingTarget(attackFacingTolerance);
    }

    public bool IsAttackAnimationLocked => Time.time < attackLockedUntil;

    public bool CanUseSkill()
    {
        return skillAnimationHash != 0 &&
               // 스킬은 여는 수가 아니다. 한 번도 휘두르지 않았는데 먼저 나가면 교전의 첫 동작이
               // 늘 스킬로 똑같아진다. 원거리 유닛에서 특히 두드러졌다 — 스폰되자마자 사거리에
               // 들어서므로 "입장하자마자 스킬"이 매 전투 고정 연출이 됐다.
               // AttackState는 콤보 레커버리로 한 번 더 거르지만, ChaseState는 이 검사가 전부다.
               hasSwungAtLeastOnce &&
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

    // 지금 방패를 올릴 수 있고, 올릴 이유도 있는가.
    //
    // "누가 나를 노리고 휘두르는가"를 여기서 찾지 않는다는 점이 중요하다. 그 감시는
    // TickThreatAwareness가 매 프레임 따로 돌린다 — 이 메서드는 공격 잠금이 풀린 짧은
    // 순간에만 불리기 때문에, 여기서 위협을 처음 알아채고 반응 시간까지 채우려 하면
    // 손이 자유로운 시간이 모자라 방어가 거의 발동하지 않는다(그게 예전 동작이었다).
    // 여기서는 이미 알아채고 반응까지 끝난 위협이 있는지만 확인한다.
    public bool CanBlock()
    {
        blockThreat = null;
        if (!CanEverBlock()) return false;
        if (!HasReactedToThreat) return false;

        blockThreat = noticedThreat;
        return true;
    }

    // 방어 중 나를 노리는 적을 향해 돈다. BlockState가 매 프레임 불러 방패 방향을 맞춘다 —
    // 그러지 않으면 방어 자세로 굳어 있는 동안 위협이 옆/뒤로 돌아가도 계속 정면으로 오인해서
    // 정면 180도 판정(IsWithinFrontArc)에 실패해 버린다.
    // 방어 중 회전은 평소보다 느리다(blockTurnSpeed < rotationSpeed) — 위협이 등 뒤로 돌아가면
    // 다 따라가지 못해서 정면 180도 판정에 허점이 생겨야 방어가 지나치게 완벽해지지 않는다.
    public void FaceBlockThreat() => FaceDirection(blockThreat, blockTurnSpeed);

    // 방어 중 나를 노리고 휘두르는 적을 다시 찾는다. 진입 시점에 잡아 둔 위협은 금방 낡는다 —
    // 그 적이 스윙을 끝내거나 쓰러져도 계속 그쪽을 보고 방패를 든 채 서 있게 된다.
    // 돌려주는 값은 "아직 막을 것이 남았는가"이고, BlockState가 이걸로 자세를 풀 시점을 정한다.
    // (CanBlock을 그대로 쓸 수 없는 이유: 방금 자세를 잡아 blockCooldown에 걸려 있어서
    //  방어 중에는 항상 false가 나온다.)
    public bool RefreshBlockThreat()
    {
        blockThreat = UnitRegistry.FindTelegraphingAttacker(this);
        return blockThreat != null;
    }

    // 방어 판정의 정면 180도 부채꼴. 공격자가 내 전방 반구 안에 있으면(코사인 0 이상) 막을 수 있고,
    // 그 밖(등 뒤~옆 뒤쪽)이면 방어 자세여도 막지 못한다.
    private bool IsWithinFrontArc(Vector3 attackerPosition)
    {
        Vector3 toAttacker = attackerPosition - transform.position;
        toAttacker.y = 0f;
        if (toAttacker.sqrMagnitude <= 0.0001f) return true;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f) return true;

        return Vector3.Dot(forward.normalized, toAttacker.normalized) >= 0f;
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
    public bool ShouldRetreatForSurvival()
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
        TakeDamage(damage, attacker, applyKnockback, fromSkill, 0f);
    }

    // poiseDamage: 이 피격이 강인도를 얼마나 깎는지. ApplyAttackDamage/ApplySkillDamage만 실제 값을
    // 넘긴다 — 그 밖의 경로(예: TakeBleedDamage 계열)는 경직을 유발하지 않는다.
    public void TakeDamage(int damage, UnitController attacker, bool applyKnockback, bool fromSkill, float poiseDamage)
    {
        // 이미 죽은 유닛에 피가 튀거나 피격 상태로 되돌아가지 않도록 여기서 끊는다.
        if (IsDead) return;

        // 방패/무기로는 정면 180도만 막는다. 등 뒤나 옆 뒤쪽에서 들어온 공격은 방어 자세여도
        // 그대로 맞는다 — 아군끼리 서로 등을 지켜줘야 하는 이유가 된다.
        bool inFrontArc = attacker == null || IsWithinFrontArc(attacker.transform.position);
        bool wantsToBlock = IsBlocking && inFrontArc;

        // 방패를 올린 직후에 들어온 공격은 막는 것이 아니라 통째로 흘려낸다.
        // 흘러가면 피해도 강인도 소모도 없으므로 아래 계산 자체를 건너뛴다.
        if (wantsToBlock && TryPerfectGuard(attacker)) return;

        bool wasBlocking = wantsToBlock;

        // 같은 공격이라도 어디서, 어떤 처지에서 맞았느냐로 실제 피해가 갈린다.
        // 이 세 가지가 "위치를 잡는 것"과 "먼저 내지르는 것"에 값을 매긴다.
        float damageMultiplier = 1f;
        if (attacker != null && !inFrontArc)
        {
            damageMultiplier *= stats.backstabDamageMultiplier;
            poiseDamage *= stats.backstabPoiseMultiplier;
        }
        // 내가 방금 칼을 내지르고 거두는 중이라 반응할 수 없는 상태.
        if (IsInAttackRecovery) damageMultiplier *= stats.recoveryVulnerabilityMultiplier;
        // 이미 자세가 무너져 있는 상태.
        if (IsStaggered) damageMultiplier *= stats.staggerDamageMultiplier;

        int incoming = damage > 0 ? Mathf.Max(1, Mathf.RoundToInt(damage * damageMultiplier)) : damage;

        int hpBefore = stats.currentHp;
        stats.TakeDamage(incoming, wasBlocking);
        int dealt = hpBefore - stats.currentHp;

        if (wasBlocking)
        {
            // 막았다는 것 자체가 보여야 한다. 예전에는 피가 안 튀는 것 말고는 아무 반응도 없었다.
            PlayBlockImpact();

            if (debugLogs)
            {
                Debug.Log($"[UnitController] {name} 방어 성공 — {(attacker != null ? attacker.name : "?")}의 공격을 막음 " +
                          $"(피해 {incoming} → {dealt}, 남은 강인도 {stats.currentPoise - poiseDamage:0}/{stats.maxPoise:0})");
            }
        }

        RecordDamage(dealt, attacker);
        RecordHitDirection(attacker);
        if (!wasBlocking) SpawnBloodEffect(attacker);
        if (emotion != null) emotion.NotifyDamaged(dealt, fromSkill);

        // 칼이 닿은 순간 양쪽의 애니메이션을 아주 짧게 눌러 붙인다. 막힌 타격은 살에 박히는
        // 것이 아니라 튕겨 나가는 것이라 더 짧게 끊는다.
        ApplyImpactHitStop(attacker, wasBlocking ? 0.6f : 1f);

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

        // 강인도: 면역 중이 아니면 이번 피격으로 깎는다.
        //
        // 막아낸 타격도 그대로 깎는다는 것이 핵심이다. 피해는 0이지만 버티는 힘은 닳으므로,
        // 방어가 공짜 무적이 되지 않는다 — 예전에는 이 역할을 방어 지구력(Block Stamina)이라는
        // 별도 자원이 맡았는데, 강인도와 하는 일이 같아서 하나로 합쳤다.
        bool poiseBroken = false;
        if (Time.time >= poiseImmuneUntil && poiseDamage > 0f)
        {
            stats.currentPoise -= poiseDamage;
            if (stats.currentPoise <= 0f) poiseBroken = true;
        }

        if (poiseBroken)
        {
            stats.ResetPoise();
            poiseImmuneUntil = Time.time + stats.poiseBreakImmunity;

            // 막고 있다가 강인도가 깨졌다 = 가드가 뚫린 것. 한 대 크게 맞은 것과는 무너지는
            // 정도가 다르다 — 방패가 젖혀지며 몇 초를 통째로 서 있어야 하는 진짜 빈틈이 된다.
            if (wasBlocking)
            {
                Stagger(stats.staggerDuration, true);
                return;
            }

            InterruptCurrentAction();
            ChangeState(HitState);
            return;
        }

        // 강인도가 안 깨졌으면 애니메이션은 끊지 않는다 — 슈퍼아머는 아니라서 살짝 밀리기만 하고
        // 곧장 다시 싸운다(콤보 마무리나 스킬만 진짜 경직을 유발한다).
        if (attacker != null) ApplyMicroPushback(attacker.transform.position);
        InterruptCurrentAction();
        ChangeState(AttackState);
    }

    // 강인도가 깨지지 않은 일반 피격의 즉각적인 밀림. HitState의 시간 분산 넉백과 달리 상태를
    // 바꾸지 않고 그 자리에서 한 번에 살짝 밀어서 애니메이션을 끊지 않고도 타격감을 준다.
    private void ApplyMicroPushback(Vector3 attackerPosition)
    {
        if (stats.poiseHitPushback <= 0f) return;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Vector3 direction = transform.position - attackerPosition;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f) return;

        agent.Move(direction.normalized * stats.poiseHitPushback);
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

    // 공격 클립의 애니메이션 이벤트가 부르는 실제 타격 순간.
    //
    // 예전에는 여기서 IsTargetValid만 보고 CurrentTarget에게 무조건 피해를 넣었다. 스윙이
    // 시작된 뒤 상대가 5m 밖으로 달아나도, 등 뒤로 돌아가도 그대로 맞았다 — 빗나감이라는
    // 것이 없으니 거리도 각도도 전투에서 아무 의미가 없었다. 이제 이 시점에 다시 잰다.
    public void ApplyAttackDamage()
    {
        // 시체가 휘두르던 칼의 이벤트가 뒤늦게 도착할 수 있다. 죽었으면 아무 일도 없다.
        if (IsDead) return;

        // 여기를 지나면 준비 동작이 끝나고 회수 동작이 시작된다.
        hasStruckThisSwing = true;

        UnitController victim = ResolveSwingVictim();
        // 발차기는 베는 대신 무너뜨린다 — 피해는 낮고 강인도 피해는 크다.
        float poiseDamage = pendingIsKick
            ? stats.poiseDamageKick
            : stats.poiseDamagePerHit + (pendingIsComboFinisher ? stats.poiseDamageComboFinisherBonus : 0f);
        int damage = ScaleDamage(pendingIsKick
            ? Mathf.RoundToInt(stats.attackDamage * stats.kickDamageMultiplier)
            : stats.attackDamage);

        // 활은 맞았는지를 근접과 똑같이 여기서 정하되, 그 한 방을 화살에 실어 보낸다.
        // 빗나간 스윙도 화살은 떠난다 — 허공으로 날아가는 화살이 곧 빗나갔다는 표시다.
        bool fired = TryFireProjectile(victim, damage, poiseDamage, false);

        if (victim == null)
        {
            OnSwingMissed();
            return;
        }

        // 화살이 떠났으면 피해는 투사체가 도착할 때 들어간다(WeaponProjectile).
        if (!fired) victim.TakeDamage(damage, this, false, false, poiseDamage);
    }

    // 주무기가 투사체를 들고 있으면 쏘고 true. 근접 무기는 false를 돌려주고 그 자리에서 때린다.
    private bool TryFireProjectile(UnitController victim, int damage, float poiseDamage, bool fromSkill)
    {
        if (equipment == null) return false;

        WeaponDefinition weapon = equipment.MainHand;
        if (weapon == null || weapon.projectile == null) return false;

        Transform origin = equipment.ProjectileOrigin;
        if (origin == null) return false;

        // 겨눈 상대가 있으면 그쪽으로, 없으면 화살이 향한 쪽으로 그대로 날린다 — 그 방향이 곧 활이 겨눈 방향이다.
        Vector3 direction = victim != null ? victim.AimPoint - origin.position : origin.forward;
        WeaponProjectile.Fire(weapon.projectile, origin.position, direction, this, victim, damage, poiseDamage, fromSkill);

        equipment.ReleaseArrow();
        return true;
    }

    public void ApplySkillDamage()
    {
        if (IsDead) return;

        hasStruckThisSwing = true;

        UnitController victim = ResolveSwingVictim();
        int damage = ScaleDamage(stats.skillDamage);

        // 스킬도 평타와 같은 규칙을 따른다 — 원거리 무기면 그 한 방을 투사체에 실어 보낸다.
        // 그러지 않으면 화살은 평타에만 날아가고, 스킬은 허공을 향해 피해만 들어간다.
        bool fired = TryFireProjectile(victim, damage, stats.poiseDamageSkill, true);

        if (victim == null)
        {
            OnSwingMissed();
            return;
        }

        if (!fired) victim.TakeDamage(damage, this, true, true, stats.poiseDamageSkill);
    }

    // 공포에 빠진 유닛은 원작 설정대로 능력치가 깎인다. 0이 되어 무해해지지는 않도록 최소 1.
    private int ScaleDamage(int damage)
    {
        return Mathf.Max(1, Mathf.RoundToInt(damage * EmotionMultiplier));
    }

    public void TriggerAttack()
    {
        // 무엇으로 칠지는 지금 상황이 정한다. 콤보 순환은 그중 기본값일 뿐이다(ChooseAttack).
        AttackChoice choice = ChooseAttack();

        pendingIsKick = choice.IsKick;
        attackAnimationDuration = choice.Duration;
        attackLockedUntil = Time.time + attackAnimationDuration;

        // 콤보 마무리 보너스는 "여러 단을 끝까지 이어붙인 것"에 대한 보상이다.
        // 공격 모션이 하나뿐인 유닛(고블린)은 이어붙일 콤보가 없으므로 해당 없다.
        pendingIsComboFinisher = choice.IsFinisher;

        // 이번 스윙은 아직 아무도 때리지 않았다. 이 플래그가 준비 동작(막을 수 있는 구간)과
        // 회수 동작(무방비 구간)을 가른다 — 클립 길이로 추정하지 않고 타격 이벤트로 안다.
        hasStruckThisSwing = false;
        hasSwungAtLeastOnce = true;

        // 물러난 뒤 한 발은 쐈다는 표시. 원거리 유닛이 Attack↔Evade만 오가는 것을 막는다.
        HasAttackedSinceEvade = true;

        // 시위를 당기기 시작한다. 앞선 화살이 떠나면서 감춰 둔 화살을 다시 물린다.
        if (equipment != null) equipment.BeginDraw();

        lungeRemaining = stats.lungeMaxDistance;

        // 다음 스윙까지의 호흡. 콤보를 완주한 뒤에는 길게 숨을 고르고, 그 밖에는 짧게 끊는다.
        float recovery = pendingIsComboFinisher ? stats.comboFinisherRecoveryTime : stats.attackRecoveryTime;
        recovery *= 1f + Random.Range(-stats.attackRecoveryRandomness, stats.attackRecoveryRandomness);
        recovery = Mathf.Max(0f, recovery);
        nextSwingReadyTime = attackLockedUntil + recovery;

        // 이번 틈에 자리를 옮길지 여기서 한 번만 정한다(FootworkThisGap 주석 참조).
        footworkThisGap = recovery >= minFootworkWindow;

        PlayAnimation(choice.Hash, true);
        attackComboIndex = choice.NextComboIndex;
    }

    // 이번 스윙에 낼 것 하나.
    private struct AttackChoice
    {
        public int Hash;
        public float Duration;
        public bool IsKick;
        public bool IsFinisher;
        public int NextComboIndex;
    }

    // 기본공격을 상황으로 고른다.
    //
    // 예전에는 콤보 인덱스만 돌렸다. 무기를 든 손이 늘 같은 순서로 움직이니, 상대가 방패를
    // 올리고 있든 자세가 무너져 있든 똑같은 스윙이 나갔다 — 고를 것이 없는 전투였다.
    //
    // 규칙은 위에서부터 본다. 어느 것도 걸리지 않으면 콤보 순환으로 떨어지므로,
    // 규칙을 전부 지우면 예전 동작 그대로가 된다.
    private AttackChoice ChooseAttack()
    {
        int step = attackComboIndex % attackAnimationHashes.Length;

        AttackChoice choice;
        choice.Hash = attackAnimationHashes[step];
        choice.Duration = attackAnimationDurationsPerStep[step];
        choice.IsKick = false;
        choice.IsFinisher = attackAnimationHashes.Length > 1 && step == attackAnimationHashes.Length - 1;
        choice.NextComboIndex = (step + 1) % attackAnimationHashes.Length;

        // ① 상대가 방패를 올렸다 → 발차기.
        //
        // 무기 스윙은 막히면 피해가 통째로 흘러가고 강인도만 조금 깎는다. 같은 스윙을 계속
        // 넣으면 가드가 열릴 때까지 아무 일도 일어나지 않는다. 발차기는 그 강인도를 크게
        // 깎아(poiseDamageKick) 가드 자체를 연다.
        //
        // 다만 지금 콘텐츠에서는 이 규칙이 거의 잠들어 있다 — CanEverBlock 주석대로
        // "방어는 아군만" 하기 때문에, 아군이 때리는 적은 애초에 방패를 올리지 않는다.
        // 막는 적이 생기면 그때 저절로 살아난다.
        if (CanKick() && IsTargetGuarding()) return KickChoice();

        // ② 한 번 더 밀면 무너진다 → 발차기로 끊는다.
        //
        // 평타로는 모자라고 발차기면 깨지는 구간이 있다(평타 15 / 발차기 45). 그 구간에서
        // 평타를 넣으면 상대는 버티고, 발차기를 넣으면 그 자리에서 무너져 다음 콤보가 통째로
        // 들어간다. 적이 방어를 하지 않는 지금, 실제로 발차기가 나가는 것은 대개 이 규칙이다.
        if (CanKick() && IsTargetPoiseRipe()) return KickChoice();

        // ③ 상대가 무너져 있다 → 콤보의 마지막 단으로 건너뛴다.
        //
        // 무너진 몇 초는 상대가 내준 진짜 빈틈이고 받는 피해도 커진다(staggerDamageMultiplier).
        // 그 자리에 콤보 1단을 넣으면 가장 약한 스윙으로 가장 좋은 기회를 쓰는 셈이 된다.
        // ②가 무너뜨린 직후가 대개 여기로 이어진다.
        if (attackAnimationHashes.Length > 1 && IsTargetStaggered())
        {
            int last = attackAnimationHashes.Length - 1;
            choice.Hash = attackAnimationHashes[last];
            choice.Duration = attackAnimationDurationsPerStep[last];
            choice.IsFinisher = true;
            choice.NextComboIndex = 0;
        }

        return choice;
    }

    private AttackChoice KickChoice()
    {
        AttackChoice choice;
        choice.Hash = kickAnimationHash;
        choice.Duration = kickAnimationDuration;
        choice.IsKick = true;
        choice.IsFinisher = false;
        // 발차기는 콤보의 일부가 아니다. 순서를 소비하지 않고 제자리에 둬서,
        // 무너뜨린 뒤에 하던 콤보를 그대로 이어 붙인다.
        choice.NextComboIndex = attackComboIndex;
        return choice;
    }

    // 발차기 한 번이면 무너지지만 평타로는 안 되는 구간에 상대가 들어와 있는가.
    // 기준은 때리는 쪽의 수치다 — 강인도를 깎는 것은 이 유닛의 발과 무기이기 때문이다.
    private bool IsTargetPoiseRipe()
    {
        if (!IsTargetValid()) return false;
        if (stats.poiseDamageKick <= stats.poiseDamagePerHit) return false;

        float poise = CurrentTarget.Stats.currentPoise;
        return poise > stats.poiseDamagePerHit && poise <= stats.poiseDamageKick;
    }

    // 발차기를 쓸 수 있는가. 모션이 있어야 하고, 근접 유닛이어야 한다 — 사거리가
    // minKeepDistanceRange 이상인 원거리 유닛은 애초에 발이 닿지 않는 거리에서 싸운다.
    private bool CanKick()
    {
        return kickAnimationHash != 0 && stats.attackRange < minKeepDistanceRange;
    }

    private bool IsTargetGuarding()
    {
        return IsTargetValid() && CurrentTarget.IsBlocking;
    }

    private bool IsTargetStaggered()
    {
        return IsTargetValid() && CurrentTarget.IsStaggered;
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
            // 방패를 올린 시각. 퍼펙트 가드 창의 기준점이 된다.
            guardRaisedTime = Time.time;
            // 이번 자세로 흘려낼 수 있는지는 드는 순간에 정해진다 — 상대의 동작을 제대로
            // 읽었느냐이지, 맞을 때마다 다시 굴릴 문제가 아니다.
            perfectGuardArmed = Random.value < stats.perfectGuardChance;
            blockImpactUntil = 0f;
            PlayAnimation(blockAnimationHash, true);
        }
        else
        {
            blockImpactUntil = 0f;
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

    public void FaceTarget() => FaceDirection(CurrentTarget, rotationSpeed);

    private void FaceDirection(UnitController facingTarget, float turnSpeed)
    {
        if (facingTarget == null) return;

        Vector3 direction = facingTarget.transform.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);

        // RotateTowards는 초당 turnSpeed도씩 돌고 목표에 정확히 도달해 멈춘다.
        //
        // 예전에는 Slerp에 (turnSpeed * deltaTime / 180)을 t로 넣었다. 그건 각속도 제한이
        // 아니라 지수 감쇠라서 두 가지가 어긋났다:
        //  - 끝까지 수렴하지 않는다. 720으로 두면 프레임당 남은 각도의 6.7%만 좁히므로,
        //    90도를 도는 데 0.5초를 써도 여전히 11도가 남는다. 도착하자마자 휘두르는
        //    첫 공격이 늘 비스듬히 나가고, 스윙 도중에도 몸이 계속 돌아가던 원인이다.
        //  - 프레임레이트에 따라 실제 회전 속도가 달라진다(t가 dt에 선형인데 감쇠는 지수라서).
        // 이제 rotationSpeed / blockTurnSpeed가 이름 그대로 초당 각도를 뜻한다.
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            turnSpeed * Time.deltaTime);
    }

    // 상대를 충분히 마주 보고 있는가. 타깃이 없으면 판단할 근거가 없으니 true로 둔다.
    public bool IsFacingTarget(float toleranceDegrees)
    {
        if (CurrentTarget == null) return true;

        Vector3 direction = CurrentTarget.transform.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f) return true;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f) return true;

        return Vector3.Angle(forward, direction) <= toleranceDegrees;
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

    // 맞은 방향에 맞는 피격 모션을 고른다(HitFront/Back/Left/Right).
    // 방향 클립이 하나도 없는 리그는 예전처럼 Hit 하나로 떨어진다.
    public void TriggerHit()
    {
        int hash = ResolveHitAnimationHash();
        PlayAnimation(hash != 0 ? hash : hitAnimationHash, true);
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
        StaggerState = new StaggerState(this);
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

    // CurrentTarget에 값을 넣는 유일한 자리. 여기서만 넣어야 "붙어 있는 적 수"가 어긋나지 않는다.
    private void AssignTarget(UnitController target)
    {
        ReleaseTargetCount();
        CurrentTarget = target;
        HoldTargetCount();
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
