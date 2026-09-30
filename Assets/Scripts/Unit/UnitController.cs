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
    [Tooltip("피 색상. 프리팹 원본의 진하기와 알파는 유지되고 색조만 이 색으로 바뀐다. (고블린은 초록)")]
    [SerializeField] private Color bloodColor = new Color(0.6f, 0f, 0f, 1f);

        [Header("Movement")]
    [SerializeField] private float runDistance = 4f;
    [SerializeField] private float destinationUpdateInterval = 0.15f;
    [SerializeField] private float chasePredictionTime = 0.25f;
    [SerializeField] private float rotationSpeed = 720f;
    [Tooltip("방어 중 위협 쪽으로 도는 속도. 일반 회전보다 느려야 등 뒤에서 들어오는 공격에 허점이 생긴다.")]
    [SerializeField] private float blockTurnSpeed = 240f;
    [Tooltip("이미 휘두르기 시작한 뒤 상대를 따라 도는 속도. 평소 회전보다 훨씬 느려야 한다 — " +
             "공격 클립은 제자리에서 베는 동작이라, 그 위에서 몸이 평소 속도로 돌아가면 " +
             "발이 땅에 붙지 않고 미끄러지는 것이 그대로 보인다. 0이면 스윙 도중에는 아예 안 돈다.")]
    [SerializeField] private float attackTurnSpeed = 120f;

    [Header("Ranged (역할이 없는 유닛의 대비책)")]
    // 아래 둘은 직업 역할(UnitStats.role / keepDistanceRange)이 정해지지 않은 유닛에만 쓰인다.
    // 로스터 캐릭터는 CharacterBattleSpawner가 역할을 얹어 주므로 이 값을 거치지 않는다 —
    // 프리팹에 직접 스탯을 넣는 유닛(고블린)과 옛 설정이 예전과 똑같이 동작하도록 남겨 둔 것이다.
    [Tooltip("역할이 없는 유닛 한정. 사거리가 이 값 이상이면 원거리로 취급해 거리를 벌린다.")]
    [SerializeField] private float minKeepDistanceRange = 4f;
    [Tooltip("역할이 없는 원거리 유닛이 물러서기 시작하는 거리 = 사거리 x 이 비율. " +
             "쫓아오는 근접 유닛과 이동 속도가 비슷해서 물러나 봐야 거의 못 벌리므로, " +
             "\"적이 실제로 때릴 수 있게 되는 순간\"에 맞춰 잡는다 — 사거리 9짜리에 0.3이면 2.7m로, " +
             "고블린의 타격 도달(1.85m) 바로 바깥이다. 더 올리면 사정권 밖의 적에게도 물러나느라 " +
             "쏘는 시간이 사라진다.")]
    [SerializeField, Range(0f, 1f)] private float keepDistanceRatio = 0.3f;

    [Tooltip("가장 가까운 아군이 이보다 멀면(미터) 전선에서 떨어져 나온 것으로 보고, " +
             "간격을 벌릴 때 적 반대쪽이 아니라 아군 쪽으로 물러난다. 0이면 제한 없음.\n\n" +
             "이게 없으면 카이팅에 끝이 없다 — 물러나면 적이 따라오고, 따라오니까 또 물러난다. " +
             "실제로 궁수가 고블린 하나를 45m 밖까지 끌고 가서 파티가 두 무리로 쪼개지고 " +
             "사제의 치료 사거리(8m) 안에 아무도 남지 않는 것을 확인했다.\n\n" +
             "값은 실측으로 잡았다: 대형 안의 유닛은 최근접 아군이 1.1~3.3m였고 " +
             "혼자 도망친 궁수만 14.7m였다. 목숨이 걸린 후퇴(ShouldRetreatForSurvival)에는 " +
             "걸리지 않는다 — 그때는 전선을 등지고 멀리 달아나는 것이 맞다.")]
    [SerializeField, Min(0f)] private float regroupDistance = 8f;

    [Tooltip("목숨이 걸린 후퇴(ShouldRetreatForSurvival)에서 한 번에 달아나는 거리(미터). " +
             "간격 벌리기(evadeRange, 2.5m)와 같은 값을 쓰면 도망 자체가 성립하지 않는다. " +
             "2.5m마다 목적지에 닿아 감속하고 다시 0에서 가속하기를 반복하므로 최고 속도에 한 번도 닿지 못한다 — " +
             "실측에서 최고 4.37m/s짜리 탱커의 평균 속도가 2.81m/s로 떨어져, 4.0m/s로 꾸준히 달려오는 " +
             "고블린에게 거리가 도로 좁혀졌다. 한 번에 멀리 잡아야 달리는 도중에 멈추지 않는다.")]
    [SerializeField, Min(1f)] private float survivalFleeDistance = 12f;

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
    [Tooltip("배속이 목표값까지 따라가는 데 걸리는 시간(초). 0이면 즉시 반영한다. " +
             "실제 이동 속도는 가속과 감속, 회피로 매 프레임 출렁이는데 배속을 거기에 그대로 물리면 " +
             "한 번 달리는 동안에도 재생 속도가 계속 바뀐다. 대신 이만큼 늦게 따라가므로 " +
             "가감속 구간에서는 발이 조금 미끄러진다 — 그 둘을 맞바꾸는 값이다.")]
    [SerializeField, Min(0f)] private float moveSpeedDampTime = 0.15f;

    [Tooltip("화면 밖 유닛의 애니메이션 계산량을 줄인다. AlwaysAnimate는 보이지 않아도 전부 계산한다.")]
    [SerializeField] private AnimatorCullingMode animatorCullingMode = AnimatorCullingMode.CullUpdateTransforms;
    [Tooltip("사망 애니메이션이 끝나면 Animator를 꺼서 시체가 계속 애니메이션되지 않도록 한다.")]
    [SerializeField] private bool disableAnimatorAfterDeath = true;

#if UNITY_EDITOR
    // 인스펙터 확인용. GetType().Name / GameObject.name은 호출할 때마다 문자열을 새로 만들기 때문에
    // 동작·타깃이 바뀔 때마다 GC 쓰레기가 쌓인다. 빌드에는 포함하지 않는다.
    [SerializeField] private string currentBehaviorName;
    [SerializeField] private string currentTargetName;
    private BTNode<UnitController> debugTrackedNode;
