using UnityEngine;

[CreateAssetMenu(menuName = "PickMeUp/EnemyConfig", fileName = "EnemyConfig")]
public class EnemyConfig : ScriptableObject
{
    [Header("이동 속도")]
    public float walkSpeed      = 2.5f;
    public float runSpeed       = 5.5f;
    public float patrolSpeed    = 2.0f;
    [Tooltip("이동 전 목표 방향으로 회전하는 속도")]
    public float moveTurnSpeed  = 540f;
    [Tooltip("목표 방향과의 각도가 이 값 이하가 되면 이동 시작")]
    public float moveStartAngle = 5f;

    [Header("감지 & 전투 범위")]
    [Tooltip("플레이어 감지 거리")]
    public float detectRange    = 8f;
    [Tooltip("근접 공격 사거리")]
    public float attackRange    = 1.8f;
    [Header("HP")]
    public int maxHp            = 100;

    [Header("쿨타임 (초)")]
    public float attackCooldown = 1.2f;
    [Tooltip("피격 경직 지속 시간 (초)")]
    public float hitDuration    = 0.4f;
    [Tooltip("Detect 연출 유지 시간 (초)")]
    public float detectDuration = 0.8f;

    [Header("순찰")]
    [Tooltip("스폰 위치 기준 순찰 반경")]
    public float patrolRadius   = 6f;
    [Tooltip("목표 도달 판정 거리")]
    public float patrolArriveThreshold = 0.4f;
    [Tooltip("목표 지점 도달 후 대기 시간 (초)")]
    public float patrolWaitTime = 1.5f;

    [Header("고착 감지")]
    public float stuckThreshold = 1.5f;

    [Header("공격 모션")]
    [Tooltip("공격 모션 완료까지 대기 시간 (초)")]
    public float attackMotionDuration = 0.6f;
}
