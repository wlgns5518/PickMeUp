using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

// 적 시뮬레이션 한 프레임. 순서가 곧 의존 관계다.
//
//   피해 적용 → 공간 해시 → 판단 박자 → 표적 고르기 → 공격 슬롯 → 전투 판단 → 이동 → (브리지 출력)
//
// 아군의 행동 트리와 하는 일이 같지만 모양이 다르다. 트리는 유닛 하나가 매 프레임 뿌리부터
// 내려오는 구조라 마리 수만큼 분기가 흩어지는데, 여기서는 같은 판단을 하는 놈들이 청크 단위로
// 붙어 있어 한 번에 훑는다. 1000마리에서 갈리는 것이 정확히 이 차이다.
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class EnemySimulationGroup : ComponentSystemGroup
{
}

// ---------------------------------------------------------------- 공간 해시

// 이웃을 찾기 위한 격자. 서로 밀어내기(분리)와 뭉침 판정이 이걸 쓴다.
//
// 이게 없으면 이웃 질의가 마리 수의 제곱이 된다. 아군 쪽에서 이미 그 벽에 부딪혀
// "나를 노리는 적 수"를 세지 않고 들고 다니게 만들었는데(UnitController.attackersByTeam),
// 1000마리에서는 그런 우회로도 통하지 않는다 — 격자가 답이다.
public struct EnemyNeighbor
{
    public float3 position;
    public float radius;
    public Entity entity;
}

public struct EnemySpatialHash : IComponentData
{
    public NativeParallelMultiHashMap<int, EnemyNeighbor> map;
    public float cellSize;

    public static int Hash(float3 position, float cellSize)
    {
        int3 cell = (int3)math.floor(position / cellSize);
        // 큰 소수 셋을 섞는 표준 방식. 격자 좌표가 음수여도 고르게 흩어진다.
        return (int)math.hash(cell);
    }
}

[UpdateInGroup(typeof(EnemySimulationGroup))]
[UpdateBefore(typeof(EnemyTargetingSystem))]
public partial struct EnemySpatialHashSystem : ISystem
{
    // 격자 한 칸의 크기. 분리 반경(대략 1m)의 두 배로 잡아, 이웃 검사가 인접 27칸이 아니라
    // 제 칸과 그 둘레만 봐도 충분하게 한다.
    private const float CellSize = 2f;

    private NativeParallelMultiHashMap<int, EnemyNeighbor> map;
    private EntityQuery enemies;

    public void OnCreate(ref SystemState state)
    {
        enemies = SystemAPI.QueryBuilder().WithAll<EnemyTag, LocalTransform, EnemyStats, EnemyAction>().Build();
        map = new NativeParallelMultiHashMap<int, EnemyNeighbor>(1024, Allocator.Persistent);
        state.EntityManager.CreateSingleton(new EnemySpatialHash { map = map, cellSize = CellSize });
    }

    public void OnDestroy(ref SystemState state)
    {
        if (map.IsCreated) map.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        int count = enemies.CalculateEntityCount();
        if (count == 0)
        {
            map.Clear();
            return;
        }

        // 매 프레임 다시 세운다. 적이 계속 움직이므로 갱신보다 새로 담는 편이 싸다.
        map.Clear();
        if (map.Capacity < count * 2) map.Capacity = count * 2;

        var job = new BuildHashJob
        {
            writer = map.AsParallelWriter(),
            cellSize = CellSize,
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);

        // 뒤따르는 시스템들이 이 맵을 읽으므로 여기서 한 번 맞춰 둔다.
        state.Dependency.Complete();

        SystemAPI.SetSingleton(new EnemySpatialHash { map = map, cellSize = CellSize });
    }

    [BurstCompile]
    private partial struct BuildHashJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, EnemyNeighbor>.ParallelWriter writer;
        public float cellSize;

        private void Execute(Entity entity, in LocalTransform transform, in EnemyStats stats, in EnemyAction action)
        {
            if (action.kind == EnemyActionKind.Dead) return;

            writer.Add(EnemySpatialHash.Hash(transform.Position, cellSize), new EnemyNeighbor
            {
                position = transform.Position,
                radius = stats.radius,
                entity = entity,
            });
        }
    }
}

// ---------------------------------------------------------------- 판단 박자

// 이번 프레임에 새 수를 고를 개체를 정한다.
//
// 예전에는 전원이 매 프레임 판단했다. 그러면 조건이 풀리는 그 프레임에 전원이 같이 움직인다 —
// 같은 프레임에 스폰돼 같은 쿨다운을 쓰는 고블린들이 한 몸처럼 칼을 올렸고, 표적이 쓰러지면
// 둘레의 전원이 같은 프레임에 다음 표적으로 돌아섰다. 마리마다 주기(stats.thinkIntervalMin~Max)와
// 시작 위상을 달리 가져가면 같은 조건이 풀려도 알아채는 순간이 흩어진다. 그 시차가 난전의 호흡이다.
//
// 묶는 것은 "새 수를 고르는 것"(표적·공격 슬롯·공격 개시)뿐이다. 맞고 움찔하고 무너지고 죽는 것은
// 사건이라 박자를 기다리지 않는다(EnemyDamageSystem).
//
// 시계는 SystemAPI.Time.ElapsedTime과 비교 한 번이다. 코루틴도 yield도 없다.
[UpdateInGroup(typeof(EnemySimulationGroup))]
[UpdateBefore(typeof(EnemyTargetingSystem))]
public partial struct EnemyThinkSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var job = new ThinkJob { now = SystemAPI.Time.ElapsedTime };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct ThinkJob : IJobEntity
    {
        public double now;

        private void Execute(Entity entity, ref EnemyTactics tactics, in EnemyStats stats, in EnemyAction action)
        {
            if (action.kind == EnemyActionKind.Dead)
            {
                tactics.thinking = false;
                return;
            }

            if (!tactics.primed) Prime(entity, ref tactics, stats);

            tactics.thinking = now >= tactics.nextThinkTime;
            if (!tactics.thinking) return;

            // 박자 자체도 조금씩 흔든다. 주기가 고정이면 비슷한 주기를 뽑은 놈들끼리 영영 같이 움직인다.
            tactics.nextThinkTime = now + tactics.thinkInterval * tactics.random.NextFloat(0.8f, 1.2f);
        }

        // 처음 판단하기 직전에 이 개체의 성격을 뽑는다. 스포너가 씨를 뿌리지 않았으면(테스트)
        // 엔티티 인덱스로 뿌린다 — 난수 상태가 0이면 Unity.Mathematics.Random이 동작하지 않는다.
        private void Prime(Entity entity, ref EnemyTactics tactics, in EnemyStats stats)
        {
            if (tactics.random.state == 0) tactics.random = Random.CreateFromIndex((uint)entity.Index);

            float intervalMin = math.max(0f, stats.thinkIntervalMin);
            float intervalMax = math.max(intervalMin, stats.thinkIntervalMax);
            tactics.thinkInterval = tactics.random.NextFloat(intervalMin, intervalMax);

            float waitMin = stats.waitDistanceMin > 0f ? stats.waitDistanceMin : stats.standoffDistance + 1f;
            float waitMax = math.max(waitMin, stats.waitDistanceMax);
            tactics.waitDistance = tactics.random.NextFloat(waitMin, waitMax);

            tactics.slotAllyIndex = EnemyTarget.None;

            // 첫 판단의 위상을 주기 안에서 흩는다. 한 파도로 같이 스폰된 무리가 첫 프레임부터
            // 같은 박자를 타지 않게 하는 자리다(주기가 0이면 곧바로 판단한다).
            tactics.nextThinkTime = now + tactics.thinkInterval * tactics.random.NextFloat();
            tactics.primed = true;
        }
    }
}

