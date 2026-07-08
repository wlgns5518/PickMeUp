using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

[RequireComponent(typeof(NavMeshAgent))]
public abstract class EnemyAIController : HFSMRunner<EnemyAIContext>, IEnemy
{
    [Header("AI")]
    [SerializeField] protected EnemyConfig config;
    [SerializeField] private Animator      animator;
    [SerializeField] private bool          showDebugGizmos = true;

    [Header("Events")]
    public UnityEvent<Transform> OnAttackEvent;
    public UnityEvent            OnDeathEvent;

    [Header("Combat")]
    [SerializeField] private int   attackDamage = 10;
    [SerializeField] private float weaponHitboxDuration = 0.15f;
    [SerializeField] private WeaponHitbox[] weaponHitboxes;

    [Header("Runtime HP")]
    [SerializeField] private int currentHp;
    [SerializeField] private int maxHp;

    // ── IEnemy 구현 ───────────────────────────────────────
    public GameObject GameObject => gameObject;
    public Vector3    Position   => transform.position;
    public float      Hp         => context?.hp ?? 0;
    public bool       IsDead     => context?.IsDead ?? false;

    public string CurrentState =>
        CurrentRootState?.GetType().Name ?? "Uninitialized";

    // ════════════════════════════════════════════════════
    //  초기화
    // ════════════════════════════════════════════════════
    protected new void Awake()
    {
        if (config == null)
        {
            Debug.LogError($"[EnemyAIController] EnemyConfig 없음: {gameObject.name}");
            enabled = false;
            return;
        }

        base.Awake();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (weaponHitboxes == null || weaponHitboxes.Length == 0)
            weaponHitboxes = GetComponentsInChildren<WeaponHitbox>(true);

        context.animator = animator;
        context.onAttack = t =>
        {
            OnAttackEvent?.Invoke(t);
            ActivateWeaponHitboxes();
        };
        context.onDeath  = () =>
        {
            SyncInspectorHp();
            OnDeathEvent?.Invoke();
            GoToDead();
        };
        context.hp = config.maxHp;
        SyncInspectorHp();

        // 레지스트리에 등록
        EnemyRegistry.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        // 오브젝트 파괴 시 안전하게 해제
        EnemyRegistry.Instance?.Unregister(this);
    }

    protected override EnemyAIContext CreateContext()
    {
        NavMeshAgent navAgent = GetComponent<NavMeshAgent>();
        navAgent.updateRotation = false;
        navAgent.stoppingDistance = 0f;
        navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

        return new EnemyAIContext
        {
            agent         = navAgent,
            transform     = transform,
            config        = config,
            spawnPosition = transform.position,
        };
    }

    protected override void Update()
    {
        RefreshPlayerTarget();
        base.Update();
    }

    // ════════════════════════════════════════════════════
    //  외부 API
    // ════════════════════════════════════════════════════
    private void RefreshPlayerTarget()
    {
        PlayerRegistry registry = PlayerRegistry.Instance;
        if (registry == null)
        {
            context.target = null;
            return;
        }

        PlayerAIController nearest = null;
        float nearestDistance = float.MaxValue;
        IReadOnlyList<PlayerAIController> players = registry.Players;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerAIController player = players[i];
            if (player == null || !player.gameObject.activeInHierarchy) continue;
            if (player.Context != null && player.Context.IsDead()) continue;

            float distance = Vector3.SqrMagnitude(player.transform.position - transform.position);
            if (distance >= nearestDistance) continue;

            nearest = player;
            nearestDistance = distance;
        }

        context.target = nearest != null ? nearest.transform : null;
    }

    public void TakeDamage(int amount)
    {
        if (IsDead) return;

        context.TakeDamage(amount);
        SyncInspectorHp();
        if (IsDead) return;

        OnTakeDamage();
    }

    protected virtual void OnTakeDamage() { }

    private void ActivateWeaponHitboxes()
    {
        if (weaponHitboxes == null || weaponHitboxes.Length == 0) return;

        for (int i = 0; i < weaponHitboxes.Length; i++)
        {
            WeaponHitbox hitbox = weaponHitboxes[i];
            if (hitbox == null) continue;

            hitbox.Configure(gameObject, HitboxOwnerType.Enemy);
            hitbox.BeginAttack(attackDamage, weaponHitboxDuration);
        }
    }

    private void SyncInspectorHp()
    {
        currentHp = context?.hp ?? 0;
        maxHp = config != null ? config.maxHp : 0;
    }

    private void OnValidate()
    {
        maxHp = config != null ? config.maxHp : 0;
    }

    public void GoToDead()
    {
        // 사망 시 레지스트리에서 즉시 해제
        EnemyRegistry.Instance?.Unregister(this);
        ChangeRootState(CreateDeadState());
    }

    protected abstract IState CreateDeadState();

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos || config == null) return;
        Vector3 pos = transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(pos, config.detectRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(pos, config.attackRange);
        UnityEditor.Handles.Label(pos + Vector3.up * 1.5f, CurrentState ?? "");
    }
#endif
}
