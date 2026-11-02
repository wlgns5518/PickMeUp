using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// 전장 바닥 — 어디에 설 수 있고, 아군 한 명 한 명에게 어느 길로 가야 하는가.
//
// 적(엔티티)은 NavMesh를 모른다(EnemyMovementSystem). 맨 평지였을 때는 그래도 됐지만, 11층부터는 전장에
// 건물과 바리케이드가 선다(BattleTerrain). 그대로 두면 적이 벽을 뚫고 지나가고 길목이 아무 의미가 없다.
// 그래서 전투를 열 때 건물 바닥면을 격자 둘로 굽는다 — Burst 잡이 그대로 읽을 수 있는 모양으로.
//
//  1) 여유(Clearance) — 막힌 곳(건물·전장 밖)의 경계까지 거리(미터). 설 수 있는 칸은 양수, 건물 속은 음수다.
//  2) 흐름장(Flow)    — 아군 한 명마다, 그 아군까지 걸어서 가는 거리. 적은 이 값이 줄어드는 쪽으로 걷는다.
//                       건물을 돌아 가고, 벽에 난 틈(길목)으로 몰린다.
//
// 높낮이는 없다. 전장은 어느 층이든 평평한 y=0이다(BattleMapBuilder) — 2026-09-26 언덕을 들였다가
// 사용자 요청으로 평지로 되돌렸다. 지형지물은 바닥이 아니라 그 위에 선 건물로 낸다.
//
// 굽지 않은 상태(1~10층, 테스트 월드)는 "어디든 막힌 곳이 없다"다. 그래서 이 파일이 생기기 전의 적 동작과
// 정확히 같다 — 굽지 않은 판에서는 잡이 격자를 한 번도 읽지 않는다.
//
// 아군은 NavMeshAgent로 걷고 건물은 NavMeshObstacle로 깎으므로 이 격자가 필요 없다. 다만 "어디가 등을 기댈
// 만한 자리인가"를 판단할 때 같은 격자를 읽는다(TerrainTactics) — 적이 실제로 걷는 땅과 아군이 믿는 땅이
// 같아야 아군이 기댄 벽을 적이 뚫고 들어오지 않는다.
public static class BattleGround
{
    // 격자가 덮는 범위(중심에서 반폭). NavMesh를 굽는 상자(±40m)를 다 덮고 여유를 둔다.
    public const float HalfExtent = 46f;
    public const float CellSize = 0.5f;
    public const int Resolution = 184;
    public const int CellCount = Resolution * Resolution;

    // 흐름장은 더 굵게 잡는다. 1m 칸이면 전장이 8,464칸이라 한 명분을 다시 구하는 데 1ms가 안 든다.
    // 길을 가르는 것은 수 미터 폭의 건물이라 이 굵기로 충분하고, 모서리는 여유 격자가 떠민다.
    public const float FlowCellSize = 1f;
    public const int FlowResolution = 92;
    public const int FlowCellCount = FlowResolution * FlowResolution;

    // 흐름장을 들고 있는 아군 수. 편성은 다섯 자리다(PartyDeck.DefaultCapacity). 넘는 아군은 흐름장 없이
    // 곧장 다가가는 예전 방식으로 떨어진다.
    public const int MaxFlowTargets = 8;

    // 여유 값의 상한. 이보다 먼 벽은 걸음에 아무 영향이 없다.
    public const float MaxClearance = 6f;

    // 흐름장에서 "닿을 수 없음".
    public const float Unreachable = 1e9f;

    // 흐름장이 지나가려면 칸 중심이 벽에서 이만큼은 떨어져 있어야 한다. 적의 몸 반지름(0.5)보다 조금 작다 —
    // 1m 칸의 중심만 보므로, 몸 폭에 딱 맞추면 1m 폭 틈이 통째로 막힌 것으로 읽힌다.
    private const float FlowPassClearance = 0.35f;

    // 굽지 않았을 때의 여유 — 어디든 막힌 데가 없다.
    private const float OpenClearance = MaxClearance;

    // 여유 배열의 마지막 한 칸은 "구웠는가"다(1이면 구움). 잡에 넘기는 값을 하나 더 늘리지 않으려는 것뿐이다.
    private const int BuiltFlagIndex = CellCount;

    public static NativeArray<float> Clearance;
    public static NativeArray<float> Flow;

