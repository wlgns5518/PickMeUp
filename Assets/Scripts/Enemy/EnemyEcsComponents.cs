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
    // 타격 순간에 다시 잰다(EnemyCombatSystem) — 아군 쪽 ResolveAttackHit와 같은 규칙이다.
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
    public float radius;

    // 몸을 돌리는 속도(도/초). 0이면 곧바로 돈다.
    //
    // 예전 turnSpeed는 지수 감쇠 배율(8)이었다 — 초당 각도가 아니라 "남은 각도의 몇 %를 좁히나"라서
    // 큰 각도일수록 빨리 돌았고, 무리에 밀려 가는 방향이 뒤집힐 때마다 홱 돌았다.
    // 게임오브젝트 고블린의 rotationSpeed(720)·attackTurnSpeed(120)와 같은 뜻으로 바꿨다.
    public float turnRate;
    public float swingTurnRate;

    // 달리기 클립이 원래 나아가는 속도(m/s). 실제 속도를 이 값으로 나눠 재생 배속을 정한다.
    // 0이면 클립 제 속도로 돈다. 고블린 Run은 2.29 — 4m/s로 달리면서 1배속으로 돌면 땅을 미끄러진다.
    public float runClipSpeed;

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

    // 도약. 0이면 뛰지 않는다.
    //
    // 사거리 밖이면서 이 거리 안일 때만 뛴다 — 이미 닿는 상대에게 뛰면 뒤로 물러났다
    // 덤비는 꼴이 된다. 아군 쪽 CanLeapAttack과 같은 규칙이다.
    public float leapRange;
    public float leapDuration;
    public float leapCooldown;

    // 물어뜯기. 0이면 물지 않는다. 붙어 있는 동안 상대를 따라다니다가 끝에 한 번 문다.
    public int biteDamage;
    public float biteDuration;
    public float biteCooldown;

    // 물린 아군이 뿌리치지 못하고 굳어 있는 시간(초). 0이면 경직시키지 않는다.
    //
    // 이게 이 동작의 전부다 — 물어뜯기는 피해로 잡는 수가 아니라 한 명을 판에서 빼는 수다.
    // 강인도를 깎아 깨지기를 기다리는 것과는 다르므로, 무는 순간에는 강인도 피해를 아예 넘기지
    // 않는다. 둘 다 넣으면 그 한 방으로 강인도가 먼저 깨지면서 면역 시간이 켜지고, 정작 경직이
    // 그 면역에 막힌다(아군 쪽 UnitController.ResolveSkillHit 주석과 같은 이유다).
    public float biteStaggerDuration;

    // 이 리그가 가진 콤보 단수. 굽힌 클립 수에서 나오므로 스포너가 아니라 EnemyHorde가 채운다 —
    // 리그마다 단수가 다르고, 없는 클립을 가리키면 그 스윙만 서 있는 그림이 된다.
    // 0이나 1이면 콤보 없이 1단만 반복한다.
    public byte comboSteps;

    // ---------------------------------------------------------------- 난전의 호흡
    //
    // 아래가 없던 시절에는 사거리 안의 모든 고블린이 쿨다운이 도는 그 프레임에 같이 휘둘렀다.
    // 같은 프레임에 스폰돼 같은 쿨다운(1.1초)을 쓰니 박자까지 완전히 같았다 — 한 명을 둘러싼
    // 여섯 마리가 한 몸처럼 칼을 올리고 내렸다. 자리를 나눠 주는 대신(포메이션 슬롯) 판단하는
    // 순간과 칼을 들 권리를 흩어 놓는다. 서 있는 자리는 여전히 뒤엉킨다.

    // 판단 주기(초). 마리마다 이 사이에서 한 번 뽑아 들고 간다. 반응(피격·경직·사망)은
    // 이 주기와 무관하게 그 프레임에 일어나고, 새 수를 고르는 것(표적·슬롯·공격 개시)만 여기 묶인다.
    public float thinkIntervalMin;
    public float thinkIntervalMax;

    // 공격 슬롯을 청할 거리. 이보다 멀면 슬롯을 따지지 않고 붙으러 간다(Chaser).
    public float slotRequestRange;

    // 슬롯 하나로 휘두를 수 있는 횟수(도약·물기도 한 번으로 친다). 다 쓰면 내려놓는다.
    public byte swingsPerSlotMin;
    public byte swingsPerSlotMax;

    // 슬롯을 내려놓은 뒤 다시 줄을 서기까지(초). 방금 휘두른 놈이 곧바로 다시 쥐면 기다리던
    // 놈들 차례가 영영 오지 않는다.
    public float slotYieldDelay;

    // 슬롯을 쥐고도 이만큼 휘두르지 못하면 내려놓는다(초). 상대가 달아나는 중이면 붙잡고 있을
    // 이유가 없다 — 그동안 곁에서 기다리던 놈이 칼을 못 든다.
    public float slotHoldTimeout;

    // 슬롯을 못 얻었을 때 표적과 벌려 두는 거리. 마리마다 이 사이에서 한 번 뽑는다 —
    // 둘레의 어느 자리인지는 정하지 않는다. 거리만 정하고 방위는 무리가 밀치며 정한다.
    public float waitDistanceMin;
    public float waitDistanceMax;

    // 공격 재사용 대기에 섞는 흔들림(비율). 0.2면 0.8~1.2배.
    public float attackCooldownJitter;

    // ---------------------------------------------------------------- 타격의 무게

    // 칼이 닿은 순간 휘두른 쪽이 스스로 멈칫하는 시간과 그동안의 재생 배속.
    // 아군 쪽 UnitStats.hitStopDuration/hitStopScale과 같은 뜻이다.
    public float hitStopDuration;
    public float hitStopScale;

    // 한 대 맞은 뒤 발이 무거워지는 시간과 배율. 움찔 모션이 끝나고도 곧바로 제 속도로
    // 달려들지 못해야 맞은 것이 몸에 남는다.
    public float hitFlinchDuration;
    public float hitFlinchMoveMultiplier;

    // ---------------------------------------------------------------- 정찰
    //
    // 표적을 잃은 놈이 할 일. 이게 없던 동안 표적이 없는 적은 그 자리에 서 있기만 했다 — 탐지 거리(8m)
    // 밖으로 달아난 아군을 아무도 찾으러 가지 않아서, 체력이 바닥나 도망친 생존자와 제자리의 고블린이
    // 서로 닿지 못한 채 전투가 끝나지 않았다(플레이 테스트에서 25초 넘게 아무 일도 일어나지 않았다).
    //
    // 정찰은 마지막으로 본 자리에서 원을 넓혀 가며 돈다(EnemyPatrolSystem). 방금까지 싸우던 곳 둘레부터
    // 훑고, 못 찾으면 점점 멀리 나간다 — 아무 데나 떠돌면 전장 반대편만 헤매다 시간을 보낸다.

    // 정찰할 수 있는 범위(전장). 적은 지형 높이를 모르므로 평평한 전장 밖으로 나가면 땅에 파묻힌다.
    // 반폭이 0이면 정찰하지 않는다(테스트, 전장 정보가 없는 원본).
    public float3 patrolCenter;
    public float2 patrolHalfExtents;

    // 표적을 잃고 이만큼(초) 지나야 정찰을 나선다. 곧바로 나서면 방금 쓰러뜨린 자리에서 둘러볼 틈도 없이 흩어진다.
    public float patrolDelay;

    // 정찰 걸음(m/s). 걷기 클립이 발을 땅에 붙이는 속도 안이어야 한다(구운 걷기 보폭 1.52m/s의 0.5~1.3배).
    public float patrolSpeed;

    // 정찰 지점에 닿은 뒤 둘러보는 시간(초). 이 사이에서 뽑는다.
    public float patrolPauseMin;
    public float patrolPauseMax;

    // 첫 정찰 반경과, 지점 하나를 돌 때마다 넓히는 폭(미터).
    public float searchRadiusStart;
    public float searchRadiusGrowth;

    public bool CanPatrol => patrolHalfExtents.x > 0f && patrolHalfExtents.y > 0f;
}

