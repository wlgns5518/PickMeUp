using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// 적 시뮬레이션 한 프레임. 순서가 곧 의존 관계다.
//
//   공간 해시 → 표적 고르기 → 전투 판단 → 이동 → (브리지 출력)
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

        private void Execute(ref EnemyTarget target, in LocalTransform transform, in EnemyStats stats,
            in EnemyAction action)
        {
            if (action.kind == EnemyActionKind.Dead) return;

            // 이미 나간 스윙 도중에는 겨눌 상대를 바꾸지 않는다. 바꾸면 칼이 엉뚱한 쪽으로 간다 —
            // 아군 쪽 UnitBehavior.LocksTarget과 같은 규칙이다.
            if (action.kind == EnemyActionKind.Windup || action.kind == EnemyActionKind.Recover) return;

            // 들고 있던 표적이 아직 쓸 만하면 그대로 둔다.
            if (IsUsable(target.allyIndex) && now < target.nextRetargetTime) return;

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
                if (i != target.allyIndex && distance > 0.01f)
                {
                    float alignment = math.dot(forward, toAlly / distance);
                    if (alignment < cosHalfFov) continue;
                }

                // 가까울수록, 어그로가 높을수록, 이미 붙은 놈이 적을수록 낫다.
                // 어그로로 나누는 것이라 탱커(3.2)는 같은 거리에서 3배 넘게 당겨진다.
                float score = distance / math.max(0.01f, ally.threatWeight);
                score += ally.attackerCount * 0.6f;

                if (score >= bestScore) continue;

                bestScore = score;
                best = i;
            }

            target.allyIndex = best;
            target.nextRetargetTime = now + retargetInterval;
        }

        private bool IsUsable(int index)
        {
            if (index < 0 || index >= allies.Length) return false;
            return allies[index].alive != 0;
        }
    }
}

// ---------------------------------------------------------------- 전투 판단

