// ============================================================
//  PlayerAIConfig.cs
//  인스펙터에서 AI 수치를 조정할 수 있는 ScriptableObject
//
//  ※ 아래 항목은 CharacterSO에서 자동 계산되므로 제거됨:
//     moveSpeed, runSpeed, attackRange, skillRange, detectRange,
//     attackCooldown, skillCooldown, potionHealAmount
// ============================================================
using UnityEngine;

[CreateAssetMenu(menuName = "PickMeUp/PlayerAIConfig", fileName = "PlayerAIConfig")]
public class PlayerAIConfig : ScriptableObject
{
    [Header("HP 임계값")]
    [Range(0f, 1f)]
    [Tooltip("이 비율 이하 → 도주 전환")]
    public float fleeHpRatio     = 0.25f;
    [Range(0f, 1f)]
    [Tooltip("이 비율 이상 → 전투 복귀 허용")]
    public float safeHpRatio     = 0.5f;

    [Header("쿨타임 (초)")]
    public float dodgeCooldown   = 1.2f;
    public float potionCooldown  = 8.0f;

    [Header("기타")]
    [Tooltip("이 거리 이상 멀어지면 도주 해제")]
    public float fleeDistance    = 8.75f;
    [Tooltip("스폰 위치 기준 순찰 반경")]
    public float patrolRadius    = 5f;
    [Tooltip("공격 모션 완료까지 대기 시간 (초)")]
    public float attackMotionDuration     = 0.2f;
    [Tooltip("헤비 어택 모션 완료까지 대기 시간 (초)")]
    public float heavyAttackMotionDuration = 0.8f;
    [Tooltip("스킬 모션 완료까지 대기 시간 (초)")]
    public float skillMotionDuration      = 0.5f;
    [Tooltip("회피 지속 시간 (초)")]
    public float dodgeDuration            = 0.3f;
    [Tooltip("고착 판정 시간 (초)")]
    public float stuckThreshold           = 1.2f;
    [Tooltip("카이팅 최대 유지 시간 → 재배치 전환")]
    public float maxKiteTime              = 3f;

    [Header("피격 & 블록")]
    [Tooltip("피격 경직 지속 시간 (초)")]
    public float hitReactionDuration      = 0.35f;
    [Tooltip("블록 유지 최대 시간 (초)")]
    public float blockDuration            = 1.2f;
    [Range(0f, 1f)]
    [Tooltip("카이팅 중 블록 진입 확률")]
    public float blockChance              = 0.3f;
}