// ---------------------------------------------------------------- 표적 고르기

// 아군 스냅샷에서 노릴 상대를 고른다.
//
// 아군 쪽 TargetScanner가 하던 일인데 레이캐스트가 없다. 시야 판정을 1000마리가 각자 쏘면
// 그것만으로 프레임이 끝나기 때문이다. 대신 거리와 시야각, 그리고 "이미 몇 명이 붙어 있는가"로
// 고른다 — 마지막 것이 없으면 전원이 탱커 한 명에게 몰려 뒷줄이 영영 닿지 못한다.
[UpdateInGroup(typeof(EnemySimulationGroup))]
[UpdateBefore(typeof(EnemyCombatSystem))]
public partial struct EnemyTargetingSystem : ISystem
{
    // 표적을 다시 고르는 간격. 매 프레임 다시 고르면 두 아군 사이에서 계속 흔들린다 —
    // 아군 쪽 targetChangeInterval과 같은 이유다.
    private const float RetargetInterval = 0.75f;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out EnemyWorldBridge.BridgeData bridge)) return;
        if (!bridge.allies.IsCreated) return;

        var job = new PickTargetJob
        {
            allies = bridge.allies.AsArray(),
            now = SystemAPI.Time.ElapsedTime,
            retargetInterval = RetargetInterval,
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct PickTargetJob : IJobEntity
    {
        [ReadOnly] public NativeArray<EnemyWorldBridge.AllyState> allies;
        public double now;
        public float retargetInterval;

        private void Execute(ref EnemyTarget target, ref EnemyTactics tactics, in LocalTransform transform,
            in EnemyStats stats, in EnemyAction action)
        {
            if (action.kind == EnemyActionKind.Dead) return;

            // 이미 나간 동작 도중에는 겨눌 상대를 바꾸지 않는다. 바꾸면 칼이 엉뚱한 쪽으로 간다 —
            // 아군 쪽 UnitBehavior.LocksTarget과 같은 규칙이다. 도약과 물기도 이미 나간 동작이다
            // (예전에는 둘이 빠져 있어서, 매달려 무는 도중에 표적이 바뀌면 다른 아군을 따라가 물었다).
            if (action.kind == EnemyActionKind.Windup || action.kind == EnemyActionKind.Recover ||
                action.kind == EnemyActionKind.Leap || action.kind == EnemyActionKind.Bite) return;

            // 표적은 판단 박자에만 다시 고른다. 겨누던 아군이 쓰러진 순간에도 마찬가지다 —
            // 둘레의 전원이 같은 프레임에 새 표적으로 돌아서는 것보다, 저마다 한 박자씩 두리번거리는 편이 낫다.
            if (!tactics.thinking) return;

            // 들고 있던 표적이 아직 쓸 만하면 그대로 둔다.
            if (IsUsable(target.allyIndex) && now < target.nextRetargetTime) return;

            // 지금 아무도 겨누고 있지 않다면 시야각을 따지지 않는다.
            //
            // 스폰 회전이 무작위라 시야각(160°) 밖에서 시작하는 개체가 절반이 넘는데, 표적이
            // 없으면 움직이지도 돌지도 않는다(EnemyMovementSystem의 facing은 표적이 있어야 잡힌다).
            // 그래서 등지고 선 고블린은 영영 아무도 못 찾고 그 자리에서 맞고만 있었다.
            // 서 있는 동안 주위를 둘러본다고 보는 편이 맞다 — 한 번 붙고 나면 아래 규칙대로
            // 다시 시야각이 걸린다.
            bool blind = !IsUsable(target.allyIndex);

            int best = EnemyTarget.None;
            float bestScore = float.MaxValue;
            float3 forward = math.normalizesafe(transform.Forward(), new float3(0f, 0f, 1f));
            float cosHalfFov = math.cos(math.radians(stats.fieldOfView * 0.5f));

            for (int i = 0; i < allies.Length; i++)
            {
                EnemyWorldBridge.AllyState ally = allies[i];
                if (ally.alive == 0) continue;

                float3 toAlly = ally.position - transform.Position;
                toAlly.y = 0f;
                float distance = math.length(toAlly);
                if (distance > stats.detectRange) continue;

                // 이미 겨누고 있던 상대는 시야각을 따지지 않는다. 등을 돌린 순간
                // 표적을 놓아 버리면 쫓아가다 말고 멈춰 선다.
                if (!blind && i != target.allyIndex && distance > 0.01f)
                {
                    float alignment = math.dot(forward, toAlly / distance);
                    if (alignment < cosHalfFov) continue;
                }

                // 가까울수록, 어그로가 높을수록, 이미 붙은 놈이 적을수록 낫다.
                // 어그로로 나누는 것이라 탱커(3.2)는 같은 거리에서 3배 넘게 당겨진다.
                float score = distance / math.max(0.01f, ally.threatWeight);

                // 지금 겨누고 있는 상대의 머릿수에서는 나를 뺀다. 빼지 않으면 붙어 있는 것만으로
                // 제 표적이 0.6씩 불리해져, 박자마다 옆 아군으로 갈아타며 오간다.
                int attackers = ally.attackerCount;
                if (i == target.allyIndex) attackers = math.max(0, attackers - 1);
                score += attackers * 0.6f;

                if (score >= bestScore) continue;

                bestScore = score;
                best = i;
            }

            target.allyIndex = best;
            // 다시 고르는 간격도 흩는다. 같이 스폰된 무리가 0.75초마다 한꺼번에 돌아보지 않게.
            target.nextRetargetTime = now + retargetInterval * tactics.random.NextFloat(0.8f, 1.25f);
        }

        private bool IsUsable(int index)
        {
            if (index < 0 || index >= allies.Length) return false;
            return allies[index].alive != 0;
        }
    }
}