// 교전 중 맡은 임시 역할.
//
// 같은 무리라도 한 순간에 칼을 드는 놈, 곁에서 틈을 보는 놈, 아직 달려오는 놈이 섞여 있어야
// 각자 판단하는 것처럼 보인다. 역할은 고정이 아니라 판단할 때마다 다시 정해지고, 칼을 들 권리
// (Attacker)는 표적 한 명당 몇 자리뿐이다(EnemyAttackSlotSystem).
public enum EnemyCombatRole : byte
{
    // 아직 멀다. 표적에게 붙으러 간다.
    Chaser,

    // 붙었지만 칼을 들 자리가 없다. 한 걸음 떨어져 틈을 본다.
    Waiter,

    // 공격 슬롯을 쥐었다. 이 역할만 휘두르고, 덤벼들고, 문다.
    Attacker,
}

// 판단 박자와 공격 슬롯. 아군의 행동 트리가 매 틱 하는 "지금 무엇을 할까"를 적은 여기에 적힌
// 박자에만 한다.
//
// 슬롯 수는 적이 세지 않는다. 매 프레임 Attacker 역할을 쥔 엔티티를 표적별로 다시 센다 —
// 따로 카운터를 들고 다니면 죽거나 표적을 잃은 놈이 돌려주지 않은 자리가 쌓여 결국 아무도
// 칼을 못 들게 된다. 들고 있는 역할 자체가 곧 슬롯이다.
public struct EnemyTactics : IComponentData
{
    public EnemyCombatRole role;

