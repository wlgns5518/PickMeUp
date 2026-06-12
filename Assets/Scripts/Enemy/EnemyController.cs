// ============================================================
//  EnemyController.cs
//  MonoBehaviour 진입점. FSM 조립 및 애니메이션/전투 연결.
//
//  ▶ 사용법
//  1. 적 GameObject에 이 컴포넌트를 추가합니다.
//  2. EnemyConfig ScriptableObject를 Config 필드에 연결합니다.
//  3. 플레이어 Transform을 PlayerTarget 필드에 연결합니다.
// ============================================================
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

// ════════════════════════════════════════════════════════
//  EnemyController
// ════════════════════════════════════════════════════════
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyController : MonoBehaviour, IEnemy
{
    // ── 인스펙터 ──────────────────────────────────────────
    [Header("AI 설정")]
    [SerializeField] private EnemyConfig _config;

    [Header("플레이어 참조")]
    [SerializeField] private Transform _playerTarget;

    [Header("외부 참조 (자동 탐색 가능)")]
    [SerializeField] private Animator _animator;

    [Header("이벤트 (선택)")]
    [Tooltip("공격 발생 시 호출. (타겟 Transform)")]
    public UnityEvent<Transform> OnAttackEvent;
    [Tooltip("사망 시 호출.")]
    public UnityEvent OnDeathEvent;

    [Header("디버그")]
    [SerializeField] private bool _logStateChanges = true;

    // ── 내부 ──────────────────────────────────────────────
    private EnemyHFSM       _fsm;
    private EnemyBlackboard _bb;
    private NavMeshAgent    _agent;
    private string          _currentClip;

    // ── IEnemy 구현 ───────────────────────────────────────
    public GameObject GameObject => gameObject;
    public Vector3    Position   => transform.position;
    public float      Hp         => _bb?.Hp ?? 0;
    public bool       IsDead     => _bb?.IsDead ?? false;

    // ── 공개 프로퍼티 ─────────────────────────────────────
    public string CurrentState => _fsm?.CurrentStateName ?? "None";

    // ════════════════════════════════════════════════════
    //  Unity 생명주기
    // ════════════════════════════════════════════════════
    private void Awake()
    {
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();

        _agent = GetComponent<NavMeshAgent>();

        if (_config == null)
        {
            Debug.LogError("[EnemyController] EnemyConfig가 연결되지 않았습니다.");
            enabled = false;
            return;
        }

        BuildFSM();
    }

    private void Start()
    {
        if (_agent == null || _config == null) return;
        _agent.updateRotation = false; // 회전은 코드에서 직접 제어
        _agent.speed = _config.walkSpeed;
    }

    private void Update()
    {
        if (_bb == null) return;

        // 플레이어 타겟 동기화
        _bb.Target = _playerTarget;

        // 블랙보드 갱신
        _bb.Refresh();

        // FSM 업데이트
        _fsm.Update();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_config == null) return;
        Vector3 pos = transform.position;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(pos, _config.detectRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(pos, _config.attackRange);

        UnityEditor.Handles.Label(pos + Vector3.up * 2f, CurrentState);
    }
#endif

    // ════════════════════════════════════════════════════
    //  FSM 조립
    // ════════════════════════════════════════════════════
    private void BuildFSM()
    {
        _bb  = new EnemyBlackboard(this, _config);
        _fsm = new EnemyHFSM();

        _fsm.Register("idle",    new EnemyIdleState(),    _bb);
        _fsm.Register("patrol",  new EnemyPatrolState(),  _bb);
        _fsm.Register("detect",  new EnemyDetectState(),  _bb);
        _fsm.Register("walk",    new EnemyWalkState(),    _bb);
        _fsm.Register("run",     new EnemyRunState(),     _bb);
        _fsm.Register("attack",  new EnemyAttackState(),  _bb);
        _fsm.Register("hit",     new EnemyHitState(),     _bb);

        if (_logStateChanges)
            _fsm.OnStateChanged += (from, to) =>
                Debug.Log($"[Enemy] {gameObject.name}: {from} → {to}");

        _fsm.Start("idle");
    }

    // ════════════════════════════════════════════════════
    //  외부 데이터 주입 API
    // ════════════════════════════════════════════════════

    /// <summary>런타임에서 플레이어 Transform을 지정할 때 사용</summary>
    public void SetPlayerTarget(Transform target)
    {
        _playerTarget = target;
    }

    // ════════════════════════════════════════════════════
    //  애니메이션
    // ════════════════════════════════════════════════════

    /// <summary>중복 재생 방지: 클립 이름이 다를 때만 Play()</summary>
    public void PlayAnim(string clipName)
    {
        if (_animator == null || _currentClip == clipName) return;
        _currentClip = clipName;
        _animator.Play(clipName);
    }

    /// <summary>항상 처음부터 재생 (공격·피격 등 일회성 동작)</summary>
    public void PlayAnimForce(string clipName)
    {
        if (_animator == null) return;
        _currentClip = clipName;
        _animator.Play(clipName);
    }

    public bool IsCurrentAnimationFinished(string clipName, int layer = 0)
    {
        if (_animator == null)
            return true;

        if (_animator.IsInTransition(layer))
            return false;

        AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(layer);
        if (!stateInfo.IsName(clipName))
            return true;

        return stateInfo.normalizedTime >= 1f;
    }

    // ════════════════════════════════════════════════════
    //  전투 콜백
    // ════════════════════════════════════════════════════

    /// <summary>공격 상태에서 호출 → 데미지 판정 이벤트 발생</summary>
    public void OnAttack(Transform target)
    {
        OnAttackEvent?.Invoke(target);
    }

    /// <summary>외부 피격 처리에서 호출 → Hit 상태 전환 트리거</summary>
    public void TakeDamage(int amount)
    {
        if (_bb == null || _bb.IsDead) return;
        _bb.TakeDamage(amount);

        // 공격·피격 클립은 항상 처음부터 재생
        PlayAnimForce("Hit");

        if (_bb.IsDead)
            OnDeathEvent?.Invoke();
    }
}
