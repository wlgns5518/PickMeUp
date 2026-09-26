using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

// 층 하나 분량의 적을 부르는 컴포넌트. 기존 CharacterBattleSpawner의 적 스폰 자리를 대신한다.
//
// 인스펙터 값은 Goblin.prefab의 UnitStats에서 그대로 옮겨 왔다. 두 세계가 같은 숫자로
// 돌아야 밸런스를 다시 잡지 않아도 되기 때문인데, 시간 관련 값 셋(준비 동작·회수·재사용)만
// 새로 생겼다 — 예전에는 그게 애니메이션 클립 길이에 숨어 있었고, 엔티티에는 클립이 없다.
public class EnemyHordeSpawner : MonoBehaviour
{
    [Header("보이는 것")]
    [Tooltip("구워 놓은 애니메이션 한 벌. 비워 두면 적은 시뮬레이션만 돌고 화면에 그려지지 않는다.\n" +
             "만드는 법: 고블린 프리팹을 고르고 메뉴에서 PickMeUp > 적 애니메이션 굽기.")]
    [SerializeField] private EnemyAnimationLibrary animationLibrary;

    [Tooltip("살에 칼이 닿을 때 뿌릴 파티클. 비워 두면 피가 튀지 않는다.\n" +
             "Goblin.prefab의 UnitController에 들어 있는 것과 같은 프리팹을 넣으면 된다.")]
    [SerializeField] private GameObject[] bloodEffectPrefabs;
    [Tooltip("피 색. 고블린은 초록이다 — 종족마다 다르므로 프리팹 원본의 색조만 바꾼다.")]
    [SerializeField] private Color bloodColor = new Color(0.2f, 0.6f, 0.1f, 1f);
    [SerializeField] private Vector3 bloodEffectOffset = new Vector3(0f, 1f, 0f);

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
    [Tooltip("몸을 돌리는 속도(도/초). 게임오브젝트 고블린의 rotationSpeed와 같은 값이다.")]
    [SerializeField, Min(0f)] private float turnRate = 720f;
    [Tooltip("휘두르는 중(준비·회수·도약·물기·움찔)에 도는 속도(도/초). 제자리에서 베는 모션 위에서 " +
             "몸이 홱 돌면 발이 미끄러진다. 게임오브젝트 고블린의 attackTurnSpeed와 같은 값이다.")]
    [SerializeField, Min(0f)] private float swingTurnRate = 120f;
    [Tooltip("달리기 클립이 나아가는 속도(m/s). 실제 속도를 이 값으로 나눠 재생 배속을 맞춘다.\n\n" +
             "대비값이다 — 애니메이션을 구우면 베이커가 리타깃된 뒤의 보폭을 직접 재서 라이브러리에 " +
             "적고(ClipRange.groundSpeed), 시뮬레이션은 그 값을 먼저 쓴다. 여기 적는 값은 아직 굽지 " +
             "않았거나 제자리 클립이라 잴 것이 없을 때만 쓰인다.\n\n" +
             "손으로 적을 때 빠지기 쉬운 함정: 클립 인스펙터의 Average Speed는 원본 리그 기준이라 " +
             "리타깃 축소(humanScale)가 빠져 있다. 고블린은 Run이 2.29로 보이지만 humanScale이 " +
             "0.73이라 실제로는 1.67로 걷는다.")]
    [SerializeField, Min(0f)] private float runClipSpeed = 1.67f;
    [Tooltip("몸의 반지름. 아군 NavMeshAgent의 반지름(0.5)과 같은 값으로 두면 " +
             "두 세계의 간격이 눈에 띄게 어긋나지 않는다.\n\n" +
             "두 마리 사이에 지켜지는 최소 간격이 이 값의 두 배다(0.5 → 1m). 밀어내는 힘만이 아니라 " +
             "자리로도 풀기 때문에(EnemyMovementSystem) 마리 수가 아무리 늘어도 이 선은 지켜진다. " +
             "더 벌려 세우고 싶으면 여기를 올린다.")]
    [SerializeField] private float radius = 0.5f;
    [Tooltip("멈춰 설 거리. 사거리보다 조금 안쪽이라 도착하자마자 휘두를 수 있다.")]
    [SerializeField] private float standoffDistance = 1.0f;