    // 슬롯을 쥔 상대(아군 스냅샷 인덱스). role이 Attacker일 때만 의미가 있다.
    public int slotAllyIndex;

    // 이번 슬롯으로 남은 스윙 수.
    public byte swingsLeft;

    // 이때까지 휘두르지 못하면 슬롯을 내려놓는다. 스윙을 시작할 때마다 뒤로 민다.
    public double slotExpireTime;

    // 이 시각 전에는 슬롯을 청하지 않는다.
    public double nextSlotRequestTime;

    // 다음 판단 시각(SystemAPI.Time.ElapsedTime 기준). 코루틴이나 yield 없이 시계 비교 하나로 돈다.
    public double nextThinkTime;

    // 이 개체의 판단 주기. 처음 판단할 때 stats의 범위에서 뽑는다.
    public float thinkInterval;

    // 이번 프레임이 판단 박자인가. EnemyThinkSystem이 세우고 같은 프레임의 뒤 시스템들이 읽는다.
    public bool thinking;

    // 슬롯이 없을 때 표적과 벌려 두는 거리. 처음 판단할 때 뽑는다.
    public float waitDistance;

    // 개체마다 따로 도는 난수. 스포너가 씨를 뿌리고, 비어 있으면 EnemyThinkSystem이 채운다.
    public Random random;

    // 판단 주기·기다리는 거리 같은 이 개체의 성격을 이미 뽑았는가.
    public bool primed;

    // ---------------------------------------------------------------- 정찰(EnemyPatrolSystem)

    // 표적을 마지막으로 본 자리. 정찰은 여기서 원을 넓혀 가며 돈다. 아무도 본 적이 없으면 태어난 자리다.
    public float3 searchCenter;
    public float searchRadius;
    public bool searchPrimed;

    // 표적이 없어진 시각. 표적을 쥐고 있는 동안에는 매 프레임 지금으로 민다.
    public double lostTargetTime;

    // 지금 걸어가는 정찰 지점. 닿지 못하고 이 시각을 넘기면 포기하고 다음 지점을 고른다.
    public float3 waypoint;
    public bool hasWaypoint;
    public double waypointGiveUpTime;

    // 지점에 닿은 뒤 둘러보다가 다음 지점을 고르는 시각.
    public double nextWaypointTime;

    // 정찰 중인가. 이동 시스템이 이 값으로 정찰 걸음을 낸다.
    public bool patrolling;

    // 칼을 들 권리를 내려놓는다. 다 휘둘렀을 때, 끊겼을 때, 표적을 잃었을 때 전부 여기로 온다.
    public void ReleaseSlot(double now, float yieldDelay)
    {
        if (role != EnemyCombatRole.Attacker) return;

        role = EnemyCombatRole.Waiter;
        slotAllyIndex = EnemyTarget.None;
        swingsLeft = 0;

        // 물러서 있는 시간도 흩는다. 같은 스윙에 같이 끊긴 둘이 같은 프레임에 다시 줄을 서지 않게.
        float jitter = random.state != 0 ? random.NextFloat(0.7f, 1.3f) : 1f;
        nextSlotRequestTime = now + math.max(0f, yieldDelay) * jitter;
    }
}

// 맞은 무게. 히트스톱·넉백·피격 둔화가 여기 모인다.
//
// 엔티티에는 Animator가 없어서 아군처럼 재생 배속을 누를 수 없다. 대신 시뮬레이션이 쓰는
// 시간 자체를 눌러 붙인다 — 타이머도, 클립 진행도도, 이동도 같은 배율로 느려지므로 셋이
// 서로 어긋나지 않는다.
public struct EnemyImpact : IComponentData
{
    public double hitStopUntil;
    public float hitStopScale;

    // 밀려날 방향과 남은 거리. 이동 시스템이 감쇠 곡선으로 풀어낸다.
    public float3 knockbackDirection;
    public float knockbackRemaining;

    public double flinchUntil;
    public float flinchMoveMultiplier;

