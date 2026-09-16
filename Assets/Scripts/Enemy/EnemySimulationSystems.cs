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

        // 구워 둔 클립 길이. 제자리걸음과 달리기는 게임플레이가 정한 시간이 없어서
        // 클립 자신의 길이로 돌려야 한다 — 없으면 1초로 보고 돈다(ClipLength).
        SystemAPI.TryGetSingleton(out EnemyAnimationLookup lookup);

        var job = new CombatJob
        {
            allies = bridge.allies.AsArray(),
            hits = bridge.hitsOnAllies.AsParallelWriter(),
            clipRanges = lookup.clipRanges,
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
            in EnemyTarget target, in EnemyStats stats, in EnemyMotion motion, ref LocalTransform transform)
        {
            if (action.kind == EnemyActionKind.Dead) return;

            action.timer -= deltaTime;

            switch (action.kind)
            {
                case EnemyActionKind.Leap:
                    TickLeap(entity, ref action, ref animation, ref transform, target, stats);
                    return;

                case EnemyActionKind.Bite:
                    TickBite(entity, ref action, ref animation, ref transform, target, stats);
                    return;

                case EnemyActionKind.HitReact:
                case EnemyActionKind.Stagger:
                    // 스스로 아무것도 못 한다. 시간이 다하면 교전으로 돌아간다.
                    if (action.timer > 0f) { Advance(ref animation, action); return; }
                    break;

                case EnemyActionKind.Windup:
                    TickWindup(entity, ref action, ref animation, target, stats, transform);
                    return;

                case EnemyActionKind.Recover:
                    if (action.timer > 0f) { Advance(ref animation, action); return; }
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
                Loop(ref animation, EnemyClip.Idle);
                return;
            }

            float distance = math.distance(Flat(ally.position), Flat(transform.Position));
            bool inRange = distance <= stats.attackRange;

            // 붙잡고 늘어지는 한 방. 평타보다 먼저 본다 — 쿨다운이 길어(5초) 기회가 왔을 때
            // 쓰지 않으면 그 사이 평타가 계속 잡아먹는다.
            //
            // 이미 물린 아군은 다시 물지 않는다(canBeBitten). 그게 없으면 한 명에게 여럿이
            // 동시에 물고 늘어져 그 자리에서 녹는다.
            if (inRange && stats.biteDamage > 0 && stats.biteDuration > 0f &&
                now >= action.nextBiteTime && ally.canBeBitten != 0)
            {
                action.kind = EnemyActionKind.Bite;
                action.timer = stats.biteDuration;
                action.animationLength = stats.biteDuration;
                action.struckThisSwing = false;
                action.nextBiteTime = now + stats.biteCooldown;
                animation.clip = EnemyClip.Bite;
                animation.normalizedTime = 0f;
                return;
            }

            if (inRange && now >= action.nextAttackTime)
            {
                action.kind = EnemyActionKind.Windup;
                action.timer = stats.attackWindup;
                // 한 번의 스윙은 들어올리기와 거두기 두 구간에 걸쳐 있고, 클립 하나가 그 둘을
                // 통째로 덮는다. 그래서 길이는 여기서 한 번만 잡고 EnterRecover는 건드리지 않는다.
                action.animationLength = stats.attackWindup + stats.attackRecovery;
                action.struckThisSwing = false;
                animation.clip = ComboClip(action.comboIndex, stats.comboSteps);
                animation.normalizedTime = 0f;
                return;
            }

            // 아직 닿지 않지만 한 번에 붙을 수 있는 거리다 — 걸어 들어가는 대신 덤벼든다.
            // 이미 닿는 상대에게는 뛰지 않는다(위 inRange가 먼저 걸린다).
            if (!inRange && stats.leapRange > 0f && stats.leapDuration > 0f &&
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
            Loop(ref animation, speedSq > walkThreshold * walkThreshold ? EnemyClip.Run : EnemyClip.Idle);
        }

        // 도는 클립(제자리걸음·달리기)을 한 칸 민다. 게임플레이가 정한 시간이 없으므로
        // 구워 둔 클립 길이로 돌린다 — 달리기는 0.867초라, 예전의 고정 1.4회/초는 21% 빨랐다.
        private void Loop(ref EnemyAnimation animation, EnemyClip clip)
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

            animation.normalizedTime = math.frac(animation.normalizedTime + deltaTime / ClipLength(clip));
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
            ref LocalTransform transform, in EnemyTarget target, in EnemyStats stats)
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
                    }
                }
            }

            EnterRecover(ref action, ref animation, stats);
        }

        // 물고 늘어지는 구간. 붙잡은 아군을 따라다니다가 끝에 한 번 크게 문다.
        //
        // 따라다니는 것이 요점이다. 제자리에 서서 물면 상대가 걸어 나가는 동안 허공을 물게
        // 되는데, 이 동작은 2초가 넘어서 그 어긋남이 그대로 보인다. 게임오브젝트 쪽은 목에
        // 매달려 해결했고(UpdateCling), 여기서는 발치에 붙어 따라간다.
        private void TickBite(Entity self, ref EnemyAction action, ref EnemyAnimation animation,
            ref LocalTransform transform, in EnemyTarget target, in EnemyStats stats)
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
                    float step = math.min(distance - stats.standoffDistance, stats.moveSpeed * deltaTime);
                    transform.Position += direction * step;
                }

                if (distance > 0.001f)
                {
                    quaternion facing = quaternion.LookRotationSafe(Flat(toAlly / distance), math.up());
                    transform.Rotation = math.slerp(transform.Rotation, facing,
                        math.saturate(stats.turnSpeed * deltaTime));
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
                            // 정작 경직이 그 면역에 막힌다(아군 쪽 ApplySkillDamage와 같은 규칙).
                            poiseDamage = 0f,
                            forceStaggerDuration = stats.biteStaggerDuration,
                            fromPosition = transform.Position,
                            source = self,
                            // 문 상대는 이 동작이 한 바퀴 돌 동안 다시 물리지 않는다.
                            skillVictimDuration = stats.biteCooldown,
                        });
                    }
                }
            }

            EnterRecover(ref action, ref animation, stats);
        }

        // 칼을 들어올린 구간. 끝나는 프레임에 딱 한 번 판정한다.
        private void TickWindup(Entity self, ref EnemyAction action, ref EnemyAnimation animation,
            in EnemyTarget target, in EnemyStats stats, in LocalTransform transform)
        {
            Advance(ref animation, action);

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
        private void Advance(ref EnemyAnimation animation, in EnemyAction action)
        {
            float length = math.max(0.01f, action.animationLength);
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

        private void Execute(Entity entity, ref LocalTransform transform, ref EnemyMotion motion,
            in EnemyStats stats, in EnemyTarget target, in EnemyAction action)
        {
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

            // 발이 묶여 있으면 그만큼 느리게 간다(창수의 부위 억제, 빙결 마법).
            // 가속에는 걸지 않는다 — 묶인 것은 다리이지 반응이 아니다.
            float moveSpeed = stats.moveSpeed * motion.SlowFactor(now);

            float3 wanted = math.normalizesafe(desired) * (holdsGround ? 0f : moveSpeed);
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
