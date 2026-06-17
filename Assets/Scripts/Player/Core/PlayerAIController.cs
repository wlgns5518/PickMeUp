using System.Collections.Generic;
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

    private PlayerAIAliveState aliveState;
    private PlayerAIDeadState  deadState;

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

        context.animator  = animator;
        context.character = character;
        context.onAttack  = (dir, go) => OnAttackEvent?.Invoke(dir, go);
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
        if (EnemyRegistry.Instance != null)
        {
            context.enemies.Clear();
            var enemies = EnemyRegistry.Instance.Enemies;
            for (int i = 0; i < enemies.Count; i++)
                context.enemies.Add(enemies[i]);
        }

        if (ProjectileRegistry.Instance != null)
        {
            context.projectiles.Clear();
            var projectiles = ProjectileRegistry.Instance.Projectiles;
            for (int i = 0; i < projectiles.Count; i++)
                context.projectiles.Add(projectiles[i]);
        }
    }

    public void SetEnemies(List<IEnemy> source)
    {
        context.enemies.Clear();
        if (source != null) context.enemies.AddRange(source);
    }

    public void SetProjectiles(List<IDangerousProjectile> source)
    {
        context.projectiles.Clear();
        if (source != null) context.projectiles.AddRange(source);
    }

    public void SetCharacter(CharacterSO newCharacter)
    {
        context.character = newCharacter;
    }

    public void TakeDamage()
    {
        if (aliveState == null || CurrentRootState == deadState) return;
        aliveState.GoToMove();
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
