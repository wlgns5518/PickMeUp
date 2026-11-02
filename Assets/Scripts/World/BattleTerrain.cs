using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Random = System.Random;

// 11층부터 전장에 건물과 바리케이드를 세운다. 1~10층은 손대지 않는다(2026-10-31 사용자 요청).
//
// 씬에 굽지 않고 전투를 열 때마다 세운다. 층마다 배치가 달라야 하고(같은 구간의 다섯 층이 한 씬을 쓴다),
// 층 씬은 테마 생성기(BattleMapBuilder)가 통째로 다시 만들기도 해서 손으로 놓은 건물이 남지 않는다.
// 마을 주민(VillageResidents)처럼 씬이 열리면 스스로 붙는다 — 층 씬 20개를 열어 저장할 일이 없다.
//
// 세운 건물은 세 곳에 알린다.
//  1) 아군 — NavMeshObstacle(깎기)로 NavMesh에 구멍을 낸다. 아군은 NavMeshAgent로 걸으므로 그것으로 끝이다.
//  2) 적   — BattleGround에 바닥면을 굽는다. 적(엔티티)은 그 격자의 흐름장을 따라 건물을 돌아 길목으로 몰린다.
//  3) 카메라 — 아군을 가리는 건물은 그림자만 남기고 숨긴다(LateUpdate). 전투 카메라는 6m 높이에서 36도로
//            내려다봐서, 8~10m 집 하나가 파티 전체를 덮는다.
//
// 배치는 네 가지 꼴을 구간마다 돌려 쓴다. 꼴은 "어디에 길목이 생기는가"로 나눴다.
//  관문   — 망루를 이은 성벽에 4m 남짓한 문이 두세 개. 적은 문으로만 넘어온다.
//  마을   — 집이 두 줄로 늘어서 골목이 생긴다. 앞줄 골목이 길목이고, 두 줄 사이가 뒷길이다.
//  바리케이드 — 허리 높이 수레·장작·울타리 줄. 길은 막지만 시야는 트여 있어 창·활이 너머로 닿는다.
//  광장   — 파티가 집과 성벽으로 둘러싸인 광장에서 시작한다. 들어오는 입구가 셋뿐이다.
// 꼴을 세운 뒤 남은 땅은 시가지(FillDistrict)로 채운다 — 길목만 있으면 빈 들판의 무대 소품처럼 보였다.
//
// 좌표: 전투 카메라는 -X를 내려다본다(화면 위 = -X, 오른쪽 = +Z). 그래서 아군을 +X에, 적을 -X에 두어
// 적이 화면 위에서 내려오게 한다. 건물 대부분이 파티 너머(-X)에 서므로 카메라를 가리는 일이 적다.
public class BattleTerrain : MonoBehaviour
{
    public const int FirstFloor = 11;

    // NavMesh를 굽는 상자(±40m) 안쪽. 이 밖은 걸을 수 없는 땅으로 친다.
    private const float ArenaHalf = 39.5f;

    // 아군·적이 처음 서는 자리를 비워 두는 반경. 아군은 다섯 명이 고리로 서서 지름 약 6m다.
    private const float AllyClearRadius = 7f;
    private const float EnemyClearRadius = 6f;

    // 바닥면을 렌더러 범위보다 이만큼 좁힌다 — 지붕 처마는 땅을 막지 않는다. 그래도 이보다 얇게는 안 깎는다
    // (울타리 두께 0.2m를 그대로 쓰면 1m 칸 흐름장이 틈으로 읽는다).
    private const float FootprintInset = 0.3f;
    private const float MinFootprintHalf = 0.4f;

    // 이보다 높은 건물만 카메라를 가리는지 본다. 수레·장작(1.6m 아래)은 사람 머리보다 낮다.
    private const float OccluderMinHeight = 2.5f;

    // 줄지어 세우는 고리(집 줄·바리케이드 줄·광장 둘레)의 안전장치. 조각 하나가 범위를 못 재면(렌더러 없음)
    // 자리가 앞으로 나가지 않아 씬을 여는 순간 에디터가 멈춘다 — 2026-10-31 실제로 한 번 멈췄다.
    private const int MaxRowSteps = 400;
    private const float MinRowStep = 0.5f;

    // 광장 둘레 조각 사이 틈(도)과, 자투리를 메울 때 바리케이드를 줄이는 하한.
    private const float PlazaPieceGapDegrees = 0.5f;
    private const float PlazaMinShrink = 0.45f;

    private enum Layout
    {
        Gate,
        Village,
        Barricade,
        Plaza,
    }

    private sealed class Occluder
    {
        public Bounds bounds;
        public Renderer[] renderers;
        public bool hidden;
    }

    // 길목 하나 — 문·골목·바리케이드 틈·광장 입구. 아군이 막아설 자리다(TerrainPart의 방어 진지).
    // position은 길목 입구의 아군 쪽 끝, outward는 적이 들어오는 쪽(바깥), width는 틈의 폭이다.
    public struct DefensePost
    {
        public Vector3 position;
        public Vector3 outward;
        public float width;
    }

    private static readonly List<DefensePost> posts = new List<DefensePost>();

    // 이번 판의 길목들. 건물이 없는 판(1~10층)에는 비어 있다.
    public static IReadOnlyList<DefensePost> Posts => posts;

    // 파티가 지킬 길목. 아군 출발 자리에서 가깝고 적이 몰려오는 쪽을 향한 것이다.
    public static int MainPost { get; private set; } = -1;

    private BattleBuildingSet set;
    private Random random;
    private Transform root;
    private readonly List<BattleGround.Footprint> footprints = new List<BattleGround.Footprint>();
    private readonly List<Occluder> occluders = new List<Occluder>();
    private readonly List<Vector4> reserved = new List<Vector4>(); // xyz = 중심, w = 반경

    private Vector3 allyStart;
    private Vector3 enemyStart;
    private Rect enemyZone;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // 씬 오브젝트의 Awake 뒤, Start 앞이다. 스포너(CharacterBattleSpawner.Start)가 진영을 세우기 전에
    // 건물과 격자와 출발 자리가 다 서 있어야 한다.
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var spawner = FindAnyObjectByType<CharacterBattleSpawner>();
        if (spawner == null) return;

        int floor = Mathf.Clamp(FloorProgress.SelectedFloor, FloorProgress.FirstFloor, FloorProgress.LastFloor);
        if (floor < FirstFloor) return;