// ---------------------------------------------------------------- 공격 슬롯

// 표적 한 명에게 동시에 칼을 들 수 있는 적 수를 제한한다.
//
// 자리를 나눠 주지 않는다는 것이 요점이다. 앞/뒤/좌/우 같은 포메이션 슬롯을 두면 고블린이 사람을
// 가운데 두고 정렬하는 그림이 되는데, 원작의 난전은 그렇게 생기지 않았다. 여기서 나누는 것은
// "칼을 들 권리"뿐이고 서 있는 자리는 여전히 스티어링과 밀치기가 정한다. 권리를 못 얻은 놈은
// 한 걸음 떨어져 틈을 보다가(Waiter), 앞의 놈이 휘두르기를 마치거나 끊기면 제 판단 박자에 그
// 자리를 채운다 — 누가 먼저 채우는지는 박자가 정하므로 순번이 보이지 않는다.
//
// 청하는 순서가 곧 받는 순서라 병렬로 돌리지 않는다(두 놈이 같은 마지막 자리를 동시에 가져간다).
// 개체당 하는 일이 정수 비교 몇 번이라 1000마리를 한 줄로 돌아도 싸다.
[UpdateInGroup(typeof(EnemySimulationGroup))]
[UpdateAfter(typeof(EnemyTargetingSystem))]
[UpdateBefore(typeof(EnemyCombatSystem))]
public partial struct EnemyAttackSlotSystem : ISystem
{
    // 아군 쪽 수치(UnitStats.enemyAttackSlots)가 0일 때 쓰는 자리 수. 한 사람을 앞뒤로 둘이 치는 것이
    // "둘러싸여 두들겨 맞는" 그림의 하한이고, 셋부터는 칼끼리 겹쳐 누가 쳤는지 읽히지 않는다.
    public const int DefaultSlotsPerAlly = 2;

    // stats가 비어 있을 때의 대비값(테스트, 굽기 전의 원본).
    private const float DefaultRequestMargin = 1.5f;
    private const float DefaultHoldTimeout = 2.5f;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out EnemyWorldBridge.BridgeData bridge)) return;
        if (!bridge.allies.IsCreated) return;

        NativeArray<EnemyWorldBridge.AllyState> allies = bridge.allies.AsArray();
        double now = SystemAPI.Time.ElapsedTime;

        // 표적별로 지금 쥐어진 자리 수. 매 프레임 역할에서 다시 센다(EnemyTactics 주석 참조).
        var taken = new NativeArray<int>(math.max(1, allies.Length), Allocator.TempJob);

        JobHandle counted = new CountHoldersJob { allies = allies, taken = taken, now = now }
            .Schedule(state.Dependency);
        JobHandle granted = new GrantSlotsJob { allies = allies, taken = taken, now = now }
            .Schedule(counted);

        state.Dependency = taken.Dispose(granted);
    }

    public static float RequestRange(in EnemyStats stats)
    {
        return stats.slotRequestRange > 0f
            ? stats.slotRequestRange
            : math.max(stats.attackRange, stats.leapRange) + DefaultRequestMargin;
    }

    public static float HoldTimeout(in EnemyStats stats)
    {
        return stats.slotHoldTimeout > 0f ? stats.slotHoldTimeout : DefaultHoldTimeout;
    }

    // 이미 나간 동작. 이 도중에는 시간이 지났다고 자리를 빼앗지 않는다.
    private static bool IsCommitted(EnemyActionKind kind)
    {
        return kind == EnemyActionKind.Windup || kind == EnemyActionKind.Recover ||
               kind == EnemyActionKind.Leap || kind == EnemyActionKind.Bite;
    }

    // 쥐고 있는 자리를 센다. 쥘 자격을 잃은 놈은 여기서 내려놓는다 — 표적이 바뀌었거나 쓰러졌거나,
    // 쥐고도 한참 휘두르지 못했을 때(상대가 달아나는 중).
    [BurstCompile]
    private partial struct CountHoldersJob : IJobEntity
    {
        [ReadOnly] public NativeArray<EnemyWorldBridge.AllyState> allies;
        public NativeArray<int> taken;
        public double now;

        private void Execute(ref EnemyTactics tactics, in EnemyTarget target, in EnemyAction action, in EnemyStats stats)
        {
            if (tactics.role != EnemyCombatRole.Attacker) return;

            int index = tactics.slotAllyIndex;
            bool valid = action.kind != EnemyActionKind.Dead &&
                         index == target.allyIndex &&
                         index >= 0 && index < allies.Length &&
                         allies[index].alive != 0;

            if (valid && !IsCommitted(action.kind) && now >= tactics.slotExpireTime) valid = false;

            if (!valid)
            {
                tactics.ReleaseSlot(now, stats.slotYieldDelay);
                return;
            }

            taken[index]++;
        }
    }

    // 판단 박자를 맞은 놈이 자리를 청한다. 남았으면 받고(Attacker), 없으면 틈을 본다(Waiter).
    [BurstCompile]
    private partial struct GrantSlotsJob : IJobEntity
    {
        [ReadOnly] public NativeArray<EnemyWorldBridge.AllyState> allies;
        public NativeArray<int> taken;
        public double now;

        private void Execute(ref EnemyTactics tactics, in EnemyTarget target, in EnemyAction action,
            in EnemyStats stats, in LocalTransform transform)
        {
            if (action.kind == EnemyActionKind.Dead) return;
            if (tactics.role == EnemyCombatRole.Attacker || !tactics.thinking) return;

            int index = target.allyIndex;
            if (index < 0 || index >= allies.Length || allies[index].alive == 0)
            {
                tactics.role = EnemyCombatRole.Chaser;
                return;
            }

            EnemyWorldBridge.AllyState ally = allies[index];
            float3 offset = ally.position - transform.Position;
            offset.y = 0f;

            float requestRange = RequestRange(stats);
            if (math.lengthsq(offset) > requestRange * requestRange)
            {
                tactics.role = EnemyCombatRole.Chaser;
                return;
            }

            // 맞고 움찔하거나 무너져 있는 동안은 줄을 서지 않는다. 손이 묶인 놈에게 자리를 주면
            // 그 자리가 빈 채로 묶여, 곁에서 기다리던 놈이 칼을 못 든다.
            bool handsFree = action.kind == EnemyActionKind.Idle || action.kind == EnemyActionKind.Approach;
            int capacity = ally.attackSlots > 0 ? ally.attackSlots : DefaultSlotsPerAlly;

            if (handsFree && now >= tactics.nextSlotRequestTime && taken[index] < capacity)
            {
                taken[index]++;

                int swingsMin = math.max(1, (int)stats.swingsPerSlotMin);
                int swingsMax = math.max(swingsMin, (int)stats.swingsPerSlotMax);

                tactics.role = EnemyCombatRole.Attacker;
                tactics.slotAllyIndex = index;
                tactics.swingsLeft = (byte)tactics.random.NextInt(swingsMin, swingsMax + 1);
                tactics.slotExpireTime = now + HoldTimeout(stats);
                return;
            }

            tactics.role = EnemyCombatRole.Waiter;
        }
    }
}

