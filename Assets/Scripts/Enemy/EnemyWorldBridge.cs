using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// 게임오브젝트로 남은 아군과, 엔티티가 된 적 사이의 유일한 통로.
//
// 두 세계가 서로를 직접 가리키지 않는 것이 이 파일의 요점이다. 적이 UnitController를 참조하면
// 그 순간 Burst가 꺼지고, 아군이 Entity를 들고 있으면 엔티티가 사라진 뒤의 참조를 매번
// 확인해야 한다. 그래서 양쪽 다 매 프레임 갱신되는 배열의 인덱스만 주고받는다.
//
// 한 프레임의 흐름:
//   1. EnemyBridgeInputSystem   아군 상태를 AllyStates에 적는다(메인 스레드)
//   2. 적 시뮬레이션 시스템들     AllyStates를 읽어 표적·이동·공격을 정한다(Burst 잡)
//   3. EnemyBridgeOutputSystem  적 상태를 EnemyStates에 적고, 밀린 피해를 양쪽으로 흘린다
//   4. 아군 UnitController.Update  EnemyStates를 읽어 평소대로 싸운다
//
// 인덱스는 그 프레임 안에서만 유효하다. 다음 프레임에 다시 만들어지므로, 어느 쪽도
// 인덱스를 프레임 너머로 들고 있으면 안 된다 — 들고 있어야 하는 것은 적의 Entity뿐이고
// 그건 EnemyState 안에 함께 실려 나간다.
public static class EnemyWorldBridge
{
    // ---------------------------------------------------------------- 주고받는 값

    public struct AllyState
    {
        public float3 position;
        public float3 forward;
        public float radius;
        public int hp;
        public int maxHp;

        // 적이 표적을 고를 때 쓰는 가중치. 탱커가 3.2, 사제가 0.3이다.
        public float threatWeight;

        // 이 아군을 이미 노리고 있는 적 수. 한 명에게 전부 몰리지 않게 하는 데 쓴다.
        public int attackerCount;

        public byte alive;

        // 붙잡는 스킬(물어뜯기)에 다시 당할 수 있는가. 아군 쪽 CanBeSkillVictim을 그대로 옮긴다 —
        // 이게 없으면 한 명에게 여럿이 동시에 물고 늘어져 그 자리에서 녹는다.
        public byte canBeBitten;
    }

    public struct EnemyState
    {
        // 아군이 이 적에게 피해를 돌려주려면 프레임을 넘겨 살아남는 손잡이가 필요하다.
        // 인덱스는 매 프레임 바뀌지만 Entity는 그렇지 않다.
        public Entity entity;

        public float3 position;
        public float3 forward;
        public float radius;
        public int hp;
        public int maxHp;

        // 남은 강인도. 아군이 "발차기 한 번이면 무너지는 구간인가"를 재는 데 쓴다
        // (UnitController.IsTargetPoiseRipe).
        public float poise;

        public float threatWeight;

        // 지금 노리고 있는 아군. 아군 쪽 "나를 쫓아오는 적이 있는가" 판단이 이걸 읽는다.
        public int targetAllyIndex;

        public EnemyActionKind action;

        // 칼을 들어올렸는가. 아군의 방어가 이 값 하나에 걸려 있다 —
        // 아군 쪽 UnitController.IsTelegraphing과 같은 뜻이다.
        public bool IsTelegraphing => action == EnemyActionKind.Windup;

        public bool IsAlive => hp > 0 && action != EnemyActionKind.Dead;
    }

    // 적이 아군을 때렸다. 메인 스레드에서 꺼내 UnitController.TakeEnemyDamage로 흘려보낸다.
    public struct HitOnAlly
    {
        public int allyIndex;
        public int damage;
        public float poiseDamage;
        public float3 fromPosition;

        // 때린 적. 아군이 흘려냈을 때(퍼펙트 가드) 그 자리에서 무너뜨리려면 손잡이가 필요하다.
        public Entity source;

