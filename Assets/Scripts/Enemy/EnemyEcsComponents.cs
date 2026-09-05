using Unity.Entities;
using Unity.Mathematics;

// 적 한 마리가 들고 있는 데이터 전부.
//
// 아군(UnitController)과 나란히 두고 보면 무엇이 빠졌는지가 이 구조의 핵심이다.
// 아군은 Animator 25상태 · NavMeshAgent · TargetScanner · UnitEmotion을 각자 들고 다닌다.
// 그 셋은 유닛 하나당 고정 비용이라 마리 수에 그대로 곱해진다 — 실측으로 시체 133구가
// Animators.Update만으로 1.0ms를 먹었다. 1000마리에서는 그 방식이 성립하지 않는다.
//
// 그래서 적은 "지금 어디에 있고, 무엇을 하는 중이고, 얼마나 남았는가"만 남긴다.
// 애니메이션은 (클립, 진행도) 두 값으로 줄여 렌더러에게 넘기고(EnemyAnimation),
// 길찾기는 NavMesh 대신 스티어링과 공간 해시로 대신한다(EnemyMovementSystem).
//
// 여기 있는 모든 구조체는 blittable이다. 관리 객체를 하나라도 넣는 순간 Burst가 꺼지고
// 청크가 쪼개져서, 이 구조를 택한 이유가 사라진다.

// 적이라는 표시. 질의를 좁히는 용도로만 쓴다.
public struct EnemyTag : IComponentData
{
}

// 프리팹에서 한 번 구워 와서 전투 내내 바뀌지 않는 수치.
//
// UnitStats에서 적이 실제로 쓰는 것만 골라 왔다. 아군 전용 수치(마나, 회복약, 치유,
// 보호막, 은신, 패링)는 여기 없다 — 고블린은 그 어느 것도 하지 않는다.
public struct EnemyStats : IComponentData
{
    public int maxHp;
    public int attackDamage;

    // 사거리와 타격 판정. 스윙이 시작된 뒤 상대가 벗어나면 빗나가야 하므로
    // 타격 순간에 다시 잰다(EnemyCombatSystem) — 아군 쪽 ApplyAttackDamage와 같은 규칙이다.
    public float attackRange;
    public float attackArcAngle;
    public float attackHitTolerance;

    // 공격 한 번의 시간 구성. 애니메이션 이벤트가 없으므로 여기 적힌 시간이 곧 타격 시점이다.
    //
    // windup이 아군에게 열리는 방어 창이다. 이 구간에 있는 적을 아군이 "칼을 들어올린 놈"으로
    // 읽고 방패를 든다(EnemyFlags.Telegraphing). 아군 쪽 blockReactionTime이 0.18초라
    // 이 값이 그보다 넉넉해야 방어가 성립한다.
    public float attackWindup;
    public float attackRecovery;
    public float attackCooldown;

    // 탐지. 시야각은 원작 고블린이 정면을 보고 달려드는 짐승이라 아군보다 좁다.
    public float detectRange;
    public float fieldOfView;

    // 이동. NavMeshAgent가 없으므로 가속과 반경을 직접 들고 있는다.
    public float moveSpeed;
    public float acceleration;
    public float turnSpeed;
    public float radius;

    // 멈춰 설 거리. 사거리보다 조금 안쪽이라 도착하자마자 휘두를 수 있다.
    public float standoffDistance;

    // 이 적의 한 대가 아군의 강인도를 얼마나 깎는가. 아군 쪽 UnitStats.poiseDamagePerHit와
    // 같은 뜻인데, 때리는 쪽의 수치라 여기 있다.
    public float poiseDamagePerHit;

    // 강인도와 무너짐. 아군의 퍼펙트 가드가 이 값을 깎아 적을 무너뜨린다.
    public float maxPoise;
    public float poiseBreakImmunity;
    public float staggerDuration;
    public float hitReactionDuration;
    public float knockbackDistance;

