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
    [SerializeField] private string runStateName = "Run";
    [SerializeField] private string jumpStateName = "";
    [SerializeField] private string attackStateName = "Attack";
    [SerializeField] private string skillStateName = "Kick";
    [SerializeField] private string blockStateName = "Block";
    [SerializeField] private string hitStateName = "Hit";
    [SerializeField] private string deathStateName = "Death";
    [SerializeField] private float animationFadeDuration = 0.08f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs;
    [SerializeField] private string currentStateName;
    [SerializeField] private string currentTargetName;

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
    public Vector3 MoveDestination { get; private set; }
    public float RunDistance => runDistance;
    public float DestinationUpdateInterval => destinationUpdateInterval;

    private float lastAttackTime = -999f;
    private float lastSkillTime = -999f;
    private float attackLockedUntil;
    private float nextDestinationUpdateTime;
    private Vector3 lastAgentDestination;
    private float lastAgentStoppingDistance;
    private bool hasAgentDestination;
    private int currentAnimationHash;
    private Vector3 roamDirection;
    private float nextRoamTime;
    private float lastTargetChangeTime = -999f;

    private void Awake()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (scanner == null) scanner = GetComponent<TargetScanner>();
        if (bodyCollider == null) bodyCollider = GetComponent<Collider>();

        if (scanner != null) scanner.Initialize(this);
        ApplyAgentSpeed(stats.runSpeed);
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
        currentStateName = initialState.GetType().Name;
    }

    private void Update()
    {
        if (scanner != null) scanner.Tick();
        stateMachine?.Update();
    }

    private void FixedUpdate()
    {
        // The current HFSM core exposes Update only. Add a FixedTick method to the core if physics-state ticks are needed later.
    }

    public void ChangeState(IState<UnitController> state)
    {
        stateMachine?.ChangeState(state);
        currentStateName = state != null ? state.GetType().Name : "";
    }

    public void SetTarget(UnitController target)
    {
        TrySetTarget(target);
    }

    public void ClearTarget()
    {
        CurrentTarget = null;
        currentTargetName = "";
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
        MoveDestination = destination;
        HasMoveDestination = true;
    }

    public void ClearMoveDestination()
    {
        HasMoveDestination = false;
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
        SetMoveDestination(hit.position);
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
               !IsAttackAnimationLocked &&
               Time.time >= lastAttackTime + stats.attackCooldown;
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
               stats.currentHp < stats.maxHp * 0.45f;
    }

    public bool CanEvade()
    {
        return IsTargetValid() && SqrDistanceToTarget() < stats.attackRange * stats.attackRange * 0.5f;
    }

    public void TakeDamage(int damage)
    {
        TakeDamage(damage, null);
    }

    public void TakeDamage(int damage, UnitController attacker)
    {
        stats.TakeDamage(damage, IsBlocking);

        if (attacker != null && !attacker.IsDead && attacker.isActiveAndEnabled && UnitRegistry.AreEnemies(this, attacker))
        {
            ForceSetAttackTarget(attacker);
        }

        if (stats.IsDead)
        {
            Die();
            return;
        }

        InterruptCurrentAction();
        ChangeState(HitState);
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
        CurrentTarget.TakeDamage(stats.skillDamage, this);
    }

    public void TriggerAttack()
    {
        lastAttackTime = Time.time;
        attackLockedUntil = Time.time + stats.attackAnimationDuration;
        PlayAnimation(attackStateName, true);
    }

    public void TriggerSkill()
    {
        lastSkillTime = Time.time;
        PlayAnimation(skillStateName, true);
    }

    public void SetBlocking(bool isBlocking)
    {
        IsBlocking = isBlocking;
        if (isBlocking)
        {
            PlayAnimation(blockStateName, true);
        }
        else
        {
            PlayAnimation(idleStateName, false);
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
            PlayAnimation(jumpStateName, false);
            return;
        }

        PlayAnimation(speed > 0.01f ? runStateName : idleStateName, false);
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
        PlayAnimation(deathStateName, true);
    }

    public void TriggerHit()
    {
        PlayAnimation(hitStateName, true);
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

    public void DisableCollider()
    {
        if (bodyCollider != null) bodyCollider.enabled = false;
    }

    public void DisableUnitAfterDeath()
    {
        if (agent != null)
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }

            agent.enabled = false;
        }

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
        currentTargetName = CurrentTarget != null ? CurrentTarget.name : "";
        lastTargetChangeTime = Time.time;
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

    private void PlayAnimation(string stateName, bool forceRestart)
    {
        if (animator == null)
        {
            if (debugLogs) Debug.LogWarning($"[UnitController] {name} cannot play '{stateName}': Animator is null.");
            return;
        }

        if (string.IsNullOrEmpty(stateName))
        {
            if (debugLogs) Debug.LogWarning($"[UnitController] {name} cannot play animation: state name is empty.");
            return;
        }

        int stateHash = Animator.StringToHash(stateName);
        if (!forceRestart && currentAnimationHash == stateHash) return;

        currentAnimationHash = stateHash;

        animator.CrossFadeInFixedTime(stateHash, animationFadeDuration);
    }
}