    // 흐름장 계산용 작업 공간(다익스트라의 힙). 전투마다 할당하지 않도록 한 번 잡아 둔다.
    private static NativeArray<float> heapKeys;
    private static NativeArray<int> heapCells;

    // 흐름장을 마지막으로 구한 목표 자리와 시각(아군 스냅샷 인덱스별).
    private static readonly Vector3[] FlowOrigins = new Vector3[MaxFlowTargets];
    private static readonly float[] FlowTimes = new float[MaxFlowTargets];
    private static readonly bool[] FlowValid = new bool[MaxFlowTargets];

    // 이만큼 움직였거나 이만큼 지났으면 그 아군의 흐름장을 다시 구한다.
    private const float FlowRebuildDistance = 0.75f;
    private const float FlowRebuildInterval = 1f;

    // 적이 태어나는 구역(BattleTerrain이 정한다). 굽지 않았으면 쓰지 않는다.
    public static Rect EnemyZone { get; private set; }

    public static bool IsAllocated => Clearance.IsCreated;

    // 굽혀 있는가(관리 쪽). 잡은 IsBuilt(clearance)로 묻는다.
    public static bool IsBuiltNow => IsAllocated && Clearance[BuiltFlagIndex] > 0f;

    // ---------------------------------------------------------------- 수명

    // 브리지와 함께 산다(EnemyWorldBridge.Initialize/Dispose). 전투 사이에 다시 잡지 않는다 —
    // 잡이 들고 있는 배열을 전투를 열 때마다 새로 만들면 싱글턴에 실린 손잡이와 엇갈린다.
    public static void Allocate()
    {
        if (IsAllocated) return;

        Clearance = new NativeArray<float>(CellCount + 1, Allocator.Persistent);
        Flow = new NativeArray<float>(MaxFlowTargets * FlowCellCount, Allocator.Persistent);
        heapKeys = new NativeArray<float>(FlowCellCount * 8, Allocator.Persistent);
        heapCells = new NativeArray<int>(FlowCellCount * 8, Allocator.Persistent);
        Reset();
    }

    public static void Dispose()
    {
        if (Clearance.IsCreated) Clearance.Dispose();
        if (Flow.IsCreated) Flow.Dispose();
        if (heapKeys.IsCreated) heapKeys.Dispose();
        if (heapCells.IsCreated) heapCells.Dispose();
    }

    // 막힘 없음으로 되돌린다. 전투를 닫을 때 부른다 — 다음 씬(마을·1~10층)에 지난 판의 건물이 남으면 안 된다.
    public static void Reset()
    {
        if (!IsAllocated) return;

        CompleteEnemyJobs();
        for (int i = 0; i < CellCount; i++) Clearance[i] = OpenClearance;
        Clearance[BuiltFlagIndex] = 0f;
        EnemyZone = default;

        for (int i = 0; i < MaxFlowTargets; i++) FlowValid[i] = false;
    }

    // ---------------------------------------------------------------- 굽기

    // 건물 하나의 바닥면. 돌아 앉은 사각형(xz)이다 — 광장을 둘러싼 집처럼 축에 맞지 않게 서는 건물이 있다.
    public struct Footprint
    {
        public Vector2 center;
        public Vector2 halfSize;
        // 로컬 x축이 월드에서 향하는 쪽(cos, sin). 월드 xz 기준.
        public Vector2 axisX;