        // 붙잡는 스킬로 문 경우. 물린 아군은 이 시간 동안 다시 물리지 않는다 —
        // 아군 쪽 MarkSkillVictim과 같은 규칙이고, 시간은 무는 쪽이 정한다.
        public float skillVictimDuration;
    }

    // 적 하나가 쓰러졌다. 누구에게 귀속시킬지만 담는다.
    public struct EnemyKill
    {
        public int allyIndex;
    }

    // 아군이 적을 때렸다. 메인 스레드에서 쌓고 ECS 쪽에서 꺼내 적용한다.
    public struct HitOnEnemy
    {
        public Entity enemy;
        public int damage;
        public float poiseDamage;
        public float3 fromPosition;

        // 때린 아군(스냅샷 인덱스). 이 적이 쓰러지면 이 아군의 처치로 센다.
        public int attackerAllyIndex;

        // 퍼펙트 가드로 흘려낸 경우. 강인도와 무관하게 그 자리에서 무너뜨린다.
        public bool forceStagger;
        public float forceStaggerDuration;

        // 발을 묶는다. 창수의 부위 억제와 빙결 마법이 이걸로 온다.
        // 피해와 따로 오는 경우가 있어(마법은 피해와 둔화를 따로 건다) 0이어도 처리한다.
        public float slowDuration;
        public float slowMultiplier;
    }

    // 잡에서 볼 수 있는 손잡이.
    //
    // 아래 정적 필드를 잡 안에서 그대로 읽을 수는 없다 — Burst는 관리 클래스의 정적 필드에
    // 접근하지 못한다. 그래서 같은 컨테이너를 싱글턴 컴포넌트에도 실어 두고, 시스템 쪽은
    // 이쪽으로만 본다. 컨테이너 자체는 하나이므로 두 경로가 같은 메모리를 가리킨다.
    public struct BridgeData : IComponentData
    {
        public NativeList<AllyState> allies;
        public NativeList<EnemyState> enemies;
        public NativeQueue<HitOnAlly> hitsOnAllies;
        public NativeQueue<HitOnEnemy> hitsOnEnemies;
        public NativeQueue<EnemyKill> kills;
    }

    public static BridgeData AsComponent() => new BridgeData
    {
        allies = AllyStates,
        enemies = EnemyStates,
        hitsOnAllies = HitsOnAllies,
        hitsOnEnemies = HitsOnEnemies,
        kills = Kills,
    };

    // ---------------------------------------------------------------- 컨테이너

    public static NativeList<AllyState> AllyStates;
    public static NativeList<EnemyState> EnemyStates;
    public static NativeQueue<HitOnAlly> HitsOnAllies;
    public static NativeQueue<HitOnEnemy> HitsOnEnemies;
    public static NativeQueue<EnemyKill> Kills;

    // 적 하나가 쓰러졌다. BattleManager가 이걸 듣고 처치 수를 센다 —
    // 게임오브젝트 쪽 UnitController.OnAnyUnitDied와 같은 자리다.
    public static event System.Action OnEnemyKilled;

    public static bool IsReady { get; private set; }

    // 인덱스 ↔ UnitController. 관리 쪽에만 있고 잡에는 넘어가지 않는다.
    private static readonly List<UnitController> AllyByIndex = new List<UnitController>(32);
    private static readonly Dictionary<UnitController, int> IndexByAlly = new Dictionary<UnitController, int>(32);

    // Entity → 스냅샷 인덱스. 아군이 들고 있는 표적 손잡이를 이번 프레임의 값으로 푸는 자리다.
    // 스냅샷은 매 프레임 다시 만들어지므로 이 표도 함께 다시 만든다.
    private static readonly Dictionary<Entity, int> IndexByEntity = new Dictionary<Entity, int>(1024);

