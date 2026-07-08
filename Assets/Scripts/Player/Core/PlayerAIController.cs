using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

[System.Serializable]
public class AttackEvent : UnityEvent<Vector3, GameObject> {}

[System.Serializable]
public class SkillEvent : UnityEvent<Vector3, GameObject> {}

[System.Serializable]
public class DodgeEvent : UnityEvent<Vector3> {}

[RequireComponent(typeof(NavMeshAgent))]
public class PlayerAIController : HFSMRunner<PlayerAIContext>
{
    [Header("AI")]
    [SerializeField] private PlayerAIConfig config;
    [SerializeField] private CharacterSO    character;
    [SerializeField] private Animator       animator;
    [SerializeField] private bool           showDebugGizmos = true;

    [Header("Events")]
    public AttackEvent OnAttackEvent;
    public SkillEvent  OnSkillEvent;
    public DodgeEvent  OnDodgeEvent;
    public UnityEvent  OnPotionEvent;

    [Header("Combat")]
    [SerializeField] private int   attackDamage = 10;
    [SerializeField] private WeaponHitbox[] weaponHitboxes;

    private const float AttackDuration = 1f;
    private PlayerAIAliveState aliveState;
    private PlayerAIDeadState  deadState;
    private int enemyRegistryVersion = -1;
    private int projectileRegistryVersion = -1;

    public PlayerAIContext Context => context;

    public string CurrentState
    {
        get
        {
            if (aliveState?.CurrentSubState != null)
                return aliveState.CurrentSubState.GetType().Name;
            return CurrentRootState?.GetType().Name ?? "Uninitialized";
        }
    }

    protected new void Awake()
    {
        if (config == null)
        {
            Debug.LogError("[PlayerAIController] PlayerAIConfig is missing.");
            enabled = false;
            return;
        }

        base.Awake();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (weaponHitboxes == null || weaponHitboxes.Length == 0)
            weaponHitboxes = GetComponentsInChildren<WeaponHitbox>(true);

        context.animator  = animator;
        context.character = character;
        context.onAttack  = (dir, go) =>
        {
            OnAttackEvent?.Invoke(dir, go);
            ActivateWeaponHitboxes();
        };
        context.onSkill   = (dir, go) => OnSkillEvent?.Invoke(dir, go);
        context.onDodge   = dir       => OnDodgeEvent?.Invoke(dir);
        context.onPotion  = ()        => OnPotionEvent?.Invoke();

        // UtilityAIEvaluator 직접 생성
        context.evaluator = new UtilityAIEvaluator(context);

        // 레지스트리에 등록
        PlayerRegistry.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        PlayerRegistry.Instance?.Unregister(this);
    }

    protected override PlayerAIContext CreateContext()
    {
        NavMeshAgent navAgent = GetComponent<NavMeshAgent>();
        navAgent.updateRotation = false;

        return new PlayerAIContext
        {
            agent         = navAgent,
            transform     = transform,
            stats         = GetComponent<IPlayerStats>(),
            config        = config,
            spawnPosition = transform.position,
        };
    }

    protected override IState CreateInitialState(PlayerAIContext context)
    {
        aliveState = new PlayerAIAliveState(context, this);
        deadState  = new PlayerAIDeadState(context);
        return aliveState;
    }

    protected override void Update()
    {
        SyncFromRegistry();
        base.Update();
    }

    private void SyncFromRegistry()
    {
        EnemyRegistry enemyRegistry = EnemyRegistry.Instance;
        if (enemyRegistry != null && enemyRegistryVersion != enemyRegistry.Version)
        {
            enemyRegistryVersion = enemyRegistry.Version;
            context.enemies.Clear();
            var enemies = enemyRegistry.Enemies;
            for (int i = 0; i < enemies.Count; i++)
                context.enemies.Add(enemies[i]);
        }

        ProjectileRegistry projectileRegistry = ProjectileRegistry.Instance;
        if (projectileRegistry != null && projectileRegistryVersion != projectileRegistry.Version)
        {
            projectileRegistryVersion = projectileRegistry.Version;
            context.projectiles.Clear();
            var projectiles = projectileRegistry.Projectiles;
            for (int i = 0; i < projectiles.Count; i++)
                context.projectiles.Add(projectiles[i]);
        }
    }

    public void SetCharacter(CharacterSO newCharacter)
    {
        context.character = newCharacter;
    }

    public void TakeDamage()
    {
        if (aliveState == null || CurrentRootState == deadState) return;

        if (context.IsDead())
        {
            GoToDead();
            return;
        }

        aliveState.GoToHit();
    }

    public void TakeDamage(int amount)
    {
        context.stats?.TakeDamage(amount);
        TakeDamage();
    }

    private void ActivateWeaponHitboxes()
    {
        if (weaponHitboxes == null || weaponHitboxes.Length == 0) return;

        for (int i = 0; i < weaponHitboxes.Length; i++)
        {
            WeaponHitbox hitbox = weaponHitboxes[i];
            if (hitbox == null) continue;

            hitbox.Configure(gameObject, HitboxOwnerType.Player);
            hitbox.BeginAttack(attackDamage, AttackDuration);
        }
    }

    public void GoToDead()
    {
        PlayerRegistry.Instance?.Unregister(this);
        ChangeRootState(deadState);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos || config == null) return;
        Vector3 pos = transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(pos, context?.DetectRange ?? 7f);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(pos, context?.AttackRange ?? 1.5f);
        UnityEditor.Handles.Label(pos + Vector3.up * 1.5f, CurrentState ?? "");
    }
#endif
}