// ---------------------------------------------------------------- 전투 판단

// 붙었으면 휘두르고, 아니면 붙는다. 고블린이 하는 일의 전부다.
//
// 애니메이션 이벤트가 없으므로 타격 시점을 시간으로 잡는다. windup이 끝나는 그 프레임에
// 한 번만 판정하고(struckThisSwing), 그때 다시 거리와 각도를 잰다 — 스윙이 시작된 뒤
// 상대가 빠져나갔으면 빗나가야 한다는 규칙은 아군 쪽 ResolveAttackHit와 똑같이 지킨다.
[UpdateInGroup(typeof(EnemySimulationGroup))]
[UpdateBefore(typeof(EnemyMovementSystem))]
public partial struct EnemyCombatSystem : ISystem
{
    // 구간표가 아직 없을 때 대신 넘기는 빈 배열.
    //
    // 잡에 넘기는 컨테이너는 비어 있더라도 만들어져 있어야 한다. 구간표 싱글턴이 없는 월드
    // (렌더러 없이 도는 테스트, 굽기 전의 전투)에서 기본값 배열을 그대로 넘겼더니 스케줄 자체가
    // 예외로 끝나, 적이 아무것도 하지 않았다(EnemyEcsTests 25개가 전부 그렇게 깨져 있었다).
    private NativeArray<float4> emptyClipRanges;

    public void OnCreate(ref SystemState state)
    {
        emptyClipRanges = new NativeArray<float4>(0, Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        if (emptyClipRanges.IsCreated) emptyClipRanges.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out EnemyWorldBridge.BridgeData bridge)) return;
        if (!bridge.allies.IsCreated) return;

        // 구워 둔 클립 길이. 제자리걸음과 달리기는 게임플레이가 정한 시간이 없어서
        // 클립 자신의 길이로 돌려야 한다 — 없으면 1초로 보고 돈다(ClipLength).
        NativeArray<float4> clipRanges = emptyClipRanges;
        if (SystemAPI.TryGetSingleton(out EnemyAnimationLookup lookup) && lookup.clipRanges.IsCreated)
        {
            clipRanges = lookup.clipRanges;
        }