    [Header("도약 (덤벼들기)")]
    [Tooltip("사거리 밖이면서 이 거리 안일 때 뛰어서 덤벼든다. 0이면 뛰지 않는다.\n" +
             "게임오브젝트 고블린의 leapAttackRange가 3이라 같은 값으로 둔다.")]
    [SerializeField] private float leapRange = 3f;
    [Tooltip("도약 한 번에 걸리는 시간(초). 구운 LeapAttack 클립이 1.1초다 — " +
             "이 값과 클립 길이가 어긋나면 뛰는 도중에 발이 미끄러진다.")]
    [SerializeField] private float leapDuration = 1.1f;
    [SerializeField] private float leapCooldown = 6f;
    [Tooltip("도약의 정점 높이(미터). 게임오브젝트 고블린의 leapAttackHeight가 0.5라 같은 값으로 둔다. 0이면 땅에 붙어 덤빈다.")]
    [SerializeField, Min(0f)] private float leapHeight = 0.5f;
    [Tooltip("도약 클립이 제 스스로 떠 있는 높이(미터). 공중 공격 클립이라 발끝이 땅에서 7~21cm 떠 있어서, " +
             "웅크림과 착지 순간에 이만큼 눌러 내려 발을 땅에 디디게 한다. 클립을 바꾸면 다시 잴 것.")]
    [SerializeField, Min(0f)] private float leapClipFloat = 0.1f;

    [Header("물어뜯기 (붙잡는 한 방)")]
    [Tooltip("붙잡고 늘어져 무는 피해. 0이면 물지 않는다.\n" +
             "게임오브젝트 고블린의 skillDamage가 24라 같은 값으로 둔다.")]
    [SerializeField] private int biteDamage = 24;
    [Tooltip("무는 데 걸리는 시간(초). 구운 Bite 클립이 2.08초다.")]
    [SerializeField] private float biteDuration = 2.08f;
    [Tooltip("재사용 대기(초). 물린 아군이 다시 물리지 않는 시간이기도 하다 — " +
             "그러지 않으면 한 명에게 여럿이 동시에 물고 늘어져 그 자리에서 녹는다.")]
    [SerializeField] private float biteCooldown = 5f;
    [Tooltip("물린 아군이 뿌리치지 못하고 굳어 있는 시간(초). 0이면 경직시키지 않는다.\n" +
             "게임오브젝트 고블린의 skillStaggerDuration이 1.5라 같은 값으로 둔다.\n\n" +
             "이게 이 동작의 전부다 — 물어뜯기는 피해로 잡는 수가 아니라 한 명을 판에서 빼는 수다. " +
             "그래서 무는 순간에는 강인도 피해를 넘기지 않는다(그쪽으로 깨지면 면역이 켜져 경직이 막힌다).")]
    [SerializeField] private float biteStaggerDuration = 1.5f;
    [Tooltip("한 전투에 물 수 있는 횟수. 0이면 제한이 없다. 게임오브젝트 고블린의 skillUseCount가 2라 같은 값으로 둔다.")]
    [SerializeField, Min(0)] private int biteUsesPerBattle = 2;
    [Tooltip("표적의 사거리 안에 이만큼(초) 붙어 있어야 문다. 게임오브젝트 고블린의 skillEngageDelay가 1이라 같은 값으로 둔다 — " +
             "달려오는 동안 시간을 세면 닿는 순간 곧바로 물어뜯는다.")]
    [SerializeField, Min(0f)] private float biteEngageDelay = 1f;

    [Header("강인도와 무너짐")]
    [Tooltip("이 적의 한 대가 아군의 강인도를 얼마나 깎는가. 아군의 maxPoise가 100이므로 " +
             "이 값이 클수록 아군이 빨리 무너진다.")]
    [SerializeField] private float poiseDamagePerHit = 15f;
    [SerializeField] private float maxPoise = 100f;
    [SerializeField] private float poiseBreakImmunity = 2.5f;
    [SerializeField] private float staggerDuration = 1.2f;
    [SerializeField] private float hitReactionDuration = 0.3f;
    [SerializeField] private float knockbackDistance = 0.6f;
    [Tooltip("칼을 내지르고 거두는 중(평타 회수·도약 착지 뒤)에 받는 피해 배율. 게임오브젝트 고블린의 " +
             "recoveryVulnerabilityMultiplier와 같다.")]
    [SerializeField, Min(0f)] private float recoveryVulnerabilityMultiplier = 1.35f;
    [Tooltip("무너져 있는 동안 받는 피해 배율. 게임오브젝트 고블린의 staggerDamageMultiplier와 같다.")]
    [SerializeField, Min(0f)] private float staggerDamageMultiplier = 1.4f;

