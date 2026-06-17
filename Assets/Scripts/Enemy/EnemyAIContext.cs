using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemyAIContext
{
    // ── 컴포넌트 ──────────────────────────────────────────
    public NavMeshAgent agent;
    public Animator     animator;
    public Transform    transform;
    public EnemyConfig  config;

    // ── 월드 데이터 ───────────────────────────────────────
    public Transform target;
    public Vector3   spawnPosition;
    public Vector3   patrolTarget;

    // ── HP ────────────────────────────────────────────────
    public int  hp;
    public int  MaxHp    => config.maxHp;
    public bool IsDead   => hp <= 0;
    public float HpRatio => MaxHp > 0 ? (float)hp / MaxHp : 0f;

    // ── 타이머 ────────────────────────────────────────────
    public float nextAttackTime;

    // ── 이벤트 ────────────────────────────────────────────
    public System.Action<Transform> onAttack;
    public System.Action            onDeath;

    // ── 편의 프로퍼티 ─────────────────────────────────────
    public bool  HasTarget       => target != null;
    public float DistanceToTarget => target != null
        ? Vector3.Distance(transform.position, target.position)
        : float.MaxValue;
    public bool InAttackRange => DistanceToTarget <= config.attackRange;
    public bool InDetectRange => DistanceToTarget <= config.detectRange;
    public bool CanAttack     => Time.time >= nextAttackTime;

    // ── 애니메이션 내부 상태 ──────────────────────────────
    private string currentClip;

    // ════════════════════════════════════════════════════
    //  HP
    // ════════════════════════════════════════════════════
    public void TakeDamage(int amount)
    {
        if (IsDead) return;
        hp = Mathf.Max(0, hp - amount);
        if (IsDead) onDeath?.Invoke();
    }

    // ════════════════════════════════════════════════════
    //  이동
    // ════════════════════════════════════════════════════
    public void MoveTo(Vector3 position, float speed)
    {
        if (agent == null || !agent.isOnNavMesh) return;

        if (!TurnTowards(position, config.moveTurnSpeed, config.moveStartAngle))
        {
            agent.isStopped = true;
            agent.ResetPath();
            return;
        }

        agent.isStopped = false;
        agent.speed = speed;
        agent.SetDestination(position);
    }

    public void StopMoving()
    {
        if (agent == null || !agent.isOnNavMesh) return;
        agent.isStopped = true;
        agent.ResetPath();
    }

    public bool Reached(Vector3 position, float threshold)
    {
        Vector3 flat = position - transform.position;
        flat.y = 0f;
        return flat.sqrMagnitude <= threshold * threshold;
    }

    public Vector3 DirectionTo(Vector3 position)
    {
        Vector3 dir = position - transform.position;
        dir.y = 0f;
        return dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward;
    }

    public void Face(Vector3 position)
    {
        transform.rotation = Quaternion.LookRotation(DirectionTo(position), Vector3.up);
    }

    private bool TurnTowards(Vector3 position, float turnSpeed, float startAngle)
    {
        Vector3 direction = DirectionTo(position);
        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        float angle = Quaternion.Angle(transform.rotation, targetRotation);

        if (angle <= startAngle)
        {
            transform.rotation = targetRotation;
            return true;
        }

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            turnSpeed * Time.deltaTime);

        return false;
    }

    // ════════════════════════════════════════════════════
    //  애니메이션
    // ════════════════════════════════════════════════════
    public void Play(string clipName, bool restart = false, float speed = 1f)
    {
        if (animator == null) return;
        animator.speed = speed;
        if (!restart && currentClip == clipName) return;
        currentClip = clipName;

        if (restart)
            animator.Play(clipName, 0, 0f);
        else
            animator.Play(clipName);
    }

    public bool IsAnimationFinished(string clipName)
    {
        if (animator == null) return true;
        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        return info.IsName(clipName) && info.normalizedTime >= 1f;
    }

    public void ResetAnimSpeed()
    {
        if (animator != null) animator.speed = 1f;
    }
}