    // 이 적을 이미 노리고 있는 아군 수. 표적을 고를 때 한 명에게 몰리지 않게 하는 데 쓴다.
    //
    // 엔티티에 세어 두지 않고 여기서 집계하는 이유는, 그 숫자를 쓰는 쪽이 아군(메인 스레드)뿐이라
    // 청크에 필드를 하나 더 얹어 1000마리분을 늘릴 이유가 없기 때문이다. 한 프레임 늦은 값이지만
    // 표적 선택은 원래 프레임 단위로 흔들리면 안 되는 판단이라(targetChangeInterval) 문제가 없다.
    private static readonly Dictionary<Entity, int> AllyAttackersByEntity = new Dictionary<Entity, int>(256);

    public static void Initialize()
    {
        if (IsReady) return;

        AllyStates = new NativeList<AllyState>(32, Allocator.Persistent);
        EnemyStates = new NativeList<EnemyState>(1024, Allocator.Persistent);
        HitsOnAllies = new NativeQueue<HitOnAlly>(Allocator.Persistent);
        HitsOnEnemies = new NativeQueue<HitOnEnemy>(Allocator.Persistent);
        Kills = new NativeQueue<EnemyKill>(Allocator.Persistent);
        IsReady = true;
    }

    public static void Dispose()
    {
        if (!IsReady) return;

        if (AllyStates.IsCreated) AllyStates.Dispose();
        if (EnemyStates.IsCreated) EnemyStates.Dispose();
        if (HitsOnAllies.IsCreated) HitsOnAllies.Dispose();
        if (HitsOnEnemies.IsCreated) HitsOnEnemies.Dispose();
        if (Kills.IsCreated) Kills.Dispose();

        AllyByIndex.Clear();
        IndexByAlly.Clear();
        IsReady = false;
    }

    // ---------------------------------------------------------------- 아군 → 적 (매 프레임 갱신)

    // 아군 목록을 그대로 옮겨 적는다. 시뮬레이션 그룹보다 먼저 도는 시스템이 부른다.
    public static void PublishAllies(IReadOnlyList<UnitController> allies)
    {
        if (!IsReady) return;

        // 아군이 들고 있는 표적을 먼저 센다. 이 값은 아군이 다음 표적을 고를 때 쓰인다.
        CountAllyAttackers(allies);

        AllyStates.Clear();
        AllyByIndex.Clear();
        IndexByAlly.Clear();

        for (int i = 0; i < allies.Count; i++)
        {
            UnitController ally = allies[i];
            if (ally == null || !ally.isActiveAndEnabled) continue;

            Transform t = ally.transform;
            UnitStats stats = ally.Stats;

            IndexByAlly[ally] = AllyByIndex.Count;
            AllyByIndex.Add(ally);

            AllyStates.Add(new AllyState
            {
                position = t.position,
                forward = t.forward,
                radius = 0.5f,
                hp = stats != null ? stats.currentHp : 0,
                maxHp = stats != null ? stats.maxHp : 1,
                threatWeight = stats != null ? stats.threatWeight : 1f,
                attackerCount = ally.AttackersFrom(UnitTeam.Enemy),
                alive = (byte)(ally.IsDead ? 0 : 1),
                canBeBitten = (byte)(ally.CanBeSkillVictim ? 1 : 0),
            });
        }
    }

    public static UnitController GetAlly(int index)
    {
        if (index < 0 || index >= AllyByIndex.Count) return null;
        return AllyByIndex[index];
    }

    public static int IndexOfAlly(UnitController ally)
    {
        if (ally == null) return -1;
        return IndexByAlly.TryGetValue(ally, out int index) ? index : -1;
    }

    // ---------------------------------------------------------------- 적 → 아군 (아군 쪽 질의)
    //
    // 아래가 아군 코드에게 보이는 "적 목록"이다. 예전 UnitRegistry.Enemies가 하던 자리인데,
    // 리스트가 아니라 값 배열이라 순회 비용이 훨씬 싸다.