    [Header("감정 (공포·패닉·빈사·출혈)")]
    [Tooltip("게임오브젝트 고블린이 달고 있던 UnitEmotion의 값 그대로다. 맞아서 HP를 잃고, 곁에서 동료가 쓰러지고, " +
             "HP가 바닥이면 공포가 차오른다 — 공포(40)면 공격력·이동속도 30% 감소, 패닉(85)이면 2.5초 행동불가.")]
    [SerializeField] private EmotionProfile emotion = new EmotionProfile { stressPerPanic = 12f, stressPerAllyDeath = 8f };

    [Header("어그로")]
    [SerializeField] private float threatWeight = 1f;

    [Header("난전의 호흡 (판단 박자)")]
    [Tooltip("마리마다 이 범위에서 판단 주기(초)를 하나 뽑는다. 표적을 고르고, 칼 들 자리를 청하고, " +
             "스윙을 시작하는 것은 이 박자에만 한다. 맞고 움찔하는 반응은 박자를 기다리지 않는다.")]
    [SerializeField] private float thinkIntervalMin = 0.18f;
    [SerializeField] private float thinkIntervalMax = 0.36f;
    [Tooltip("공격 재사용 대기에 섞는 흔들림(비율). 0.2면 0.8~1.2배. 0이면 전원이 같은 박자로 휘두른다.")]
    [SerializeField, Range(0f, 0.5f)] private float attackCooldownJitter = 0.2f;

    [Header("공격 슬롯 (한 사람에게 동시에 칼을 드는 수)")]
    [Tooltip("표적과 이 거리(미터) 안에 들어오면 칼 들 자리를 청한다. 자리 수 자체는 맞는 쪽이 정한다 " +
             "(UnitStats.enemyAttackSlots, 0이면 2). 0이면 사거리·도약 거리에 1.5m를 더한 값을 쓴다.")]
    [SerializeField] private float slotRequestRange = 4.5f;
    [Tooltip("자리 하나로 휘두르는 횟수(도약·물기도 한 번). 다 쓰면 곁에서 기다리던 놈에게 넘긴다.")]
    [SerializeField, Range(1, 7)] private int swingsPerSlotMin = 1;
    [SerializeField, Range(1, 7)] private int swingsPerSlotMax = 3;
    [Tooltip("자리를 내놓은 뒤 다시 청하기까지(초). 실제로는 0.7~1.3배로 흔들린다.")]
    [SerializeField] private float slotYieldDelay = 0.9f;
    [Tooltip("자리를 쥐고도 이만큼(초) 휘두르지 못하면 내려놓는다. 상대가 달아나는 중일 때다.")]
    [SerializeField] private float slotHoldTimeout = 2.5f;
    [Tooltip("자리를 못 얻었을 때 표적과 벌려 두는 거리(미터). 마리마다 이 사이에서 하나 뽑는다. " +
             "어느 방향에 설지는 정하지 않는다 — 무리가 서로 밀치며 정한다.")]
    [SerializeField] private float waitDistanceMin = 2.0f;
    [SerializeField] private float waitDistanceMax = 2.9f;

    [Header("타격의 무게")]
    [Tooltip("칼이 닿은 순간 휘두른 쪽이 멈칫하는 시간(초). 도약은 1.5배, 물기는 2배.")]
    [SerializeField] private float hitStopDuration = 0.05f;
    [Tooltip("멈칫하는 동안의 시간 배율. 0에 가까울수록 완전히 멈춘다.")]
    [SerializeField, Range(0f, 1f)] private float hitStopScale = 0.1f;
    [Tooltip("살에 닿은 한 대를 맞은 뒤 발이 무거워지는 시간(초). 무너졌을 때는 걸지 않는다(이미 못 움직인다).")]
    [SerializeField] private float hitFlinchDuration = 0.4f;
    [SerializeField, Range(0.1f, 1f)] private float hitFlinchMoveMultiplier = 0.5f;