        var job = new CombatJob
        {
            allies = bridge.allies.AsArray(),
            hits = bridge.hitsOnAllies.AsParallelWriter(),
            clipRanges = clipRanges,
            deltaTime = SystemAPI.Time.DeltaTime,
            now = SystemAPI.Time.ElapsedTime,
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct CombatJob : IJobEntity
    {
        [ReadOnly] public NativeArray<EnemyWorldBridge.AllyState> allies;
        public NativeQueue<EnemyWorldBridge.HitOnAlly>.ParallelWriter hits;
        // x = 시작 줄, y = 프레임 수, z = 초 단위 길이. 여기서는 z만 쓴다.
        [ReadOnly] public NativeArray<float4> clipRanges;
        public float deltaTime;
        public double now;

        private void Execute(Entity entity, ref EnemyAction action, ref EnemyAnimation animation,
            ref EnemyTactics tactics, ref EnemyImpact impact,
            in EnemyTarget target, in EnemyStats stats, in EnemyMotion motion, ref LocalTransform transform)
        {
            if (action.kind == EnemyActionKind.Dead) return;

            // 히트스톱. 시간을 누르면 구간 타이머·클립 진행도·도약 궤적이 같은 배율로 멈칫한다 —
            // 셋 중 하나만 누르면 모션은 멈췄는데 몸은 날아가는 그림이 된다.
            float dt = deltaTime * impact.TimeScale(now);
            action.timer -= dt;

            switch (action.kind)
            {
                case EnemyActionKind.Leap:
                    TickLeap(entity, ref action, ref animation, ref transform, ref tactics, ref impact, target, stats);
                    return;

                case EnemyActionKind.Bite:
                    TickBite(entity, ref action, ref animation, ref transform, ref tactics, ref impact, target, stats, dt);
                    return;

                case EnemyActionKind.HitReact:
                case EnemyActionKind.Stagger:
                    // 스스로 아무것도 못 한다. 시간이 다하면 교전으로 돌아간다.
                    if (action.timer > 0f) { Advance(ref animation, action, dt); return; }
                    break;

                case EnemyActionKind.Windup:
                    TickWindup(entity, ref action, ref animation, ref tactics, ref impact, target, stats, transform, dt);
                    return;

                case EnemyActionKind.Recover:
                    if (action.timer > 0f) { Advance(ref animation, action, dt); return; }

                    // 휘두르기를 다 마쳤다. 이번 자리로 휘두를 몫을 다 썼으면 곁에서 기다리던 놈에게 내준다.
                    // 회수가 끝난 뒤에 내놓는 것이 맞다 — 칼을 거두는 도중에 자리가 넘어가면 둘이 같은
                    // 사람을 동시에 베는 순간이 생긴다.
                    if (tactics.role == EnemyCombatRole.Attacker && tactics.swingsLeft == 0)
                    {
                        tactics.ReleaseSlot(now, stats.slotYieldDelay);
                    }
                    break;
            }

            // 구간이 끝났으면 그 프레임에 바로 다음 수를 고른다.
            //
            // 예전에는 여기서 EnterApproach가 달리기 클립을 0부터 물려 놓고 끝냈다. 그런데
            // 바로 아래 판단이 다음 프레임에 대개 제자리걸음으로 덮어써서, 스윙이 끝날 때마다
            // 달리기 첫 프레임이 한 장씩 끼어들었다 — 공격 쿨다운이 1.1초라 마리마다 1초에
            // 한 번씩 튀는 것이 그대로 보였다.

            // 여기부터가 Idle/Approach — 스스로 다음 수를 고를 수 있는 구간이다.
            if (!TryGetAlly(target.allyIndex, out EnemyWorldBridge.AllyState ally))
            {
                action.kind = EnemyActionKind.Idle;
                // 겨눌 상대가 사라졌으면 콤보도 처음으로 돌아간다. 다음에 붙는 상대에게
                // 5단부터 시작하면 그 앞 네 단을 건너뛴 셈이 된다.
                action.comboIndex = 0;
                Loop(ref animation, EnemyClip.Idle, dt);
                return;
            }

            float distance = math.distance(Flat(ally.position), Flat(transform.Position));
            bool inRange = distance <= stats.attackRange;

            // 새 수(물기·스윙·도약)는 판단 박자에, 칼을 들 권리를 쥔 놈만 고른다.
            //
            // 둘 다 있어야 한다. 슬롯만 있으면 자리가 비는 그 프레임에 기다리던 전원이 같이 달려들고,
            // 박자만 있으면 한 사람을 둘러싼 여섯 마리가 조금씩 어긋나게 전부 휘두른다.
            bool mayStrike = tactics.thinking && tactics.role == EnemyCombatRole.Attacker;

            // 붙잡고 늘어지는 한 방. 평타보다 먼저 본다 — 쿨다운이 길어(5초) 기회가 왔을 때
            // 쓰지 않으면 그 사이 평타가 계속 잡아먹는다.
            //
            // 이미 물린 아군은 다시 물지 않는다(canBeBitten). 그게 없으면 한 명에게 여럿이
            // 동시에 물고 늘어져 그 자리에서 녹는다.
            if (mayStrike && inRange && stats.biteDamage > 0 && stats.biteDuration > 0f &&
                now >= action.nextBiteTime && ally.canBeBitten != 0)
            {
                action.kind = EnemyActionKind.Bite;
                action.timer = stats.biteDuration;
                action.animationLength = stats.biteDuration;
                action.struckThisSwing = false;
                action.nextBiteTime = now + stats.biteCooldown;
                animation.clip = EnemyClip.Bite;
                animation.normalizedTime = 0f;
                CommitSlot(ref tactics, stats);
                return;
            }

            if (mayStrike && inRange && now >= action.nextAttackTime)
            {
                action.kind = EnemyActionKind.Windup;
                action.timer = stats.attackWindup;
                // 한 번의 스윙은 들어올리기와 거두기 두 구간에 걸쳐 있고, 클립 하나가 그 둘을
                // 통째로 덮는다. 그래서 길이는 여기서 한 번만 잡고 EnterRecover는 건드리지 않는다.
                action.animationLength = stats.attackWindup + stats.attackRecovery;
                action.struckThisSwing = false;
                animation.clip = ComboClip(action.comboIndex, stats.comboSteps);
                animation.normalizedTime = 0f;
                CommitSlot(ref tactics, stats);
                return;
            }

            // 아직 닿지 않지만 한 번에 붙을 수 있는 거리다 — 걸어 들어가는 대신 덤벼든다.
            // 이미 닿는 상대에게는 뛰지 않는다(위 inRange가 먼저 걸린다).
            // 곁에서 틈을 보던 놈이 자리를 받는 순간이 대개 여기다 — 한 걸음 떨어져 있었으므로.
            if (mayStrike && !inRange && stats.leapRange > 0f && stats.leapDuration > 0f &&
                distance <= stats.leapRange && now >= action.nextLeapTime)
            {
                float3 toAlly = Flat(ally.position - transform.Position);
                float flat = math.length(toAlly);
                if (flat > 0.001f)
                {
                    action.kind = EnemyActionKind.Leap;
                    action.timer = stats.leapDuration;
                    action.animationLength = stats.leapDuration;
                    action.struckThisSwing = false;
                    action.leapDirection = toAlly / flat;
                    // 멈춰 설 거리만큼 남기고 뛴다. 상대에게 그대로 파고들면 겹쳐 선다.
                    action.leapDistance = math.max(0f, flat - stats.standoffDistance);
                    action.leapTravelled = 0f;
                    action.nextLeapTime = now + stats.leapCooldown;
                    animation.clip = EnemyClip.LeapAttack;
                    animation.normalizedTime = 0f;
                    CommitSlot(ref tactics, stats);
                    return;
                }
            }

            action.kind = EnemyActionKind.Approach;

            // 걷는 모션은 실제로 움직이는지로 고른다. 예전에는 사거리 안인지로 골랐는데,
            // 멈춰 서는 거리(1.0m)와 사거리(1.2m)가 달라서 그 사이 20cm 구간에서는 미끄러지며
            // 제자리걸음을 했고, 상대가 그 경계에서 오가면 두 클립이 매 프레임 뒤바뀌었다.
            // 속도는 가속으로 이미 완만해져 있어(lerp) 그 자체가 경계에서의 떨림을 막아 준다.
            float speedSq = math.lengthsq(motion.velocity);
            float walkThreshold = stats.moveSpeed * 0.25f;
            Loop(ref animation, speedSq > walkThreshold * walkThreshold ? EnemyClip.Run : EnemyClip.Idle, dt);
        }

        // 쥔 자리로 한 수를 냈다. 휘두르는 동안은 자리를 빼앗기지 않게 만료를 뒤로 민다.
        private void CommitSlot(ref EnemyTactics tactics, in EnemyStats stats)
        {
            tactics.slotExpireTime = now + EnemyAttackSlotSystem.HoldTimeout(stats);
        }

        // 도는 클립(제자리걸음·달리기)을 한 칸 민다. 게임플레이가 정한 시간이 없으므로
        // 구워 둔 클립 길이로 돌린다 — 달리기는 0.867초라, 예전의 고정 1.4회/초는 21% 빨랐다.
        private void Loop(ref EnemyAnimation animation, EnemyClip clip, float dt)
        {
            // 클립이 바뀌는 순간에만 처음으로 돌린다. 한 번만 재생하는 클립을 마치고 오면
            // 진행도가 1에 가깝게 남아 있어서, 그대로 이어 붙이면 걷는 모션이 끝자락부터 시작한다.
            // 매 프레임 0으로 되돌리면 안 된다 — 스폰 때 흩어 놓은 시작 지점(EnemyHorde)이
            // 지워져 1000마리가 같은 박자로 숨 쉬게 된다.
            if (animation.clip != clip)
            {
                animation.clip = clip;
                animation.normalizedTime = 0f;
                return;
            }

            animation.normalizedTime = math.frac(animation.normalizedTime + dt / ClipLength(clip));
        }

        // 구워 둔 클립 길이(초). 구간표가 아직 없으면 1초로 본다.
        private float ClipLength(EnemyClip clip)
        {
            int index = (int)clip;
            if (!clipRanges.IsCreated || index < 0 || index >= clipRanges.Length) return 1f;

            float length = clipRanges[index].z;
            return length > 0.01f ? length : 1f;
        }

        // 덤벼드는 구간. 클립 진행도에 맞춰 밀고, 착지하는 프레임에 한 번 때린다.
        //
        // 이동을 여기서 통째로 가져가는 것이 요점이다. 스티어링에 맡기면 뛰는 궤적을 지역
        // 회피가 옆에서 밀어 "뛰는데 옆으로 흐르는" 그림이 된다(아군 쪽 UpdateLeap과 같은 이유).
        // 남은 거리를 진행도에 맞춰 따라가게 두므로, 클립이 눌려도 몸과 모션이 어긋나지 않는다.
        private void TickLeap(Entity self, ref EnemyAction action, ref EnemyAnimation animation,
            ref LocalTransform transform, ref EnemyTactics tactics, ref EnemyImpact impact,
            in EnemyTarget target, in EnemyStats stats)
        {
            float length = math.max(0.01f, stats.leapDuration);
            float progress = math.saturate(1f - action.timer / length);
            animation.normalizedTime = progress;

            float wanted = action.leapDistance * progress;
            float step = wanted - action.leapTravelled;
            if (step > 0f)
            {
                action.leapTravelled = wanted;
                transform.Position += action.leapDirection * step;
            }

            if (action.timer > 0f) return;

            // 착지. 닿았으면 한 대 넣고, 아니면 헛뛴 것으로 끝난다 — 스윙과 같은 규칙이다.
            if (!action.struckThisSwing)
            {
                action.struckThisSwing = true;
                if (TryGetAlly(target.allyIndex, out EnemyWorldBridge.AllyState ally))
                {
                    float3 toAlly = Flat(ally.position - transform.Position);
                    float distance = math.length(toAlly);
                    if (distance <= stats.attackRange + stats.attackHitTolerance)
                    {
                        hits.Enqueue(new EnemyWorldBridge.HitOnAlly
                        {
                            allyIndex = target.allyIndex,
                            damage = stats.attackDamage,
                            poiseDamage = stats.poiseDamagePerHit,
                            fromPosition = transform.Position,
                            source = self,
                        });

                        // 몸을 실어 떨어진 한 방이라 평타보다 오래 멈칫한다.
                        impact.ApplyHitStop(now, stats.hitStopDuration * 1.5f, stats.hitStopScale);
                    }
                }
            }

            EnterRecover(ref action, ref tactics, stats);
        }

        // 물고 늘어지는 구간. 붙잡은 아군을 따라다니다가 끝에 한 번 크게 문다.
        //
        // 따라다니는 것이 요점이다. 제자리에 서서 물면 상대가 걸어 나가는 동안 허공을 물게
        // 되는데, 이 동작은 2초가 넘어서 그 어긋남이 그대로 보인다. 게임오브젝트 쪽은 목에
        // 매달려 해결했고(UpdateCling), 여기서는 발치에 붙어 따라간다.
        private void TickBite(Entity self, ref EnemyAction action, ref EnemyAnimation animation,
            ref LocalTransform transform, ref EnemyTactics tactics, ref EnemyImpact impact,
            in EnemyTarget target, in EnemyStats stats, float dt)
        {
            float length = math.max(0.01f, stats.biteDuration);
            animation.normalizedTime = math.saturate(1f - action.timer / length);

            bool hasAlly = TryGetAlly(target.allyIndex, out EnemyWorldBridge.AllyState ally);

            // 붙잡은 쪽을 따라간다. 멈춰 설 거리만큼 남겨 겹쳐 서지 않게 한다.
            if (hasAlly)
            {
                float3 toAlly = Flat(ally.position - transform.Position);
                float distance = math.length(toAlly);
                if (distance > stats.standoffDistance && distance > 0.001f)
                {
                    float3 direction = toAlly / distance;
                    float step = math.min(distance - stats.standoffDistance, stats.moveSpeed * dt);
                    transform.Position += direction * step;
                }

                if (distance > 0.001f)
                {
                    quaternion facing = quaternion.LookRotationSafe(Flat(toAlly / distance), math.up());
                    transform.Rotation = math.slerp(transform.Rotation, facing,
                        math.saturate(stats.turnSpeed * dt));
                }
            }

            if (action.timer > 0f) return;

            if (!action.struckThisSwing)
            {
                action.struckThisSwing = true;

                // 끝까지 붙어 있었을 때만 들어간다. 상대가 떨쳐내고 걸어 나갔으면 헛문 것이다.
                if (hasAlly)
                {
                    float3 toAlly = Flat(ally.position - transform.Position);
                    if (math.length(toAlly) <= stats.attackRange + stats.attackHitTolerance)
                    {
                        hits.Enqueue(new EnemyWorldBridge.HitOnAlly
                        {
                            allyIndex = target.allyIndex,
                            damage = stats.biteDamage,
                            // 강인도 피해는 일부러 0이다. 무는 수의 강인도 효과는 아래 경직 그 자체이고,
                            // 둘 다 넣으면 이 한 방으로 강인도가 먼저 깨지면서 면역 시간이 켜져
                            // 정작 경직이 그 면역에 막힌다(아군 쪽 ResolveSkillHit와 같은 규칙).
                            poiseDamage = 0f,
                            forceStaggerDuration = stats.biteStaggerDuration,
                            fromPosition = transform.Position,
                            source = self,
                            // 문 상대는 이 동작이 한 바퀴 돌 동안 다시 물리지 않는다.
                            skillVictimDuration = stats.biteCooldown,
                        });

                        // 이빨이 박히는 순간. 이 동작에서 가장 무거운 한 방이다.
                        impact.ApplyHitStop(now, stats.hitStopDuration * 2f, stats.hitStopScale);
                    }
                }
            }

            EnterRecover(ref action, ref tactics, stats);
        }

        // 칼을 들어올린 구간. 끝나는 프레임에 딱 한 번 판정한다.
        //
        // 여기가 "공격 결정"과 "타격 판정"이 갈리는 자리다. 스윙을 시작하기로 한 것은 판단 박자에
        // 슬롯을 쥔 채였지만(위 Execute), 실제로 맞았는지는 이 구간이 끝나는 프레임에 거리와 각도를
        // 다시 재서 정하고, 피해는 큐를 건너 메인 스레드에서 들어간다(EnemyWorldBridge.DrainHitsOnAllies).
        // 엔티티에는 애니메이션 이벤트가 없으므로 windup이 곧 타격 프레임이다.
        private void TickWindup(Entity self, ref EnemyAction action, ref EnemyAnimation animation,
            ref EnemyTactics tactics, ref EnemyImpact impact,
            in EnemyTarget target, in EnemyStats stats, in LocalTransform transform, float dt)
        {
            Advance(ref animation, action, dt);

            if (action.timer > 0f) return;
            if (action.struckThisSwing)
            {
                EnterRecover(ref action, ref tactics, stats);
                return;
            }

            action.struckThisSwing = true;

            if (TryGetAlly(target.allyIndex, out EnemyWorldBridge.AllyState ally))
            {
                float3 toAlly = Flat(ally.position - transform.Position);
                float distance = math.length(toAlly);

                // 스윙이 시작된 뒤 벗어났으면 빗나간다. 거리에는 약간의 여유를 준다 —
                // 그 여유가 없으면 경계에서 시작한 스윙이 거의 전부 허공을 간다.
                bool reached = distance <= stats.attackRange + stats.attackHitTolerance;
                bool aimed = true;

                if (reached && distance > 0.01f)
                {
                    float3 forward = math.normalizesafe(transform.Forward(), new float3(0f, 0f, 1f));
                    float cosHalfArc = math.cos(math.radians(stats.attackArcAngle * 0.5f));
                    aimed = math.dot(forward, toAlly / distance) >= cosHalfArc;
                }

                if (reached && aimed)
                {
                    hits.Enqueue(new EnemyWorldBridge.HitOnAlly
                    {
                        allyIndex = target.allyIndex,
                        damage = stats.attackDamage,
                        poiseDamage = stats.poiseDamagePerHit,
                        fromPosition = transform.Position,
                        source = self,
                    });

                    // 휘두른 쪽도 멈칫한다. 맞은 쪽만 멈추면 부딪힌 것이 아니라 렉으로 보인다
                    // (맞은 아군 쪽 멈춤은 UnitController.TakeDamage가 건다).
                    impact.ApplyHitStop(now, stats.hitStopDuration, stats.hitStopScale);
                }
            }

            EnterRecover(ref action, ref tactics, stats);
        }

        private void EnterRecover(ref EnemyAction action, ref EnemyTactics tactics, in EnemyStats stats)
        {
            action.kind = EnemyActionKind.Recover;
            action.timer = stats.attackRecovery;

            // 재사용 대기에 흔들림을 섞는다. 같은 박자로 휘두르기 시작한 둘이 다음 스윙까지 같은
            // 박자로 가지 않게 한다.
            float jitter = math.saturate(stats.attackCooldownJitter);
            float scale = jitter > 0f && tactics.random.state != 0
                ? tactics.random.NextFloat(1f - jitter, 1f + jitter)
                : 1f;
            action.nextAttackTime = now + stats.attackCooldown * scale;

            // 한 수를 냈다(스윙·도약·물기). 자리를 내놓는 것은 회수가 끝난 뒤다(Execute의 Recover).
            if (tactics.role == EnemyCombatRole.Attacker && tactics.swingsLeft > 0) tactics.swingsLeft--;

            // 다음 스윙은 다음 단이다. 마지막 단을 지나면 처음으로 돌아온다.
            int steps = math.max(1, stats.comboSteps);
            action.comboIndex = (byte)((action.comboIndex + 1) % steps);
        }

        // 몇 단째의 클립인가. 1단은 Attack이고 2단부터는 열거에 이어 붙어 있다
        // (EnemyClip 주석 — 그 순서가 곧 단수다).
        private static EnemyClip ComboClip(byte index, byte steps)
        {
            int limit = math.max(1, steps);
            int step = index % limit;
            if (step <= 0) return EnemyClip.Attack;
            return (EnemyClip)((int)EnemyClip.Attack2 + (step - 1));
        }

        // 한 번만 재생하는 클립의 진행도를 밀어 준다. 실제로 뼈를 움직이는 것은 렌더러의 몫이다.
        //
        // 길이는 구간을 시작한 쪽이 적어 둔 값을 쓴다(EnemyAction.animationLength). 예전에는
        // 클립 종류로 골랐는데 표에 Attack·Hit·Stagger 셋만 있어서, 나머지가 전부 1초로 떨어졌다 —
        // 콤보 2~7단은 0.75초짜리 스윙을 1초로 나눠 75%에서 잘렸고, 방향별 피격 네 종은
        // 0.3초 동안 30%만 재생되고 끊겼다. 고블린이 움찔하다 마는 것처럼 보이던 원인이다.
        private static void Advance(ref EnemyAnimation animation, in EnemyAction action, float dt)
        {
            float length = math.max(0.01f, action.animationLength);
            animation.normalizedTime = math.saturate(animation.normalizedTime + dt / length);
        }

        private bool TryGetAlly(int index, out EnemyWorldBridge.AllyState ally)
        {
            if (index < 0 || index >= allies.Length)
            {
                ally = default;
                return false;
            }

            ally = allies[index];
            return ally.alive != 0;
        }

        private static float3 Flat(float3 v) => new float3(v.x, 0f, v.z);
    }
}

// ---------------------------------------------------------------- 이동

// 스티어링으로 붙는다. NavMesh는 쓰지 않는다.
//
// NavMeshAgent는 DOTS 대응이 없고, 있었더라도 1000마리에서 지역 회피(RVO)가 먼저 무너진다.
// 대신 "표적 쪽으로 밀고, 이웃에게서 밀려나는" 두 힘만 쓴다. 아군 쪽 ChaseBehavior가
// 회피에 밀려 제자리에서 떠는 문제를 따로 잡아야 했던 것과 달리, 여기서는 밀어내는 힘이
// 처음부터 이동에 섞여 있어 그런 진동이 생기지 않는다.
[UpdateInGroup(typeof(EnemySimulationGroup))]
public partial struct EnemyMovementSystem : ISystem
{
    // 이웃에게서 밀려나는 세기. 너무 크면 전선이 벌어지고, 너무 작으면 겹쳐 선다.
    private const float SeparationStrength = 2.2f;

