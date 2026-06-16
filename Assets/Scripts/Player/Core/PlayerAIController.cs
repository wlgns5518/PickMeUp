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
    [SerializeField] private CharacterSO character;          // ← 추가
    [SerializeField] private Animator animator;
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private bool autoCollectTargets = true;
    [SerializeField] private float autoCollectInterval = 0.5f;

    [Header("Events")]
    public AttackEvent OnAttackEvent;
    public SkillEvent  OnSkillEvent;
    public DodgeEvent  OnDodgeEvent;
    public UnityEvent  OnPotionEvent;

    private PlayerAIAliveState aliveState;
    private PlayerAIDeadState  deadState;
    private float nextCollectTime;

    public PlayerAIContext Context => context;
    public string CurrentState
{
    get
    {
        if (aliveState?.CurrentSubState != null)
            return aliveState.CurrentSubState.GetType().Name;

        if (CurrentRootState != null)
            return CurrentRootState.GetType().Name;

        return "Uninitialized";
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

        base.Awake(); // context = CreateContext()

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        context.animator   = animator;
        context.character  = character;                      // ← 추가
        context.onAttack   = (dir, go) => OnAttackEvent?.Invoke(dir, go);
        context.onSkill    = (dir, go) => OnSkillEvent?.Invoke(dir, go);
        context.onDodge    = dir       => OnDodgeEvent?.Invoke(dir);
        context.onPotion   = ()        => OnPotionEvent?.Invoke();
    }

    protected override PlayerAIContext CreateContext()
    {
        return new PlayerAIContext
        {
            agent         = GetComponent<NavMeshAgent>(),
            transform     = transform,
            stats         = GetComponent<IPlayerStats>(),
            config        = config,
            spawnPosition = transform.position,
            // character는 Awake()에서 주입 (CreateContext 시점엔 character 필드가 아직 직렬화됨)
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
        if (autoCollectTargets && Time.time >= nextCollectTime)
        {
            nextCollectTime = Time.time + Mathf.Max(0.1f, autoCollectInterval);
            AutoCollectWorldTargets();
        }

        base.Update();
    }

    // ── 외부 주입 ──────────────────────────────────────────
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

    /// <summary>런타임에 캐릭터 교체 시 호출 (예: 전직, 캐릭터 스왑)</summary>
    public void SetCharacter(CharacterSO newCharacter)
    {
        context.character = newCharacter;
    }

    // ── 외부 이벤트 ────────────────────────────────────────
    public void TakeDamage()
    {
        if (aliveState == null || CurrentRootState == deadState) return;
        aliveState.GoToMove(); // GoToFlee() → GoToMove()
    }

    public void GoToDead()
    {
        ChangeRootState(deadState);
    }

    private void AutoCollectWorldTargets()
    {
        context.enemies.Clear();
        foreach (var enemy in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            context.enemies.Add(enemy);

        context.projectiles.Clear();
        foreach (var projectile in FindObjectsByType<ProjectileAdapter>(FindObjectsSortMode.None))
            context.projectiles.Add(projectile);
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

        // ✅ 플레이 중일 때만 상태 표시
        if (Application.isPlaying)
            UnityEditor.Handles.Label(pos + Vector3.up * 1.5f, CurrentState ?? "");
    }
#endif
}