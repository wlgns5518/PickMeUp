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
    }

    // 아군이 적을 때렸다. 메인 스레드에서 쌓고 ECS 쪽에서 꺼내 적용한다.
    public struct HitOnEnemy
    {
        public Entity enemy;
        public int damage;
        public float poiseDamage;
        public float3 fromPosition;

        // 퍼펙트 가드로 흘려낸 경우. 강인도와 무관하게 그 자리에서 무너뜨린다.
        public bool forceStagger;
        public float forceStaggerDuration;
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
    }

    public static BridgeData AsComponent() => new BridgeData
    {
        allies = AllyStates,
        enemies = EnemyStates,
        hitsOnAllies = HitsOnAllies,
        hitsOnEnemies = HitsOnEnemies,
    };

    // ---------------------------------------------------------------- 컨테이너

    public static NativeList<AllyState> AllyStates;
    public static NativeList<EnemyState> EnemyStates;
    public static NativeQueue<HitOnAlly> HitsOnAllies;
    public static NativeQueue<HitOnEnemy> HitsOnEnemies;

    public static bool IsReady { get; private set; }

    // 인덱스 ↔ UnitController. 관리 쪽에만 있고 잡에는 넘어가지 않는다.
    private static readonly List<UnitController> AllyByIndex = new List<UnitController>(32);
    private static readonly Dictionary<UnitController, int> IndexByAlly = new Dictionary<UnitController, int>(32);

    public static void Initialize()
    {
        if (IsReady) return;

        AllyStates = new NativeList<AllyState>(32, Allocator.Persistent);
        EnemyStates = new NativeList<EnemyState>(1024, Allocator.Persistent);
        HitsOnAllies = new NativeQueue<HitOnAlly>(Allocator.Persistent);
        HitsOnEnemies = new NativeQueue<HitOnEnemy>(Allocator.Persistent);
        IsReady = true;
    }

    public static void Dispose()
    {
        if (!IsReady) return;

        if (AllyStates.IsCreated) AllyStates.Dispose();
        if (EnemyStates.IsCreated) EnemyStates.Dispose();
        if (HitsOnAllies.IsCreated) HitsOnAllies.Dispose();
        if (HitsOnEnemies.IsCreated) HitsOnEnemies.Dispose();

        AllyByIndex.Clear();
        IndexByAlly.Clear();
        IsReady = false;
    }

    // ---------------------------------------------------------------- 아군 → 적 (매 프레임 갱신)

    // 아군 목록을 그대로 옮겨 적는다. 시뮬레이션 그룹보다 먼저 도는 시스템이 부른다.
    public static void PublishAllies(IReadOnlyList<UnitController> allies)
    {
        if (!IsReady) return;

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
    public static void DamageEnemy(Entity enemy, int damage, float poiseDamage, float3 fromPosition)
    {
        if (!IsReady || enemy == Entity.Null) return;

        HitsOnEnemies.Enqueue(new HitOnEnemy
        {
            enemy = enemy,
            damage = damage,
            poiseDamage = poiseDamage,
            fromPosition = fromPosition,
        });
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

    // 적이 아군을 때린 것을 실제 UnitController로 흘려보낸다. 메인 스레드에서만 부른다.
    public static void DrainHitsOnAllies()
    {
        if (!IsReady) return;

        while (HitsOnAllies.TryDequeue(out HitOnAlly hit))
        {
            UnitController ally = GetAlly(hit.allyIndex);
            if (ally == null || ally.IsDead) continue;

            ally.TakeEnemyDamage(hit.damage, hit.fromPosition, hit.source, hit.poiseDamage);
        }
    }
}