    // 이번 프레임에 이 개체에게 흐르는 시간의 배율.
    public float TimeScale(double now)
    {
        return now < hitStopUntil ? math.saturate(hitStopScale) : 1f;
    }

    public float FlinchFactor(double now)
    {
        return now < flinchUntil && flinchMoveMultiplier > 0f ? flinchMoveMultiplier : 1f;
    }

    // 이미 걸린 멈춤보다 짧으면 덮지 않는다. 여럿에게 연달아 맞는 동안 멈춤이 짧아지면
    // 무거운 한 방의 멈칫이 뒤따른 가벼운 한 방에 잘린다.
    public void ApplyHitStop(double now, float duration, float scale)
    {
        if (duration <= 0f) return;

        double until = now + duration;
        if (until <= hitStopUntil) return;

        hitStopUntil = until;
        hitStopScale = scale;
    }

    // 밀려나는 거리는 더하지 않고 큰 쪽을 남긴다. 사방에서 두들겨 맞는 놈이 합산으로 멀리
    // 튕겨 나가면 난전이 아니라 핀볼이 된다.
    public void ApplyKnockback(float3 direction, float distance)
    {
        if (distance <= 0f) return;

        direction.y = 0f;
        if (math.lengthsq(direction) <= 0.0001f) return;

        if (distance >= knockbackRemaining) knockbackDirection = math.normalize(direction);
        knockbackRemaining = math.max(knockbackRemaining, distance);
    }
}

// 매 프레임 바뀌는 것.
public struct EnemyHealth : IComponentData
{
    public int current;
    public float poise;

    // 강인도가 깨진 뒤의 면역 시간. 이게 없으면 여럿에게 둘러싸인 순간 무한 경직에 빠진다 —
    // 아군 쪽 poiseImmuneUntil과 같은 규칙이다.
    public double poiseImmuneUntil;

    // 마지막으로 이 적을 때린 아군(스냅샷 인덱스). 처치를 누구에게 귀속시킬지 정한다.
    // 아군 쪽 UnitController.lastAttacker와 같은 자리인데, 참조를 담을 수 없으므로 인덱스다.
    public int lastAttackerAllyIndex;
}

public struct EnemyMotion : IComponentData
{
    public float3 velocity;

    // 이번 프레임에 가려는 쪽. 스티어링 결과를 이동 시스템이 여기 적고 통합한다.
    public float3 desiredDirection;

    // 걷는 모션을 고를 때 보는 속도. 실제 속도를 한 박자 눌러 따라간다.
    //
    // 무리 속의 속도는 프레임마다 튄다 — 이웃에게 밀리고, 겹침이 풀리며 파고들던 성분이 지워진다.
    // 그 날것으로 클립을 고르면 문턱 언저리에서 제자리걸음 ↔ 걷기 ↔ 달리기가 계속 뒤바뀐다.
    // 이력(문턱 둘)만으로는 모자랐다 — 실측(고블린 30마리가 한 사람에게 몰린 10초)에서
    // 마리당 초당 0.86회였고, 여기서 눌러 준 뒤 0.44회가 됐다.
    //
    // 이동에는 쓰지 않는다. 몸이 가는 속도는 여전히 velocity이고, 이건 그것을 보는 눈이다.
    public float smoothedSpeed;

    // 발이 묶인 상태. 창수의 부위 억제와 빙결 마법이 여기로 온다
    // (아군 쪽 UnitController.ApplySlow와 같은 뜻이다).
    //
    // 배율을 EnemyStats가 아니라 여기 두는 이유: 스탯은 프리팹에서 한 번 구워 오는 값이고
    // 이것은 매 프레임 바뀌는 상태다. 스탯 쪽에 두면 같은 청크의 적들이 서로 다른 값을
    // 갖게 되어 굽는 의미가 사라진다.
    public double slowUntil;
    public float slowMultiplier;

    // 지금 걸린 이동 배율. 시간이 지났으면 1이다.
    public float SlowFactor(double now)
    {
        return now < slowUntil && slowMultiplier > 0f ? slowMultiplier : 1f;
    }
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
    // 표적이 없다. 제자리에서 둘러보거나, 한동안 아무도 못 찾았으면 정찰을 돈다(EnemyPatrolSystem).
    Idle,

    // 표적에게 붙는 중.
    Approach,