    // 넉백이 풀리는 빠르기(1/초). 남은 거리에 비례해 밀므로 처음이 세고 끝이 잦아든다 —
    // 12면 0.12초에 76%, 0.25초에 95%를 간다. 일정한 속도로 밀면 맞은 몸이 미끄러지는 썰매가 된다.
    private const float KnockbackDecay = 12f;
    private const float KnockbackEpsilon = 0.01f;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out EnemyWorldBridge.BridgeData bridge)) return;
        if (!bridge.allies.IsCreated) return;
        if (!SystemAPI.TryGetSingleton(out EnemySpatialHash hash)) return;

        var job = new SteerJob
        {
            allies = bridge.allies.AsArray(),
            hash = hash.map,
            cellSize = hash.cellSize,
            deltaTime = SystemAPI.Time.DeltaTime,
            now = SystemAPI.Time.ElapsedTime,
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct SteerJob : IJobEntity
    {
        [ReadOnly] public NativeArray<EnemyWorldBridge.AllyState> allies;
        [ReadOnly] public NativeParallelMultiHashMap<int, EnemyNeighbor> hash;
        public float cellSize;
        public float deltaTime;
        public double now;

        private void Execute(Entity entity, ref LocalTransform transform, ref EnemyMotion motion, ref EnemyImpact impact,
            in EnemyStats stats, in EnemyTarget target, in EnemyAction action, in EnemyTactics tactics)
        {
            // 히트스톱 동안은 이동도 같이 눌린다(EnemyCombatSystem과 같은 배율).
            float dt = deltaTime * impact.TimeScale(now);

            // 제자리에서 무언가를 하는 중에는 발을 떼지 않는다.
            // 아군 쪽 UnitBehavior.HoldsGround와 같은 자리다.
            // 도약도 여기 든다. 그쪽은 이동을 전투 시스템이 통째로 가져가므로, 스티어링이
            // 옆에서 밀면 뛰는 궤적이 그만큼 휘어 "뛰는데 옆으로 흐르는" 그림이 된다.
            bool holdsGround = action.kind == EnemyActionKind.Windup ||
                               action.kind == EnemyActionKind.Recover ||
                               action.kind == EnemyActionKind.Stagger ||
                               action.kind == EnemyActionKind.HitReact ||
                               action.kind == EnemyActionKind.Leap ||
                               action.kind == EnemyActionKind.Bite ||
                               action.kind == EnemyActionKind.Dead;

            float3 desired = float3.zero;

            if (!holdsGround && target.allyIndex >= 0 && target.allyIndex < allies.Length)
            {
                EnemyWorldBridge.AllyState ally = allies[target.allyIndex];
                if (ally.alive != 0)
                {
                    float3 toAlly = ally.position - transform.Position;
                    toAlly.y = 0f;
                    float distance = math.length(toAlly);

                    // 칼을 들 자리를 못 얻은 놈은 한 걸음 떨어진 데서 멈춘다. 얼마나 떨어질지만
                    // 개체마다 다르고(EnemyTactics.waitDistance), 둘레의 어디에 설지는 정하지 않는다 —
                    // 방위는 무리가 서로 밀치며 정한다. 이미 그보다 안쪽에 있으면(방금 휘두르고 자리를
                    // 내준 놈) 물러나지도 않는다. 등을 돌려 달아나는 그림이 되기 때문이다.
                    float standoff = stats.standoffDistance;
                    if (tactics.role == EnemyCombatRole.Waiter)
                    {
                        // 달려오던 속도로는 멈추기까지 v/가속만큼 더 미끄러진다(아래 lerp의 합).
                        // 그만큼 앞에서 밀기를 멈춰야 기다리는 거리에 실제로 선다 — 안 그러면 0.5m씩
                        // 파고들어, 기다린다면서 칼 든 놈 등에 붙어 선다.
                        float braking = math.length(motion.velocity) / math.max(0.01f, stats.acceleration);
                        standoff = math.max(standoff, tactics.waitDistance) + braking;
                    }

                    // 멈춰 설 거리 안에 들어오면 더 밀지 않는다. 계속 밀면 서로 파고들어
                    // 분리하는 힘과 싸우느라 그 자리에서 떨게 된다.
                    if (distance > standoff && distance > 0.001f)
                    {
                        desired += toAlly / distance;
                    }
                }
            }

            // 이웃에게서 밀려난다. 제 칸과 둘레 여덟 칸만 본다 — 격자 한 칸이 분리 반경의
            // 두 배라 그 바깥의 이웃은 어차피 닿지 않는다.
            float3 push = float3.zero;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    float3 probe = transform.Position + new float3(dx * cellSize, 0f, dz * cellSize);
                    int key = EnemySpatialHash.Hash(probe, cellSize);

                    if (!hash.TryGetFirstValue(key, out EnemyNeighbor neighbor, out var it)) continue;

                    do
                    {
                        if (neighbor.entity == entity) continue;

                        float3 away = transform.Position - neighbor.position;
                        away.y = 0f;
                        float distance = math.length(away);
                        float minimum = stats.radius + neighbor.radius;
                        if (distance >= minimum || distance <= 0.0001f) continue;

                        // 가까울수록 세게 민다. 겹친 정도에 비례시켜야 살짝 스친 이웃이
                        // 전선을 흔들지 않는다.
                        push += (away / distance) * ((minimum - distance) / minimum);
                    }
                    while (hash.TryGetNextValue(out neighbor, ref it));
                }
            }

            desired += push * SeparationStrength;

            // 발이 묶여 있으면 그만큼 느리게 간다(창수의 부위 억제, 빙결 마법).
            // 가속에는 걸지 않는다 — 묶인 것은 다리이지 반응이 아니다.
            // 방금 맞은 놈은 발이 무겁다(피격 둔화). 움찔 모션이 끝나자마자 제 속도로 달려들면
            // 맞은 것이 몸에 남지 않는다.
            float moveSpeed = stats.moveSpeed * motion.SlowFactor(now) * impact.FlinchFactor(now);

            float3 wanted = math.normalizesafe(desired) * (holdsGround ? 0f : moveSpeed);
            motion.desiredDirection = math.normalizesafe(desired);
            motion.velocity = math.lerp(motion.velocity, wanted, math.saturate(stats.acceleration * dt));

            transform.Position += motion.velocity * dt;

            // 넉백. 제자리를 지키는 중(경직·피격·시체)에도 밀린다 — 밀리는 것은 발이 아니라 몸이다.
            // 히트스톱이 걸린 동안은 dt가 눌려 거의 밀리지 않다가, 멈칫이 풀리는 순간 튕겨 나간다.
            //
            // 지형을 벗어나지 않게 하는 장치는 따로 없다. 적은 처음부터 NavMesh 없이 스티어링으로
            // 움직이고(이 시스템 머리 주석), 한 번에 밀리는 거리가 스탯의 knockbackDistance(0.6m)에
            // 무게를 곱한 정도라 평소 한 프레임의 이동과 같은 규모다.
            if (impact.knockbackRemaining > 0f)
            {
                float step = impact.knockbackRemaining * (1f - math.exp(-KnockbackDecay * dt));
                if (impact.knockbackRemaining - step < KnockbackEpsilon) step = impact.knockbackRemaining;

                transform.Position += impact.knockbackDirection * step;
                impact.knockbackRemaining -= step;
            }

            // 가는 쪽을 본다. 붙어 있는 동안에는 겨눈 아군을 본다.
            float3 facing = motion.velocity;
            if (holdsGround || math.lengthsq(facing) < 0.01f)
            {
                if (target.allyIndex >= 0 && target.allyIndex < allies.Length)
                {
                    facing = allies[target.allyIndex].position - transform.Position;
                }
            }

            facing.y = 0f;
            if (math.lengthsq(facing) > 0.0001f)
            {
                quaternion wantedRotation = quaternion.LookRotationSafe(math.normalize(facing), math.up());
                transform.Rotation = math.slerp(transform.Rotation, wantedRotation,
                    math.saturate(stats.turnSpeed * dt));
            }
        }
    }
}