    [Header("정찰 (표적을 잃었을 때)")]
    [Tooltip("표적을 잃고 이만큼(초) 지나면 정찰을 나선다. 마지막으로 본 자리에서 원을 넓혀 가며 돈다.\n\n" +
             "이게 없으면 탐지 거리 밖으로 도망친 생존자를 아무도 찾으러 가지 않아 전투가 끝나지 않는다.")]
    [SerializeField, Min(0f)] private float patrolDelay = 3f;
    [Tooltip("정찰 걸음(m/s). 걷기 클립 보폭(구운 값 1.52m/s)의 0.5~1.3배 안이어야 발이 미끄러지지 않는다.")]
    [SerializeField, Min(0f)] private float patrolSpeed = 1.5f;
    [Tooltip("정찰 지점에 닿은 뒤 둘러보는 시간(초). 마리마다 이 사이에서 뽑는다.")]
    [SerializeField, Min(0f)] private float patrolPauseMin = 1f;
    [SerializeField, Min(0f)] private float patrolPauseMax = 2.5f;
    [Tooltip("첫 정찰 반경(미터). 마지막으로 본 자리 둘레부터 찾는다.")]
    [SerializeField, Min(0f)] private float searchRadiusStart = 6f;
    [Tooltip("지점 하나를 돌 때마다 정찰 반경을 넓히는 폭(미터). 못 찾으면 점점 멀리 나가 결국 전장 전체를 덮는다.")]
    [SerializeField, Min(0f)] private float searchRadiusGrowth = 6f;
    [Tooltip("정찰 범위를 전장 가장자리에서 이만큼(미터) 안쪽으로 줄인다.\n\n" +
             "범위는 아군이 설 수 있는 NavMesh에서 잰다. 적은 지형 높이를 모르므로 평평한 전장을 벗어나면 " +
             "땅에 파묻히거나 뜬다 — 아군이 갈 수 없는 곳까지 찾으러 갈 이유도 없다.")]
    [SerializeField, Min(0f)] private float patrolEdgeMargin = 2f;
    [Tooltip("NavMesh가 없을 때 쓰는 정찰 범위(원점 중심 반폭, 미터). 0이면 정찰하지 않는다.")]
    [SerializeField, Min(0f)] private float fallbackPatrolHalfExtent = 38f;

    [Header("층별 배율")]
    [Tooltip("층이 하나 오를 때마다 체력에 곱해지는 비율. CharacterBattleSpawner와 같은 규칙이다.")]
    [SerializeField] private float hpPerLevel = 0.15f;
    [SerializeField] private float damagePerLevel = 0.1f;

    // 레벨이 오를수록 체력과 공격력이 선형으로 는다. 레벨은 부르는 쪽이 층에서 뽑는다
    // (CharacterBattleSpawner — 층 + 1~2).
    //
    // 한때 양 진영 체력에 100배를 곱하는 임시 손잡이(debugHealthMultiplier)가 여기로 넘어왔다.
    // 전투 흐름을 오래 보려던 것이고, 1층부터 밸런스를 잡기 시작하면서 걷어냈다(2026-09).
    public EnemyStats BuildStats(int level)
    {
        int steps = Mathf.Max(0, level - 1);

        return new EnemyStats
        {
            maxHp = Mathf.Max(1, Mathf.RoundToInt(maxHp * (1f + hpPerLevel * steps))) * BattleTestTuning.HealthMultiplier,
            attackDamage = Mathf.Max(1, Mathf.RoundToInt(attackDamage * (1f + damagePerLevel * steps))),

            attackRange = attackRange,
            attackArcAngle = attackArcAngle,
            attackHitTolerance = attackHitTolerance,

            leapRange = leapRange,
            leapDuration = leapDuration,
            leapCooldown = leapCooldown,
            leapHeight = leapHeight,
            leapClipFloat = leapClipFloat,

            biteDamage = Mathf.Max(0, Mathf.RoundToInt(biteDamage * (1f + damagePerLevel * steps))),
            biteDuration = biteDuration,
            biteCooldown = biteCooldown,
            biteStaggerDuration = biteStaggerDuration,
            biteUsesPerBattle = biteUsesPerBattle,
            biteEngageDelay = biteEngageDelay,

            attackWindup = attackWindup,
            attackRecovery = attackRecovery,
            attackCooldown = attackCooldown,

            detectRange = detectRange,
            fieldOfView = fieldOfView,

            moveSpeed = moveSpeed,
            acceleration = acceleration,
            turnRate = turnRate,
            swingTurnRate = swingTurnRate,
            runClipSpeed = runClipSpeed,
            radius = radius,
            standoffDistance = standoffDistance,

            poiseDamagePerHit = poiseDamagePerHit,
            maxPoise = maxPoise,
            poiseBreakImmunity = poiseBreakImmunity,
            staggerDuration = staggerDuration,
            hitReactionDuration = hitReactionDuration,
            knockbackDistance = knockbackDistance,
            recoveryVulnerabilityMultiplier = recoveryVulnerabilityMultiplier,
            staggerDamageMultiplier = staggerDamageMultiplier,

            threatWeight = threatWeight,

            thinkIntervalMin = Mathf.Max(0f, thinkIntervalMin),
            thinkIntervalMax = Mathf.Max(thinkIntervalMin, thinkIntervalMax),
            attackCooldownJitter = attackCooldownJitter,

            slotRequestRange = slotRequestRange,
            swingsPerSlotMin = (byte)Mathf.Clamp(swingsPerSlotMin, 1, 255),
            swingsPerSlotMax = (byte)Mathf.Clamp(Mathf.Max(swingsPerSlotMin, swingsPerSlotMax), 1, 255),
            slotYieldDelay = slotYieldDelay,
            slotHoldTimeout = slotHoldTimeout,
            waitDistanceMin = waitDistanceMin,
            waitDistanceMax = Mathf.Max(waitDistanceMin, waitDistanceMax),

            hitStopDuration = hitStopDuration,
            hitStopScale = hitStopScale,
            hitFlinchDuration = hitFlinchDuration,
            hitFlinchMoveMultiplier = hitFlinchMoveMultiplier,

            // 정찰 범위는 전투를 열 때 한 번 잰다(ResolvePatrolArea). 여기서는 걸음과 박자만 싣는다.
            patrolDelay = patrolDelay,
            patrolSpeed = patrolSpeed,
            patrolPauseMin = patrolPauseMin,
            patrolPauseMax = Mathf.Max(patrolPauseMin, patrolPauseMax),
            searchRadiusStart = searchRadiusStart,
            searchRadiusGrowth = searchRadiusGrowth,
        };
    }

