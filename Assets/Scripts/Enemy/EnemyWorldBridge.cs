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

        // 이 아군에게 동시에 칼을 들 수 있는 적 수(공격 슬롯). 0이면 기본값을 쓴다
        // (EnemyAttackSlotSystem.DefaultSlotsPerAlly). 몸으로 막는 직군일수록 넓다(UnitStats.enemyAttackSlots).
        public byte attackSlots;

        // 그림자 속에 있는가(암살자 은신)와, 그래도 들키는 거리. 적은 이 거리 밖의 은신한 아군을 겨누지 못하고
        // 겨누던 것도 놓는다 — 아군 쪽 UnitRegistry.IsHiddenFrom과 같은 규칙이다. 0이면 코앞에서도 안 보인다.
        public byte hidden;
        public float revealRange;
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

        // 덤벼들거나 달라붙어 아직 한 방을 넣지 않았다(도약의 웅크림~공중, 물기의 이빨이 박히기 전).
        public byte lungeOrBiteIncoming;

        // 칼을 들어올렸는가. 아군의 방어가 이 값 하나에 걸려 있다 —
        // 아군 쪽 UnitController.IsTelegraphing과 같은 뜻이다.
        //
        // 도약과 물기도 든다. 예전에는 칼의 준비 동작만 셌기에 둘은 누구도 막을 수 없었다 — 실측(12층, 5인 90초):
        // 막기를 우선한 뒤에도 남은 피해의 절반이 물려서 굳은(1.5초) 사이에 들어왔다. 웅크렸다 뛰는 몸과 달라붙는
        // 몸은 칼보다 더 잘 보인다. 막아 낸 물기는 굳히지 못한다(DrainHitsOnAllies).
        public bool IsTelegraphing => action == EnemyActionKind.Windup || lungeOrBiteIncoming != 0;

        public bool IsAlive => hp > 0 && action != EnemyActionKind.Dead;
    }

    // 살에 칼이 닿은 자리. 메인 스레드가 꺼내 피를 뿌린다.
    //
    // 피해 적용은 Burst 잡 안에서 일어나는데 파티클은 관리 객체라 그 자리에서 만들 수 없다.
    // 그래서 자리만 큐에 남기고 뿌리는 것은 바깥에서 한다 — 피해가 큐를 건너가는 것과 같은 이유다.
    public struct BloodOnEnemy
    {
        public float3 position;
        public float3 fromPosition;
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

        // 물린 아군을 그 자리에서 굳혀 두는 시간(초). 0이면 경직시키지 않는다.
        // 강인도를 깎아 깨뜨리는 것과 다르다 — 이쪽은 강인도와 무관하게 무너뜨린다.
        public float forceStaggerDuration;
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

        // 칼이 닿은 순간 맞은 쪽이 멈칫하는 시간과 그동안의 배속. 때린 아군의 수치를 싣는다 —
        // 무거운 무기에 맞은 놈이 더 오래 멈춰야 하므로 맞는 쪽이 아니라 때린 쪽이 정한다.
        public float hitStopDuration;
        public float hitStopScale;

        // 이 한 방의 무게. 1이 평타이고 콤보 마무리·스킬·실드 배시가 더 크다. 0은 1로 친다.
        // 넉백 거리에 곱해진다(히트스톱 시간에는 보내는 쪽이 이미 곱해서 싣는다).
        public float impactWeight;

        // 때린 아군의 급소 타격 확률(암살자, UnitStats.bleedChanceOnHit). 뒤에서 그었으면 두 배다 —
        // 게임오브젝트 적은 맞는 쪽(ApplyOnHitDebuffs)이 때린 쪽 스탯을 직접 읽었는데, 엔티티는 때린
        // UnitController를 볼 수 없어서 보내는 쪽이 실어 온다.
        public float bleedChance;

        // 스킬(강타)로 맞았는가. 강타에는 맞는 쪽의 출혈 확률(EnemyEmotionProfile.bleedChanceOnSkillHit)이 따로 붙는다.
        public bool fromSkill;

        // 출혈 한 틱이다(EnemyEmotionSystem). 살에 새로 닿은 것이 아니라 흐르던 피라서 움찔·넉백·멈칫·
        // 피 튀김·맞받아치기·공포가 전부 없고 HP만 깎인다 — 아군 쪽 TakeBleedDamage와 같다.
        public bool bleed;
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
        public NativeQueue<BloodOnEnemy> bloodOnEnemies;
    }

    public static BridgeData AsComponent() => new BridgeData
    {
        allies = AllyStates,
        enemies = EnemyStates,
        hitsOnAllies = HitsOnAllies,
        hitsOnEnemies = HitsOnEnemies,
        kills = Kills,
        bloodOnEnemies = BloodOnEnemies,
    };

    // ---------------------------------------------------------------- 컨테이너

    public static NativeList<AllyState> AllyStates;
    public static NativeList<EnemyState> EnemyStates;
    public static NativeQueue<HitOnAlly> HitsOnAllies;
    public static NativeQueue<HitOnEnemy> HitsOnEnemies;
    public static NativeQueue<EnemyKill> Kills;
    public static NativeQueue<BloodOnEnemy> BloodOnEnemies;

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
        BloodOnEnemies = new NativeQueue<BloodOnEnemy>(Allocator.Persistent);
        ClearSnapshotLookups();
        IsReady = true;
    }

    // 스냅샷에서 파생된 표들. 스냅샷과 함께 살고 죽어야 한다.
    //
    // 특히 생존 수가 그렇다. 전투를 닫았다 새로 여는 사이에 지난 판의 값이 남아 있으면
    // HasLivingEnemy가 아무도 없는 맵에서 참을 돌려준다 — 아군 전원이 없는 적을 찾아
    // 헤매고 전투가 끝나지 않는다(UnitRegistry.ResetOnPlay가 같은 이유로 있다).
    private static void ClearSnapshotLookups()
    {
        IndexByEntity.Clear();
        AllyAttackersByEntity.Clear();
        aliveEnemyCount = 0;
        aliveEnemyHp = 0f;
        totalEnemyMaxHp = 0f;
    }

    public static void Dispose()
    {
        if (!IsReady) return;

        if (AllyStates.IsCreated) AllyStates.Dispose();
        if (EnemyStates.IsCreated) EnemyStates.Dispose();
        if (HitsOnAllies.IsCreated) HitsOnAllies.Dispose();
        if (HitsOnEnemies.IsCreated) HitsOnEnemies.Dispose();
        if (Kills.IsCreated) Kills.Dispose();
        if (BloodOnEnemies.IsCreated) BloodOnEnemies.Dispose();

        AllyByIndex.Clear();
        IndexByAlly.Clear();
        EntityAttackersByAlly.Clear();
        ClearSnapshotLookups();
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
                // 엔티티 적은 AddAttacker를 부르지 않으므로(TargetRef.AddAttacker 주석) 지난 프레임에
                // 적 스냅샷을 내보내며 센 수를 더한다. 이 합이 빠져 있던 동안 표적 점수의 "이미 붙은 수"
                // 항이 늘 0이라, 고블린 전원이 어그로 가중치대로 탱커 한 명에게만 몰렸다.
                attackerCount = ally.AttackersFrom(UnitTeam.Enemy) + EntityAttackersOn(ally),
                alive = (byte)(ally.IsDead ? 0 : 1),
                canBeBitten = (byte)(ally.CanBeSkillVictim ? 1 : 0),
                attackSlots = stats != null ? (byte)Mathf.Clamp(stats.enemyAttackSlots, 0, 255) : (byte)0,
                hidden = (byte)(ally.IsStealthed ? 1 : 0),
                revealRange = stats != null ? stats.stealthRevealRange : 0f,
            });
        }
    }

    // 이 아군을 겨누고 있는 엔티티 적 수(지난 프레임 집계). 적 스냅샷을 내보낼 때 함께 센다.
    private static readonly Dictionary<UnitController, int> EntityAttackersByAlly = new Dictionary<UnitController, int>(32);

    private static int EntityAttackersOn(UnitController ally)
    {
        return EntityAttackersByAlly.TryGetValue(ally, out int count) ? count : 0;
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

    // 살아 있는 적 수와 체력 합. 아래 루프가 어차피 전부 도는 김에 함께 센다.
    private static int aliveEnemyCount;
    private static float aliveEnemyHp;
    private static float totalEnemyMaxHp;

    // 적 스냅샷을 다 채운 뒤 부른다. 손잡이로 찾을 수 있게 표를 다시 세운다.
    public static void RebuildEnemyIndex()
    {
        IndexByEntity.Clear();
        aliveEnemyCount = 0;
        aliveEnemyHp = 0f;
        totalEnemyMaxHp = 0f;
        if (!IsReady) return;

        for (int i = 0; i < EnemyStates.Length; i++)
        {
            EnemyState enemy = EnemyStates[i];
            IndexByEntity[enemy.entity] = i;
            totalEnemyMaxHp += Mathf.Max(0f, enemy.maxHp);
            if (!enemy.IsAlive) continue;

            aliveEnemyCount++;
            aliveEnemyHp += Mathf.Max(0f, enemy.hp);
        }
    }

    // 적이 누구를 겨누는지 아군 쪽으로 옮겨 센다. 인덱스는 이번 프레임에만 유효하므로
    // UnitController 참조로 바꿔 들고, 다음 프레임의 아군 스냅샷에 싣는다(PublishAllies).
    public static void CountEntityAttackers()
    {
        EntityAttackersByAlly.Clear();
        if (!IsReady) return;

        for (int i = 0; i < EnemyStates.Length; i++)
        {
            EnemyState enemy = EnemyStates[i];
            if (!enemy.IsAlive) continue;

            UnitController ally = GetAlly(enemy.targetAllyIndex);
            if (ally == null) continue;

            EntityAttackersByAlly.TryGetValue(ally, out int count);
            EntityAttackersByAlly[ally] = count + 1;
        }
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

    // 엔티티가 된 적의 체력 합. 화면의 적 체력바가 팀 전체를 하나로 보여 주므로
    // 게임오브젝트 쪽 합과 그대로 더하면 된다(UI/EnemyHealthBar).
    //
    // 체력바가 매 프레임 묻는 값이라 여기서 다시 훑지 않는다. 표를 세울 때 함께 센 값이다.
    public static void SumEnemyHealth(out float current, out float max, out int alive)
    {
        bool ready = IsReady;
        current = ready ? aliveEnemyHp : 0f;
        max = ready ? totalEnemyMaxHp : 0f;
        alive = ready ? aliveEnemyCount : 0;
    }

    // 표를 세울 때 함께 센 값이라 훑지 않는다. 겨눌 상대가 없는 아군이 매 틱 묻는 질문이다.
    public static bool HasLivingEnemy() => IsReady && aliveEnemyCount > 0;

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

    // 파티 집중 표적의 후보(UnitRegistry.PickFocusTarget). 아군이 이미 붙어 있는 적 중 가장 많이 깎인 놈과,
    // 전선에서 가장 가까운 놈. 같은 누적값을 받아 더 나은 후보가 있을 때만 덮는다.
    public static void AccumulateFocusCandidates(Vector3 origin, ref Entity engaged, ref float bestRatio,
        ref Entity nearest, ref float nearestSqr)
    {
        if (!IsReady) return;

        for (int i = 0; i < EnemyStates.Length; i++)
        {
            EnemyState enemy = EnemyStates[i];
            if (!enemy.IsAlive) continue;

            float sqr = math.distancesq(enemy.position, (float3)origin);
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = enemy.entity;
            }

            if (AllyAttackersOn(enemy.entity) <= 0) continue;

            float ratio = enemy.maxHp > 0 ? enemy.hp / (float)enemy.maxHp : 0f;
            if (ratio >= bestRatio) continue;

            bestRatio = ratio;
            engaged = enemy.entity;
        }
    }

    // 이 아군의 간격 안까지 들어온 적(TargetScanner.ClosestPressuringEnemy). 이 아군을 노리고 쫓아온 놈과,
    // 그냥 가장 가까운 놈을 따로 남긴다 — 둘 다 이미 들어온 최단거리보다 가까울 때만 덮는다.
    public static void AccumulatePressure(Vector3 origin, float radius, int allyIndex,
        ref Entity chasing, ref float chasingSqr, ref Entity nearest, ref float nearestSqr)
    {
        if (!IsReady) return;

        float radiusSqr = radius * radius;
        for (int i = 0; i < EnemyStates.Length; i++)
        {
            EnemyState enemy = EnemyStates[i];
            if (!enemy.IsAlive) continue;

            float3 offset = enemy.position - (float3)origin;
            offset.y = 0f;
            float sqr = math.lengthsq(offset);
            if (sqr > radiusSqr) continue;

            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = enemy.entity;
            }

            if (allyIndex < 0 || enemy.targetAllyIndex != allyIndex || sqr >= chasingSqr) continue;

            chasingSqr = sqr;
            chasing = enemy.entity;
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

    // ---------------------------------------------------------------- 피해 전달

    // 아군이 적을 때렸다. 실제 적용은 ECS 쪽 시스템이 한다.
    //
    // 때린 쪽 직군이 남기는 흔적(출혈·부위 억제)도 여기서 함께 싣는다. 게임오브젝트 적은 맞는 쪽이
    // 때린 UnitController의 스탯을 직접 읽어 걸었는데(UnitController.ApplyOnHitDebuffs), 엔티티는 그
    // 참조를 볼 수 없어서 이 둘이 통째로 빠져 있었다 — 암살자의 출혈도 창수의 발 묶기도 고블린에게는 없었다.
    public static void DamageEnemy(Entity enemy, int damage, float poiseDamage, float3 fromPosition,
        UnitController attacker = null, float impactWeight = 1f, bool fromSkill = false)
    {
        if (!IsReady || enemy == Entity.Null) return;

        UnitStats stats = attacker != null ? attacker.Stats : null;
        bool slows = stats != null && stats.slowOnHitDuration > 0f && stats.slowOnHitMultiplier < 1f;

        HitsOnEnemies.Enqueue(new HitOnEnemy
        {
            enemy = enemy,
            damage = damage,
            poiseDamage = poiseDamage,
            fromPosition = fromPosition,
            attackerAllyIndex = IndexOfAlly(attacker),
            hitStopDuration = stats != null ? stats.hitStopDuration * impactWeight : 0f,
            hitStopScale = stats != null ? stats.hitStopScale : 1f,
            impactWeight = impactWeight,
            slowDuration = slows ? stats.slowOnHitDuration : 0f,
            slowMultiplier = slows ? stats.slowOnHitMultiplier : 1f,
            bleedChance = stats != null ? stats.bleedChanceOnHit : 0f,
            fromSkill = fromSkill,
        });
    }

    // 이 아군을 겨누고 있는 엔티티 적 수(지난 프레임 집계). 사제가 보호막 걸 사람을 고를 때
    // 게임오브젝트 적 수(AttackersFrom)와 더해 쓴다.
    public static int EntityAttackersOnAlly(UnitController ally)
    {
        return ally != null ? EntityAttackersOn(ally) : 0;
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
    public static void StaggerEnemy(Entity enemy, float duration, float3 fromPosition,
        float hitStopDuration = 0f, float hitStopScale = 1f)
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
            hitStopDuration = hitStopDuration,
            hitStopScale = hitStopScale,
            impactWeight = 1f,
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

    private const float PinImpactWeight = 1.5f;
    private const float PinShake = 0.5f;

    // 적이 아군을 때린 것을 실제 UnitController로 흘려보낸다. 메인 스레드에서만 부른다.
    public static void DrainHitsOnAllies()
    {
        if (!IsReady) return;

        while (HitsOnAllies.TryDequeue(out HitOnAlly hit))
        {
            UnitController ally = GetAlly(hit.allyIndex);
            if (ally == null || ally.IsDead) continue;

            // 붙잡아 무는 한 방은 평타보다 무겁다 — 더 오래 멈칫하고 더 밀린다.
            // 다만 방패·무기를 든 쪽으로 물고 들어왔으면 이빨이 막힌 것이라 굳지 않는다(막는 것이 먼저다).
            // 막은 만큼의 피해 감소는 평소 방어처럼 TakeDamage가 한다.
            bool pins = hit.forceStaggerDuration > 0f && !ally.IsGuardingAgainst(hit.fromPosition);
            ally.TakeEnemyDamage(hit.damage, hit.fromPosition, hit.source, hit.poiseDamage,
                pins ? PinImpactWeight : 1f);

            // 물린 아군은 그 자리에서 굳는다. 물어뜯기는 피해로 잡는 수가 아니라 한 명을
            // 판에서 빼는 수라, 이 경직이 빠지면 "물려도 그냥 계속 싸우는" 그림이 된다.
            //
            // 피해 뒤에 부르는 것이 맞다. 아군 쪽 ResolveSkillHit도 TakeDamage 다음에
            // TryForceStagger를 부르고, 흘려낸(퍼펙트 가드) 경우에는 무는 쪽이 대신 무너지므로
            // TryForceStagger가 스스로 면역을 보고 물러난다.
            //
            // 동료 하나가 판에서 빠지는 순간이라 화면에도 남긴다. 실제로 무너뜨렸을 때만이다.
            if (pins && ally.TryForceStagger(hit.forceStaggerDuration))
            {
                CombatImpulse.Emit(ally, PinShake);
            }

            // 물린 아군에게 면역 시간을 건다. 피해보다 먼저 걸면 안 된다 —
            // 이 한 대로 쓰러지는 경우까지 포함해 "맞고 나서" 세는 것이 맞다.
            if (hit.skillVictimDuration > 0f) ally.MarkSkillVictim(hit.skillVictimDuration);
        }
    }
}