    // 아군이 표적을 고를 때 쓰는 가중치. 아군 쪽 UnitStats.threatWeight와 같은 뜻이다.
    public float threatWeight;
}

// 매 프레임 바뀌는 것.
public struct EnemyHealth : IComponentData
{
    public int current;
    public float poise;

    // 강인도가 깨진 뒤의 면역 시간. 이게 없으면 여럿에게 둘러싸인 순간 무한 경직에 빠진다 —
    // 아군 쪽 poiseImmuneUntil과 같은 규칙이다.
    public double poiseImmuneUntil;
}

public struct EnemyMotion : IComponentData
{
    public float3 velocity;

    // 이번 프레임에 가려는 쪽. 스티어링 결과를 이동 시스템이 여기 적고 통합한다.
    public float3 desiredDirection;
}

// 지금 겨누고 있는 아군.
//
// UnitController 참조가 아니라 아군 스냅샷의 인덱스다(EnemyWorldBridge). 그래야 표적 선택이
// Burst 잡 안에서 돌 수 있다 — 관리 참조를 하나라도 들고 있으면 그 잡 전체가 메인 스레드로
// 내려온다. 인덱스는 스냅샷을 다시 만들 때마다 유효성이 확인된다.
public struct EnemyTarget : IComponentData
{
    public int allyIndex;
    public double nextRetargetTime;

    public const int None = -1;
}

// 지금 무엇을 하는 중인가. 아군의 행동 트리에 해당하는 자리인데, 잎이 훨씬 적다.
//
// 고블린에게는 방어도 스킬도 영창도 없다. 붙고, 휘두르고, 맞고, 무너지고, 죽는 것이 전부다.
// 그래서 트리를 세우지 않고 열거 하나로 둔다 — 가지가 여섯 개뿐인데 트리를 얹으면
// 청크마다 분기가 늘어나기만 한다.
public enum EnemyActionKind : byte
{
    // 표적이 없다. 제자리에서 둘러본다.
    Idle,

    // 표적에게 붙는 중.
    Approach,

    // 칼을 들어올렸다. 이 구간이 아군에게 열리는 방어 창이다.
    Windup,

    // 내지른 뒤 회수. 이 동안은 스스로 아무것도 바꾸지 않는다.
    Recover,

    // 한 대 맞아 잠깐 끊겼다.
    HitReact,

    // 자세가 통째로 무너졌다.
    Stagger,

    // 쓰러졌다.
    Dead,
}

public struct EnemyAction : IComponentData
{
    public EnemyActionKind kind;

    // 지금 구간이 끝나기까지 남은 시간.
    public float timer;

    // 다음 스윙이 가능해지는 시각.
    public double nextAttackTime;

    // 이번 스윙의 타격을 이미 넣었는가. windup이 끝나는 프레임에 한 번만 넣기 위한 것.
    public bool struckThisSwing;
}

// 렌더러에게 넘기는 애니메이션 상태. 이 둘이면 GPU에서 굽든 인스턴싱을 하든 그릴 수 있다.
//
// Animator를 두지 않은 이유가 여기 있다. 1000마리면 Animator만으로 프레임이 무너지는데,
// 실제로 필요한 정보는 "어느 클립의 몇 퍼센트 지점인가" 두 값뿐이다.
public enum EnemyClip : byte
{
    Idle,
    Run,
    Attack,
    Hit,
    Stagger,
    Death,
}

public struct EnemyAnimation : IComponentData
{
    public EnemyClip clip;
    public float normalizedTime;
}

// 스폰 요청. 층마다 마리 수가 달라지므로 값으로 받는다.
public struct EnemySpawnRequest : IComponentData
{
    public Entity prefab;
    public int count;
    public float3 center;
    public float spread;
    public int level;

    // 층이 올라갈수록 붙는 배율. 아군 쪽 CharacterBattleSpawner.BuildEnemyStats와 같은 규칙이다.
    public float hpMultiplier;
    public float damageMultiplier;
}
