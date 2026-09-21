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

// ---------------------------------------------------------------- 정찰

// 표적을 잃은 놈이 전장을 돌며 다시 찾는다(EnemyStats 정찰 절 주석).
//
// 여기서 정하는 것은 "어디로 걸어갈까"뿐이다. 실제로 걷는 것은 이동 시스템이고(EnemyMovementSystem),
// 찾았는지는 표적 시스템이 평소대로 정한다 — 표적이 없는 동안은 시야각을 따지지 않으므로(blind)
// 걷다가 탐지 거리 안에 들어온 아군을 곧바로 잡는다. 잡으면 다시 여기로 와서 그 자리를 기억해 둔다.
[UpdateInGroup(typeof(EnemySimulationGroup))]
[UpdateAfter(typeof(EnemyTargetingSystem))]
[UpdateBefore(typeof(EnemyCombatSystem))]
public partial struct EnemyPatrolSystem : ISystem
{
    // 정찰 지점에 닿았다고 보는 거리. 무리가 같은 곳을 찾을 때 서로 밀려 정확히 닿지 못하므로 넉넉히 둔다.
    private const float ArrivalRadius = 1.2f;

    // 한 지점을 향해 이만큼(초)을 걸어도 닿지 못하면 포기하고 다음 지점을 고른다. 무리에 막혔거나 밀려난 경우다.
    private const float WaypointTimeout = 15f;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out EnemyWorldBridge.BridgeData bridge)) return;
        if (!bridge.allies.IsCreated) return;

        var job = new PatrolJob
        {
            allies = bridge.allies.AsArray(),
            now = SystemAPI.Time.ElapsedTime,
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct PatrolJob : IJobEntity
    {
        [ReadOnly] public NativeArray<EnemyWorldBridge.AllyState> allies;
        public double now;

        private void Execute(Entity entity, ref EnemyTactics tactics, in EnemyTarget target, in EnemyStats stats,
            in EnemyAction action, in LocalTransform transform)
        {
            if (action.kind == EnemyActionKind.Dead)
            {
                tactics.patrolling = false;
                return;
            }

            // 겨누는 상대가 있으면 정찰할 일이 없다. 그 자리만 기억해 둔다 — 놓치면 여기서부터 찾는다.
            int index = target.allyIndex;
            if (index >= 0 && index < allies.Length && allies[index].alive != 0)
            {
                tactics.searchCenter = allies[index].position;
                tactics.searchRadius = stats.searchRadiusStart;
                tactics.searchPrimed = true;
                tactics.lostTargetTime = now;
                tactics.patrolling = false;
                tactics.hasWaypoint = false;
                return;
            }

            if (!stats.CanPatrol)
            {
                tactics.patrolling = false;
                return;
            }

            // 아무도 본 적이 없으면 태어난 자리 둘레부터 찾는다.
            if (!tactics.searchPrimed)
            {
                tactics.searchCenter = transform.Position;
                tactics.searchRadius = stats.searchRadiusStart;
                tactics.lostTargetTime = now;
                tactics.searchPrimed = true;
            }

            if (now - tactics.lostTargetTime < stats.patrolDelay)
            {
                tactics.patrolling = false;
                return;
            }

            tactics.patrolling = true;
            if (tactics.random.state == 0) tactics.random = Random.CreateFromIndex((uint)entity.Index);

            if (tactics.hasWaypoint)
            {
                float3 offset = tactics.waypoint - transform.Position;
                offset.y = 0f;
                bool arrived = math.lengthsq(offset) <= ArrivalRadius * ArrivalRadius;
                if (!arrived && now < tactics.waypointGiveUpTime) return;

                // 닿았으면 잠깐 둘러본다. 포기했으면 곧바로 다음 지점으로 간다.
                tactics.hasWaypoint = false;
                float pauseMin = math.max(0f, stats.patrolPauseMin);
                float pause = tactics.random.NextFloat(pauseMin, math.max(pauseMin, stats.patrolPauseMax));
                tactics.nextWaypointTime = arrived ? now + pause : now;
            }

            if (now < tactics.nextWaypointTime) return;

            PickWaypoint(ref tactics, stats);
        }

        // 마지막으로 본 자리를 중심으로 반경 안의 한 점을 고른다. 고를 때마다 반경을 넓혀, 같은 자리만
        // 맴돌지 않고 결국 전장 전체를 덮는다. 전장 밖으로는 나가지 않는다(정찰 절 주석).
        private void PickWaypoint(ref EnemyTactics tactics, in EnemyStats stats)
        {
            float radius = math.max(1f, tactics.searchRadius);
            float2 offset = tactics.random.NextFloat2Direction() * (math.sqrt(tactics.random.NextFloat()) * radius);

            float3 extent = new float3(stats.patrolHalfExtents.x, 0f, stats.patrolHalfExtents.y);
            float3 point = tactics.searchCenter + new float3(offset.x, 0f, offset.y);
            point = math.clamp(point, stats.patrolCenter - extent, stats.patrolCenter + extent);

            tactics.waypoint = point;
            tactics.hasWaypoint = true;
            tactics.waypointGiveUpTime = now + WaypointTimeout;

            float widest = 2f * math.max(stats.patrolHalfExtents.x, stats.patrolHalfExtents.y);
            tactics.searchRadius = math.min(radius + math.max(0f, stats.searchRadiusGrowth), widest);
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
        // x = 시작 줄, y = 프레임 수, z = 초 단위 길이, w = 이 클립이 표현하는 이동 속도(m/s).
        // 여기서는 z(클립을 한 칸 미는 데)와 w(재생 배속을 맞추는 데)를 쓴다. 줄 번호 둘은
        // 그리는 쪽이 읽는다(EnemyAnimationRenderSystem).
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

                // 표적이 없어도 걸을 수 있다 — 정찰을 돌거나(EnemyPatrolSystem) 무리에 밀린다. 예전처럼
                // 제자리 모션을 고정해 두면 정찰하는 놈이 선 채로 미끄러진다. 서 있으면 여기서도 제자리다.
                TickLocomotion(ref animation, motion.smoothedSpeed, stats, dt);
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
            // 날것의 속도가 아니라 한 박자 눌러 따라간 값을 본다(EnemyMotion.smoothedSpeed).
            TickLocomotion(ref animation, motion.smoothedSpeed, stats, dt);
        }

        // 서 있기 · 걷기 · 달리기 중 하나를 고르고 한 칸 민다.
        //
        // 고르는 기준은 걸음 속도의 비율이 아니라 클립이 실제로 표현하는 속도다(구울 때 잰 값 —
        // EnemyAnimationLibrary.ClipRange.groundSpeed). 클립을 제 속도의 0.5~1.3배로 돌리는 동안은
        // 발이 땅에 붙어 있고, 그 바깥으로 벗어나면 미끄러진다. 그래서 걷기 클립이 견디는 구간이
        // 곧 걷는 구간이고, 그보다 빠르면 달리기로 넘긴다.
        //
        // 문턱은 켤 때와 끌 때가 다르다(이력). 무리 속에서는 이웃에게 밀리고 겹침이 풀리며 속도가
        // 문턱 언저리에서 계속 흔들리는데, 하나로 두면 그때마다 클립이 뒤바뀐다. 실측(고블린
        // 30마리가 한 사람에게 몰린 10초): 문턱 하나였을 때 마리당 초당 0.57회 뒤바뀌었다.
        private void TickLocomotion(ref EnemyAnimation animation, float speed, in EnemyStats stats, float dt)
        {
            float walkSpeed = ClipGroundSpeed(EnemyClip.Walk);

            // 걷기를 굽지 않은 리그는 예전처럼 둘로만 나눈다. 구간표에 없는 클립을 가리키면
            // 그 줄이 통째로 비어 몸이 사라진다.
            if (walkSpeed <= 0.01f)
            {
                bool wasRunning = animation.clip == EnemyClip.Run;
                float onlyRun = stats.moveSpeed * (wasRunning ? RunStopRatio : RunStartRatio);

                if (speed > onlyRun) Loop(ref animation, EnemyClip.Run, dt * PlaybackRate(speed, RunGroundSpeed(stats)));
                else Loop(ref animation, EnemyClip.Idle, dt);
                return;
            }

            bool moving = animation.clip == EnemyClip.Walk || animation.clip == EnemyClip.Run;
            bool running = animation.clip == EnemyClip.Run;

            // 걷기 클립을 이보다 느리게 돌리면 발이 미끄러지기 시작한다 — 거기서부터 선다.
            float walkFloor = walkSpeed * (moving ? MinPlaybackRate * StopHysteresis : MinPlaybackRate);

            // 걷기 클립이 견디는 윗선. 넘으면 달리기로 넘긴다.
            float walkCeiling = walkSpeed * (running ? MaxWalkPlaybackRate * StopHysteresis : MaxWalkPlaybackRate);

            if (speed > walkCeiling)
            {
                Loop(ref animation, EnemyClip.Run, dt * PlaybackRate(speed, RunGroundSpeed(stats)));
                return;
            }

            if (speed > walkFloor)
            {
                Loop(ref animation, EnemyClip.Walk, dt * PlaybackRate(speed, walkSpeed));
                return;
            }

            Loop(ref animation, EnemyClip.Idle, dt);
        }

        // 걷기 클립이 없을 때만 쓰는 문턱(제 걸음 속도에 대한 비율).
        private const float RunStartRatio = 0.25f;
        private const float RunStopRatio = 0.18f;

        // 켠 것을 끌 때는 문턱을 이만큼 낮춰 잡는다.
        private const float StopHysteresis = 0.75f;

        // 걷기 클립을 이 배속까지만 밀어붙인다. 더 밀면 걷는 모션이 종종걸음이 된다.
        private const float MaxWalkPlaybackRate = 1.3f;

        // 재생 배속. 다리가 땅을 미는 속도와 몸이 나아가는 속도를 맞춘다.
        //
        // 예전에는 늘 1배속이었다. 게임오브젝트 고블린은 UnitController.ApplyMoveAnimationSpeed가
        // 같은 계산으로 맞추고 있었다.
        private static float PlaybackRate(float speed, float clipSpeed)
        {
            if (clipSpeed <= 0.01f) return 1f;
            return math.clamp(speed / clipSpeed, MinPlaybackRate, MaxPlaybackRate);
        }

        // 달리기 클립이 표현하는 속도. 구울 때 잰 값을 먼저 쓰고, 없으면 손으로 적은 값을 쓴다.
        //
        // 손으로 적은 값(stats.runClipSpeed)은 원본 클립의 averageSpeed라 리타깃 축소가 빠져 있다 —
        // 고블린은 2.29로 적혀 있지만 humanScale이 0.73이라 실제로는 1.67로 걷는다. 그 차이만큼
        // 다리가 덜 돌아 발이 미끄러졌다. 구운 값은 리타깃된 뒤의 보폭을 직접 잰 것이라 그 함정이 없다.
        private float RunGroundSpeed(in EnemyStats stats)
        {
            float baked = ClipGroundSpeed(EnemyClip.Run);
            return baked > 0.01f ? baked : stats.runClipSpeed;
        }

        private float ClipGroundSpeed(EnemyClip clip)
        {
            int index = (int)clip;
            if (!clipRanges.IsCreated || index < 0 || index >= clipRanges.Length) return 0f;
            return clipRanges[index].w;
        }

        private const float MinPlaybackRate = 0.5f;
        private const float MaxPlaybackRate = 2.2f;

        // 쥔 자리로 한 수를 냈다. 휘두르는 동안은 자리를 빼앗기지 않게 만료를 뒤로 민다.
        private void CommitSlot(ref EnemyTactics tactics, in EnemyStats stats)
        {
            tactics.slotExpireTime = now + EnemyAttackSlotSystem.HoldTimeout(stats);
        }

        // 도는 클립(제자리걸음·달리기)을 한 칸 민다. 게임플레이가 정한 시간이 없으므로
        // 구워 둔 클립 길이로 돌린다 — 달리기는 0.867초라, 예전의 고정 1.4회/초는 21% 빨랐다.
        private void Loop(ref EnemyAnimation animation, EnemyClip clip, float dt)
        {
            // 클립이 바뀌는 순간에만 처음으로 돌린다. 한 번만 재생하는 클립(스윙·도약·물기·움찔)을
            // 마치고 오면 진행도가 1에 가깝게 남아 있어서, 그대로 이어 붙이면 걷는 모션이 끝자락부터
            // 시작한다. 매 프레임 0으로 되돌리면 안 된다 — 스폰 때 흩어 놓은 시작 지점(EnemyHorde)이
            // 지워져 1000마리가 같은 박자로 숨 쉬게 된다.
            //
            // 도는 클립끼리(제자리걸음 ↔ 걷기 ↔ 달리기) 오갈 때는 진행도를 그대로 들고 간다. 셋 다
            // 순환하는 클립이라 어디서 이어도 끊기지 않는데, 0으로 되돌리면 걷기를 잠깐 스쳤다 오는
            // 것만으로 달리기 주기가 처음으로 감긴다. 그게 화면에서 "달리다 되감긴다"로 보인다.
            if (animation.clip != clip)
            {
                bool fromLooping = IsLooping(animation.clip);
                animation.clip = clip;
                if (!fromLooping) animation.normalizedTime = 0f;
                return;
            }

            animation.normalizedTime = math.frac(animation.normalizedTime + dt / ClipLength(clip));
        }

        // 끝에서 처음으로 이어 붙여 도는 클립인가. 베이커가 제자리로 만들어 두는 것과 같은 셋이다
        // (EnemyAnimationBaker.Wanted의 loops).
        private static bool IsLooping(EnemyClip clip)
        {
            return clip == EnemyClip.Idle || clip == EnemyClip.Walk || clip == EnemyClip.Run;
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

                // 무는 상대를 보는 회전은 이동 시스템이 한다(EnemyMovementSystem.TickFacing, 휘두르는 속도).
                // 예전에는 여기서도 따로 돌려서, 같은 프레임에 두 번 돌았다.
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
//
// 간격은 두 겹으로 지킨다. 힘 하나로는 안 된다.
//
//  1) 미는 힘(SeparationStrength) — 부드럽고 미리 갈라 준다. 다만 힘은 힘과 싸운다.
//     표적 쪽으로 미는 힘이 1까지 나오고 밀려나는 힘이 겹친 비율 × 2.2라, 둘이 맞서는 자리가
//     겹친 비율 1/2.2 ≈ 0.45다. 즉 앞을 막은 놈 하나와 반지름의 45%까지 파고든 채로 평형에 든다.
//     마리 수가 늘면 그 평형이 무리 전체로 번진다 — 마흔 마리를 한 사람에게 몰아 4초를 돌리면
//     가장 가까운 두 마리가 0.73m였다(반지름 합 1m).
//  2) 자리로 푸는 한 걸음(UnstackRelaxation) — 속도를 적분한 뒤 남은 겹침을 좌표로 직접 밀어낸다.
//     힘의 평형과 무관하게 반지름 합이 바닥선이 된다. 제자리를 지키는 중(휘두르기·경직·물기)에도
//     적용된다 — 0.75초짜리 스윙 내내 못 밀려나는 놈은 무리 한가운데에서 기둥이 되고,
//     둘레의 전원이 그 몸을 통과해 선다. 적끼리만이 아니라 아군의 몸도 같은 바닥선을 갖는다.
//
// 서로 맞물린 셋째 조각이 속도 지우기다. 자리로 밀어낸 방향의 반대로 파고들던 속도를 지우지 않으면
// 매 프레임 "밀고 들어갔다 도로 밀려나기"를 반복한다. 몸은 제자리인데 속도만 남는데, 걷는 모션과
// 그 재생 배속이 motion.velocity 크기로 정해지므로(EnemyCombatSystem) 그대로 발이 미끄러진다.
// 실측(고블린 30마리가 한 사람에게 몰린 상태): 지우기 전에는 0.44m/s를 들고 0.02m/s를 갔다.
[UpdateInGroup(typeof(EnemySimulationGroup))]
public partial struct EnemyMovementSystem : ISystem
{
    // 이웃에게서 밀려나는 세기. 너무 크면 전선이 벌어지고, 너무 작으면 겹쳐 선다.
    private const float SeparationStrength = 2.2f;

    // 남은 겹침을 한 프레임에 몇 %나 자리로 풀 것인가.
    //
    // 겹친 쌍이 서로 절반씩 물러나면 한 번에 딱 떨어진다(1.0). 다만 이웃이 여럿일 때는 각자가
    // 같은 스냅샷을 보고 동시에 물러나므로(야코비), 한 번에 다 풀면 서로의 몫까지 겹쳐 밀려
    // 무리가 펄떡인다. 이웃 수로 나눠 평균을 내고 그 절반씩만 간다 — 두세 프레임이면 닿는다.
    private const float UnstackRelaxation = 0.5f;

    // 자리로 밀려나는 속도의 상한(제 걸음 속도에 대한 비율).
    //
    // 평소에 필요한 몫은 한 프레임에 밀고 들어간 만큼(수 cm)이라 여기에 걸리지 않는다. 걸리는 것은
    // 깊이 겹쳤을 때 — 도약해 남의 몸 위에 내려앉았거나, 스윙을 마쳤더니 무리가 그 사이 파고들었거나,
    // 겹쳐 태어났을 때다.
    //
    // 한때 1.0(제 걸음 속도)이었다. 깊은 겹침이 한두 프레임에 풀리는 대신, 달리는 모션 위에서 몸만
    // 3.36m/s로 뒤로 튀었다 — 되감긴 것처럼 보이던 것이 이것이다(30마리 10초 실측).
    // 0.35면 같은 겹침이 0.3초에 걸쳐 풀려, 뒤로 밀리는 것이 걸음의 일부로 읽힌다.
    private const float MaxUnstackSpeedRatio = 0.35f;

    // 넉백이 풀리는 빠르기(1/초). 남은 거리에 비례해 밀므로 처음이 세고 끝이 잦아든다 —
    // 12면 0.12초에 76%, 0.25초에 95%를 간다. 일정한 속도로 밀면 맞은 몸이 미끄러지는 썰매가 된다.
    private const float KnockbackDecay = 12f;
    private const float KnockbackEpsilon = 0.01f;

    // 감속의 바닥. 0까지 내리면 문턱 바로 앞에서 영영 기어간다.
    private const float MinArrivalFactor = 0.2f;

    // 걷는 모션이 보는 속도가 실제 속도를 따라잡는 데 걸리는 시간(초).
    //
    // 짧으면 프레임마다 튀는 것이 그대로 클립 선택에 들어오고, 길면 뛰기 시작한 뒤에도 한참
    // 서 있는 모션이 남는다. 0.12초면 가속(8)으로 문턱을 넘는 데 걸리는 시간과 같은 자릿수라,
    // 늦는 것이 눈에 띄지 않으면서 프레임 단위의 떨림은 전부 걸러진다.
    private const float SpeedSmoothTime = 0.12f;

    // 멈춰 설 거리 몇 m 앞에서부터 감속할 것인가.
    //
    // 속도는 가속 lerp로 목표 속도를 따라가고, 목표 속도는 남은 거리에 비례한다. 그 둘을 합치면
    // 남은 거리가 d'' + a·d' + (a·v/L)·d = 0 을 따른다(a = 가속, v = 최고 속도, L = 이 거리).
    // L이 4v/a보다 짧으면 덜 감쇠돼 멈춰 설 거리를 지나쳤다 되돌아오고(1m로 뒀을 때 30cm 앞에서도
    // 2.4m/s였다), 길면 한참 앞에서부터 기어온다. 4v/a가 딱 임계 감쇠라 지나치지 않고 가장 빨리 선다.
    // 고블린(4m/s, 가속 8)이면 2m다.
    private static float ArrivalSlowDistance(in EnemyStats stats)
    {
        return ArrivalSlowDistance(stats.moveSpeed, stats);
    }

    private static float ArrivalSlowDistance(float speed, in EnemyStats stats)
    {
        return math.clamp(4f * speed / math.max(0.01f, stats.acceleration), 0.5f, 4f);
    }

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

            bool hasTarget = target.allyIndex >= 0 && target.allyIndex < allies.Length &&
                             allies[target.allyIndex].alive != 0;

            float3 toTarget = float3.zero;
            if (hasTarget)
            {
                toTarget = allies[target.allyIndex].position - transform.Position;
                toTarget.y = 0f;
            }

            float3 desired = float3.zero;

            // 표적 없이 정찰 지점을 향하는 중인가(EnemyPatrolSystem). 걸음이 느려지고, 가는 쪽을 본다.
            bool patrolling = !hasTarget && tactics.patrolling && tactics.hasWaypoint;
            float speedLimit = patrolling && stats.patrolSpeed > 0f ? math.min(stats.patrolSpeed, stats.moveSpeed) : stats.moveSpeed;

            if (!holdsGround && patrolling)
            {
                float3 toWaypoint = tactics.waypoint - transform.Position;
                toWaypoint.y = 0f;
                float distance = math.length(toWaypoint);
                if (distance > 0.001f)
                {
                    // 붙으러 갈 때와 같은 도착 감속. 기준 속도만 정찰 걸음으로 잡는다.
                    float arrival = math.saturate(distance / ArrivalSlowDistance(speedLimit, stats));
                    desired += toWaypoint / distance * math.max(MinArrivalFactor, arrival);
                }
            }

            if (!holdsGround && hasTarget)
            {
                float distance = math.length(toTarget);

                // 칼을 들 자리를 못 얻은 놈은 한 걸음 떨어진 데서 멈춘다. 얼마나 떨어질지만
                // 개체마다 다르고(EnemyTactics.waitDistance), 둘레의 어디에 설지는 정하지 않는다 —
                // 방위는 무리가 서로 밀치며 정한다. 이미 그보다 안쪽에 있으면(방금 휘두르고 자리를
                // 내준 놈) 물러나지도 않는다. 등을 돌려 달아나는 그림이 되기 때문이다.
                float standoff = tactics.role == EnemyCombatRole.Waiter
                    ? math.max(stats.standoffDistance, tactics.waitDistance)
                    : stats.standoffDistance;

                // 멈춰 설 거리 안에 들어오면 더 밀지 않는다. 계속 밀면 서로 파고들어
                // 분리하는 힘과 싸우느라 그 자리에서 떨게 된다.
                if (distance > standoff && distance > 0.001f)
                {
                    // 다 와 갈수록 약하게 민다(도착 감속).
                    //
                    // 예전에는 멈춰 설 거리의 문턱까지 최고 속도로 밀다가 거기서 딱 끊었다. 가속 lerp만
                    // 남아 0.5m를 더 미끄러지며 섰고, 그 그림이 "상대 코앞까지 쏜살같이 달려와 박힌다"였다.
                    // 게임오브젝트 고블린 시절에는 NavMeshAgent의 자동 감속이 이 일을 했다.
                    float arrival = math.saturate((distance - standoff) / ArrivalSlowDistance(stats));
                    desired += toTarget / distance * math.max(MinArrivalFactor, arrival);
                }
            }

            // 이웃에게서 밀려난다. 제 칸과 둘레 여덟 칸만 본다 — 격자 한 칸이 분리 반경의
            // 두 배라 그 바깥의 이웃은 어차피 닿지 않는다.
            //
            // 한 번 훑으면서 두 몫을 같이 담는다. push는 이동에 섞일 힘이고, escape는 이번 프레임에
            // 자리로 풀어야 할 겹침(미터)이다. 둘 다 같은 자리에서 나오므로 질의는 한 번뿐이다.
            float3 push = float3.zero;
            float3 escape = float3.zero;
            int contacts = 0;

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

                        float3 direction = away / distance;

                        // 가까울수록 세게 민다. 겹친 정도에 비례시켜야 살짝 스친 이웃이
                        // 전선을 흔들지 않는다.
                        push += direction * ((minimum - distance) / minimum);

                        // 파고든 깊이의 절반. 상대도 같은 스냅샷을 보고 반대로 같은 몫을 물러나므로
                        // 둘을 합치면 정확히 반지름 합까지 벌어진다. 한쪽만 온전히 물러나게 하면
                        // 밀려난 쪽이 무리를 가로질러 흐른다.
                        escape += direction * ((minimum - distance) * 0.5f);
                        contacts++;
                    }
                    while (hash.TryGetNextValue(out neighbor, ref it));
                }
            }

            desired += push * SeparationStrength;

            // 아군의 몸도 자리를 차지한다. 격자에는 적만 담기므로 따로 훑는데, 편성이 다섯 자리라
            // (PartyDeck.DefaultCapacity) 마리마다 전부 봐도 이웃 질의 한 번보다 싸다.
            //
            // 여기가 없으면 뒤에서 미는 무리가 앞줄을 아군 몸속으로 밀어 넣는다. 멈춰 설 거리(1.0m)가
            // 반지름 합과 똑같아서, 스스로 서는 그 선을 한 뼘만 넘어도 곧바로 몸이 겹친다.
            //
            // 자리만 밀고 힘(push)에는 더하지 않는다. 멈춰 설 거리에 딱 선 놈은 아직 파고들지 않았으니
            // 힘이 0이고, 거기에 힘을 얹으면 붙는 힘과 맞서 문턱에서 떨게 된다.
            // 파고든 깊이도 반으로 나누지 않는다 — 아군은 제 행동 트리로 걷는 게임오브젝트라
            // 이 잡이 밀어 줄 수 없다. 물러나는 것은 적뿐이다.
            for (int i = 0; i < allies.Length; i++)
            {
                EnemyWorldBridge.AllyState ally = allies[i];
                if (ally.alive == 0) continue;

                float3 away = transform.Position - ally.position;
                away.y = 0f;
                float distance = math.length(away);
                float minimum = stats.radius + ally.radius;
                if (distance >= minimum || distance <= 0.0001f) continue;

                escape += (away / distance) * (minimum - distance);
                contacts++;
            }

            // 발이 묶여 있으면 그만큼 느리게 간다(창수의 부위 억제, 빙결 마법).
            // 가속에는 걸지 않는다 — 묶인 것은 다리이지 반응이 아니다.
            // 방금 맞은 놈은 발이 무겁다(피격 둔화). 움찔 모션이 끝나자마자 제 속도로 달려들면
            // 맞은 것이 몸에 남지 않는다.
            float moveSpeed = speedLimit * motion.SlowFactor(now) * impact.FlinchFactor(now);

            // 속도는 미는 힘의 크기를 따른다(1에서 자른다).
            //
            // 예전에는 방향만 남기고(normalize) 늘 최고 속도를 냈다. 그래서 표적 쪽으로 밀 일이 없는 놈
            // (멈춰 설 거리 안, 자리를 기다리는 놈)도 이웃과 살짝만 겹치면 그 방향으로 4m/s로 튀었고,
            // 밀치는 방향이 매 프레임 바뀌어 무리 전체가 제자리에서 빠르게 흔들렸다.
            float strength = math.min(1f, math.length(desired));
            float3 wanted = math.normalizesafe(desired) * (holdsGround ? 0f : moveSpeed * strength);
            motion.desiredDirection = math.normalizesafe(desired);
            motion.velocity = math.lerp(motion.velocity, wanted, math.saturate(stats.acceleration * dt));

            // 힘으로 다 풀지 못한 겹침을 자리로 민다.
            //
            // 시체는 건드리지 않는다. 격자에 실리지 않아(EnemySpatialHashSystem) 남을 밀지 못하는데
            // 혼자 밀려나기만 하면, 쓰러진 몸이 산 놈들을 따라 바닥을 미끄러진다.
            float3 correction = float3.zero;
            if (contacts > 0 && action.kind != EnemyActionKind.Dead)
            {
                correction = escape / contacts * UnstackRelaxation;

                float step = math.length(correction);
                float limit = MaxUnstackSpeedRatio * stats.moveSpeed * dt;
                if (step > limit && step > 0.0001f) correction *= limit / step;

                // 밀려나는 쪽으로 파고들던 속도는 지운다. 남기면 매 프레임 밀고 들어갔다 도로 밀려나며
                // 제자리에서 달리기 모션을 재생한다. 옆으로 비낀 성분만 남아 무리를 타고 흐른다.
                float3 normal = math.normalizesafe(correction);
                float into = -math.dot(motion.velocity, normal);
                if (into > 0f) motion.velocity += normal * into;
            }

            transform.Position += motion.velocity * dt;

            // 걷는 모션이 볼 속도. 클립을 고르는 것은 전투 시스템이지만 속도를 아는 것은 여기다.
            motion.smoothedSpeed = math.lerp(motion.smoothedSpeed, math.length(motion.velocity),
                math.saturate(dt / SpeedSmoothTime));

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

            // 겹침 풀기는 맨 끝이다. 이건 힘이 아니라 자리라서, 속도와 넉백이 다 더해진 뒤에
            // 그 위에 얹어야 한다(여기에 다시 dt를 곱하지 않는 이유이기도 하다).
            //
            // 재는 자리는 이번 프레임이 시작될 때의 격자다. 서로 상대를 한 프레임 전 자리로 보는
            // 셈인데, 60프레임에서 그 사이에 움직이는 거리가 7cm라 반지름 합 1m 앞에서는 묻힌다.
            // 대신 쌍의 양쪽이 같은 값을 반대로 보게 되어, 둘이 서로 다른 만큼 물러나며
            // 무리가 한쪽으로 흐르는 일이 없다.
            transform.Position += correction;

            TickFacing(ref transform, motion, stats, action, hasTarget, toTarget, dt);
        }

        // 어느 쪽을 볼 것인가.
        //
        // 겨눈 상대가 있으면 그 상대를 본다 — 붙으러 가든, 자리를 기다리든, 휘두르든. 게임오브젝트 고블린이
        // 쫓는 내내 표적을 보던 것(ChaseBehavior.FaceTarget)과 같다.
        //
        // 예전에는 "가는 쪽"을 봤다. 멀리서 달려올 때는 가는 쪽이 곧 표적 쪽이라 차이가 없었지만, 붙은 뒤에는
        // 속도가 거의 전부 이웃에게 밀린 성분이라 방향이 프레임마다 뒤집혔고, 몸이 그걸 따라 돌았다.
        // 실측(고블린 24 대 아군 2, 표적 8m 안): 칼 안 든 동안 평균 90~223도/초로 돌았고, 절반 가까이
        // 표적을 등지거나 옆을 보고 있었다.
        //
        // 회전은 초당 각도로 제한한다(지수 감쇠가 아니다 — 아군 쪽 FaceDirection 주석과 같은 이유).
        // 휘두르는 중에는 훨씬 느리게 돈다. 제자리에서 베는 모션 위에서 몸이 홱 돌면 발이 미끄러진다.
        // 무너졌거나 쓰러진 동안은 돌지 않는다.
        private static void TickFacing(ref LocalTransform transform, in EnemyMotion motion, in EnemyStats stats,
            in EnemyAction action, bool hasTarget, float3 toTarget, float dt)
        {
            if (action.kind == EnemyActionKind.Stagger || action.kind == EnemyActionKind.Dead) return;

            float3 facing = hasTarget ? toTarget : motion.velocity;
            facing.y = 0f;
            if (math.lengthsq(facing) <= 0.0001f) return;

            bool swinging = action.kind == EnemyActionKind.Windup ||
                            action.kind == EnemyActionKind.Recover ||
                            action.kind == EnemyActionKind.HitReact ||
                            action.kind == EnemyActionKind.Leap ||
                            action.kind == EnemyActionKind.Bite;
            float degreesPerSecond = swinging ? stats.swingTurnRate : stats.turnRate;

            quaternion wanted = quaternion.LookRotationSafe(math.normalize(facing), math.up());

            // 회전 속도가 0 이하면 제한 없음(곧바로 돈다)으로 본다 — 수치를 채우지 않은 원본·테스트용.
            // 히트스톱으로 dt가 0이 된 것과 헷갈리면 안 된다(그때는 돌지 않아야 한다).
            if (degreesPerSecond <= 0f)
            {
                transform.Rotation = wanted;
                return;
            }

            transform.Rotation = RotateTowards(transform.Rotation, wanted, math.radians(degreesPerSecond) * dt);
        }

        // 초당 각도 제한 회전. 이번 프레임에 돌 수 있는 각도(라디안)만큼만 목표 쪽으로 돈다.
        private static quaternion RotateTowards(quaternion from, quaternion to, float maxRadians)
        {
            if (maxRadians <= 0f) return from;

            float dot = math.abs(math.dot(from.value, to.value));
            if (dot >= 0.99999f) return to;

            float angle = 2f * math.acos(math.min(dot, 1f));
            return angle <= maxRadians ? to : math.slerp(from, to, maxRadians / angle);
        }
    }
}