    public static int EnemyCount => IsReady && EnemyStates.IsCreated ? EnemyStates.Length : 0;

    public static EnemyState GetEnemy(int index) => EnemyStates[index];

    // 적 스냅샷을 다 채운 뒤 부른다. 손잡이로 찾을 수 있게 표를 다시 세운다.
    public static void RebuildEnemyIndex()
    {
        IndexByEntity.Clear();
        if (!IsReady) return;

        for (int i = 0; i < EnemyStates.Length; i++) IndexByEntity[EnemyStates[i].entity] = i;
    }

    // 손잡이로 이번 프레임의 상태를 푼다. 이미 지워진 엔티티면 거짓.
    public static bool TryGetEnemy(Entity entity, out EnemyState state)
    {
        if (IsReady && entity != Entity.Null && IndexByEntity.TryGetValue(entity, out int index))
        {
            state = EnemyStates[index];
            return true;
        }

        state = default;
        return false;
    }

    public static bool IsEnemyAlive(Entity entity)
    {
        return TryGetEnemy(entity, out EnemyState state) && state.IsAlive;
    }

    // 이 적에게 이미 붙어 있는 아군 수(지난 프레임 집계).
    public static int AllyAttackersOn(Entity entity)
    {
        if (entity == Entity.Null) return 0;
        return AllyAttackersByEntity.TryGetValue(entity, out int count) ? count : 0;
    }

    public static bool HasLivingEnemy()
    {
        if (!IsReady) return false;

        for (int i = 0; i < EnemyStates.Length; i++)
        {
            if (EnemyStates[i].IsAlive) return true;
        }

        return false;
    }

    // 가장 가까운 적. 시야 판정(레이캐스트)은 부르는 쪽이 후보를 받고 나서 한다 —
    // 여기서 매번 쏘면 1000마리에서 그대로 무너진다.
    public static bool TryFindNearestEnemy(float3 from, float range, out int index)
    {
        index = -1;
        if (!IsReady) return false;

        float bestSqr = range * range;
        for (int i = 0; i < EnemyStates.Length; i++)
        {
            EnemyState enemy = EnemyStates[i];
            if (!enemy.IsAlive) continue;

            float sqr = math.distancesq(enemy.position, from);
            if (sqr > bestSqr) continue;

            bestSqr = sqr;
            index = i;
        }

        return index >= 0;
    }

    // 아군이 겨눌 적을 고른다. 거리만 보지 않고 이미 붙은 아군 수도 함께 본다 —
    // 그러지 않으면 파티 전원이 같은 한 마리에 달라붙고 나머지는 그 뒤에서 겉돈다.
    // 적 쪽 표적 선택(EnemyTargetingSystem.PickTargetJob)과 같은 모양의 점수다.
    public static bool TryFindBestEnemy(float3 from, float range, out Entity enemy)
    {
        enemy = Entity.Null;
        if (!IsReady) return false;

        float bestScore = float.MaxValue;
        float sqrRange = range * range;

        for (int i = 0; i < EnemyStates.Length; i++)
        {
            EnemyState state = EnemyStates[i];
            if (!state.IsAlive) continue;

            float sqr = math.distancesq(state.position, from);
            if (sqr > sqrRange) continue;

            float score = math.sqrt(sqr) + AllyAttackersOn(state.entity) * 1.5f;
            if (score >= bestScore) continue;

            bestScore = score;
            enemy = state.entity;
        }

        return enemy != Entity.Null;
    }

    // 아군이 지금 어느 적을 겨누고 있는지 세어 둔다. PublishAllies가 매 프레임 부른다.
    private static void CountAllyAttackers(IReadOnlyList<UnitController> allies)
    {
        AllyAttackersByEntity.Clear();

        for (int i = 0; i < allies.Count; i++)
        {
            UnitController ally = allies[i];
            if (ally == null || ally.IsDead) continue;

            Entity target = ally.CurrentTarget.Entity;
            if (target == Entity.Null) continue;

            AllyAttackersByEntity.TryGetValue(target, out int count);
            AllyAttackersByEntity[target] = count + 1;
        }
    }