#endif

    // 이 유닛의 판단 전체. 어떤 동작이 있고 무엇이 무엇보다 먼저인지는 UnitBehaviorTree에 있다.
    private BehaviorTree<UnitController> brain;

    // 클립의 타격 이벤트를 받아 넘겨주는 자리(Animator와 같은 오브젝트). 구독에 쓰는 델리게이트는
    // Awake에서 한 번만 만든다 — 메서드 그룹을 += 할 때마다 새 델리게이트가 할당되기 때문이다.
    private UnitAnimationEvents animationEvents;
    private Action attackHitHandler;
    private Action skillHitHandler;

    public UnitTeam Team => team;
    public UnitEmotion Emotion => emotion;
    // 이 전투 유닛이 어떤 로스터 캐릭터인지. 사망 시 영구 사망 처리에 쓴다.
    public CharacterSO SourceCharacter { get; private set; }

    // 전투 기여도 — MVP 선정과 경험치 정산이 이 값을 읽는다(UnitCombatRecord).
    private readonly UnitCombatRecord combatRecord = new UnitCombatRecord();
    public UnitCombatRecord CombatRecord => combatRecord;
    public int DamageDealt => combatRecord.DamageDealt;
    public int DamageTaken => combatRecord.DamageTaken;
    public int Kills => combatRecord.Kills;
    public UnitStats Stats => stats;
    public NavMeshAgent Agent => agent;
    public Animator Animator => animator;
    public TargetScanner Scanner => scanner;
    public TargetRef CurrentTarget { get; private set; }
    public bool IsDead => stats.IsDead;
    public bool IsBlocking { get; private set; }
    public bool HasMoveDestination { get; private set; }
    public bool IsRoamingMoveDestination { get; private set; }
    public Vector3 MoveDestination { get; private set; }
    public float RunDistance => runDistance;
    public float DestinationUpdateInterval => destinationUpdateInterval;

    private int currentAnimationHash;
    private Collider[] bodyColliders;

    // 이 유닛에 속한 콜라이더 전부. UnitRegistry가 시야 레이의 히트 중 유닛 몸통을 걸러내는 데 쓴다.
    public Collider[] BodyColliders => bodyColliders;

    // 원거리 공격이 겨누는 지점. 발밑이 아니라 몸통 한가운데다 —
    // transform.position으로 쏘면 화살이 발등에 꽂힌다.
    public Vector3 AimPoint => bodyCollider != null ? bodyCollider.bounds.center : transform.position + Vector3.up;

    // 목덜미. 물고 늘어지는 쪽이 붙잡는 자리다.
    //
    // 휴머노이드 리그면 목뼈를 그대로 쓴다 — 키가 제각각인 유닛들(고블린 1.65m, 아군 1.8m)에
    // 같은 높이를 쓰면 누구에게는 얼굴을, 누구에게는 가슴을 물게 된다. 리그에 목이 없으면
    // 몸통 위쪽으로 어림한다.
    public Vector3 NeckPoint
    {
        get
        {
            if (neckBone != null) return neckBone.position;
            return bodyCollider != null
                ? new Vector3(transform.position.x, bodyCollider.bounds.max.y - 0.15f, transform.position.z)
                : transform.position + Vector3.up * 1.4f;
        }
    }

    private Transform neckBone;

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
    private int skillAnimationHash;
    private int blockAnimationHash;
    private int potionAnimationHash;
    private int healAnimationHash;
    private int dodgeAnimationHash;
    private int hitAnimationHash;
    private int deathAnimationHash;
    private bool hasWalkAnimationState;

    private float skillAnimationDuration;
    private float potionAnimationDuration;
    private float healAnimationDuration;
    private float dodgeAnimationDuration;
    private float hitAnimationDuration;
    private float deathAnimationDuration;

    public float SkillAnimationDuration => skillAnimationDuration;
    public float PotionAnimationDuration => potionAnimationDuration;
    public float HealAnimationDuration => healAnimationDuration;
    // 회피 모션이 없는 리그(고블린)는 0. EvadeBehavior가 이 값으로 "뛸지 걸어서 뺄지"를 정한다.
    public float DodgeAnimationDuration => dodgeAnimationHash != 0 ? dodgeAnimationDuration : 0f;

    public float BackpedalSpeed => backpedalSpeed;

    public float SurvivalFleeDistance => survivalFleeDistance;

    // 도약이 실제로 나아갈 속도 — 클립이 원래 나아가는 속도 그대로다.
    //
    // 예전에는 "회피 거리를 클립 길이 안에 소화하도록" 최대 1.8배까지 올렸다. 그러면 원하는
    // 거리는 나오지만 다리보다 몸이 빨라져 발이 미끄러진다. 도약은 화면에 크게 보이는
    // 동작이라 그 미끄러짐이 그대로 읽힌다.
    //
    // 그래서 반대로 뒤집는다: 속도는 클립에 맞추고, 나아가는 거리는 그 결과로 정해진다
    // (기본 클립 기준 4.08m/s x 0.67초 = 약 2.7m). 거리를 바꾸고 싶으면 클립을 바꿔야 한다.
    public float DodgeMoveSpeed(float distance) => dodgeClipSpeed;

    // 회피 도약을 실제 이동으로 옮긴다. 목적지를 주지 않고 직접 민다(LocomotionPart.MoveDodge 주석 참조).
    public void MoveDodge(Vector3 direction, float speed, float deltaTime) =>
        Locomotion.MoveDodge(direction, speed, deltaTime);

    // 도약 중인가. 켜져 있는 동안은 다른 이동 수단이 이 유닛을 건드리지 않는다.
    public bool IsLeapingDodge => Locomotion.IsLeapingDodge;

    // 지금 실제로 땅 위를 나아가는 속도.
    // 재생 배속은 "요청한 속도"가 아니라 이 값에 맞춰야 한다 — NavMeshAgent는 가속 중이거나
    // 코너를 돌 때, 다른 유닛을 피할 때 요청보다 느리게 간다. 그 구간마다 다리가 헛돈다.
    public float CurrentMoveSpeed => Locomotion.CurrentSpeed;

    // 마지막으로 물러난 뒤에 한 번이라도 공격했는가.
    //
    // 원거리 유닛의 "가까워지면 즉시 물러난다"를 조건 없이 지키면, 한 번 물러나서 벌어지는
    // 거리가 임계보다 작을 때 Attack에 들어서자마자 다시 Evade로 나가 한 발도 쏘지 못한다
    // (물러나는 0.9초 동안 적도 따라오므로 실제로 벌어지는 건 0.5m 남짓이다).
    // 물러난 직후 한 발은 반드시 쏘게 해서 그 왕복을 끊는다.
    public bool HasAttackedSinceEvade { get; private set; }

    // 회피(EvadeBehavior)와 도주(FleeBehavior)가 물러나기 시작할 때 부른다.
    public void MarkEvadeStarted()
    {
        HasAttackedSinceEvade = false;
    }

    // "물러난 뒤 한 수를 냈다"를 세운다. 평타(TriggerAttack)와 영창 시작(BeginSpellCast)이 부른다 —
    // 마법사에게는 영창이 그 한 수다.
    private void MarkAttackedSinceEvade()
    {
        HasAttackedSinceEvade = true;
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
            // 출전 횟수는 스포너가 이 뒤에 올린다(CharacterBattleSpawner.SpawnAllies). 여기서 읽는 값이
            // "이번이 처음인가"다.
            emotion.Configure(source.hiddenStats, source.starCount, CharacterProgress.IsFirstBattle(source));
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
        BindAnimationEvents();
        if (scanner == null) scanner = GetComponent<TargetScanner>();
        if (bodyCollider == null) bodyCollider = GetComponent<Collider>();
        // 시야 판정이 자기 몸(과 래그돌 콜라이더)을 장애물로 세지 않도록 레지스트리에 넘길 목록.
        // 무기 모델의 콜라이더는 WeaponEquipper가 꺼 두므로 여기 들어오지 않아도 된다.
        bodyColliders = GetComponentsInChildren<Collider>(true);
        UnitRegistry.RegisterColliders(this);

        // 물어뜯는 쪽이 붙잡을 자리(NeckPoint). 리그를 매 프레임 뒤지지 않도록 한 번만 찾는다.
        if (animator != null && animator.isHuman)
        {
            neckBone = animator.GetBoneTransform(HumanBodyBones.Neck)
                    ?? animator.GetBoneTransform(HumanBodyBones.Head);
        }

        if (emotion == null) emotion = GetComponent<UnitEmotion>();
        if (equipment == null) equipment = GetComponent<WeaponEquipper>();
        if (equipment != null) equipment.WeaponAnimatorChanged += RefreshAttackAnimations;

        if (scanner != null) scanner.Initialize(this);

        // 배회 시각도 유닛마다 흩어 놓는다(LocomotionPart.ScatterRoamClock).
        Locomotion.ScatterRoamClock();
        if (emotion != null) emotion.Initialize(this);
        ApplyAgentSpeed(stats.runSpeed);
        // 에이전트가 회전을 맡는 구간(도주, 이동, 배회)도 코드가 돌릴 때와 같은 속도로 돌게 한다.
        // NavMeshAgent 기본값은 120도/초다. 등을 보이고 달아날 때는 적의 정반대를 봐야 해서 매번
        // 180도를 돌아야 하는데, 그 회전에만 1.5초가 걸렸다 — 그동안 유닛은 이동 방향으로 천천히
        // 돌면서 옆걸음으로 미끄러져 나간다. 회전 주도권을 둘로 나눈 이유는 SetCodeDrivenFacing 참조.
        if (agent != null) agent.angularSpeed = rotationSpeed;
        CacheAnimationHashes();
        ApplyAnimatorCulling();
    }

    private void CacheAnimationHashes()
    {
        idleAnimationHash = ToHash(idleStateName);
        runAnimationHash = ToHash(runStateName);
        Swing.CacheCombo();

        // 아래 상태들은 리그마다 있을 수도 없을 수도 있다(고블린 컨트롤러에는 Kick도 Block도 없다).
        // 이름만 보고 "있다"고 판단하면 존재하지 않는 상태로 CrossFade해서 유닛이 그 자리에 굳는다.
        // 실제로 애니메이터에 있는 것만 해시를 남기고, 없으면 0으로 둬서 부르는 쪽이 대체 동작을 타게 한다.
        walkAnimationHash = ResolveStateHash(walkStateName);
        jumpAnimationHash = ResolveStateHash(jumpStateName);
        skillAnimationHash = ResolveStateHash(skillStateName);
        Swing.CacheKick();
        blockAnimationHash = ResolveStateHash(blockStateName);
        potionAnimationHash = ResolveStateHash(potionStateName);
        healAnimationHash = ResolveStateHash(healStateName);
        dodgeAnimationHash = ResolveStateHash(dodgeStateName);
        hitAnimationHash = ResolveStateHash(hitStateName);
        deathAnimationHash = ResolveStateHash(deathStateName);

        hasWalkAnimationState = walkAnimationHash != 0;

        skillAnimationDuration = GetAnimationClipDuration(skillStateName, 1f);
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
        CacheMagicAnimationHashes();
    }

    // 이름이 비어 있거나 애니메이터에 그런 상태가 없으면 0.
    // 애니메이터 자체가 없으면 판단할 근거가 없으므로 이름 해시를 그대로 돌려준다.
    private int ResolveStateHash(string stateName)
    {
        int hash = ToHash(stateName);
        if (hash == 0 || animator == null) return hash;

        return animator.HasState(0, hash) ? hash : 0;
    }

    // 공격 모션이 하나라도 있는가. 없으면 CanAttack이 false가 되어 공격 상태로 들어가지 않는다.
    public bool HasAttackAnimation => Swing.HasAnimation;

    // 콤보가 한 바퀴를 다 돌아 다음 스윙이 다시 1번부터 시작하는 시점. 트리가 이때만
    // 스킬 같은 재량 전환을 검토한다 — 콤보 스텝 사이마다 검토하면 스윙 하나 끝날 때마다
    // 다른 상태로 튀어서 콤보가 거의 끝까지 이어지지 않는다.
    //
    // 한 번도 휘두르지 않은 상태를 레커버리로 세면 안 된다. 콤보 순번(SwingPart)은 처음부터 0이라
    // 교전에 들어선 첫 프레임에 이 값이 참이 되고, 유닛이 공격을 하기도 전에 스킬부터 쓴다.
    // 원거리 유닛에서 특히 드러났다 — 스폰되자마자 사거리 안이라 첫 행동이 곧바로 스킬이 되는데,
    // 스킬 모션은 근접 동작이라 7m 밖에서 발차기를 하며 피해를 넣었다.
    public bool IsComboRecoveryPoint => Swing.IsComboRecoveryPoint;

    // 콤보를 끝냈으니 일단 빠질 것인가(암살자의 게릴라 리듬 — StalkBehavior 주석 참조).
    //
    // 부르는 쪽은 이미 콤보 레커버리 시점인지 확인한 뒤다. 여기서는 "빠질 이유가 있는가"만 본다.
    // 지난번에 빠진 뒤로 실제로 한 번이라도 휘둘렀는가.
    //
    // 이게 없으면 빠지기가 무한 루프가 된다. 콤보 복귀 판정(IsComboRecoveryPoint)은
    // "한 번이라도 휘둘렀는가"와 콤보 순번으로 재는데, 그 둘은 상태를 넘어 남는다.
    // 그래서 빠졌다 돌아와 공격 동작에 들어서는 순간 이미 "콤보를 막 끝낸 시점"으로 읽혀
    // 한 번도 휘두르지 않고 곧바로 다시 빠졌다 — 실측에서 암살자의 준피해가 t=50부터
    // 25초 동안 1,237에서 1도 오르지 않았다.
    // StalkBehavior가 진입할 때 부른다. 다음 빠지기는 반드시 새 콤보 뒤에만 나온다.
    public void MarkStalkStarted() => Swing.MarkStalkStarted();

    public bool ShouldStalk()
    {
        if (!stats.stalkAfterCombo || IsDead) return false;
        if (!Swing.HasSwungSinceStalk) return false;
        if (!HasUsableTarget()) return false;

        // 다 잡아 가는 상대는 놓지 않는다. 급소를 문 채 물러나는 것은 게릴라가 아니라 그냥 도망이다.
        if (CurrentTarget.HpRatio <= stats.stalkSkipHpRatio) return false;

        // 이미 아무도 안 붙어 있으면 빠질 것도 없다 — 그대로 다음 표적으로 간다.
        float contact = Mathf.Max(1f, stats.attackRange * 1.2f);
        return UnitRegistry.CountEnemiesAround(this, transform.position, contact) > 0;
    }

    // WeaponEquipper가 무기를 갈아 끼운 뒤 호출. Awake는 장비가 붙기 전에 한 번 끝나므로
    // 처음 캐시된 길이는 맨손 기준이라 무기 장착 후 다시 계산해야 한다.
    public void RefreshAttackAnimations() => Swing.CacheCombo();

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

    // 클립 길이는 컨트롤러 단위로 한 번만 재어 모든 유닛이 나눠 쓴다(AnimatorClipLengths).
    private float GetAnimationClipDuration(string stateName, float fallback) =>
        AnimatorClipLengths.Of(animator, stateName, fallback);

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
        SubscribeAnimationEvents(true);
    }

    private void OnDisable()
    {
        UnitRegistry.Unregister(this);
        if (emotion != null) emotion.OnStateChanged -= HandleEmotionChanged;
        SubscribeAnimationEvents(false);
    }

    // 타격 이벤트를 받는 컴포넌트를 Animator 옆에 둔다(UnitAnimationEvents 주석 참조).
    //
    // 몸은 런타임에 세워지는 경우가 많아(CharacterBodyFactory) 프리팹에 미리 붙여 둘 수 없다.
    // 스폰 때 한 번 붙이는 것이라 매 프레임 비용은 없다.
    private void BindAnimationEvents()
    {
        if (attackHitHandler == null) attackHitHandler = ResolveAttackHit;
        if (skillHitHandler == null) skillHitHandler = ResolveSkillHit;
        if (animator == null) return;

        animationEvents = animator.GetComponent<UnitAnimationEvents>();
        if (animationEvents == null) animationEvents = animator.gameObject.AddComponent<UnitAnimationEvents>();
    }

    private void SubscribeAnimationEvents(bool subscribe)
    {
        // 플레이 중 스크립트를 고쳐 도메인 리로드가 일어나면 Awake 없이 OnEnable만 다시 불린다.
        // 그때 비직렬화 필드(이 컴포넌트 참조와 델리게이트)가 비어 있어 칼이 닿아도 아무 일이 없게 된다.
        if (subscribe && (animationEvents == null || attackHitHandler == null)) BindAnimationEvents();
        if (animationEvents == null) return;

        // 두 번 붙지 않게 먼저 뗀다. 도메인 리로드 뒤 OnEnable이 다시 불리는 경로가 있다.
        animationEvents.AttackHit -= attackHitHandler;
        animationEvents.SkillHit -= skillHitHandler;
        if (!subscribe) return;

        animationEvents.AttackHit += attackHitHandler;
        animationEvents.SkillHit += skillHitHandler;
    }

    private void Start()
    {
        brain = UnitBehaviorTree.Build(this);
        SyncDebugState();
    }

    private void Update()
    {
        // 히트스톱 해제와 방어 자세 복귀는 트리보다 먼저 처리한다 — 동작의 타이머가
        // AnimatorSpeed로 시간을 재기 때문에, 그 전에 이번 프레임의 배속이 확정돼 있어야 한다.
        TickCombat();

        if (scanner != null) scanner.Tick();
        if (emotion != null) emotion.Tick(Time.deltaTime);
        // 받아 둔 집중 표적을 틈이 나는 대로 주 표적에 적용한다. 트리가 이번 틱에 그 표적을 놓고 고르도록 먼저 돈다.
        TickCommand();
        // 사망/패닉/경직/회복약/치료처럼 무엇을 하고 있든 걸리는 판단은 트리 위쪽 가지가
        // 들고 있다(UnitBehaviorTree 참조 — 예전에는 그 판단이 여기 있었다).
        brain?.Tick();
        SyncDebugState();
    }

    // 하던 동작을 그 자리에서 접는다. 다음 틱에 트리가 뿌리부터 다시 고른다.
    //
    // 바깥에서 들어오는 개입이 여기로 온다 — 피격으로 하던 동작이 끊겼을 때와, 팀이 표적을
    // 넘겨줘 교전을 다시 잡아야 할 때다. 예전에는 그 둘이 ChangeState(AttackState)로 특정
    // 상태를 지목했는데, 지목한 상태는 진입하자마자 다시 판단해서 다른 곳으로 넘어가곤 했다
    // (사거리 밖이면 그 프레임에 곧바로 Chase로). 트리에서는 지목할 이유가 없다.
    public void InterruptBehavior()
    {
        brain?.Abort();
    }

    // 지금 돌고 있는 동작에게 무언가를 물어볼 때 쓴다
    // (HoldsGround / LocksTarget / AcceptsCombatRedirect).
    //
    // as로 내리는 이유는 트리가 BTNode<UnitController>만 알기 때문이다. 전투 유닛의 잎은
    // 전부 UnitBehavior라 실패할 일이 없지만, 그 사실을 타입으로 강제하지는 않는다 —
    // 트리 쪽은 전투를 모르는 채로 남겨 두는 편이 맞다.
    private UnitBehavior RunningBehavior => brain?.RunningLeaf as UnitBehavior;

    // 인스펙터 표시용. GetType().Name은 호출할 때마다 문자열을 새로 만들므로
    // 참조가 바뀐 프레임에만 읽는다.
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void SyncDebugState()
    {
#if UNITY_EDITOR
        BTNode<UnitController> node = brain?.RunningLeaf;
        if (ReferenceEquals(node, debugTrackedNode)) return;

        debugTrackedNode = node;
        currentBehaviorName = node != null ? node.GetType().Name : "";
#endif
    }

    // ---------------------------------------------------------------- 표적(TargetingPart)
    //
    // 표적을 잡고 갈아타고 놓는 규칙, 맞았을 때 돌아설지(어그로), "나를 노리는 적 수" 장부는
    // TargetingPart에 있다. CurrentTarget은 그 부품의 Assign/Clear만 바꾼다.

    private TargetingPart targetingPart;
    private TargetingPart Targeting => targetingPart ?? (targetingPart = new TargetingPart(this));

    public void ClearTarget() => Targeting.Clear();

    // 지금 이 유닛을 노리고 있는 team 소속 유닛 수.
    public int AttackersFrom(UnitTeam team) => Targeting.AttackersFrom(team);

    internal void AddAttacker(UnitTeam team, int delta) => Targeting.AddAttacker(team, delta);

    // 레지스트리에 들고 날 때 호출된다. 죽은 유닛은 레지스트리에서 빠지므로 자연히 숫자에서도 빠진다.
    internal void HoldTargetCount() => Targeting.HoldCount();

    internal void ReleaseTargetCount() => Targeting.ReleaseCount();

    public bool TrySetTarget(TargetRef target) => Targeting.TrySet(target);

    // 어그로 재평가(TargetScanner.ReviewAggro)가 고른 새 타깃으로 갈아탄다(TargetingPart.TryRetarget 주석 참조).
    public bool TryRetarget(TargetRef target) => Targeting.TryRetarget(target);

    // 엔티티가 된 적 중에서 겨눌 상대를 찾는다.
    public bool TryAcquireEntityTarget() => Targeting.TryAcquireEntity();

    public bool ShouldReleaseCurrentTarget() => Targeting.ShouldRelease();

    public bool HasUsableTarget() => Targeting.HasUsable();

    public void ReceiveSharedTarget(TargetRef target) => Targeting.ReceiveShared(target);

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

    public bool TrySetRoamDestination() => Locomotion.TrySetRoamDestination();

    public bool IsTargetValid()
    {
        return CurrentTarget.IsAlive;
    }

    public float SqrDistanceToTarget()
    {
        if (!CurrentTarget.Exists) return float.MaxValue;
        return (CurrentTarget.Position - transform.position).sqrMagnitude;
    }

    public bool IsTargetInAttackRange()
    {
        float range = stats.attackRange + stats.moveStopDistance;
        return SqrDistanceToTarget() <= range * range;
    }

    public Vector3 GetPredictedTargetPosition()
    {
        if (!CurrentTarget.Exists) return transform.position;

        Vector3 targetPosition = CurrentTarget.Position;
        targetPosition += CurrentTarget.Velocity * chasePredictionTime;

        return targetPosition;
    }

    // 마법사인가. 마법사에게는 평타가 없다 — 모든 공격이 영창을 거쳐 마법으로 나간다.
    public bool IsCaster => stats.affinity != MagicAffinity.None;

    public bool CanAttack()
    {
        // 마법사는 칼을 휘두르듯 즉발로 내지르는 동작이 하나도 없다. 가장 작은 마법(기본 마법)조차
        // 짧게나마 마력을 모아 나가므로, 공격은 전부 CastBehavior를 거친다(UnitController.Magic.cs).
        // 여기서 막지 않으면 마법사가 맨손으로 허공을 치는 평타 모션을 섞어 쓰게 된다.
        if (IsCaster) return false;

        return HasAttackAnimation &&
               IsTargetValid() &&
               IsTargetInAttackRange() &&
               !IsAttackAnimationLocked &&
               // 스윙과 스윙 사이의 호흡. 이게 없으면 클립이 끝난 프레임에 곧바로 다음 스윙이
               // 나가 쉼 없이 칼을 돌린다. 그 사이 시간에 AttackBehavior가 발놀림을 한다.
               IsSwingReady &&
               // 나를 향해 칼을 든 적이 보이면 새로 휘두르지 않는다. 막는 것이 먼저다(IsHoldingForGuard 주석).
               !IsHoldingForGuard &&
               // 마주 보기 전에는 휘두르지 않는다. 도착하자마자 등을 진 채 스윙을 시작하면
               // 모션이 비스듬히 나갈 뿐 아니라, 타격 판정(attackArcAngle)에서 그대로 빗나간다.
               // 몸을 돌리는 것도 전투의 일부다 — 그동안 상대는 먼저 칠 기회를 얻는다.
               IsFacingTarget(attackFacingTolerance);
    }

    public bool IsAttackAnimationLocked => Swing.IsLocked;

    // ---------------------------------------------------------------- 스킬(SkillPart)

    private SkillPart skillPart;
    private SkillPart Skill => skillPart ?? (skillPart = new SkillPart(this));

    // 지금 스킬을 쓸 수 있는가. 한 합 주고받은 뒤에만, 쿨다운·마나·남은 횟수가 허락할 때만 나간다.
    public bool CanUseSkill() => Skill.CanUse();

    public void TriggerSkill() => Skill.Trigger();

    // 이 유닛이 붙잡는 스킬에 다시 당할 수 있는가(무는 쪽이 아니라 물리는 쪽의 시계다).
    public bool CanBeSkillVictim => Skill.CanBeVictim;

    // 붙잡는 스킬에 당했다고 표시한다. 시간은 건 쪽이 정한다 — 경직(Stagger)과 같은 규칙이다.
    public void MarkSkillVictim(float duration) => Skill.MarkVictim(duration);

    // 적은 전투 중 HP를 되돌릴 수단이 없다 — 인스펙터에서 회복약 개수를 넣어도 마시지 않는다.
    // 밸런스 수치가 아니라 설계 규칙이라 데이터가 아니라 코드에 둔다.
    // 앞으로 회복 스킬이나 아이템을 추가할 때도 이 프로퍼티를 먼저 확인하면 규칙이 유지된다.
    public bool CanRecoverHp => team != UnitTeam.Enemy;

    // ---------------------------------------------------------------- 회복약·치유·보호막(SupportCastPart)

    private SupportCastPart supportCastPart;
    private SupportCastPart SupportCast => supportCastPart ?? (supportCastPart = new SupportCastPart(this));

    public bool CanUsePotion() => SupportCast.CanUsePotion();

    public void UsePotion() => SupportCast.UsePotion();

    public void TriggerPotion()
    {
        // 전용 모션이 없으면 Idle로 대체한다. 마시는 동안 멈춰 서 있는 것만으로도 충분히 읽힌다.
        PlayAnimation(potionAnimationHash != 0 ? potionAnimationHash : idleAnimationHash, true);
    }

    public bool CanHealAlly() => SupportCast.CanHealAlly();

    public bool CanShieldAlly() => SupportCast.CanShieldAlly();

    public void BeginShieldCast() => SupportCast.BeginShield();

    public void CompleteShield() => SupportCast.CompleteShield();

    public void CancelShieldCast() => SupportCast.CancelShield();

    public void BeginHealCast() => SupportCast.BeginHeal();

    public void CompleteHeal() => SupportCast.CompleteHeal();

    public void CancelHealCast() => SupportCast.CancelHeal();

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

    // 등 뒤를 잡혔는가를 가르는 정면 반구(180도). 이건 몸의 앞뒤이므로 직군과 무관하게 고정이다 —
    // 배후 피해 배율(backstabDamageMultiplier)과 피격 방향 판정이 이 값을 쓴다.
    private bool IsWithinFrontArc(Vector3 attackerPosition) => IsWithinArc(attackerPosition, 180f);

    // 자세로 받아낼 수 있는 각도. 이쪽은 막는 방식이 정한다(stats.guardArcAngle) —
    // 방패는 정면 반구를 통째로 가리지만(180), 검신으로 쳐내는 패링은 훨씬 좁다(110).
    // 검사가 여럿에게 둘러싸이면 방패병처럼 버티지 못하는 이유가 이 숫자다.
    //
    // 정면 반구와 나눠 둔 것이 중요하다. 하나로 합치면 패링 각도를 좁히는 순간 그 바깥이
    // 통째로 "등 뒤"가 되어, 검사만 ±55도 밖에서 맞을 때마다 1.6배를 맞게 된다.
    private bool IsWithinGuardArc(Vector3 attackerPosition) => IsWithinArc(attackerPosition, stats.guardArcAngle);

    // 지금 그쪽에서 오는 것을 막고 있는가(자세를 들었고, 방어 각도 안이다). 붙잡는 수가 막혔는지 가를 때 쓴다.
    public bool IsGuardingAgainst(Vector3 attackerPosition) => IsBlocking && IsWithinGuardArc(attackerPosition);

    private bool IsWithinArc(Vector3 attackerPosition, float arcAngle)
    {
        Vector3 toAttacker = attackerPosition - transform.position;
        toAttacker.y = 0f;
        if (toAttacker.sqrMagnitude <= 0.0001f) return true;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f) return true;

        float minDot = Mathf.Cos(Mathf.Clamp(arcAngle * 0.5f, 0f, 180f) * Mathf.Deg2Rad);
        return Vector3.Dot(forward.normalized, toAttacker.normalized) >= minDot;
    }

    // ---------------------------------------------------------------- 간격 유지
    //
    // 물러날지, 쫓기는지, 전선에서 떨어졌는지, 어느 쪽으로 물러날지는 UnitSpacing이 판단한다.
    // 아래는 행동 트리와 동작들이 부르는 입구다. 인스펙터 값(minKeepDistanceRange 등)은 프리팹에
    // 적혀 있으므로 여기 그대로 두고 UnitSpacing이 읽어 간다.

    [Tooltip("한 번 정한 후퇴 방향을 유지하는 시간(초). 이 안에 다시 물러나면 같은 쪽으로 간다. " +
             "0이면 물러날 때마다 새로 계산한다 — 그러면 방향이 계속 바뀌어 갈팡질팡하는 것처럼 보인다.")]
    [SerializeField, Min(0f)] private float retreatDirectionHold = 1.5f;

    private UnitSpacing spacing;

    // 도메인 리로드 뒤에는 Awake 없이 필드가 비므로 처음 물을 때 만든다.
    private UnitSpacing Spacing => spacing ?? (spacing = new UnitSpacing(this));

    internal float MinKeepDistanceRange => minKeepDistanceRange;
    internal float KeepDistanceRatio => keepDistanceRatio;
    internal float RegroupDistance => regroupDistance;
    internal float RetreatDirectionHold => retreatDirectionHold;

    public bool IsRangedFighter => Spacing.IsRangedFighter;

    private float KeepDistanceThreshold => Spacing.KeepDistanceThreshold;

    public bool ShouldKeepDistance() => Spacing.ShouldKeepDistance();

    // 영창을 버리고 빠져야 하는가.
    //
    // 영창 중인 마법사를 실제로 위협하는 것은 겨눈 적이 아니라 옆에서 파고든 적이다 — 먼 적을 조준한 채
    // 발밑의 고블린에게 맞고 있으면 겨눈 상대와의 거리는 영영 가깝지 않다. 그래서 "내 간격 안에 적이
    // 들어왔는가"(ShouldKeepDistance)와 같은 것을 본다. 한때 이 둘이 서로 다른 기준을 쓰다가
    // "영창은 접는데 물러나지는 않는" 어긋남을 만들었다 — 이제 한 곳에서 판단한다.
    //
    // 접어도 마력은 잃지 않는다(CancelSpellCast). 대가는 서 있던 시간과 다시 모으기까지의
    // 한 박자(spellRetryDelay)뿐이라, 붙잡힌 채 버티는 것보다 언제나 낫다 — 영창 중에는
    // 받는 피해가 castVulnerabilityMultiplier만큼 커지기 때문이다.
    public bool ShouldAbandonCast() => ShouldKeepDistance();

    public bool IsBeingChased() => Spacing.IsBeingChased();

    public bool IsSeparatedFromLine => Spacing.IsSeparatedFromLine;

    public Vector3 GetSpacingRetreatDirection() => Spacing.GetRetreatDirection();

    public void CommitRetreatDirection(Vector3 direction) => Spacing.CommitRetreatDirection(direction);

    public bool TryFindRetreatSpot(Vector3 preferred, float distance, out Vector3 direction, out Vector3 destination) =>
        Spacing.TryFindRetreatSpot(preferred, distance, out direction, out destination);

    // HP가 위험 수위인데 회복 수단(회복약/치료)이 없거나 이미 바닥났을 때 거리를 벌린다.
    // 적은 CanRecoverHp가 항상 false라 회복약이 없으면 이게 유일한 생존 수단이다.
    // 쿨다운으로 한 번만 물러나게 막으면, 여전히 위험한데도 한 박자 쉬고 다시 근접전으로
    // 걸어 들어가 버린다(맞다가 죽는 원인). HP가 임계치 아래인 동안은 매 프레임 계속 true를 줘서
    // FleeBehavior가 안전해질 때까지 반복해서 물러나게 한다.
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

    // poiseDamage: 이 피격이 강인도를 얼마나 깎는지. ResolveAttackHit/ResolveSkillHit만 실제 값을
    // 넘긴다 — 그 밖의 경로(예: TakeBleedDamage 계열)는 경직을 유발하지 않는다.
    public void TakeDamage(int damage, UnitController attacker, bool applyKnockback, bool fromSkill, float poiseDamage)
    {
        TakeDamage(damage, attacker, applyKnockback, fromSkill, poiseDamage, 1f);
    }

    // impactWeight: 이 한 방의 무게. 1이 평타이고 콤보 마무리·스킬·실드 배시가 더 크다.
    // 히트스톱 시간과 즉시 밀림에 곱해진다 — 같은 칼이라도 마무리 일격은 더 오래 멈칫해야 한다.
    public void TakeDamage(int damage, UnitController attacker, bool applyKnockback, bool fromSkill, float poiseDamage,
        float impactWeight)
    {
        TakeDamage(damage, attacker,
            attacker != null ? attacker.transform.position : Vector3.zero, attacker != null,
            Unity.Entities.Entity.Null, applyKnockback, fromSkill, poiseDamage, impactWeight);
    }

    // 엔티티가 된 적이 때렸다(EnemyWorldBridge.DrainHitsOnAllies가 부른다).
    //
    // 때린 쪽이 UnitController가 아니므로 참조 대신 위치와 Entity만 온다. 방어 각도, 배후 판정,
    // 강인도, 퍼펙트 가드, 넉백, 반격 표적 지정까지 규칙은 전부 같은 경로를 탄다 — 다른 것은 하나다:
    //  - 때린 쪽의 멈칫은 여기서 걸지 않는다. 엔티티는 타격 프레임에 스스로 건다(EnemyCombatSystem).
    //
    // 넉백은 켜서 보낸다. 예전에는 꺼져 있었고 즉시 밀림도 때린 UnitController가 있어야만 걸려서,
    // 고블린에게 맞은 영웅은 강인도가 깨져도 제자리에서 움찔만 했다 — 맞은 몸이 어디로도 밀리지 않았다.
    public void TakeEnemyDamage(int damage, Vector3 attackerPosition, Unity.Entities.Entity attackerEntity,
        float poiseDamage = 0f, float impactWeight = 1f)
    {
        TakeDamage(damage, null, attackerPosition, true, attackerEntity, true, false, poiseDamage, impactWeight);
    }

    // 맞은 한 대가 거치는 길(흘려내기 → 배율 → 체력 → 흔적 → 히트스톱 → 어그로 → 강인도)은
    // DamageIntakePart에 있다. 순서가 곧 규칙이라 그 부품 한곳에서 읽힌다.
    private void TakeDamage(int damage, UnitController attacker, Vector3 attackerPosition, bool hasAttackerPosition,
        Unity.Entities.Entity attackerEntity, bool applyKnockback, bool fromSkill, float poiseDamage,
        float impactWeight = 1f)
        => Intake.Take(damage, attacker, attackerPosition, hasAttackerPosition, attackerEntity, applyKnockback,
            fromSkill, poiseDamage, impactWeight);

    private DamageIntakePart intakePart;
    private DamageIntakePart Intake => intakePart ?? (intakePart = new DamageIntakePart(this));

    // ---------------------------------------------------------------- 바깥에서 들어오는 반응 요청
    //
    // 피격 리액션과 경직은 조건이 아니라 사건이다. 피해는 바깥에서(공격자의 애니메이션
    // 이벤트에서) 들어오므로 트리가 매 틱 조건을 물어보는 것으로는 알아챌 수 없다.
    // 그래서 표시를 세워 두고 트리 위쪽 가지가 그것을 본다 — 예전에 TakeDamage와 Stagger가
    // ChangeState로 상태를 직접 지목하던 자리다.
    //
    // 표시는 동작이 시작할 때 지운다(Consume). 세운 뒤 실제로 그 가지가 잡힐 때까지
    // 한 틱이 걸리는데, 그 사이는 예전 상태머신의 지연 적용과 정확히 같은 간격이다.

    public bool HasPendingHitReaction { get; private set; }
    public bool HasPendingStagger { get; private set; }

    public void RequestHitReaction() => HasPendingHitReaction = true;

    // 경직은 피격 리액션보다 무겁다. 같은 프레임에 둘 다 들어오면 경직만 남는다 —
    // 무너진 유닛이 그 앞에 짧게 움찔하고 시작할 이유가 없다.
    public void RequestStagger()
    {
        HasPendingStagger = true;
        HasPendingHitReaction = false;
    }

    public void ConsumeHitReaction() => HasPendingHitReaction = false;

    public void ConsumeStagger() => HasPendingStagger = false;

    // 죽거나 행동불가에 빠지면 밀려 있던 반응은 버린다. 시체나 굳어 버린 유닛이
    // 뒤늦게 움찔할 이유가 없다.
    public void ClearPendingReactions()
    {
        HasPendingHitReaction = false;
        HasPendingStagger = false;
    }

    // 쓰러졌다. 어디로 갈지 지목할 필요가 없다 — 사망은 트리 맨 위의 조건이라
    // (UnitBehaviorTree) 다음 틱에 DeadBehavior가 무조건 받는다. 여기서는 하던 동작을
    // 그 자리에서 접어 뒷정리(방어 자세 내리기, 공중에 뜬 몸 내려놓기)만 앞당긴다.
    public void Die()
    {
        InterruptBehavior();
    }

    // 출혈처럼 시간에 따라 들어오는 피해. 피격 리액션을 일으키지 않아야
    // 출혈이 공격 모션을 매초 끊어먹는 일이 없다.
    public void TakeBleedDamage(int damage) => Intake.TakeBleed(damage);

    // DeadBehavior 진입 시 호출. 같은 팀에 죽음을 알려 공포를 전파하고(원작의 핵심 기믹)
    // 전투 매니저/UI 같은 외부 구독자에게 통지한다.
    public void NotifyDeath()
    {
        // 처치는 마지막으로 피해를 준 유닛에게 귀속시킨다. 출혈로 쓰러진 경우에도
        // 출혈을 걸어둔 공격자가 LastAttacker로 남아 있어 기여가 사라지지 않는다.
        UnitController killer = combatRecord.LastAttacker;
        if (killer != null && killer != this) killer.CreditKill();

        // 동료가 쓰러지는 순간. 한 번의 죽음이 영구 소멸인 전투라 가장 크게 흔든다.
        if (team != UnitTeam.Enemy) GameServices.Shake.Current.Emit(this, AllyDeathShake);

        UnitEmotion.BroadcastAllyDeath(this);
        OnAnyUnitDied?.Invoke(this);
    }

    // 공격·스킬 클립의 타격 프레임. 클립 이벤트 → UnitAnimationEvents → 여기 → SwingPart.
    // 이 시점에 거리와 각도를 다시 재서 누가 맞았는지 정한다(SwingPart.ResolveHit 주석 참조).
    private void ResolveAttackHit() => Swing.ResolveHit();

    private void ResolveSkillHit() => Swing.ResolveSkillHit();

    // 한 방의 무게(히트스톱 시간·밀림 배율). 평타가 1이다. 스킬과 마법이 함께 쓴다 —
    // 평타 쪽 무게(마무리·발차기·실드 배시)는 SwingPart에 있다.
    private const float SkillImpactWeight = 1.6f;

    // 화면 흔들림 세기(0~1). CombatImpulse 주석의 "판을 가르는 순간"만 흔든다.
    private const float AllyDeathShake = 0.8f;

    // 주무기가 투사체를 들고 있으면 쏘고 true. 근접 무기는 false를 돌려주고 그 자리에서 때린다.
    private bool TryFireProjectile(TargetRef victim, int damage, float poiseDamage, bool fromSkill)
    {
        if (equipment == null) return false;

        WeaponDefinition weapon = equipment.MainHand;
        if (weapon == null || weapon.projectile == null) return false;

        Transform origin = equipment.ProjectileOrigin;
        if (origin == null) return false;

        // 겨눈 상대가 있으면 그쪽으로, 없으면 화살이 향한 쪽으로 그대로 날린다 — 그 방향이 곧 활이 겨눈 방향이다.
        Vector3 direction = victim.Exists ? victim.AimPoint - origin.position : origin.forward;
        WeaponProjectile.Fire(weapon.projectile, origin.position, direction, this, victim, damage, poiseDamage, fromSkill);

        equipment.ReleaseArrow();
        return true;
    }

    // 공포에 빠진 유닛은 원작 설정대로 능력치가 깎인다. 0이 되어 무해해지지는 않도록 최소 1.
    private int ScaleDamage(int damage)
    {
        return Mathf.Max(1, Mathf.RoundToInt(damage * EmotionMultiplier));
    }

    // 기본공격 한 번. 무엇으로 칠지(콤보·발차기·마무리)와 스윙 시계는 SwingPart가 정한다.
    public void TriggerAttack() => Swing.Trigger();

    // ---------------------------------------------------------------- 이동(LocomotionPart)

    private LocomotionPart locomotionPart;
    private LocomotionPart Locomotion => locomotionPart ?? (locomotionPart = new LocomotionPart(this));

    // 멈춰 선다. 그림과 몸은 같이 멈춰야 하므로 남은 속도까지 지우고 대기 자세로 바꾼다.
    public void StopMovement()
    {
        Locomotion.Stop();
        SetMoveAnimation(0f, false, false);
    }

    // 회피 도약을 시작하기 전에 부른다. 에이전트의 남은 속도를 지워 도약과 겹치지 않게 한다.
    public void BeginDodgeMove() => Locomotion.BeginDodge();

    // 도약이 끝났다. 이동 권한을 에이전트에게 돌려준다.
    public void EndDodgeMove() => Locomotion.EndDodge();

    public void MoveTo(Vector3 destination, float speed) => Locomotion.MoveTo(destination, speed, stats.moveStopDistance);

    public void MoveTo(Vector3 destination, float speed, float stoppingDistance) =>
        Locomotion.MoveTo(destination, speed, stoppingDistance);

    public bool HasReachedDestination(Vector3 destination) => Locomotion.HasReached(destination);

    public void ApplyKnockback(float progress) => Locomotion.ApplyKnockback(progress);

    public void DisableAgentAfterDeath() => Locomotion.DisableAfterDeath();

    private void ApplyAgentSpeed(float speed) => Locomotion.ApplySpeed(speed);

    // 감정과 둔화가 걸린 값을 다시 계산해 에이전트에 밀어 넣는다(값이 바뀌는 순간에만).
    private void RefreshAgentSpeed() => Locomotion.RefreshSpeed();

    // ---------------------------------------------------------------- 보행 모션(GaitPart)

    private GaitPart gaitPart;
    private GaitPart Gait => gaitPart ?? (gaitPart = new GaitPart(this));

    public void SetMoveAnimation(float speed, bool isRunning, bool isJumping) =>
        Gait.SetMoveAnimation(speed, isRunning, isJumping);

    // 이동 모션을 재생하는 표준 경로. 재생 배속을 "지금 실제로 나아가는 속도"에 맞춘다.
    public void SetMoveAnimationFromGroundSpeed(bool isRunning) => Gait.SetFromGroundSpeed(isRunning);

    // 실제로 나아가는 쪽에 맞는 다리를 고른다. 앞이면 달리기, 옆이나 뒤면 그 방향 클립.
    public void SetDirectionalMoveAnimationFromGroundSpeed() => Gait.SetDirectionalFromGroundSpeed();

    // ---------------------------------------------------------------- 몸 돌리기(FacingPart)

    private FacingPart facingPart;
    private FacingPart Facing => facingPart ?? (facingPart = new FacingPart(this));

    public void FaceTarget() => Facing.Toward(CurrentTarget, rotationSpeed);

    // 주어진 쪽을 즉시 바라본다. 서서히 도는 것이 아니라 그 프레임에 맞춘다.
    public void SnapFacing(Vector3 forwardDirection) => Facing.Snap(forwardDirection);

    // 주어진 방향을 등지고 선다. 뒷점프처럼 "뒤로 가는" 모션이 실제 이동과 맞으려면
    // 몸이 그 반대쪽을 보고 있어야 한다.
    public void FaceAwayFrom(Vector3 moveDirection) => Facing.Snap(-moveDirection);

    // 이미 내지른 스윙 도중의 회전. 겨누는 것은 휘두르기 전에 끝나 있어야 하고
    // (attackFacingTolerance), 여기서는 상대가 조금 움직인 만큼만 따라간다.
    // attackTurnSpeed가 0이면 스윙에 들어간 순간 방향이 고정된다.
    public void FaceTargetWhileAttacking()
    {
        if (attackTurnSpeed <= 0f) return;
        Facing.Toward(CurrentTarget, attackTurnSpeed);
    }

    // 가려는 쪽으로 몸을 돌린다. 다 돌았으면(또는 돌 방향이 없으면) true(FacingPart 주석 참조).
    public bool TurnTowardsMoveDirection(float toleranceDegrees) => Facing.TurnTowardsMoveDirection(toleranceDegrees);

    // 상대를 충분히 마주 보고 있는가. 타깃이 없으면 판단할 근거가 없으니 true로 둔다.
    public bool IsFacingTarget(float toleranceDegrees) => Facing.IsFacingTarget(toleranceDegrees);

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

    // 맞은 방향에 맞는 피격 모션을 고른다(HitFront/Back/Left/Right). 가드가 뚫린 그 한 대만은
    // 방향과 무관하게 BlockBreak으로 낸다(HitReactionPart.PlayHit 주석 참조).
    public void TriggerHit() => HitReaction.PlayHit();

    public void InterruptCurrentAction()
    {
        Swing.ReleaseLock();
        IsBlocking = false;
        Locomotion.Interrupt();
    }

    public void DisableCollider()
    {
        if (bodyCollider != null) bodyCollider.enabled = false;
    }

    // 사망 애니메이션이 끝난 뒤 호출. Animator를 끄지 않으면 시체가 늘어날수록
    // Animators.Update 비용이 그대로 쌓인다(프로파일러에서 시체 128구가 살아있는 유닛과
    // 동일한 1.0ms를 계속 소비하는 것을 확인). 마지막 프레임 포즈는 그대로 유지된다.
    public void FinalizeDeath()
    {
        if (disableAnimatorAfterDeath && animator != null) animator.enabled = false;
        enabled = false;
    }

    // 공포 상태에서는 이동속도도 함께 깎인다. 요청 속도를 따로 들고 있는 이유는
    // 감정이 바뀌었을 때 MoveTo를 기다리지 않고 바로 다시 계산하기 위해서다.
    private void ResetCombatRecord() => combatRecord.Reset();

    // 엔티티를 때렸을 때 기여도를 세는 자리(TargetRef.TakeDamage가 부른다).
    //
    // 게임오브젝트끼리는 맞은 쪽이 DamageIntakePart에서 "실제로 깎인 만큼"을 때린 쪽에 얹는다.
    // 엔티티는 피해가 큐를 건너 ECS에서 배율까지 적용되므로 그 값이 여기로 돌아오지 않는다.
    // 그래서 휘두른 값을 그대로 센다 — MVP 선정에 쓰는 순위용 숫자라 배후 배율만큼의
    // 차이로 순서가 뒤집히지는 않는다.
    public void CreditDamageDealt(int damage) => combatRecord.CreditDealt(damage);

    // 엔티티를 쓰러뜨렸다(EnemyWorldBridge.DrainKills가 부른다).
    // 게임오브젝트 쪽에서는 NotifyDeath가 LastAttacker의 처치를 올리는 자리와 같다.
    public void CreditKill() => combatRecord.CreditKill();

    private float EmotionMultiplier => emotion != null ? emotion.StatMultiplier : 1f;

    // 실제로 다리가 움직이는 배율. 공포(전 능력치 감소)와 둔화(부위 억제)가 함께 곱해진다.
    // 피해량에는 감정만 곱한다(ScaleDamage) — 다리를 찔린 것과 겁에 질린 것은 다른 일이다.
    private float MoveMultiplier => EmotionMultiplier * SlowMultiplier * FlinchMultiplier;

    private void HandleEmotionChanged(UnitEmotion changed)
    {
        RefreshAgentSpeed();
    }

    // 명령(CommandPart)이 표적을 직접 넣거나 적대 여부를 물을 때 쓰는 입구.
    private void AssignTarget(TargetRef target) => Targeting.Assign(target);

    private bool IsHostileTo(TargetRef target) => Targeting.IsHostileTo(target);

    private void PlayAnimation(int stateHash, bool forceRestart)
    {
        if (animator == null) return;
        if (stateHash == 0) return;

        if (!forceRestart && currentAnimationHash == stateHash) return;

        currentAnimationHash = stateHash;

        animator.CrossFadeInFixedTime(stateHash, animationFadeDuration);
    }
}
