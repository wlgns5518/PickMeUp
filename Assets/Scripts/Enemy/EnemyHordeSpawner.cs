using UnityEngine;

// 층 하나 분량의 적을 부르는 컴포넌트. 기존 CharacterBattleSpawner의 적 스폰 자리를 대신한다.
//
// 인스펙터 값은 Goblin.prefab의 UnitStats에서 그대로 옮겨 왔다. 두 세계가 같은 숫자로
// 돌아야 밸런스를 다시 잡지 않아도 되기 때문인데, 시간 관련 값 셋(준비 동작·회수·재사용)만
// 새로 생겼다 — 예전에는 그게 애니메이션 클립 길이에 숨어 있었고, 엔티티에는 클립이 없다.
public class EnemyHordeSpawner : MonoBehaviour
{
    [Header("체력과 피해")]
    [SerializeField] private int maxHp = 100;
    [SerializeField] private int attackDamage = 40;

    [Header("사거리와 판정")]
    [SerializeField] private float attackRange = 1.2f;
    [SerializeField] private float attackArcAngle = 130f;
    [Tooltip("타격 순간 사거리에 주는 여유. 없으면 경계에서 시작한 스윙이 거의 전부 빗나간다.")]
    [SerializeField] private float attackHitTolerance = 0.4f;

    [Header("공격 한 번의 시간 구성")]
    [Tooltip("칼을 들어올린 뒤 타격까지. 이 구간이 아군에게 열리는 방어 창이라, " +
             "아군의 반응 시간(blockReactionTime 0.18초)보다 넉넉해야 방어가 성립한다.")]
    [SerializeField] private float attackWindup = 0.4f;
    [Tooltip("내지른 뒤 회수까지. 이 동안 적은 스스로 아무것도 바꾸지 않는다.")]
    [SerializeField] private float attackRecovery = 0.35f;
    [SerializeField] private float attackCooldown = 1.1f;

    [Header("탐지")]
    [SerializeField] private float detectRange = 8f;
    [Tooltip("정면을 보고 달려드는 짐승이라 아군보다 좁다.")]
    [SerializeField] private float fieldOfView = 160f;

    [Header("이동")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float acceleration = 8f;
    [SerializeField] private float turnSpeed = 8f;
    [Tooltip("서로 밀어내는 반경. 아군 NavMeshAgent의 반지름(0.5)과 같은 값으로 두면 " +
             "두 세계의 간격이 눈에 띄게 어긋나지 않는다.")]
    [SerializeField] private float radius = 0.5f;
    [Tooltip("멈춰 설 거리. 사거리보다 조금 안쪽이라 도착하자마자 휘두를 수 있다.")]
    [SerializeField] private float standoffDistance = 1.0f;

    [Header("강인도와 무너짐")]
    [Tooltip("이 적의 한 대가 아군의 강인도를 얼마나 깎는가. 아군의 maxPoise가 100이므로 " +
             "이 값이 클수록 아군이 빨리 무너진다.")]
    [SerializeField] private float poiseDamagePerHit = 15f;
    [SerializeField] private float maxPoise = 100f;
    [SerializeField] private float poiseBreakImmunity = 2.5f;
    [SerializeField] private float staggerDuration = 1.2f;
    [SerializeField] private float hitReactionDuration = 0.3f;
    [SerializeField] private float knockbackDistance = 0.6f;

    [Header("어그로")]
    [SerializeField] private float threatWeight = 1f;

    [Header("층별 배율")]
    [Tooltip("층이 하나 오를 때마다 체력에 곱해지는 비율. CharacterBattleSpawner와 같은 규칙이다.")]
    [SerializeField] private float hpPerLevel = 0.15f;
    [SerializeField] private float damagePerLevel = 0.1f;

    public EnemyStats BuildStats(int level)
    {
        int steps = Mathf.Max(0, level - 1);

        return new EnemyStats
        {
            maxHp = Mathf.Max(1, Mathf.RoundToInt(maxHp * (1f + hpPerLevel * steps))),
            attackDamage = Mathf.Max(1, Mathf.RoundToInt(attackDamage * (1f + damagePerLevel * steps))),

            attackRange = attackRange,
            attackArcAngle = attackArcAngle,
            attackHitTolerance = attackHitTolerance,

            attackWindup = attackWindup,
            attackRecovery = attackRecovery,
            attackCooldown = attackCooldown,

            detectRange = detectRange,
            fieldOfView = fieldOfView,

            moveSpeed = moveSpeed,
            acceleration = acceleration,
            turnSpeed = turnSpeed,
            radius = radius,
            standoffDistance = standoffDistance,

            poiseDamagePerHit = poiseDamagePerHit,
            maxPoise = maxPoise,
            poiseBreakImmunity = poiseBreakImmunity,
            staggerDuration = staggerDuration,
            hitReactionDuration = hitReactionDuration,
            knockbackDistance = knockbackDistance,

            threatWeight = threatWeight,
        };
    }

    // 층 하나를 시작할 때 부른다. 돌려주는 값은 실제로 만들어진 마리 수.
    public int SpawnWave(int count, Vector3 center, float spread, int level, uint seed = 1)
    {
        EnemyStats stats = BuildStats(level);
        return EnemyHorde.Spawn(stats, count, center, spread, seed);
    }
}