// 붙었으면 휘두르고, 아니면 붙는다. 고블린이 하는 일의 전부다.
//
// 애니메이션 이벤트가 없으므로 타격 시점을 시간으로 잡는다. windup이 끝나는 그 프레임에
// 한 번만 판정하고(struckThisSwing), 그때 다시 거리와 각도를 잰다 — 스윙이 시작된 뒤
// 상대가 빠져나갔으면 빗나가야 한다는 규칙은 아군 쪽 ApplyAttackDamage와 똑같이 지킨다.
[UpdateInGroup(typeof(EnemySimulationGroup))]
[UpdateBefore(typeof(EnemyMovementSystem))]
public partial struct EnemyCombatSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out EnemyWorldBridge.BridgeData bridge)) return;
        if (!bridge.allies.IsCreated) return;

        var job = new CombatJob
        {
            allies = bridge.allies.AsArray(),
            hits = bridge.hitsOnAllies.AsParallelWriter(),
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
        public float deltaTime;
        public double now;

        private void Execute(Entity entity, ref EnemyAction action, ref EnemyAnimation animation,
            in EnemyTarget target, in EnemyStats stats, in LocalTransform transform)
        {
            if (action.kind == EnemyActionKind.Dead) return;

            action.timer -= deltaTime;

            switch (action.kind)
            {
                case EnemyActionKind.HitReact:
                case EnemyActionKind.Stagger:
                    // 스스로 아무것도 못 한다. 시간이 다하면 교전으로 돌아간다.
                    if (action.timer <= 0f) EnterApproach(ref action, ref animation);
                    else Advance(ref animation, action, stats);
                    return;

                case EnemyActionKind.Windup:
                    TickWindup(entity, ref action, ref animation, target, stats, transform);
                    return;

                case EnemyActionKind.Recover:
                    if (action.timer <= 0f) EnterApproach(ref action, ref animation);
                    else Advance(ref animation, action, stats);
                    return;
            }

            // 여기부터가 Idle/Approach — 스스로 다음 수를 고를 수 있는 구간이다.
            if (!TryGetAlly(target.allyIndex, out EnemyWorldBridge.AllyState ally))
            {
                action.kind = EnemyActionKind.Idle;
                animation.clip = EnemyClip.Idle;
                animation.normalizedTime = math.frac(animation.normalizedTime + deltaTime * 0.5f);
                return;
            }

            float distance = math.distance(Flat(ally.position), Flat(transform.Position));
            bool inRange = distance <= stats.attackRange;

            if (inRange && now >= action.nextAttackTime)
            {
                action.kind = EnemyActionKind.Windup;
                action.timer = stats.attackWindup;
                action.struckThisSwing = false;
                animation.clip = EnemyClip.Attack;
                animation.normalizedTime = 0f;
                return;
            }

            action.kind = EnemyActionKind.Approach;
            animation.clip = inRange ? EnemyClip.Idle : EnemyClip.Run;
            animation.normalizedTime = math.frac(animation.normalizedTime + deltaTime * 1.4f);
        }

        // 칼을 들어올린 구간. 끝나는 프레임에 딱 한 번 판정한다.
        private void TickWindup(Entity self, ref EnemyAction action, ref EnemyAnimation animation,
            in EnemyTarget target, in EnemyStats stats, in LocalTransform transform)
        {
            Advance(ref animation, action, stats);

            if (action.timer > 0f) return;
            if (action.struckThisSwing)
            {
                EnterRecover(ref action, ref animation, stats);
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
                }
            }

            EnterRecover(ref action, ref animation, stats);
        }

        private void EnterRecover(ref EnemyAction action, ref EnemyAnimation animation, in EnemyStats stats)
        {
            action.kind = EnemyActionKind.Recover;
            action.timer = stats.attackRecovery;
            action.nextAttackTime = now + stats.attackCooldown;
        }

        private void EnterApproach(ref EnemyAction action, ref EnemyAnimation animation)
        {
            action.kind = EnemyActionKind.Approach;
            animation.clip = EnemyClip.Run;
            animation.normalizedTime = 0f;
        }

        // 클립 진행도만 밀어 준다. 실제로 뼈를 움직이는 것은 렌더러의 몫이다.
        private void Advance(ref EnemyAnimation animation, in EnemyAction action, in EnemyStats stats)
        {
            float length = animation.clip switch
            {
                EnemyClip.Attack => math.max(0.01f, stats.attackWindup + stats.attackRecovery),
                EnemyClip.Hit => math.max(0.01f, stats.hitReactionDuration),
                EnemyClip.Stagger => math.max(0.01f, stats.staggerDuration),
                _ => 1f,
            };

            animation.normalizedTime = math.saturate(animation.normalizedTime + deltaTime / length);
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

        private void Execute(Entity entity, ref LocalTransform transform, ref EnemyMotion motion,
            in EnemyStats stats, in EnemyTarget target, in EnemyAction action)
        {
            // 제자리에서 무언가를 하는 중에는 발을 떼지 않는다.
            // 아군 쪽 UnitBehavior.HoldsGround와 같은 자리다.
            bool holdsGround = action.kind == EnemyActionKind.Windup ||
                               action.kind == EnemyActionKind.Recover ||
                               action.kind == EnemyActionKind.Stagger ||
                               action.kind == EnemyActionKind.HitReact ||
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

                    // 멈춰 설 거리 안에 들어오면 더 밀지 않는다. 계속 밀면 서로 파고들어
                    // 분리하는 힘과 싸우느라 그 자리에서 떨게 된다.
                    if (distance > stats.standoffDistance && distance > 0.001f)
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

            float3 wanted = math.normalizesafe(desired) * (holdsGround ? 0f : stats.moveSpeed);
            motion.desiredDirection = math.normalizesafe(desired);
            motion.velocity = math.lerp(motion.velocity, wanted, math.saturate(stats.acceleration * deltaTime));

            transform.Position += motion.velocity * deltaTime;

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
                    math.saturate(stats.turnSpeed * deltaTime));
            }
        }
    }
}