    public static int CountEnemiesAround(float3 center, float radius)
    {
        if (!IsReady) return 0;

        int count = 0;
        float sqrRadius = radius * radius;
        for (int i = 0; i < EnemyStates.Length; i++)
        {
            EnemyState enemy = EnemyStates[i];
            if (!enemy.IsAlive) continue;
            if (math.distancesq(enemy.position, center) <= sqrRadius) count++;
        }

        return count;
    }

    // ---------------------------------------------------------------- 두 세계를 함께 훑는 질의
    //
    // 아래 셋은 답 하나를 돌려주지 않고 진행 중인 계산에 제 몫을 더한다.
    // 게임오브젝트로 남은 적과 엔티티가 된 적을 각자 따로 고른 뒤 합치면 규칙이 무너지기
    // 때문이다 — 무게중심은 마리 수가 적은 쪽이 과대평가되고, "가장 가까운 하나"는 세계마다
    // 하나씩 둘이 나온다. 그래서 UnitRegistry가 같은 누적값을 들고 양쪽을 이어서 훑는다.

    // 어느 지점 둘레에 있는 적을 목록에 더한다(UnitRegistry.FindEnemiesAround).
    //
    // 광역 마법이 실제로 때릴 상대를 고르는 자리다. 이게 없으면 마법사의 광역기가
    // 엔티티에게는 아무것도 하지 않는다 — 착탄은 하는데 맞는 놈이 하나도 없다.
    public static void AppendEnemiesAround(Vector3 center, float radius, List<Entity> results)
    {
        if (!IsReady || results == null) return;

        float sqrRadius = radius * radius;
        for (int i = 0; i < EnemyStates.Length; i++)
        {
            EnemyState enemy = EnemyStates[i];
            if (!enemy.IsAlive) continue;

            float3 offset = enemy.position - (float3)center;
            offset.y = 0f;
            if (math.lengthsq(offset) > sqrRadius) continue;

            results.Add(enemy.entity);
        }
    }

    // 어느 지점 둘레에 있는 적들의 자리를 합에 더한다(UnitRegistry.TryGetEnemyCentroidAround).
    public static void AccumulateCentroidAround(Vector3 center, float radius, ref Vector3 sum, ref int count)
    {
        if (!IsReady) return;

        float sqrRadius = radius * radius;
        for (int i = 0; i < EnemyStates.Length; i++)
        {
            EnemyState enemy = EnemyStates[i];
            if (!enemy.IsAlive) continue;

            // 높이는 빼고 잰다. 게임오브젝트 쪽과 같은 규칙이어야 두 합이 섞인다.
            float3 offset = enemy.position - (float3)center;
            offset.y = 0f;
            if (math.lengthsq(offset) > sqrRadius) continue;

            sum += (Vector3)enemy.position;
            count++;
        }
    }

    // 스윙 궤적(사거리 + 정면 부채꼴) 안에서 가장 가까운 적 하나를 고른다.
    // bestSqr에는 이미 지금까지의 최단거리가 들어 있고, 그보다 가까운 적을 찾았을 때만 덮는다
    // (UnitRegistry.FindEnemyInArc).
    public static void AccumulateEnemyInArc(Vector3 origin, Vector3 forward, float arcAngle,
        ref Entity best, ref float bestSqr)
    {
        if (!IsReady) return;

        float3 flatForward = new float3(forward.x, 0f, forward.z);
        bool hasForward = math.lengthsq(flatForward) > 0.0001f;
        if (hasForward) flatForward = math.normalize(flatForward);

        float minDot = math.cos(math.radians(math.clamp(arcAngle * 0.5f, 0f, 180f)));

        for (int i = 0; i < EnemyStates.Length; i++)
        {
            EnemyState enemy = EnemyStates[i];
            if (!enemy.IsAlive) continue;

            float3 toEnemy = enemy.position - (float3)origin;
            toEnemy.y = 0f;

            float sqr = math.lengthsq(toEnemy);
            if (sqr > bestSqr || sqr <= 0.0001f) continue;
            if (hasForward && math.dot(flatForward, toEnemy / math.sqrt(sqr)) < minDot) continue;

            bestSqr = sqr;
            best = enemy.entity;
        }
    }

