using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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

[Header("Blood VFX")]
    [SerializeField] private GameObject[] bloodEffectPrefabs;
    [SerializeField] private Vector3 bloodEffectOffset = new Vector3(0f, 1f, 0f);

        [Header("Movement")]
    [SerializeField] private float runDistance = 4f;
    [SerializeField] private float destinationUpdateInterval = 0.15f;
    [SerializeField] private float chasePredictionTime = 0.25f;
    [SerializeField] private float rotationSpeed = 720f;

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

    public UnitTeam Team => team;
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

    private int idleAnimationHash;
    private int walkAnimationHash;
    private int runAnimationHash;
    private int jumpAnimationHash;
    private int attackAnimationHash;
    private int skillAnimationHash;
    private int blockAnimationHash;
    private int hitAnimationHash;
    private int deathAnimationHash;
    private bool hasWalkAnimationState;

    private float attackAnimationDuration;
    private float skillAnimationDuration;
    private float hitAnimationDuration;
    private float deathAnimationDuration;

    public float AttackAnimationDuration => attackAnimationDuration;
    public float SkillAnimationDuration => skillAnimationDuration;
    public float HitAnimationDuration => hitAnimationDuration;
    public float DeathAnimationDuration => deathAnimationDuration;

    // 스포너가 Instantiate 직후 팀/스탯을 덮어쓸 때 사용. 이미 OnEnable로 UnitRegistry에
    // 등록된 상태에서 팀이 바뀌면 기존 리스트에서 빼고 새 팀 리스트로 다시 등록해준다.
    public void Configure(UnitTeam newTeam, UnitStats newStats)
    {
        bool teamChanged = newTeam != team;
        if (teamChanged && isActiveAndEnabled) UnitRegistry.Unregister(this);

        team = newTeam;
        if (newStats != null) stats = newStats;
        stats.ResetHp();
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

        if (scanner != null) scanner.Initialize(this);
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
        hitAnimationHash = ToHash(hitStateName);
        deathAnimationHash = ToHash(deathStateName);

        hasWalkAnimationState = animator != null &&
                                 !string.IsNullOrEmpty(walkStateName) &&
                                 animator.HasState(0, walkAnimationHash);

        attackAnimationDuration = GetAnimationClipDuration(attackStateName, 1f);
        skillAnimationDuration = GetAnimationClipDuration(skillStateName, 1f);
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
    }

    private void OnDisable()
    {
        UnitRegistry.Unregister(this);
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
        stateMachine?.Update();
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
               Time.time >= lastSkillTime + stats.skillCooldown;
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
        bool wasBlocking = IsBlocking;
        stats.TakeDamage(damage, IsBlocking);
        if (!wasBlocking) SpawnBloodEffect(attacker);


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

    public void ApplyAttackDamage()
    {
        if (!IsTargetValid()) return;
        CurrentTarget.TakeDamage(stats.attackDamage, this);
    }

    public void ApplySkillDamage()
    {
        if (!IsTargetValid()) return;
        CurrentTarget.TakeDamage(stats.skillDamage, this, true);
    }

    public void TriggerAttack()
    {
        attackLockedUntil = Time.time + attackAnimationDuration;
        PlayAnimation(attackAnimationHash, true);
    }

    public void TriggerSkill()
    {
        lastSkillTime = Time.time;
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
    }

    private void ApplyAgentSpeed(float speed)
    {
        if (agent != null) agent.speed = speed;
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
