using UnityEngine;

/// <summary>
/// 적 AI 공통 Utility 평가기 베이스.
/// 각 적의 전용 Evaluator는 이 클래스를 상속해서 사용.
/// </summary>
public abstract class EnemyUtilityEvaluator
{
    protected EnemyAIContext context;

    // ── 피격 추적 ─────────────────────────────────────────
    private bool  isHit;
    private float hitTimer;

    protected EnemyUtilityEvaluator(EnemyAIContext context)
    {
        this.context = context;
    }

    // ── 공통 알림 ─────────────────────────────────────────
    public void NotifyHit()
    {
        isHit    = true;
        hitTimer = context.config.hitDuration;
    }

    public virtual void Tick(float deltaTime)
    {
        if (hitTimer > 0f)
        {
            hitTimer -= deltaTime;
            if (hitTimer <= 0f) isHit = false;
        }
    }

    // ── 공통 상태 접근 ────────────────────────────────────
    public bool IsHit => isHit;
}