        public Footprint(Vector2 center, Vector2 halfSize, float yawDegrees)
        {
            this.center = center;
            this.halfSize = halfSize;
            // 유니티의 y축 회전(시계 방향, 위에서 볼 때)에서 로컬 +X는 (cos, -sin)으로 간다.
            float radians = yawDegrees * Mathf.Deg2Rad;
            axisX = new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians));
        }

        // 바닥면을 덮는 축 정렬 사각형.
        public Rect Bounds
        {
            get
            {
                float ex = Mathf.Abs(axisX.x) * halfSize.x + Mathf.Abs(axisX.y) * halfSize.y;
                float ez = Mathf.Abs(axisX.y) * halfSize.x + Mathf.Abs(axisX.x) * halfSize.y;
                return new Rect(center.x - ex, center.y - ez, ex * 2f, ez * 2f);
            }
        }

        public bool Contains(Vector2 point)
        {
            Vector2 d = point - center;
            float lx = d.x * axisX.x + d.y * axisX.y;
            float lz = -d.x * axisX.y + d.y * axisX.x;
            return Mathf.Abs(lx) <= halfSize.x && Mathf.Abs(lz) <= halfSize.y;
        }
    }

    // 건물 바닥면과 걸을 수 있는 전장(arena)으로 격자를 굽는다. 적을 띄우기 전에 한 번(BattleTerrain).
    public static void Build(IReadOnlyList<Footprint> footprints, Rect arena, Rect enemyZone)
    {
        if (!IsAllocated) return;

        CompleteEnemyJobs();

        var walkable = new NativeArray<byte>(CellCount, Allocator.Temp);
        for (int z = 0; z < Resolution; z++)
        {
            for (int x = 0; x < Resolution; x++)
            {
                float3 center = CellCenter(x, z);
                walkable[z * Resolution + x] = (byte)(arena.Contains(new Vector2(center.x, center.z)) ? 1 : 0);
            }
        }

        foreach (Footprint footprint in footprints)
        {
            Rect rect = footprint.Bounds;
            int2 min = CellOf(new float3(rect.xMin, 0f, rect.yMin));
            int2 max = CellOf(new float3(rect.xMax, 0f, rect.yMax));
            for (int z = math.max(0, min.y); z <= math.min(Resolution - 1, max.y); z++)
            for (int x = math.max(0, min.x); x <= math.min(Resolution - 1, max.x); x++)
            {
                float3 center = CellCenter(x, z);
                if (footprint.Contains(new Vector2(center.x, center.z))) walkable[z * Resolution + x] = 0;
            }
        }

        BuildClearance(walkable);
        walkable.Dispose();

        EnemyZone = enemyZone;
        Clearance[BuiltFlagIndex] = 1f;
        for (int i = 0; i < MaxFlowTargets; i++) FlowValid[i] = false;
    }

    // 설 수 있는 칸에서 가장 가까운 막힌 칸까지, 막힌 칸에서 가장 가까운 설 수 있는 칸까지의 거리.
    // 두 번 훑는 챔퍼 거리 변환이다(대각선 √2) — 정확한 유클리드 거리와 최대 8% 어긋나지만 걸음에는 충분하다.
    private static void BuildClearance(NativeArray<byte> walkable)
    {
        var toBlocked = new NativeArray<float>(CellCount, Allocator.Temp);
        var toWalkable = new NativeArray<float>(CellCount, Allocator.Temp);
        for (int i = 0; i < CellCount; i++)
        {
            bool stand = walkable[i] != 0;
            toBlocked[i] = stand ? Unreachable : 0f;
            toWalkable[i] = stand ? 0f : Unreachable;
        }

        Chamfer(toBlocked);
        Chamfer(toWalkable);

        // 경계는 칸과 칸 사이에 있다. 막힌 칸 바로 옆 칸의 거리는 한 칸이지만 경계까지는 반 칸이다.
        float half = CellSize * 0.5f;
        for (int i = 0; i < CellCount; i++)
        {
            float value = walkable[i] != 0 ? toBlocked[i] - half : -(toWalkable[i] - half);
            Clearance[i] = math.clamp(value, -MaxClearance, MaxClearance);
        }

        toBlocked.Dispose();
        toWalkable.Dispose();
    }

    private static void Chamfer(NativeArray<float> field)
    {
        float a = CellSize;
        float b = CellSize * math.SQRT2;

        for (int z = 0; z < Resolution; z++)
        for (int x = 0; x < Resolution; x++)
        {
            int i = z * Resolution + x;
            float v = field[i];
            if (x > 0) v = math.min(v, field[i - 1] + a);
            if (z > 0)
            {
                v = math.min(v, field[i - Resolution] + a);
                if (x > 0) v = math.min(v, field[i - Resolution - 1] + b);
                if (x < Resolution - 1) v = math.min(v, field[i - Resolution + 1] + b);
            }
            field[i] = v;
        }

        for (int z = Resolution - 1; z >= 0; z--)
        for (int x = Resolution - 1; x >= 0; x--)
        {
            int i = z * Resolution + x;
            float v = field[i];
            if (x < Resolution - 1) v = math.min(v, field[i + 1] + a);
            if (z < Resolution - 1)
            {
                v = math.min(v, field[i + Resolution] + a);
                if (x < Resolution - 1) v = math.min(v, field[i + Resolution + 1] + b);
                if (x > 0) v = math.min(v, field[i + Resolution - 1] + b);
            }
            field[i] = v;
        }
    }

    // 굽거나 되돌리기 전에 이 배열을 읽는 잡을 전부 끝낸다.
    private static void CompleteEnemyJobs()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated) world.EntityManager.CompleteAllTrackedJobs();
    }

    // ---------------------------------------------------------------- 흐름장

    // 아군 스냅샷을 보고, 목표가 움직였거나 오래된 흐름장 하나를 다시 구한다. 한 프레임에 한 명분만 —
    // 다섯 명이 한꺼번에 움직여도 프레임 비용이 튀지 않게 한다. 적 시뮬레이션 앞(EnemyBridgeInputSystem)에서
    // 잡이 전부 끝난 뒤에 부른다.
    public static void UpdateFlowFields(NativeList<EnemyWorldBridge.AllyState> allies, float now)
    {
        if (!IsBuiltNow || !allies.IsCreated) return;

        int count = math.min(allies.Length, MaxFlowTargets);
        int pick = -1;
        float oldest = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            EnemyWorldBridge.AllyState ally = allies[i];
            if (ally.alive == 0) continue;

            bool stale = !FlowValid[i] ||
                         now - FlowTimes[i] >= FlowRebuildInterval ||
                         math.distancesq((float3)FlowOrigins[i], ally.position) >= FlowRebuildDistance * FlowRebuildDistance;
            if (!stale) continue;

            // 한 번도 안 구한 흐름장이 먼저다. 그다음은 가장 오래된 것.
            float age = FlowValid[i] ? FlowTimes[i] : float.MinValue;
            if (age >= oldest) continue;

            oldest = age;
            pick = i;
        }

        if (pick < 0) return;

        EnemyWorldBridge.AllyState target = allies[pick];
        new FlowFieldJob
        {
            clearance = Clearance,
            flow = Flow,
            heapKeys = heapKeys,
            heapCells = heapCells,
            slot = pick,
            target = target.position,
        }.Run();

        FlowOrigins[pick] = target.position;
        FlowTimes[pick] = now;
        FlowValid[pick] = true;
    }

    // 목표 칸에서 퍼져 나가는 다익스트라. 8방향, 모서리를 깎아 지나가지 않는다.
    [BurstCompile]
    private struct FlowFieldJob : IJob
    {
        [ReadOnly] public NativeArray<float> clearance;
        public NativeArray<float> flow;
        public NativeArray<float> heapKeys;
        public NativeArray<int> heapCells;
        public int slot;
        public float3 target;

        public void Execute()
        {
            int offset = slot * FlowCellCount;
            for (int i = 0; i < FlowCellCount; i++) flow[offset + i] = Unreachable;

            int start = NearestWalkableFlowCell(target);
            if (start < 0) return;

            int heapCount = 0;
            flow[offset + start] = 0f;
            Push(ref heapCount, 0f, start);

            while (heapCount > 0)
            {
                Pop(ref heapCount, out float distance, out int cell);
                if (distance > flow[offset + cell]) continue;

                int cx = cell % FlowResolution;
                int cz = cell / FlowResolution;

                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cx + dx;
                    int nz = cz + dz;
                    if (!FlowWalkable(nx, nz)) continue;
                    // 대각선은 양옆 두 칸이 다 열려 있어야 지난다. 건물 모서리를 비스듬히 뚫고 가지 않게.
                    if (dx != 0 && dz != 0 && (!FlowWalkable(cx + dx, cz) || !FlowWalkable(cx, cz + dz))) continue;

                    float step = (dx != 0 && dz != 0 ? math.SQRT2 : 1f) * FlowCellSize;
                    int neighbor = nz * FlowResolution + nx;
                    float next = distance + step;
                    if (next >= flow[offset + neighbor]) continue;

                    flow[offset + neighbor] = next;
                    Push(ref heapCount, next, neighbor);
                }
            }
        }

        private int NearestWalkableFlowCell(float3 position)
        {
            int2 cell = FlowCellOf(position);
            for (int radius = 0; radius <= 3; radius++)
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (math.max(math.abs(dx), math.abs(dz)) != radius) continue;
                int x = cell.x + dx;
                int z = cell.y + dz;
                if (FlowWalkable(x, z)) return z * FlowResolution + x;
            }
            return -1;
        }

        private bool FlowWalkable(int x, int z)
        {
            if (x < 0 || z < 0 || x >= FlowResolution || z >= FlowResolution) return false;
            return SampleClearance(clearance, FlowCellCenter(x, z)) > FlowPassClearance;
        }

        private void Push(ref int count, float key, int cell)
        {
            if (count >= heapKeys.Length) return;

            int i = count++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (heapKeys[parent] <= key) break;
                heapKeys[i] = heapKeys[parent];
                heapCells[i] = heapCells[parent];
                i = parent;
            }
            heapKeys[i] = key;
            heapCells[i] = cell;
        }

        private void Pop(ref int count, out float key, out int cell)
        {
            key = heapKeys[0];
            cell = heapCells[0];

            count--;
            if (count == 0) return;

            float lastKey = heapKeys[count];
            int lastCell = heapCells[count];
            int i = 0;
            while (true)
            {
                int child = i * 2 + 1;
                if (child >= count) break;
                if (child + 1 < count && heapKeys[child + 1] < heapKeys[child]) child++;
                if (heapKeys[child] >= lastKey) break;
                heapKeys[i] = heapKeys[child];
                heapCells[i] = heapCells[child];
                i = child;
            }
            heapKeys[i] = lastKey;
            heapCells[i] = lastCell;
        }
    }

    // 아군(스냅샷 인덱스 slot)에서 그 자리까지 걸어서 가는 거리. 흐름장이 없으면 -1, 닿을 수 없으면 Unreachable.
    // 아군이 "가까워 보이지만 벽 너머라 한참 돌아가야 하는 자리"를 거르는 데 쓴다(TerrainTactics).
    public static float WalkDistance(int slot, Vector3 position)
    {
        if (!IsBuiltNow || slot < 0 || slot >= MaxFlowTargets || !FlowValid[slot]) return -1f;

        int2 cell = FlowCellOf(position);
        if (cell.x < 0 || cell.y < 0 || cell.x >= FlowResolution || cell.y >= FlowResolution) return Unreachable;
        return Flow[slot * FlowCellCount + cell.y * FlowResolution + cell.x];
    }

    // ---------------------------------------------------------------- 잡에서 부르는 질의

    public static bool IsBuilt(NativeArray<float> clearance)
    {
        return clearance.IsCreated && clearance.Length > BuiltFlagIndex && clearance[BuiltFlagIndex] > 0f;
    }

    public static float3 CellCenter(int x, int z)
    {
        return new float3(-HalfExtent + (x + 0.5f) * CellSize, 0f, -HalfExtent + (z + 0.5f) * CellSize);
    }

    public static int2 CellOf(float3 position)
    {
        return new int2(
            (int)math.floor((position.x + HalfExtent) / CellSize),
            (int)math.floor((position.z + HalfExtent) / CellSize));
    }

    public static float3 FlowCellCenter(int x, int z)
    {
        return new float3(-HalfExtent + (x + 0.5f) * FlowCellSize, 0f, -HalfExtent + (z + 0.5f) * FlowCellSize);
    }

    public static int2 FlowCellOf(float3 position)
    {
        return new int2(
            (int)math.floor((position.x + HalfExtent) / FlowCellSize),
            (int)math.floor((position.z + HalfExtent) / FlowCellSize));
    }

    public static bool IsInside(float3 position)
    {
        return math.abs(position.x) < HalfExtent && math.abs(position.z) < HalfExtent;
    }

    // 칸 중심 사이를 선형 보간한 값. 격자 밖은 가장자리 값을 잇는다.
    private static float Bilinear(NativeArray<float> field, float3 position)
    {
        float fx = (position.x + HalfExtent) / CellSize - 0.5f;
        float fz = (position.z + HalfExtent) / CellSize - 0.5f;
        fx = math.clamp(fx, 0f, Resolution - 1.001f);
        fz = math.clamp(fz, 0f, Resolution - 1.001f);

        int x0 = (int)fx;
        int z0 = (int)fz;
        float tx = fx - x0;
        float tz = fz - z0;

        int i = z0 * Resolution + x0;
        float bottom = math.lerp(field[i], field[i + 1], tx);
        float top = math.lerp(field[i + Resolution], field[i + Resolution + 1], tx);
        return math.lerp(bottom, top, tz);
    }

    // 막힌 땅의 경계까지 거리. 굽지 않았으면 늘 넉넉하다.
    // 굽은 뒤 격자 밖이면 밖으로 나간 만큼 음수다 — 전장 밖은 설 수 없는 땅이다.
    public static float SampleClearance(NativeArray<float> clearance, float3 position)
    {
        if (!IsBuilt(clearance)) return OpenClearance;

        float value = Bilinear(clearance, position);
        float outside = math.max(math.abs(position.x), math.abs(position.z)) - HalfExtent;
        return outside > 0f ? math.min(value, -outside) : value;
    }

    public static float SampleClearance(Vector3 position) => SampleClearance(Clearance, position);

    // 여유가 커지는 쪽(막힌 곳에서 멀어지는 쪽). 트인 곳이면 0.
    public static float3 ClearanceNormal(NativeArray<float> clearance, float3 position)
    {
        if (!IsBuilt(clearance)) return float3.zero;

        // 격자 밖이면 가운데로 돌아온다.
        if (!IsInside(position)) return math.normalizesafe(new float3(-position.x, 0f, -position.z));

        float h = CellSize;
        float gx = SampleClearance(clearance, position + new float3(h, 0f, 0f)) -
                   SampleClearance(clearance, position - new float3(h, 0f, 0f));
        float gz = SampleClearance(clearance, position + new float3(0f, 0f, h)) -
                   SampleClearance(clearance, position - new float3(0f, 0f, h));
        return math.normalizesafe(new float3(gx, 0f, gz));
    }

    // 흐름장이 가리키는 쪽. 둘레 여덟 칸 중 목표까지 더 가까워지는 칸들을 줄어드는 만큼 섞는다 —
    // 가장 가까운 한 칸만 따르면 45도 계단을 밟으며 간다.
    public static bool TryFlowDirection(NativeArray<float> flow, int slot, float3 position, out float3 direction)
    {
        direction = float3.zero;
        if (!flow.IsCreated || slot < 0 || slot >= MaxFlowTargets) return false;

        int2 cell = FlowCellOf(position);
        if (cell.x < 0 || cell.y < 0 || cell.x >= FlowResolution || cell.y >= FlowResolution) return false;

        int offset = slot * FlowCellCount;
        float here = flow[offset + cell.y * FlowResolution + cell.x];

        // 서 있는 칸이 막혔거나 닿을 수 없는 칸이면(벽에 밀려 들어갔거나 흐름장이 아직 없다)
        // 둘레에서 가장 가까운 칸을 기준으로 삼는다.
        if (here >= Unreachable)
        {
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cell.x + dx, z = cell.y + dz;
                if (x < 0 || z < 0 || x >= FlowResolution || z >= FlowResolution) continue;
                here = math.min(here, flow[offset + z * FlowResolution + x] + math.length(new float2(dx, dz)) * FlowCellSize);
            }
            if (here >= Unreachable) return false;
        }

        float3 sum = float3.zero;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dz == 0) continue;
            int x = cell.x + dx, z = cell.y + dz;
            if (x < 0 || z < 0 || x >= FlowResolution || z >= FlowResolution) continue;

            float there = flow[offset + z * FlowResolution + x];
            if (there >= Unreachable) continue;

            float length = math.length(new float2(dx, dz));
            float gain = (here - there) / length;
            if (gain <= 0f) continue;

            sum += new float3(dx, 0f, dz) / length * gain;
        }

        direction = math.normalizesafe(sum);
        return math.lengthsq(direction) > 0f;
    }

    // 두 점 사이를 막힘 없이 걸어갈 수 있는가(여유가 margin보다 큰 채로). 반 칸 간격으로 짚어 본다.
    public static bool IsSegmentClear(NativeArray<float> clearance, float3 from, float3 to, float margin)
    {
        if (!IsBuilt(clearance)) return true;

        float3 delta = to - from;
        delta.y = 0f;
        float length = math.length(delta);
        int steps = math.max(1, (int)math.ceil(length / CellSize));
        for (int i = 1; i <= steps; i++)
        {
            float3 point = from + delta * (i / (float)steps);
            if (SampleClearance(clearance, point) < margin) return false;
        }
        return true;
    }
}