    // 덤벼드는 중. 사거리 밖이지만 한 번에 붙을 수 있는 거리에서 뛴다.
    //
    // 이 구간에는 이동을 전투 시스템이 통째로 가져간다(스티어링이 아니라 클립 진행도에 맞춰
    // 민다). 아군 쪽 LeapAttackBehavior와 같은 이유다 — 뛰는 궤적을 지역 회피가 옆에서
    // 밀면 "뛰는데 옆으로 흐르는" 그림이 된다.
    Leap,

    // 물고 늘어지는 중. 붙잡은 아군을 따라다니며 버티다가 끝에 한 번 크게 문다.
    //
    // 게임오브젝트 고블린은 상대의 목에 매달렸다(UnitController.UpdateCling). 여기서는
    // 목 좌표까지 스냅샷에 싣지 않고 발치에 붙어 따라가는 것으로 대신한다 — 스냅샷을
    // 키우는 비용이 1000마리에 그대로 곱해지는데, 그 차이는 잡몹 거리에서 보이지 않는다.
    Bite,

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

    // 지금 재생 중인 클립이 걸쳐야 할 시간(초). 이 구간이 끝나는 프레임에 클립도 끝나도록
    // 진행도를 이 값으로 나눠 민다(EnemyCombatSystem.Advance).
    //
    // 클립 종류로 길이를 고르던 때는 표에 없는 클립이 전부 1초로 떨어져, 콤보 2~7단은 75%에서
    // 잘리고 방향별 피격은 30%만 재생되고 끊겼다. 구간을 시작하는 쪽이 자기 길이를 적어 두면
    // 클립을 새로 늘려도 그 구멍이 다시 생기지 않는다.
    public float animationLength;

    // 다음 스윙이 가능해지는 시각.
    public double nextAttackTime;

    // 다음 도약이 가능해지는 시각.
    public double nextLeapTime;

    // 다음 물어뜯기가 가능해지는 시각.
    public double nextBiteTime;

    // 이번 도약에서 가야 할 곳과 남은 거리. 클립 진행도에 비례해 밀기 위해 출발할 때 잡는다 —
    // 매 프레임 표적을 다시 보면 상대가 움직일 때마다 궤적이 휘어 뛰는 것으로 보이지 않는다.
    public float3 leapDirection;
    public float leapDistance;
    public float leapTravelled;

    // 이번 스윙의 타격을 이미 넣었는가. windup이 끝나는 프레임에 한 번만 넣기 위한 것.
    public bool struckThisSwing;

    // 지금 콤보의 몇 단인가(0이 1단). 스윙 하나가 끝날 때마다 오르고, 마지막 단을 지나면
    // 처음으로 돌아온다. 표적을 잃으면 다시 1단부터다.
    //
    // 같은 클립만 반복하면 마리 수가 많을수록 "복사본이 같은 동작을 하는" 것이 눈에 띈다.
    // 게임오브젝트 고블린이 일곱 단을 돌리던 것을 그대로 옮긴 값이다.
    public byte comboIndex;
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

    // 콤보. 게임오브젝트 고블린이 쓰던 일곱 단을 그대로 옮긴다 — Attack이 곧 1단이므로
    // 여기는 2단부터다. 한 단씩 이어 붙여야 "같은 동작을 반복하는 인형"이 아니게 된다.
    //
    // 순서가 곧 단수라 중간에 값을 끼워 넣으면 안 된다. 굽는 쪽(EnemyAnimationBaker.Wanted)과
    // 고르는 쪽(EnemyCombatSystem.ComboClip)이 이 순서를 그대로 읽는다.
    Attack2,
    Attack3,
    Attack4,
    Attack5,
    Attack6,
    Attack7,

    // 발차기와 도약. 붙어서 밀어내는 한 방과, 거리를 한 번에 좁히는 덤벼들기다.
    Kick,
    LeapAttack,

    // 물어뜯기. 붙잡고 늘어지는 한 방이라 다른 것들보다 길다(2.08초).
    Bite,

    // 방향별 피격. 어디서 맞았는지가 보이면 난전의 그림이 통째로 달라진다.
    HitFront,
    HitBack,
    HitLeft,
    HitRight,

    // 걷기. 자리로는 Run 옆이 맞지만 맨 끝에 둔다 — 값이 곧 구운 자산에 적힌 번호라,
    // 중간에 끼워 넣으면 이미 구워 둔 라이브러리의 클립이 통째로 한 칸씩 밀린다.
    //
    // 이게 없던 동안은 느린 이동도 전부 Run을 재생 배속 바닥에 눌러 돌렸다. 다리는 아무리
    // 느려도 제 속도의 0.5배로 땅을 미는데 몸이 그보다 느리면 그 차이가 그대로 미끄러짐이다.
    Walk,
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