    // 정찰할 수 있는 범위. 아군이 설 수 있는 NavMesh의 테두리에서 가장자리 여유만큼 안으로 줄인다.
    //
    // 전장 크기를 숫자로 적어 두지 않는 이유: 맵마다 생성기가 NavMesh를 다시 굽고(BattleMapBuilder),
    // 적이 찾아야 할 상대는 그 판 위에만 설 수 있다. 판을 재면 맵이 바뀌어도 따라온다.
    // 전투를 열 때 한 번만 부른다 — 삼각형을 전부 받아 오므로 매 프레임 부를 것이 아니다.
    private bool ResolvePatrolArea(out Vector3 center, out Vector2 halfExtents)
    {
        NavMeshTriangulation mesh = NavMesh.CalculateTriangulation();
        if (mesh.vertices != null && mesh.vertices.Length > 0)
        {
            var bounds = new Bounds(mesh.vertices[0], Vector3.zero);
            for (int i = 1; i < mesh.vertices.Length; i++) bounds.Encapsulate(mesh.vertices[i]);

            center = bounds.center;
            halfExtents = new Vector2(
                Mathf.Max(0f, bounds.extents.x - patrolEdgeMargin),
                Mathf.Max(0f, bounds.extents.z - patrolEdgeMargin));
            return halfExtents.x > 0f && halfExtents.y > 0f;
        }

        center = Vector3.zero;
        halfExtents = new Vector2(fallbackPatrolHalfExtent, fallbackPatrolHalfExtent);
        return fallbackPatrolHalfExtent > 0f;
    }

    // 층 하나를 시작할 때 부른다. 돌려주는 값은 실제로 만들어진 마리 수.
    public int SpawnWave(int count, Vector3 center, float spread, int level, uint seed = 1)
    {
        // 무엇으로 그릴지 먼저 알려 준다. 굽지 않았으면 보이지 않을 뿐 전투는 그대로 돈다.
        if (animationLibrary != null && animationLibrary.IsBaked)
        {
            EnemyHorde.ConfigureVisual(animationLibrary.skinnedMesh, animationLibrary.material, animationLibrary);
        }

        EnemyHorde.ConfigureBlood(bloodEffectPrefabs, bloodColor, bloodEffectOffset);

        EnemyStats stats = BuildStats(level);

        // 적은 지형을 모르므로 정찰 지점의 높이는 전장 바닥에 맞춘다(스폰 높이와 같다).
        if (ResolvePatrolArea(out Vector3 patrolCenter, out Vector2 patrolHalfExtents))
        {
            stats.patrolCenter = new float3(patrolCenter.x, center.y, patrolCenter.z);
            stats.patrolHalfExtents = patrolHalfExtents;
        }

        // 스트레스 누적은 아군과 같은 스위치를 따른다(테스트 중에는 꺼 둔다).
        EnemyEmotionProfile emotionProfile = EnemyEmotionProfile.From(emotion, CharacterStress.AccumulationEnabled);

        return EnemyHorde.Spawn(stats, count, center, spread, seed, emotionProfile);
    }
}