        var set = Resources.Load<BattleBuildingSet>(BattleBuildingSet.ResourcePath);
        if (set == null)
        {
            Debug.LogWarning("[BattleTerrain] Resources/BattleBuildingSet이 없어 전장에 건물을 세우지 않는다.");
            return;
        }

        var terrain = new GameObject("[BattleTerrain]").AddComponent<BattleTerrain>();
        SceneManager.MoveGameObjectToScene(terrain.gameObject, scene);
        terrain.Build(set, floor, spawner);
    }

    // 지금 격자와 길목을 쥐고 있는 판. 층에서 층으로 곧장 넘어가면 새 판이 세워진 뒤에 옛 판이 지워질 수 있어,
    // 옛 판의 정리가 새 판의 격자를 지우지 않게 주인만 정리한다.
    private static BattleTerrain owner;

    private void OnDestroy()
    {
        if (owner != this) return;
        owner = null;
        posts.Clear();
        MainPost = -1;
        // 다음 씬(마을·1~10층)에 이 판의 건물이 남지 않게 막힘 없음으로 되돌린다.
        BattleGround.Reset();
    }

    // ---------------------------------------------------------------- 세우기

    private void Build(BattleBuildingSet buildingSet, int floor, CharacterBattleSpawner spawner)
    {
        owner = this;
        set = buildingSet;
        random = new Random(floor * 7919 + 101);
        root = transform;
        posts.Clear();
        MainPost = -1;

        Layout layout = LayoutFor(floor);
        switch (layout)
        {
            case Layout.Gate: BuildGate(); break;
            case Layout.Village: BuildVillage(); break;
            case Layout.Barricade: BuildBarricade(); break;
            default: BuildPlaza(); break;
        }

        MainPost = PickMainPost();
        spawner.Relocate(allyStart, enemyStart);

        var arena = new Rect(-ArenaHalf, -ArenaHalf, ArenaHalf * 2f, ArenaHalf * 2f);
        BattleGround.Build(footprints, arena, enemyZone);

        Debug.Log($"[BattleTerrain] {floor}층 · {layout} — 바닥면 {footprints.Count}개, 가림 검사 {occluders.Count}개");
    }

    // 구간(다섯 층)마다 꼴을 돌린다. 11~15 관문, 16~20 마을, 21~25 바리케이드, 26~30 광장, 31~35 다시 관문 …
    // 같은 구간 안에서는 꼴이 같고 문·골목 자리만 층마다 다르다(씨앗이 층이다).
    private static Layout LayoutFor(int floor)
    {
        int stage = (floor - FirstFloor) / FloorProgress.FloorsPerStage;
        return (Layout)(stage % 4);
    }

    private void AddPost(Vector3 position, Vector3 outward, float width)
    {
        outward.y = 0f;
        posts.Add(new DefensePost { position = position, outward = outward.normalized, width = width });
    }

    // 파티가 지킬 길목 — 아군 출발 자리에서 가깝고, 적 출발 자리 쪽을 바라보는 것.
    private int PickMainPost()
    {
        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < posts.Count; i++)
        {
            float score = Vector3.Distance(posts[i].position, allyStart) + 0.5f * Vector3.Distance(posts[i].position, enemyStart);
            if (score >= bestScore) continue;
            bestScore = score;
            best = i;
        }
        return best;
    }

    private void SetStarts(Vector3 ally, Vector3 enemy, float enemyFrontX)
    {
        allyStart = ally;
        enemyStart = enemy;
        enemyZone = Rect.MinMaxRect(-ArenaHalf + 1f, -ArenaHalf + 1f, enemyFrontX, ArenaHalf - 1f);
        reserved.Add(new Vector4(ally.x, 0f, ally.z, AllyClearRadius));
        reserved.Add(new Vector4(enemy.x, 0f, enemy.z, EnemyClearRadius));
    }

    // 관문 — 전장을 가로지르는 성벽. 8m 칸 열 개 중 두세 칸을 비워 문을 낸다. 문 양옆 망루가 폭을 4m 남짓으로 좁힌다.
    private void BuildGate()
    {
        float wallX = Range(-1f, 2f);
        SetStarts(new Vector3(wallX + 11f, 0f, Range(-4f, 4f)), new Vector3(wallX - 20f, 0f, Range(-8f, 8f)), wallX - 6f);

        const int slots = 10;
        float slotLength = 80f / slots;
        var gates = new HashSet<int>();
        // 하나는 가운데 가까이(파티 앞), 나머지는 흩어 둔다. 붙은 두 칸을 비우면 문이 아니라 뚫린 벽이다.
        gates.Add(random.Next(4, 6));
        int extra = random.Next(1, 3);
        for (int tries = 0; gates.Count < 1 + extra && tries < 20; tries++)
        {
            int slot = random.Next(1, slots - 1);
            if (gates.Contains(slot) || gates.Contains(slot - 1) || gates.Contains(slot + 1)) continue;
            gates.Add(slot);
        }

        for (int i = 0; i < slots; i++)
        {
            float z = -40f + slotLength * (i + 0.5f);
            if (!gates.Contains(i)) Place(set.wallSegment, new Vector3(wallX, 0f, z), 90f, set.wallScale);
        }
        for (int i = 0; i <= slots; i++)
            Place(set.wallTower, new Vector3(wallX, 0f, -40f + slotLength * i), 0f, set.wallScale);

        // 문 안쪽(아군 쪽) 양옆에 화로 — 어디가 문인지 멀리서 보인다. 문 입구가 지킬 자리다.
        float towerHalf = PrefabBounds(set.wallTower).extents.x * set.wallScale;
        foreach (int gate in gates)
        {
            float z = -40f + slotLength * (gate + 0.5f);
            float width = slotLength - towerHalf * 2f;
            AddPost(new Vector3(wallX + towerHalf + 0.4f, 0f, z), Vector3.left, width);
            PlaceLight(new Vector3(wallX + towerHalf + 2.2f, 0f, z + width * 0.5f + 1.3f));
            PlaceLight(new Vector3(wallX + towerHalf + 2.2f, 0f, z - width * 0.5f - 1.3f));
        }

        // 성벽 안은 빽빽한 성안 마을, 밖은 듬성한 변두리. 성벽 양옆 6m는 비워 문 앞이 늘 트여 있게.
        // 시가지가 먼저다 — 흩뿌린 소품이 먼저 서면 그 둘레 골목 폭 때문에 집이 들어설 칸이 없다.
        districtClear.Add(Rect.MinMaxRect(wallX - 6f, -ArenaHalf, wallX + 6f, ArenaHalf));
        FillDistrict(wallX, 0.85f, 0.45f, 0.55f);

        // 아군 쪽: 등을 기댈 바리케이드 몇 개.
        ScatterBarricades(4, wallX + 5f, wallX + 14f, -22f, 22f);

        // 적 쪽: 폐허. 성벽 너머로 몰려오는 길을 몇 갈래로 나눈다.
        ScatterRuins(5, -34f, wallX - 9f);
    }

    // 마을 — 집 두 줄. 앞줄(아군 쪽) 골목이 길목이고, 뒷줄은 반 칸 어긋나게 세워 골목이 곧게 뚫리지 않는다.
    private void BuildVillage()
    {
        float frontX = Range(0f, 3f);
        float backX = frontX - Range(13f, 16f);

        // 두 줄은 시작을 어긋나게 세운다 — 앞줄 골목 너머가 뒷줄 집 벽이라 곧게 뚫린 길이 없다.
        var streets = new List<Vector2>();
        HouseRow(frontX, -ArenaHalf - Range(0f, 4f), streets);
        HouseRow(backX, -ArenaHalf - Range(4f, 10f), null);

        // 파티는 가운데에 가장 가까운 골목 앞에서 시작한다.
        float mainZ = 0f;
        float nearest = float.MaxValue;
        foreach (Vector2 street in streets)
        {
            if (Mathf.Abs(street.x) >= nearest) continue;
            nearest = Mathf.Abs(street.x);
            mainZ = street.x;
        }
        SetStarts(new Vector3(frontX + 12f, 0f, mainZ), new Vector3(backX - 12f, 0f, Range(-8f, 8f)), backX - 6f);

        // 앞줄 골목 어귀(아군 쪽 끝)가 지킬 자리다. 집 두께가 6m 남짓이라 줄 중심에서 3.5m 앞이다.
        foreach (Vector2 street in streets)
            AddPost(new Vector3(frontX + 3.5f, 0f, street.x), Vector3.left, street.y);

        // 두 줄과 그 사이 뒷길은 그대로 두고, 그 밖을 동네로 채운다.
        districtClear.Add(Rect.MinMaxRect(backX - 5f, -ArenaHalf, frontX + 5f, ArenaHalf));
        FillDistrict(frontX, 0.8f, 0.7f, 0.5f);

        // 골목 어귀를 반쯤 막은 수레·상자. 골목이 한 사람 폭으로 좁아지는 자리가 생긴다.
        ScatterBarricades(5, backX + 3f, frontX - 3f, -30f, 30f);
        ScatterBarricades(3, frontX + 5f, frontX + 12f, -20f, 20f);
        ScatterRuins(3, -34f, backX - 6f);
    }

    // 한 줄로 늘어선 집. 긴 쪽이 Z를 따라 서고, 집 사이(4.2~6.5m)가 골목이다.
    // streets가 있으면 집 사이 골목(z 중심, 폭)을 담는다.
    private void HouseRow(float x, float startZ, List<Vector2> streets)
    {
        float z = startZ;
        float lastEnd = float.NaN;
        for (int guard = 0; z < ArenaHalf && guard < MaxRowSteps; guard++)
        {
            GameObject prefab = Pick(set.houses);
            float scale = 1f;
            if (prefab == null)
            {
                prefab = set.wallSegment;
                scale = set.wallScale;
            }

            GameObject house = Place(prefab, new Vector3(x, 0f, z), random.Next(2) == 0 ? 90f : 270f, scale, alignStart: true);
            if (house == null) break;

            Bounds b = BoundsOf(house);
            if (streets != null && !float.IsNaN(lastEnd))
                streets.Add(new Vector2((lastEnd + b.min.z) * 0.5f, b.min.z - lastEnd));
            lastEnd = Mathf.Max(b.max.z, z + MinRowStep);
            z = lastEnd + Range(4.2f, 6.5f);
        }
    }

    // 바리케이드 — 허리 높이 줄 하나로 전장을 가른다. 틈은 셋에서 넷, 하나는 파티 정면이다.
    private void BuildBarricade()
    {
        float lineX = Range(0f, 2f);
        SetStarts(new Vector3(lineX + 10f, 0f, Range(-3f, 3f)), new Vector3(lineX - 20f, 0f, Range(-8f, 8f)), lineX - 6f);

        var gaps = new List<float> { Range(-3f, 3f) };
        int count = random.Next(2, 4);
        for (int tries = 0; gaps.Count < count + 1 && tries < 30; tries++)
        {
            float g = Range(-30f, 30f);
            bool far = true;
            foreach (float other in gaps) far &= Mathf.Abs(other - g) > 12f;
            if (far) gaps.Add(g);
        }

        const float gapWidth = 3.6f;
        float z = -ArenaHalf;
        for (int guard = 0; z < ArenaHalf && guard < MaxRowSteps; guard++)
        {
            float gapHere = float.NaN;
            foreach (float g in gaps)
                if (z + 0.5f > g - gapWidth * 0.5f && z < g + gapWidth * 0.5f) gapHere = g;
            if (!float.IsNaN(gapHere))
            {
                z = gapHere + gapWidth * 0.5f;
                continue;
            }

            // 울타리 칸을 기본으로, 사이사이 수레·장작더미를 끼운다.
            bool fence = set.fence != null && random.NextDouble() < 0.55;
            GameObject prefab = fence ? set.fence : Pick(set.barricades);
            if (prefab == null) break;

            // 울타리는 긴 쪽이 이미 Z다. 나머지는 긴 쪽이 Z를 따르게 돌린다.
            float yaw = fence ? 0f : LongSideAlongZYaw(prefab);
            GameObject piece = Place(prefab, new Vector3(lineX + Range(-0.3f, 0.3f), 0f, z), yaw, 1f, alignStart: true);
            if (piece == null) break;

            // 다음 조각이 다음 틈을 넘어서지 않게, 조각이 틈을 덮었으면 걷어 낸다.
            Bounds b = BoundsOf(piece);
            bool blocksGap = false;
            foreach (float g in gaps) blocksGap |= b.max.z > g - gapWidth * 0.5f && b.min.z < g + gapWidth * 0.5f;
            if (blocksGap)
            {
                RemoveLast(piece);
                z += 0.5f;
                continue;
            }
            z = Mathf.Max(b.max.z, z + MinRowStep);
        }

        // 틈 양옆에 화로. 틈 어귀가 지킬 자리다.
        foreach (float g in gaps)
        {
            AddPost(new Vector3(lineX + 1.4f, 0f, g), Vector3.left, gapWidth);
            PlaceLight(new Vector3(lineX + 2.6f, 0f, g + gapWidth * 0.5f + 1.3f));
            PlaceLight(new Vector3(lineX + 2.6f, 0f, g - gapWidth * 0.5f - 1.3f));
        }

        // 마을 어귀에 급히 친 방어선 — 뒤는 동네, 앞은 수레와 짐이 널린 바깥 길.
        districtClear.Add(Rect.MinMaxRect(lineX - 6f, -ArenaHalf, lineX + 6f, ArenaHalf));
        FillDistrict(lineX, 0.7f, 0.45f, 0.65f);

        ScatterBarricades(3, lineX + 5f, lineX + 13f, -24f, 24f);
        ScatterRuins(6, -34f, lineX - 8f);
    }

    // 광장 — 파티를 둘러싼 집과 성벽. 입구는 셋: 적 쪽(-X)으로 둘, 옆으로 하나.
    private void BuildPlaza()
    {
        var center = new Vector3(Range(6f, 10f), 0f, Range(-4f, 4f));
        float radius = Range(13f, 15f);
        // 파티는 광장 한가운데에서 입구까지 10m 남짓 걸어가야 한다. 적이 먼저 닿지 않게 멀찍이 세운다.
        float enemyX = Mathf.Max(-ArenaHalf + 8f, center.x - radius - 22f);
        SetStarts(center, new Vector3(enemyX, 0f, Range(-8f, 8f)), center.x - radius - 6f);

        // 각도는 +X에서 +Z 쪽으로 잰다(위에서 볼 때 반시계).
        var openings = new List<float>
        {
            180f + Range(25f, 40f),
            180f - Range(25f, 40f),
            (random.Next(2) == 0 ? 90f : 270f) + Range(-15f, 15f),
        };
        const float openingWidth = 4.5f;

        float halfOpening = openingWidth * 0.5f / radius * Mathf.Rad2Deg;
        float angle = 0f;
        for (int guard = 0; angle < 360f && guard < MaxRowSteps; guard++)
        {
            float openingAt = float.NaN;
            foreach (float o in openings)
                if (Mathf.Abs(Mathf.DeltaAngle(angle, o)) < halfOpening + 0.02f) openingAt = o;
            if (!float.IsNaN(openingAt))
            {
                angle = openingAt + halfOpening + 0.5f;
                continue;
            }

            // 다음 입구(없으면 한 바퀴 끝)까지 남은 호. 그 안에 들어가는 조각만 고른다 — 아무거나 뽑아 넣어 보고
            // 밀어내면 입구 말고도 몇 m짜리 구멍이 남는다.
            float room = 360f - angle;
            foreach (float o in openings)
            {
                float gap = o - halfOpening - angle;
                if (gap > 0.01f && gap < room) room = gap;
            }
            float roomLength = (room - PlazaPieceGapDegrees) * Mathf.Deg2Rad * radius;

            // 적 쪽 반원은 집, 카메라 쪽(+X) 반원은 낮은 성벽(망루 없이) — 카메라 쪽에 집을 세우면 광장을 덮는다.
            bool enemySide = Mathf.Cos(angle * Mathf.Deg2Rad) < 0.2f;
            GameObject prefab = null;
            float scale = 1f;
            if (enemySide)
            {
                prefab = PickFitting(set.houses, roomLength);
            }
            else if (set.wallSegment != null && LongSide(set.wallSegment) * set.wallScale <= roomLength)
            {
                prefab = set.wallSegment;
                scale = set.wallScale;
            }
            if (prefab == null) prefab = PickFitting(set.barricades, roomLength);
            if (prefab == null && set.fence != null && LongSide(set.fence) <= roomLength) prefab = set.fence;

            // 가장 짧은 바리케이드도 안 들어가는 자투리 — 줄여서라도 막는다. 그보다 좁으면 고블린도 못 지나간다.
            if (prefab == null)
            {
                GameObject shortest = Shortest(set.barricades) ?? set.fence;
                if (shortest == null) break;
                float shrink = roomLength / LongSide(shortest);
                if (shrink < PlazaMinShrink)
                {
                    // 입구 가장자리에 딱 맞춰 서면 위의 입구 검사가 놓치고 0에 가까운 걸음으로 맴돈다 — 조금 더 들어간다.
                    angle += room + 0.01f;
                    continue;
                }
                prefab = shortest;
                scale = shrink;
            }

            float span = LongSide(prefab) * scale / radius * Mathf.Rad2Deg;
            float mid = angle + span * 0.5f + PlazaPieceGapDegrees;

            float rad = mid * Mathf.Deg2Rad;
            var position = center + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radius;
            // 긴 쪽(로컬 X)이 원의 접선을 따르게. 접선 방향의 월드 각도에서 유니티 yaw로 옮긴다.
            float tangentYaw = -(mid + 90f) + YawOfLongSide(prefab);
            Place(prefab, position, tangentYaw, scale, reserve: false);
            angle = mid + span * 0.5f;
        }

        // 입구 안쪽 어귀가 지킬 자리다. 화로는 어귀 옆에 — 한가운데 두면 입구를 막는다.
        foreach (float o in openings)
        {
            float rad = o * Mathf.Deg2Rad;
            var outward = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            var side = new Vector3(-outward.z, 0f, outward.x);
            AddPost(center + outward * (radius - 2.5f), outward, openingWidth);
            PlaceLight(center + outward * (radius - 4f) + side * (openingWidth * 0.5f + 1.3f));
        }

        // 광장을 둘러싼 시가지. 둘레 바깥 5m는 비워 입구 앞이 막히지 않게.
        districtClearCircles.Add(new Vector4(center.x, 0f, center.z, radius + 5f));
        FillDistrict(center.x - radius, 0.85f, 0.6f, 0.5f);

        ScatterRuins(6, -34f, center.x - radius - 8f);
        ScatterBarricades(4, center.x - radius - 8f, center.x - radius - 2f, -30f, 30f);
    }

    // ---------------------------------------------------------------- 시가지

    // 길목 꼴만 세우면 빈 들판 한가운데 성벽 한 줄·집 몇 채가 덩그러니 선다(2026-11-02 사용자: "너무 단편적, 더 넓게").
    // 꼴을 세운 뒤 전장 끝을 집으로 두르고, 남은 땅을 한두세 채짜리 동과 마당(수레·장작·울타리)으로 채운다.
    //
    // 비우는 곳: 길목 꼴의 둘레(DistrictClear), 출발 자리, 적 집결지, 적 집결지에서 길목마다 이어지는 큰길,
    // 카메라와 파티 사이(낮은 소품만). 그래야 길목이 여전히 싸움의 중심이다.
    //
    // 막힌 주머니가 생기면 거기서 태어난 적이 영영 못 나와 전투가 끝나지 않는다. 그래서 시가지 조각은 다른 모든
    // 바닥면·전장 끝과 축 정렬 상자 기준으로 틈을 둔다 — 서로 떨어진 볼록한 덩어리들의 바깥은 늘 한 덩어리다.
    private const int DistrictHouseAttempts = 500;
    private const int DistrictLooseYards = 14;
    private const float DistrictStreet = 3.5f;   // 집과 집 사이 — 골목
    private const float DistrictYardGap = 2f;    // 마당 소품 둘레. 고블린 둘이 비켜 지나갈 폭
    private const int DistrictMaxTerrace = 3;    // 한 동에 덧붙이는 집 수의 상한(배타) — 동은 한 채에서 세 채
    private const float TerraceJoint = 0.2f;     // 맞붙인 집 사이. 0이면 벽이 겹쳐 깜박인다
    private const float DistrictLaneHalfWidth = 3.5f;
    private const float EnemyMusterRadius = 11f;
    private const float CameraClearHalfWidth = 12f;

    private readonly List<Rect> districtClear = new List<Rect>();
    private readonly List<Vector4> districtClearCircles = new List<Vector4>(); // xyz = 중심, w = 반경
    private readonly List<Vector4> districtLanes = new List<Vector4>();        // (ax, az, bx, bz)

    // frontX: 이보다 +X는 아군 쪽. 아군 쪽은 파티가 등을 기대는 동네, 적 쪽은 몰려오는 무리가 갈라지는 변두리.
    private void FillDistrict(float frontX, float allyHouseChance, float enemyHouseChance, float yardChance)
    {
        districtClearCircles.Add(new Vector4(enemyStart.x, 0f, enemyStart.z, EnemyMusterRadius));
        districtClearCircles.Add(new Vector4(allyStart.x, 0f, allyStart.z, AllyClearRadius + 3f));
        foreach (DefensePost post in posts)
        {
            Vector3 mouth = post.position + post.outward * 6f;
            districtLanes.Add(new Vector4(enemyStart.x, enemyStart.z, mouth.x, mouth.z));
        }

        // 카메라는 파티 뒤(+X)에서 내려다본다. 그 앞에 집을 세우면 숨기기(LateUpdate) 전에 화면 절반이 지붕이다.
        Rect cameraClear = Rect.MinMaxRect(allyStart.x - 4f, allyStart.z - CameraClearHalfWidth,
            ArenaHalf, allyStart.z + CameraClearHalfWidth);

        // 전장 끝 바깥을 집으로 두른다. 걸을 수 있는 땅 밖이라 길을 막지 않고, 들판 끝이 아니라 마을 속 한 구역처럼
        // 보이게 한다. 카메라 뒤(+X)는 보이지 않아 두르지 않는다.
        const float overhang = 8f;
        PerimeterRow(new Vector3(-ArenaHalf, 0f, -ArenaHalf - overhang), new Vector3(-ArenaHalf, 0f, ArenaHalf + overhang), Vector3.left);
        PerimeterRow(new Vector3(-ArenaHalf - overhang, 0f, ArenaHalf), new Vector3(ArenaHalf + overhang, 0f, ArenaHalf), Vector3.forward);
        PerimeterRow(new Vector3(-ArenaHalf - overhang, 0f, -ArenaHalf), new Vector3(ArenaHalf + overhang, 0f, -ArenaHalf), Vector3.back);

        // 안쪽은 아무 데나 던져 보고 들어가는 자리에만 세운다. 11m 칸에 한 채씩 세웠더니 성벽 옆 칸·전장 끝 칸이
        // 통째로 빠져 13층 관문에 집이 여섯 채뿐이었다(2026-11-02). 집을 다 세운 뒤에 마당을 깐다 — 함께 세우면
        // 앞서 깐 수레가 다음 집의 골목 폭 안에 들어와 집이 서지 못한다.
        var yards = new List<Bounds>();
        for (int attempt = 0; attempt < DistrictHouseAttempts; attempt++)
        {
            var p = new Vector3(Range(-ArenaHalf + 4f, ArenaHalf - 4f), 0f, Range(-ArenaHalf + 4f, ArenaHalf - 4f));
            float chance = p.x > frontX ? allyHouseChance : enemyHouseChance;
            if (random.NextDouble() >= chance) continue;
            if (cameraClear.Contains(new Vector2(p.x, p.z))) continue;

            GameObject first = Pick(set.houses);
            float yaw = random.Next(4) * 90f;
            int blockStart = footprints.Count;
            GameObject house = TryPlaceDistrict(first, p, yaw, 1f, DistrictStreet);
            if (house == null) continue;

            Bounds block = BoundsOf(house);
            ExtendTerrace(first, yaw, blockStart, ref block);
            if (random.NextDouble() < yardChance) yards.Add(block);
        }

        // 카메라 앞을 포함해 빈 땅 군데군데에도 짐 더미.
        int looseYards = Mathf.RoundToInt(DistrictLooseYards * yardChance);
        for (int i = 0; i < looseYards; i++)
            yards.Add(new Bounds(new Vector3(Range(-ArenaHalf + 4f, ArenaHalf - 4f), 0f, Range(-ArenaHalf + 4f, ArenaHalf - 4f)),
                new Vector3(2f, 0f, 2f)));

        foreach (Bounds yard in yards) Yard(yard);
    }

    // 첫 집의 긴 쪽 방향으로 이웃집을 한두 채 맞붙여 한 동을 만든다. 골목 폭은 동과 동 사이에만 둔다 — 집마다 두면
    // 집 한 채가 사방 3.5m씩 땅을 차지해 전장에 일고여덟 채밖에 못 들어간다. 한 줄로 붙인 동은 구멍이 없어서
    // 동 바깥이 한 덩어리라는 약속은 그대로다.
    private void ExtendTerrace(GameObject first, float firstYaw, int blockStart, ref Bounds block)
    {
        bool alongX = block.size.x >= block.size.z;
        Vector3 axis = alongX ? Vector3.right : Vector3.forward;
        float sign = random.Next(2) == 0 ? 1f : -1f;
        int extra = random.Next(0, DistrictMaxTerrace);
        float longYaw = firstYaw - YawOfLongSide(first);

        for (int i = 0; i < extra; i++)
        {
            GameObject next = Pick(set.houses);
            if (next == null) return;
            float yaw = longYaw + YawOfLongSide(next) + (random.Next(2) == 0 ? 0f : 180f);

            // 돌린 뒤의 범위를 미리 계산해 지금 동의 끝에 맞붙는 자리를 구한다. Place는 세우는 순간 바닥면을 굽기 때문에
            // 세운 뒤 옮길 수 없다.
            Bounds local = PrefabBounds(next);
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 centerOffset = rotation * Vector3.Scale(local.center, next.transform.localScale);
            Vector3 size = rotation * Vector3.Scale(local.size, next.transform.localScale);
            Vector3 extents = new Vector3(Mathf.Abs(size.x), 0f, Mathf.Abs(size.z)) * 0.5f;

            float blockEdge = sign > 0f ? Vector3.Dot(block.max, axis) : Vector3.Dot(block.min, axis);
            float nextExtent = Vector3.Dot(extents, axis);
            Vector3 target = block.center;
            target += axis * (blockEdge + sign * (TerraceJoint + nextExtent) - Vector3.Dot(block.center, axis));
            // 앞면을 동의 한쪽 면에 맞춘다 — 크기가 다른 집이 가운데로만 맞으면 들쭉날쭉하다.
            Vector3 across = alongX ? Vector3.forward : Vector3.right;
            float frontSign = random.Next(2) == 0 ? 1f : -1f;
            float blockFront = frontSign > 0f ? Vector3.Dot(block.max, across) : Vector3.Dot(block.min, across);
            target += across * (blockFront - frontSign * Vector3.Dot(extents, across) - Vector3.Dot(target, across));
            target.y = 0f;

            int before = footprints.Count;
            GameObject placed = Place(next, target - new Vector3(centerOffset.x, 0f, centerOffset.z), yaw, 1f, reserve: false);
            if (placed == null) return;
            if (footprints.Count != before + 1 || DistrictBlocked(footprints[before].Bounds, DistrictStreet, blockStart))
            {
                RemoveLast(placed);
                return;
            }
            block.Encapsulate(BoundsOf(placed));
        }
    }

    // from에서 to까지 긴 쪽을 맞대어 집을 늘어세운다. outward 쪽으로 물려 세워 집 전체가 from–to 선 바깥에 선다.
    private void PerimeterRow(Vector3 from, Vector3 to, Vector3 outward)
    {
        Vector3 dir = (to - from).normalized;
        float length = Vector3.Distance(from, to);
        float dirYaw = -Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;

        float along = 0f;
        for (int guard = 0; along < length && guard < MaxRowSteps; guard++)
        {
            GameObject prefab = Pick(set.houses);
            if (prefab == null) return;

            Bounds b = PrefabBounds(prefab);
            float longSide = Mathf.Max(MinRowStep, LongSide(prefab));
            float depth = Mathf.Min(b.size.x, b.size.z);
            float yaw = dirYaw + YawOfLongSide(prefab) + (random.Next(2) == 0 ? 0f : 180f);
            // 물림 거리를 집마다 달리한다 — 자로 잰 듯한 한 줄은 성벽처럼 보인다.
            Vector3 position = from + dir * (along + longSide * 0.5f) + outward * (depth * 0.5f + Range(0.5f, 3f));
            Place(prefab, position, yaw, 1f, reserve: false);
            along += longSide + Range(0.8f, 3f);
        }
    }

    // 집 한 변 바깥에 수레·장작·울타리 한두 개, 가끔 화로.
    private void Yard(Bounds near)
    {
        int count = random.Next(1, 3);
        for (int i = 0; i < count; i++)
        {
            bool fence = set.fence != null && random.NextDouble() < 0.3;
            GameObject prefab = fence ? set.fence : Pick(set.barricades);
            if (prefab == null) return;

            Vector3 side = SideOf(random.Next(4));
            Vector3 along = new Vector3(side.z, 0f, side.x);
            float outward = Mathf.Abs(side.x) * near.extents.x + Mathf.Abs(side.z) * near.extents.z + Range(1.5f, 3f);
            float slide = (Mathf.Abs(along.x) * near.extents.x + Mathf.Abs(along.z) * near.extents.z) * Range(-0.8f, 0.8f);
            Vector3 position = near.center + side * outward + along * slide;
            position.y = 0f;

            // 울타리는 집 벽을 따라, 나머지는 아무렇게나 세워 둔 것처럼.
            float yaw = fence ? (Mathf.Abs(side.x) > 0f ? 0f : 90f) : (float)(random.NextDouble() * 360.0);
            TryPlaceDistrict(prefab, position, yaw, 1f, DistrictYardGap);
        }

        if (random.NextDouble() < 0.25)
        {
            Vector3 side = SideOf(random.Next(4));
            Vector3 position = near.center + side * (Mathf.Abs(side.x) * near.extents.x + Mathf.Abs(side.z) * near.extents.z + 1.5f);
            position.y = 0f;
            TryPlaceDistrict(Pick(set.lights), position, (float)(random.NextDouble() * 360.0), LightScale, DistrictYardGap);
        }
    }

    private static Vector3 SideOf(int index) => index switch
    {
        0 => Vector3.right,
        1 => Vector3.left,
        2 => Vector3.forward,
        _ => Vector3.back,
    };

    // 세워 보고, 비울 곳을 덮거나 다른 바닥면·전장 끝에 gap보다 붙으면 걷어 낸다.
    private GameObject TryPlaceDistrict(GameObject prefab, Vector3 position, float yaw, float scale, float gap)
    {
        if (prefab == null) return null;
        int before = footprints.Count;
        GameObject placed = Place(prefab, position, yaw, scale, reserve: false);
        if (placed == null) return null;
        if (footprints.Count != before + 1)
        {
            RemoveLast(placed);
            return null;
        }

        Rect mine = footprints[before].Bounds;
        if (DistrictBlocked(mine, gap, before))
        {
            RemoveLast(placed);
            return null;
        }
        return placed;
    }

    private bool DistrictBlocked(Rect mine, float gap, int count)
    {
        float edge = ArenaHalf - gap;
        if (mine.xMin < -edge || mine.xMax > edge || mine.yMin < -edge || mine.yMax > edge) return true;

        Rect padded = Rect.MinMaxRect(mine.xMin - gap, mine.yMin - gap, mine.xMax + gap, mine.yMax + gap);
        for (int i = 0; i < count; i++)
            if (padded.Overlaps(footprints[i].Bounds)) return true;

        foreach (Rect clear in districtClear)
            if (mine.Overlaps(clear)) return true;

        foreach (Vector4 r in reserved)
            if (DistanceToRect(mine, new Vector2(r.x, r.z)) < r.w) return true;
        foreach (Vector4 r in districtClearCircles)
            if (DistanceToRect(mine, new Vector2(r.x, r.z)) < r.w) return true;

        foreach (Vector4 lane in districtLanes)
        {
            var a = new Vector2(lane.x, lane.y);
            var b = new Vector2(lane.z, lane.w);
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
            for (int s = 0; s <= steps; s++)
                if (DistanceToRect(mine, Vector2.Lerp(a, b, s / (float)steps)) < DistrictLaneHalfWidth) return true;
        }
        return false;
    }

    private static float DistanceToRect(Rect rect, Vector2 point)
    {
        float dx = Mathf.Max(rect.xMin - point.x, 0f, point.x - rect.xMax);
        float dz = Mathf.Max(rect.yMin - point.y, 0f, point.y - rect.yMax);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ---------------------------------------------------------------- 흩뿌리기

    // 낱개 바리케이드. 서로·건물·출발 자리와 겹치지 않는 곳에만 둔다.
    private void ScatterBarricades(int count, float xMin, float xMax, float zMin, float zMax)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject prefab = Pick(set.barricades);
            if (prefab == null) return;
            for (int tries = 0; tries < 8; tries++)
            {
                var position = new Vector3(Range(xMin, xMax), 0f, Range(zMin, zMax));
                if (TryPlaceScattered(prefab, position, (float)(random.NextDouble() * 360.0), 1f, 1.5f) != null) break;
            }
        }
    }

    // 적 쪽 폐허 — 무너진 기둥과 빈집. 몰려오는 무리를 몇 갈래로 나눈다.
    private void ScatterRuins(int count, float xMin, float xMax)
    {
        if (xMax <= xMin) return;
        for (int i = 0; i < count; i++)
        {
            bool house = random.NextDouble() < 0.4;
            GameObject prefab = house ? Pick(set.houses) : Pick(set.pillars);
            if (prefab == null) prefab = Pick(set.pillars);
            if (prefab == null) return;
            for (int tries = 0; tries < 8; tries++)
            {
                var position = new Vector3(Range(xMin, xMax), 0f, Range(-34f, 34f));
                if (TryPlaceScattered(prefab, position, random.Next(4) * 90f, 1f, 3f) != null) break;
            }
        }
    }

    private GameObject TryPlaceScattered(GameObject prefab, Vector3 position, float yaw, float scale, float gap)
    {
        GameObject placed = Place(prefab, position, yaw, scale);
        if (placed == null) return null;

        Bounds b = BoundsOf(placed);
        var footprint = new Vector2(b.center.x, b.center.z);
        float radius = new Vector2(b.extents.x, b.extents.z).magnitude;

        bool clear = Mathf.Abs(b.center.x) + b.extents.x < ArenaHalf && Mathf.Abs(b.center.z) + b.extents.z < ArenaHalf;
        foreach (Vector4 r in reserved)
            clear &= Vector2.Distance(footprint, new Vector2(r.x, r.z)) > radius + r.w;
        for (int i = 0; i < footprints.Count - 1 && clear; i++)
        {
            BattleGround.Footprint other = footprints[i];
            float otherRadius = other.halfSize.magnitude;
            clear &= Vector2.Distance(footprint, other.center) > radius + otherRadius + gap;
        }

        if (clear) return placed;
        RemoveLast(placed);
        return null;
    }

    // ---------------------------------------------------------------- 놓기

    // 프리팹 하나를 세우고, 바닥면을 막고, NavMesh를 깎고, 높으면 가림 검사에 올린다.
    // alignStart면 position.z가 바닥면의 -Z 끝이 되게 민다(줄지어 세울 때).
    // reserve가 참이면 출발 자리를 덮는 건물은 세우지 않는다.
    private GameObject Place(GameObject prefab, Vector3 position, float yaw, float scale, bool alignStart = false,
        bool reserve = true)
    {
        if (prefab == null) return null;

        GameObject instance = Instantiate(prefab, position, Quaternion.Euler(0f, yaw, 0f), root);
        instance.transform.localScale = prefab.transform.localScale * scale;
        StripColliders(instance);

        if (!TryLocalBounds(instance, out Bounds local))
        {
            Destroy(instance);
            return null;
        }

        if (alignStart)
        {
            Bounds world = BoundsOf(instance);
            instance.transform.position += new Vector3(0f, 0f, position.z - world.min.z);
        }

        Bounds placedBounds = BoundsOf(instance);
        if (reserve)
        {
            var center2 = new Vector2(placedBounds.center.x, placedBounds.center.z);
            foreach (Vector4 r in reserved)
            {
                float dx = Mathf.Max(0f, Mathf.Abs(r.x - center2.x) - placedBounds.extents.x);
                float dz = Mathf.Max(0f, Mathf.Abs(r.z - center2.y) - placedBounds.extents.z);
                if (dx * dx + dz * dz < r.w * r.w)
                {
                    Destroy(instance);
                    return null;
                }
            }
        }

        AddFootprint(instance, local);
        AddObstacle(instance, local);
        if (placedBounds.size.y >= OccluderMinHeight)
            occluders.Add(new Occluder { bounds = placedBounds, renderers = instance.GetComponentsInChildren<Renderer>(true) });
        return instance;
    }

    // 길목 옆 화로. 마을 화로(지름 2.6m)는 사람 옆에서 너무 커서 줄여 세운다. 작은 장애물로 친다 —
    // 사람이 불 속을 지나가면 안 되니까.
    private const float LightScale = 0.7f;

    private void PlaceLight(Vector3 position)
    {
        Place(Pick(set.lights), position, (float)(random.NextDouble() * 360.0), LightScale, reserve: false);
    }

    private void AddFootprint(GameObject instance, Bounds local)
    {
        Vector3 scale = instance.transform.lossyScale;
        Vector3 center = instance.transform.TransformPoint(local.center);
        var half = new Vector2(
            Mathf.Max(MinFootprintHalf, local.extents.x * Mathf.Abs(scale.x) - FootprintInset),
            Mathf.Max(MinFootprintHalf, local.extents.z * Mathf.Abs(scale.z) - FootprintInset));
        footprints.Add(new BattleGround.Footprint(new Vector2(center.x, center.z), half, instance.transform.eulerAngles.y));
    }

    // 아군의 NavMesh에 같은 바닥면만큼 구멍을 낸다. 상자는 로컬 단위라 배율은 트랜스폼이 곱한다.
    private static void AddObstacle(GameObject instance, Bounds local)
    {
        var obstacle = instance.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = new Vector3(local.center.x, local.size.y * 0.5f + local.min.y, local.center.z);
        Vector3 scale = instance.transform.lossyScale;
        float inset = FootprintInset * 2f;
        obstacle.size = new Vector3(
            Mathf.Max(MinFootprintHalf * 2f / Mathf.Abs(scale.x), local.size.x - inset / Mathf.Abs(scale.x)),
            Mathf.Max(1f, local.size.y),
            Mathf.Max(MinFootprintHalf * 2f / Mathf.Abs(scale.z), local.size.z - inset / Mathf.Abs(scale.z)));
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
    }

    private void RemoveLast(GameObject placed)
    {
        if (footprints.Count > 0) footprints.RemoveAt(footprints.Count - 1);
        if (occluders.Count > 0 && occluders[occluders.Count - 1].renderers.Length > 0 &&
            occluders[occluders.Count - 1].renderers[0] != null &&
            occluders[occluders.Count - 1].renderers[0].transform.IsChildOf(placed.transform))
            occluders.RemoveAt(occluders.Count - 1);
        Destroy(placed);
    }

    // 전투 판정은 격자가 한다. 건물 충돌체가 남아 있으면 화살·시야 광선이 격자와 다른 벽을 본다.
    private static void StripColliders(GameObject instance)
    {
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) Destroy(collider);
    }

    // 메시 범위를 space 좌표로 옮긴 것. renderer.bounds를 쓰지 않는다 — 같은 프레임에 막 세운(LOD 묶음) 렌더러는
    // 범위가 비어 나올 때가 있어, 줄지어 세우는 고리가 한 자리에서 맴돌다 에디터가 멈췄다(2026-10-31).
    private static bool MeshBounds(GameObject root, Matrix4x4 toSpace, out Bounds bounds)
    {
        bounds = default;
        bool has = false;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;
            Matrix4x4 m = toSpace * filter.transform.localToWorldMatrix;
            Bounds mb = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!has)
                {
                    bounds = new Bounds(p, Vector3.zero);
                    has = true;
                }
                else bounds.Encapsulate(p);
            }
        }
        return has;
    }

    // 루트 기준(회전·배율 빼고) 범위. 프리팹마다 원점이 제각각이라 메시에서 직접 잰다.
    private static bool TryLocalBounds(GameObject instance, out Bounds bounds) =>
        MeshBounds(instance, instance.transform.worldToLocalMatrix, out bounds);

    // 월드 범위.
    private static Bounds BoundsOf(GameObject instance) =>
        MeshBounds(instance, Matrix4x4.identity, out Bounds bounds)
            ? bounds
            : new Bounds(instance.transform.position, Vector3.zero);

    // 긴 쪽의 길이와 방향(로컬 X면 0도, 로컬 Z면 90도).
    private static float LongSide(GameObject prefab)
    {
        Bounds b = PrefabBounds(prefab);
        return Mathf.Max(b.size.x, b.size.z);
    }

    private static float YawOfLongSide(GameObject prefab)
    {
        Bounds b = PrefabBounds(prefab);
        return b.size.z > b.size.x ? 90f : 0f;
    }

    // 긴 쪽이 월드 Z를 따르게 하는 yaw.
    private float LongSideAlongZYaw(GameObject prefab)
    {
        float baseYaw = YawOfLongSide(prefab) > 0f ? 0f : 90f;
        return baseYaw + (random.Next(2) == 0 ? 0f : 180f);
    }

    private static readonly Dictionary<GameObject, Bounds> PrefabBoundsCache = new Dictionary<GameObject, Bounds>();

    private static Bounds PrefabBounds(GameObject prefab)
    {
        if (PrefabBoundsCache.TryGetValue(prefab, out Bounds cached)) return cached;
        MeshBounds(prefab, prefab.transform.worldToLocalMatrix, out Bounds bounds);
        PrefabBoundsCache[prefab] = bounds;
        return bounds;
    }

    private GameObject Pick(GameObject[] options)
    {
        if (options == null || options.Length == 0) return null;
        int start = random.Next(options.Length);
        for (int i = 0; i < options.Length; i++)
        {
            GameObject option = options[(start + i) % options.Length];
            if (option != null) return option;
        }
        return null;
    }

    // 길이가 room 안에 드는 것 가운데 무작위 하나.
    private GameObject PickFitting(GameObject[] options, float room)
    {
        if (options == null || options.Length == 0) return null;
        int start = random.Next(options.Length);
        for (int i = 0; i < options.Length; i++)
        {
            GameObject option = options[(start + i) % options.Length];
            if (option != null && LongSide(option) <= room) return option;
        }
        return null;
    }

    private static GameObject Shortest(GameObject[] options)
    {
        GameObject best = null;
        if (options == null) return null;
        foreach (GameObject option in options)
            if (option != null && (best == null || LongSide(option) < LongSide(best))) best = option;
        return best;
    }

    private float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

    // ---------------------------------------------------------------- 가림

    // 카메라와 아군 사이를 막은 건물은 그림자만 남기고 숨긴다. 높은 건물(OccluderMinHeight)만 본다.
    // 건물이 수십 채, 아군이 다섯이라 광선-상자 검사 수백 번이다 — 물리 엔진을 부를 일이 아니다.
    private void LateUpdate()
    {
        Camera camera = Camera.main;
        if (camera == null || occluders.Count == 0) return;

        Vector3 eye = camera.transform.position;
        IReadOnlyList<UnitController> allies = UnitRegistry.GetTeam(UnitTeam.Ally);

        foreach (Occluder occluder in occluders)
        {
            bool hide = false;
            for (int i = 0; i < allies.Count && !hide; i++)
            {
                UnitController ally = allies[i];
                if (ally == null || ally.IsDead) continue;

                Vector3 target = ally.transform.position + Vector3.up;
                Vector3 toTarget = target - eye;
                float distance = toTarget.magnitude;
                if (distance < 0.01f) continue;

                hide = occluder.bounds.IntersectRay(new Ray(eye, toTarget / distance), out float hit) && hit < distance;
            }

            if (hide == occluder.hidden) continue;
            occluder.hidden = hide;
            ShadowCastingMode mode = hide ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            foreach (Renderer renderer in occluder.renderers)
                if (renderer != null) renderer.shadowCastingMode = mode;
        }
    }
}
