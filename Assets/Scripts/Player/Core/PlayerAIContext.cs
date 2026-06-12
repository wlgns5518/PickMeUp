using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class PlayerAIContext
{
    // ── 컴포넌트 ──────────────────────────────────────────
    public NavMeshAgent agent;
    public Animator animator;
    public Transform transform;
    public IPlayerStats stats;
    public PlayerAIConfig config;

    // ── 캐릭터 데이터 (CharacterSO) ───────────────────────
    public CharacterSO character;

    // ── 계산 프로퍼티 (CharacterSO → AI 수치) ─────────────
    private int Agility  => character?.stats?.agility  ?? 10;
    private int Vitality => character?.stats?.vitality ?? 10;

    public float MoveSpeed        => CharacterRules.MoveSpeed(Agility);
    public float RunSpeed         => CharacterRules.RunSpeed(Agility);
    public float AttackCooldown   => CharacterRules.AttackCooldown(Agility);
    public float SkillCooldown    => CharacterRules.SkillCooldown(Agility);
    public int   PotionHealAmount => CharacterRules.PotionHealAmount(Vitality);

    private CharacterRules.CombatRanges CombatRanges =>
        character != null
            ? CharacterRules.GetCombatRanges(character.job)
            : CharacterRules.GetCombatRanges(JobType.Melee);

    public float AttackRange => CombatRanges.attackRange;
    public float SkillRange  => CombatRanges.skillRange;
    public float DetectRange => CombatRanges.detectRange;

    // ── 월드 데이터 ───────────────────────────────────────
    public readonly List<IEnemy> enemies = new List<IEnemy>();
    public readonly List<IDangerousProjectile> projectiles = new List<IDangerousProjectile>();
    public IEnemy target;
    public Vector3 spawnPosition;
    public Vector3 patrolTarget;

    // ── 타이머 ────────────────────────────────────────────
    public float nextAttackTime;

    // ── 이벤트 (외부 Unity Event에 연결) ──────────────────
    public System.Action<Vector3, GameObject> onAttack;
    public System.Action<Vector3, GameObject> onSkill;
    public System.Action<Vector3> onDodge;
    public System.Action onPotion;

    // ── 애니메이션 내부 상태 ──────────────────────────────
    private string currentClip;

    // ════════════════════════════════════════════════════
    //  타겟 갱신
    // ════════════════════════════════════════════════════
    public void RefreshTarget()
    {
        target = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < enemies.Count; i++)
        {
            IEnemy enemy = enemies[i];
            if (enemy == null || enemy.IsDead) continue;

            float distance = Vector3.Distance(transform.position, enemy.Position);
            if (distance <= DetectRange && distance < bestDistance)
            {
                bestDistance = distance;
                target = enemy;
            }
        }
    }

    // ════════════════════════════════════════════════════
    //  이동
    // ════════════════════════════════════════════════════
    public void MoveTo(Vector3 position, float speed)
    {
        if (agent == null || !agent.isOnNavMesh) return;
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

    // ════════════════════════════════════════════════════
    //  HP 판단
    // ════════════════════════════════════════════════════
    public bool IsDead()   => stats != null && stats.Hp <= 0;
    public bool IsLowHp()  => stats != null && stats.MaxHp > 0 && (float)stats.Hp / stats.MaxHp <= config.fleeHpRatio;
    public bool IsSafeHp() => stats == null || stats.MaxHp <= 0 || (float)stats.Hp / stats.MaxHp >= config.safeHpRatio;

    // ════════════════════════════════════════════════════
    //  애니메이션
    // ════════════════════════════════════════════════════
    public void Play(string clipName, bool restart = false)
    {
        if (animator == null) return;
        if (!restart && currentClip == clipName) return;
        currentClip = clipName;

        if (restart)
            animator.Play(clipName, 0, 0f); // 0프레임부터 강제 재시작
        else
            animator.Play(clipName);
    }

    /// <summary>
    /// 현재 재생 중인 애니메이션이 clipName과 일치하고 완료됐는지 확인
    /// normalizedTime >= 1이면 완료로 판단
    /// </summary>
    public bool IsAnimationFinished(string clipName)
    {
        if (animator == null) return true;

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        return info.IsName(clipName) && info.normalizedTime >= 1f;
    }
}