// ============================================================
//  EnemyBlackboard.cs
//  모든 EnemyState가 공유하는 AI 데이터 저장소
// ============================================================
using UnityEngine;

public class EnemyBlackboard
{
    // ── 참조 ────────────────────────────────────────────
    public readonly EnemyController Controller;
    public readonly EnemyConfig     Config;

    // ── 환경 데이터 ──────────────────────────────────────
    /// <summary>현재 추적/공격 대상 (플레이어)</summary>
    public Transform Target;

    // ── 내비게이션 ───────────────────────────────────────
    public Vector3 SpawnPosition;
    public Vector3 PatrolTarget;

    // ── 쿨타임 타이머 ─────────────────────────────────────
    public float LastAttackTime = float.MinValue;

    // ── 피격 감지 ─────────────────────────────────────────
    private float _hitTime = float.MinValue;
    /// <summary>NotifyHit() 호출 후 hitDuration 동안 true</summary>
    public bool IsHit => UnityEngine.Time.time - _hitTime < Config.hitDuration;
    public void NotifyHit() => _hitTime = UnityEngine.Time.time;

    // ── HP ────────────────────────────────────────────────
    public int Hp { get; private set; }
    public bool IsDead => Hp <= 0;

    // ── 고착 감지 ─────────────────────────────────────────
    public float    StuckTimer       { get; private set; }
    private Vector3 _lastPosition;
    private bool    _hasLastPosition;

    // ── 공개 시간 참조 ────────────────────────────────────
    public float Time => UnityEngine.Time.time;

    // ── 생성자 ───────────────────────────────────────────
    public EnemyBlackboard(EnemyController controller, EnemyConfig config)
    {
        Controller    = controller;
        Config        = config;
        Hp            = config.maxHp;
        SpawnPosition = controller.transform.position;
    }

    // ── 매 프레임 갱신 ───────────────────────────────────
    public void Refresh()
    {
        Vector3 myPos = Controller.transform.position;

        // 고착 감지
        if (_hasLastPosition)
        {
            float moved = Vector3.Distance(myPos, _lastPosition);
            StuckTimer = moved < 0.05f ? StuckTimer + UnityEngine.Time.deltaTime : 0f;
        }
        _lastPosition    = myPos;
        _hasLastPosition = true;
    }

    // ── 쿨타임 조회 ──────────────────────────────────────
    public bool CanAttack() => Time - LastAttackTime >= Config.attackCooldown;

    // ── 범위 조회 ─────────────────────────────────────────
    public bool IsTargetInDetectRange()
    {
        if (Target == null) return false;
        return Vector3.Distance(Controller.transform.position, Target.position) <= Config.detectRange;
    }

    public bool IsTargetInAttackRange()
    {
        if (Target == null) return false;
        return Vector3.Distance(Controller.transform.position, Target.position) <= Config.attackRange;
    }

    // ── 데미지 처리 ───────────────────────────────────────
    public void TakeDamage(int amount)
    {
        if (IsDead) return;
        Hp = Mathf.Max(0, Hp - amount);
        NotifyHit();
    }
}