    // 시야에 적이 없을 때 걸어갈 자리의 후보. 이미 누군가와 붙어 있는 적(전선)이 먼저이고,
    // 아무도 교전 중이 아니면 가장 가까운 적으로 떨어진다(UnitRegistry.FindRallyEnemy).
    public static void AccumulateRallyCandidates(Vector3 from, ref Entity engaged, ref float engagedSqr,
        ref Entity nearest, ref float nearestSqr)
    {
        if (!IsReady) return;

        for (int i = 0; i < EnemyStates.Length; i++)
        {
            EnemyState enemy = EnemyStates[i];
            if (!enemy.IsAlive) continue;

            float sqr = math.distancesq(enemy.position, (float3)from);
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = enemy.entity;
            }

            // 이 적이 누군가를 물고 있는가. 그 자리가 곧 전선이다.
            if (enemy.targetAllyIndex < 0) continue;
            if (sqr >= engagedSqr) continue;

            engagedSqr = sqr;
            engaged = enemy.entity;
        }
    }

    // 나를 향해 칼을 들어올린 적. 아군의 방어 판단이 이걸 읽는다
    // (예전 UnitRegistry.FindTelegraphingAttacker).
    public static bool TryFindTelegraphingAttacker(int allyIndex, float3 allyPosition, float reach, out int index)
    {
        index = -1;
        if (!IsReady || allyIndex < 0) return false;

        float bestSqr = reach * reach;
        for (int i = 0; i < EnemyStates.Length; i++)
        {
            EnemyState enemy = EnemyStates[i];
            if (!enemy.IsAlive || !enemy.IsTelegraphing) continue;
            if (enemy.targetAllyIndex != allyIndex) continue;

            float sqr = math.distancesq(enemy.position, allyPosition);
            if (sqr > bestSqr) continue;

            bestSqr = sqr;
            index = i;
        }

        return index >= 0;
    }

    // 나를 노리고 쫓아오는 적이 아직 붙어 있는가(예전 UnitRegistry.HasEnemyChasing).
    public static bool HasEnemyChasing(int allyIndex, float3 allyPosition, float range)
    {
        if (!IsReady || allyIndex < 0) return false;

        float sqrRange = range * range;
        for (int i = 0; i < EnemyStates.Length; i++)
        {
            EnemyState enemy = EnemyStates[i];
            if (!enemy.IsAlive || enemy.targetAllyIndex != allyIndex) continue;
            if (math.distancesq(enemy.position, allyPosition) <= sqrRange) return true;
        }

        return false;
    }

    // 싸움이 벌어지고 있는 자리. 시야에 적이 없을 때 걸어갈 곳을 찾는다
    // (예전 UnitRegistry.FindRallyEnemy).
    public static bool TryFindRallyEnemy(float3 from, out int index)
    {
        index = -1;
        if (!IsReady) return false;

        float bestSqr = float.MaxValue;
        for (int i = 0; i < EnemyStates.Length; i++)
        {
            EnemyState enemy = EnemyStates[i];
            if (!enemy.IsAlive || enemy.targetAllyIndex < 0) continue;

            float sqr = math.distancesq(enemy.position, from);
            if (sqr >= bestSqr) continue;

            bestSqr = sqr;
            index = i;
        }

        return index >= 0;
    }

    // ---------------------------------------------------------------- 피해 전달

    // 아군이 적을 때렸다. 실제 적용은 ECS 쪽 시스템이 한다.
    public static void DamageEnemy(Entity enemy, int damage, float poiseDamage, float3 fromPosition,
        UnitController attacker = null)
    {
        if (!IsReady || enemy == Entity.Null) return;

        HitsOnEnemies.Enqueue(new HitOnEnemy
        {
            enemy = enemy,
            damage = damage,
            poiseDamage = poiseDamage,
            fromPosition = fromPosition,
            attackerAllyIndex = IndexOfAlly(attacker),
        });
    }

    // 쓰러진 적을 때린 아군에게 처치로 얹는다. 메인 스레드에서만 부른다.
    //
    // 게임오브젝트 쪽 NotifyDeath가 하던 일과 같다 — 마지막으로 피해를 준 쪽에게 귀속시키고,
    // 전투 매니저가 들을 수 있게 알린다.
    public static void DrainKills()
    {
        if (!IsReady) return;

        while (Kills.TryDequeue(out EnemyKill kill))
        {
            UnitController killer = GetAlly(kill.allyIndex);
            if (killer != null) killer.CreditKill();

            OnEnemyKilled?.Invoke();
        }
    }

    // 아군이 흘려냈다(퍼펙트 가드). 피해 없이 그 자리에서 무너뜨린다 —
    // 아군 쪽 UnitController.Stagger가 하던 일을 적에게 거는 경로다.
    public static void StaggerEnemy(Entity enemy, float duration, float3 fromPosition)
    {
        if (!IsReady || enemy == Entity.Null || duration <= 0f) return;

        HitsOnEnemies.Enqueue(new HitOnEnemy
        {
            enemy = enemy,
            damage = 0,
            poiseDamage = 0f,
            fromPosition = fromPosition,
            forceStagger = true,
            forceStaggerDuration = duration,
        });
    }

    // 발을 묶는다. 창수가 찌른 부위를 억제하거나, 빙결 마법이 얼릴 때 부른다.
    //
    // 피해와 같은 큐로 보낸다. 아군 쪽에서는 TakeDamage와 ApplySlow가 따로 불리지만,
    // 여기서는 둘 다 "이 적에게 무언가를 건다"라 한 줄로 흘려보내는 편이 낫다 —
    // 큐를 하나 더 두면 그만큼 매 프레임 비우고 맞춰야 할 것이 늘어난다.
    public static void SlowEnemy(Entity enemy, float duration, float multiplier, float3 fromPosition)
    {
        if (!IsReady || enemy == Entity.Null) return;
        if (duration <= 0f || multiplier >= 1f) return;

        HitsOnEnemies.Enqueue(new HitOnEnemy
        {
            enemy = enemy,
            damage = 0,
            poiseDamage = 0f,
            fromPosition = fromPosition,
            attackerAllyIndex = -1,
            slowDuration = duration,
            slowMultiplier = multiplier,
        });
    }

    // 적이 아군을 때린 것을 실제 UnitController로 흘려보낸다. 메인 스레드에서만 부른다.
    public static void DrainHitsOnAllies()
    {
        if (!IsReady) return;

        while (HitsOnAllies.TryDequeue(out HitOnAlly hit))
        {
            UnitController ally = GetAlly(hit.allyIndex);
            if (ally == null || ally.IsDead) continue;

            ally.TakeEnemyDamage(hit.damage, hit.fromPosition, hit.source, hit.poiseDamage);

            // 물린 아군에게 면역 시간을 건다. 피해보다 먼저 걸면 안 된다 —
            // 이 한 대로 쓰러지는 경우까지 포함해 "맞고 나서" 세는 것이 맞다.
            if (hit.skillVictimDuration > 0f) ally.MarkSkillVictim(hit.skillVictimDuration);
        }
    }
}
