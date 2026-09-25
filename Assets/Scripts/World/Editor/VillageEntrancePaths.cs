using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 마을 바닥을 참고 그림처럼 깐다: 돌 포장 큰길·갈래길, 건물 입구마다 이어지는 골목, 집 묶음을 앉히는 잔디 부지.
//
//   입구 찾기 ─▶ 1m 격자에 장애물(건물·기단·울타리·나무·성벽 밖) 칠하기 ─▶ 큰길 전체에서 거꾸로 퍼지는 최단 거리
//            ─▶ 입구에서 거리가 줄어드는 쪽으로 따라 내려가 큰길에 닿기 ─▶ 꺾은선 줄이기 ─▶ 동서·남북·45도로 펴기
//            ─▶ VillageBlockout 길 목록
//
// 큰길 칸 전부를 출발점으로 삼아 거리를 한 번에 잰다(다중 출발 다익스트라). 그래서 이웃한 집들의 길이 저절로
// 한 줄로 모여 큰길로 나간다. 모든 길을 동서·남북·45도 토막으로 펴서 참고 그림의 반듯한 가로망처럼 보이게 하고,
// 겹친 길은 마을 좌표로 무늬를 입혀 이음매가 보이지 않는다(VillageBlockout.BuildRoads).
//
// 입구:
//   - Meshy 시설: 계단이 있으면 계단 발치(합성소·소환소·무기창고·대장간), 계단 자리에 탑이 있으면 탑 앞(비행선착장),
//     계단이 없으면 문마다.
//   - 거리(Kind.Street)의 Gaia 3DForge 집·대장간: 문이 +X 박공 끝에 있다(계단 조각이 있으면 그 z, 없으면 가운데).
//   - 시공의 틈·훈련소는 큰길 끝이 곧 입구라 따로 내지 않는다.
//
// 결과는 씬의 길 목록에 "입구 · …" 이름으로 들어간다. 다시 돌리면 그 이름의 길만 지우고 새로 낸다 — 큰길(나머지 길)은
// 건드리지 않는다. 입구 길을 손으로 고쳐 남기려면 이름에서 "입구 · "를 떼어 둔다.
public static class VillageEntrancePaths
{
    public const string Prefix = "입구 · ";

    private const float Cell = 1f;
    private const float Extent = 124f;          // 격자가 덮는 반지름(마을 바닥 모서리)
    private const float InsideWall = 113f;      // 성벽 안쪽 면(117.8)에서 벽 두께 반과 여유를 뺀 걸을 수 있는 반지름
    private const float ObstacleMargin = 0.8f;  // 벽에서 이만큼은 비운다
    private const float FlatHeight = 0.6f;      // 바닥에서 이보다 낮은 것(바닥판·광장 무늬·묻힌 계단·병아리)은 밟고 지나간다
    private const float Overhead = 2.5f;        // 밑동이 이보다 높은 것(선착장 갑판)은 밑으로 지나간다
    private const float HouseWidth = 4f;
    private const float FacilityWidth = 6f;

    public const string LotPrefix = "부지 · ";
    private const float LotMargin = 3f;          // 건물 둘레로 잔디를 이만큼 더 깐다
    private const float LotBuildingArea = 15f;   // 바닥 넓이가 이보다 작은 것(등불·깃발·짐·수레)은 부지 크기를 정하지 않는다
    private const float LotBuildingHeight = 2.5f;

    private struct Entrance
    {
        public string label;
        public Vector3 door;     // 마을 좌표, 문턱
        public Vector3 forward;  // 문 밖으로
        public float width;
    }

    [MenuItem("PickMeUp/Village/7. 길·부지 다시 깔기 (건물 입구마다)", priority = 63)]
    public static void Connect()
    {
        var village = UnityEngine.Object.FindAnyObjectByType<VillageBlockout>();
        if (village == null) { Debug.LogWarning("[VillageEntrancePaths] 열린 씬에 VillageBlockout이 없다."); return; }

        ApplyMaterials(village);

        // 지금 배치 그대로 잰다. 윗 레벨 파츠(꺼져 있는 것)도 장애물로 친다 — 나중에 탑이 길 위에 서지 않게.
        // 큰길이 하나도 없으면 기본 큰길부터 깐다.
        if (village.Roads.Count == 0) village.LoadDefaultRoads();
        var trunk = new List<VillageBlockout.Road>();
        foreach (VillageBlockout.Road road in village.Roads)
            if (road != null && (road.label == null || !road.label.StartsWith(Prefix))) trunk.Add(road);
        var keptLots = new List<VillageBlockout.Lot>();
        foreach (VillageBlockout.Lot lot in village.Lots)
            if (lot != null && (lot.label == null || !lot.label.StartsWith(LotPrefix))) keptLots.Add(lot);
        village.EditorSetRoads(new List<VillageBlockout.Road>(trunk), keptLots);

        // 부지: 거리의 집 묶음마다 잔디를 깐다(참고 그림의 녹지 블록). 부지는 밟는 바닥이라 길 찾기에 걸리지 않는다.
        List<VillageBlockout.Lot> lots = FindLots(village);
        lots.InsertRange(0, keptLots);

        var grid = new Grid(village);
        grid.PaintObstacles();
        grid.PaintTrunk(trunk);
        grid.SpreadFromTrunk();

        var result = new List<VillageBlockout.Road>(trunk);
        int made = 0, skipped = 0;
        foreach (Entrance entrance in FindEntrances(village))
        {
            List<Vector2> points = grid.PathToTrunk(entrance.door, entrance.forward);
            if (points == null)
            {
                skipped++;
                Debug.LogWarning($"[VillageEntrancePaths] {entrance.label}: 큰길까지 길을 찾지 못했다(문 앞이 막혀 있다).");
                continue;
            }
            result.Add(new VillageBlockout.Road { label = Prefix + entrance.label, width = entrance.width, points = points });
            made++;
        }

        village.EditorSetRoads(result, lots);
        Debug.Log($"[VillageEntrancePaths] 입구 길 {made}줄, 부지 {lots.Count - keptLots.Count}곳을 깔았다(길을 못 낸 곳 {skipped}).");
    }

    /// 길·연석·부지·땅 재질과 부지 나무를 마을에 건다. 조립기의 "씬에 걸기"도 이것을 부른다(Connect를 거쳐).
    public static void ApplyMaterials(VillageBlockout village)
    {
        var so = new SerializedObject(village);
        so.FindProperty("roadMaterial").objectReferenceValue = VillageGroundTextures.Road();
        so.FindProperty("roadEdgeMaterial").objectReferenceValue = VillageGroundTextures.RoadEdge();
        so.FindProperty("lotMaterial").objectReferenceValue = VillageGroundTextures.Lot();
        so.FindProperty("groundMaterial").objectReferenceValue =
            VillageGroundTextures.Ground(so.FindProperty("groundRadius").floatValue * 2f);
        so.FindProperty("roadTile").floatValue = 4f;

        // 부지 나무: 수관이 빽빽한 Book of the Dead 나무 둘(7~9m)과 작은 침엽수, 둥근 덤불. 집(9~13m)을 가리지 않는 크기만
        // 고른다. 참고 그림의 둥근 녹지가 되도록 잎이 성긴 pine_006·갈색 잔가지 덤불(Bush_b1·향나무)은 뺐다 — 말라 보였다.
        // 같은 이름을 두 번 넣으면 그만큼 자주 나온다.
        var trees = new List<GameObject>();
        foreach (string name in LotTrees)
        {
            GameObject prefab = FindPrefab(name);
            if (prefab != null) trees.Add(prefab);
            else Debug.LogWarning($"[VillageEntrancePaths] 부지 나무 프리팹이 없다: {name} (Gaia가 설치돼 있어야 한다)");
        }
        SerializedProperty list = so.FindProperty("lotTreePrefabs");
        list.arraySize = trees.Count;
        for (int i = 0; i < trees.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = trees[i];
        so.ApplyModifiedProperties();
    }

    private static readonly string[] LotTrees =
    {
        // 기본 카메라 거리(100~230m)에서는 거의 다 마지막 LOD로 그려진다. 그 단계의 무게로 골랐다:
        // S2 12(빌보드)·Bush_qilgP2 104·M2 1,424·pine_004_3_0 1,751. M3(3,068)는 108그루 기준 삼각형을 30만 가까이
        // 늘려서 뺐다.
        "Pine_002_S2_baked_hierarchy", "Pine_002_S2_baked_hierarchy", "Pine_002_S2_baked_hierarchy",
        "Pine_002_M2_baked_hierarchy", "Pine_002_M2_baked_hierarchy",
        "pine_004_3_0_baked_hierarchy",
        "Bush_qilgP2_6x6x4_PF", "Bush_qilgP2_6x6x4_PF",
    };

    private static GameObject FindPrefab(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }

    // ---- 부지 ------------------------------------------------------------------

    // 거리(Kind.Street) 구역마다, 건물(바닥 넓이 15㎡ 넘고 2.5m 넘게 솟은 것)을 다 덮는 마을 축 네모에 여유를 더한다.
    private static List<VillageBlockout.Lot> FindLots(VillageBlockout village)
    {
        var lots = new List<VillageBlockout.Lot>();
        Transform root = village.transform;
        foreach (Transform district in root)
        {
            var facility = district.GetComponent<VillageFacility>();
            if (facility == null) continue;
            if (facility.kind != VillageBlockout.Kind.Street) continue;

            bool any = false;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (MeshRenderer renderer in district.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                VillageBox(root, renderer.transform, filter.sharedMesh.bounds, out Vector3 lo, out Vector3 hi);
                if ((hi.x - lo.x) * (hi.z - lo.z) < LotBuildingArea || hi.y - lo.y < LotBuildingHeight) continue;
                min = Vector2.Min(min, new Vector2(lo.x, lo.z));
                max = Vector2.Max(max, new Vector2(hi.x, hi.z));
                any = true;
            }
            if (!any) continue;

            min -= Vector2.one * LotMargin;
            max += Vector2.one * LotMargin;
            lots.Add(new VillageBlockout.Lot { label = LotPrefix + district.name, center = (min + max) * 0.5f, size = max - min });
        }
        return lots;
    }

    // 메시 상자를 마을 좌표로 옮긴 축 정렬 상자.
    private static void VillageBox(Transform village, Transform part, Bounds bounds, out Vector3 min, out Vector3 max)
    {
        Matrix4x4 m = village.worldToLocalMatrix * part.localToWorldMatrix;
        min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int c = 0; c < 8; c++)
        {
            Vector3 p = m.MultiplyPoint3x4(new Vector3((c & 1) == 0 ? bounds.min.x : bounds.max.x,
                                                       (c & 2) == 0 ? bounds.min.y : bounds.max.y,
                                                       (c & 4) == 0 ? bounds.min.z : bounds.max.z));
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
    }

    // ---- 입구 ------------------------------------------------------------------

    private static List<Entrance> FindEntrances(VillageBlockout village)
    {
        var list = new List<Entrance>();
        Transform root = village.transform;

        foreach (Transform district in root)
        {
            var facility = district.GetComponent<VillageFacility>();
            if (facility == null) continue;
            if (facility.kind == VillageBlockout.Kind.Rift || facility.kind == VillageBlockout.Kind.Training) continue;

            var building = district.GetComponentInChildren<FacilityBuilding>(true);
            if (building != null)
            {
                bool any = false;
                Transform stairs = building.SlotRoot(FacilityBuilding.Slot.Stairs);
                if (stairs != null)
                {
                    foreach (Transform s in stairs)
                    {
                        if (!s.gameObject.activeSelf) continue;
                        list.Add(Front(root, s, district.name, FacilityWidth));
                        any = true;
                    }
                }

                Transform doors = building.SlotRoot(FacilityBuilding.Slot.Entrance);
                if (!any && doors != null)
                {
                    int n = 0;
                    foreach (Transform d in doors)
                    {
                        if (!d.gameObject.activeSelf || d.GetComponent<MeshFilter>() == null) continue;
                        list.Add(Front(root, d, $"{district.name} {++n}", HouseWidth));
                    }
                }
            }

            if (facility.kind != VillageBlockout.Kind.Street) continue;
            foreach (Transform t in district.GetComponentsInChildren<Transform>(true))
            {
                if (!IsGaiaBuilding(t)) continue;
                list.Add(GaiaDoor(root, t, $"{district.name} {CleanName(t.name)}"));
            }
        }
        return list;
    }

    // Meshy 파츠(문·계단·탑)의 앞면 가운데. 파츠는 정면이 +Z이고 발밑 가운데가 원점이다.
    private static Entrance Front(Transform village, Transform part, string label, float width)
    {
        var filter = part.GetComponent<MeshFilter>();
        float front = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds.max.z : 0f;
        Vector3 door = village.InverseTransformPoint(part.TransformPoint(new Vector3(0f, 0f, front)));
        Vector3 forward = village.InverseTransformDirection(part.forward);
        forward.y = 0f;
        return new Entrance { label = label, door = door, forward = forward.normalized, width = width };
    }

    private static bool IsGaiaBuilding(Transform t)
    {
        if (t.GetComponent<MeshFilter>() == null) return false;
        string name = t.name;
        return name.Contains("fi_vil_GaiaHouse") || name.Contains("fi_vil_GaiaForge");
    }

    // 3DForge 집·대장간은 문이 +X 박공 끝에 있다. 계단 조각(비탈용, 평지에선 땅에 묻힌다)이 있으면 문은 그 z에 있다.
    private static Entrance GaiaDoor(Transform village, Transform house, string label)
    {
        Bounds bounds = house.GetComponent<MeshFilter>().sharedMesh.bounds;
        float z = bounds.center.z;
        foreach (Transform child in house)
        {
            if (!child.name.Contains("stair")) continue;
            var renderer = child.GetComponent<Renderer>();
            if (renderer != null) z = house.InverseTransformPoint(renderer.bounds.center).z;
        }
        Vector3 door = village.InverseTransformPoint(house.TransformPoint(new Vector3(bounds.max.x, 0f, z)));
        Vector3 forward = village.InverseTransformDirection(house.right);
        forward.y = 0f;
        return new Entrance { label = label, door = door, forward = forward.normalized, width = HouseWidth };
    }

    private static string CleanName(string name)
    {
        name = name.Replace("_Sp_", "").Replace("fi_vil_Gaia", "");
        int paren = name.IndexOf(" (", StringComparison.Ordinal);
        return paren > 0 ? name.Substring(0, paren) : name;
    }

    // ---- 격자 ------------------------------------------------------------------

    private sealed class Grid
    {
        private readonly VillageBlockout village;
        private readonly int size;
        private readonly bool[] blocked;
        private readonly bool[] trunk;
        private readonly float[] cost;
        private readonly float[] dist;
        private readonly int[] parent;
        private readonly float groundY;

        public Grid(VillageBlockout village)
        {
            this.village = village;
            size = Mathf.CeilToInt(Extent * 2f / Cell);
            blocked = new bool[size * size];
            trunk = new bool[size * size];
            cost = new float[size * size];
            dist = new float[size * size];
            parent = new int[size * size];

            Transform ground = village.transform.Find("바닥");
            groundY = ground != null && ground.childCount > 0 ? ground.GetChild(0).localPosition.y : 0f;
        }

        private int Index(int x, int z) => z * size + x;
        private Vector2 Center(int i) => new Vector2((i % size + 0.5f) * Cell - Extent, (i / size + 0.5f) * Cell - Extent);

        private bool ToCell(Vector3 p, out int i)
        {
            int x = Mathf.FloorToInt((p.x + Extent) / Cell), z = Mathf.FloorToInt((p.z + Extent) / Cell);
            i = -1;
            if (x < 0 || z < 0 || x >= size || z >= size) return false;
            i = Index(x, z);
            return true;
        }

        public void PaintObstacles()
        {
            for (int i = 0; i < blocked.Length; i++)
                blocked[i] = Center(i).magnitude > InsideWall;

            Transform root = village.transform;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                // 바닥과 길은 밟는 것이다. 부지 나무는 길을 피해 다시 심기므로 길이 나무를 피할 까닭이 없다.
                Transform top = renderer.transform;
                while (top.parent != null && top.parent != root) top = top.parent;
                if (top.name == "바닥" || top.name == "길" || top.name == "부지 나무") continue;

                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Paint(renderer.transform, filter.sharedMesh.bounds);
            }

            // 벽에서 떨어져 걷도록 벽에 가까울수록 비싸게 한다(4칸까지).
            var near = new int[blocked.Length];
            var queue = new Queue<int>();
            for (int i = 0; i < blocked.Length; i++)
            {
                near[i] = blocked[i] ? 0 : int.MaxValue;
                if (blocked[i]) queue.Enqueue(i);
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                if (near[i] >= 4) continue;
                int x = i % size, z = i / size;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= size || nz >= size) continue;
                    int n = Index(nx, nz);
                    if (near[n] <= near[i] + 1) continue;
                    near[n] = near[i] + 1;
                    queue.Enqueue(n);
                }
            }
            for (int i = 0; i < cost.Length; i++)
                cost[i] = 1f + Mathf.Max(0, 4 - Mathf.Min(near[i], 4)) * 0.45f;
        }

        // 렌더러의 메시 상자(자기 공간)를 격자에 칠한다. 돌려 놓인 집도 돌린 그대로 칠한다.
        private void Paint(Transform transform, Bounds bounds)
        {
            Matrix4x4 toVillage = village.transform.worldToLocalMatrix * transform.localToWorldMatrix;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) == 0 ? bounds.min.x : bounds.max.x,
                                         (c & 2) == 0 ? bounds.min.y : bounds.max.y,
                                         (c & 4) == 0 ? bounds.min.z : bounds.max.z);
                Vector3 p = toVillage.MultiplyPoint3x4(corner);
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
            if (maxY - groundY < FlatHeight) return;     // 밟고 지나간다
            if (minY - groundY > Overhead) return;       // 밑으로 지나간다

            Matrix4x4 toLocal = toVillage.inverse;
            Vector3 scale = transform.lossyScale;
            float mx = ObstacleMargin / Mathf.Max(0.01f, Mathf.Abs(scale.x));
            float mz = ObstacleMargin / Mathf.Max(0.01f, Mathf.Abs(scale.z));
            float midY = (minY + maxY) * 0.5f;

            int x0 = Mathf.Max(0, Mathf.FloorToInt((minX - ObstacleMargin + Extent) / Cell));
            int x1 = Mathf.Min(size - 1, Mathf.FloorToInt((maxX + ObstacleMargin + Extent) / Cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((minZ - ObstacleMargin + Extent) / Cell));
            int z1 = Mathf.Min(size - 1, Mathf.FloorToInt((maxZ + ObstacleMargin + Extent) / Cell));
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int i = Index(x, z);
                if (blocked[i]) continue;
                Vector2 c = Center(i);
                Vector3 local = toLocal.MultiplyPoint3x4(new Vector3(c.x, midY, c.y));
                if (local.x < bounds.min.x - mx || local.x > bounds.max.x + mx) continue;
                if (local.z < bounds.min.z - mz || local.z > bounds.max.z + mz) continue;
                blocked[i] = true;
            }
        }

        // 큰길이 덮는 칸. VillageBlockout이 그리는 것과 같은 꺾은선으로 짚는다.
        public void PaintTrunk(List<VillageBlockout.Road> roads)
        {
            foreach (VillageBlockout.Road road in roads)
            {
                if (road == null || road.points == null || road.points.Count < 2) continue;
                List<Vector2> p = road.points;
                float half = road.width * 0.5f - 0.5f;
                for (int s = 0; s < p.Count - 1; s++)
                {
                    Vector2 p1 = p[s], p2 = p[s + 1];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 0.5f));
                    for (int k = 0; k <= steps; k++)
                    {
                        Vector2 c = Vector2.Lerp(p1, p2, k / (float)steps);
                        for (float dz = -half; dz <= half; dz += Cell * 0.5f)
                        for (float dx = -half; dx <= half; dx += Cell * 0.5f)
                        {
                            if (dx * dx + dz * dz > half * half) continue;
                            if (ToCell(new Vector3(c.x + dx, 0f, c.y + dz), out int i) && !blocked[i]) trunk[i] = true;
                        }
                    }
                }
            }
        }

        // 큰길 칸 전부에서 동시에 퍼지는 최단 거리. parent를 따라가면 가장 가까운 큰길에 닿는다.
        public void SpreadFromTrunk()
        {
            var heap = new MinHeap(size * size / 4);
            for (int i = 0; i < dist.Length; i++)
            {
                parent[i] = -1;
                dist[i] = float.PositiveInfinity;
                if (!trunk[i]) continue;
                dist[i] = 0f;
                heap.Push(i, 0f);
            }

            const float Diagonal = 1.41421356f;
            while (heap.Count > 0)
            {
                heap.Pop(out int u, out float du);
                if (du > dist[u]) continue;
                int x = u % size, z = u / size;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= size || nz >= size) continue;
                    int n = Index(nx, nz);
                    if (blocked[n]) continue;
                    // 대각선으로 벽 모서리를 스쳐 빠져나가지 않게.
                    if (dx != 0 && dz != 0 && (blocked[Index(x + dx, z)] || blocked[Index(x, z + dz)])) continue;
                    float step = (dx != 0 && dz != 0 ? Diagonal : 1f) * (cost[u] + cost[n]) * 0.5f * Cell;
                    float dn = du + step;
                    if (dn >= dist[n]) continue;
                    dist[n] = dn;
                    parent[n] = u;
                    heap.Push(n, dn);
                }
            }
        }

        /// 문턱에서 큰길까지의 꺾은선(마을 좌표 x,z). 문 앞이 막혀 있으면 null.
        public List<Vector2> PathToTrunk(Vector3 door, Vector3 forward)
        {
            // 문 앞 2m에서 시작한다. 거기가 벽 여유에 걸리면(집이 빽빽한 묶음) 조금씩 더 나가거나 옆으로 비켜 본다.
            int start = -1;
            Vector3 startPoint = door;
            for (float d = 2f; d <= 10f && start < 0; d += 0.75f)
            {
                foreach (float side in new[] { 0f, 1.2f, -1.2f, 2.4f, -2.4f, 3.6f, -3.6f })
                {
                    Vector3 p = door + forward * d + new Vector3(forward.z, 0f, -forward.x) * side;
                    if (!ToCell(p, out int i) || blocked[i] || float.IsInfinity(dist[i])) continue;
                    start = i;
                    startPoint = p;
                    break;
                }
            }
            if (start < 0) return null;

            var cells = new List<Vector2> { new Vector2(startPoint.x, startPoint.z) };
            for (int i = start; i >= 0; i = parent[i])
            {
                cells.Add(Center(i));
                if (parent[i] < 0) break;
            }

            List<Vector2> simple = Octilinear(Simplify(cells, 1f));
            // 큰길 안쪽으로 조금 더 밀어 넣어 이음매를 덮는다.
            if (simple.Count >= 2)
            {
                Vector2 last = simple[simple.Count - 1], before = simple[simple.Count - 2];
                Vector2 dir = (last - before).sqrMagnitude > 0.01f ? (last - before).normalized : Vector2.zero;
                simple[simple.Count - 1] = last + dir * 1.5f;
            }

            var points = new List<Vector2> { new Vector2(door.x, door.z) };
            points.AddRange(simple);
            return points;
        }

        // 참고 그림처럼 동서·남북·45도로만 달리게 편다. 비스듬한 한 토막은 "사선 + 곧은 길" 두 토막으로 나누고,
        // 사선을 먼저 갈지 나중에 갈지는 장애물에 걸리지 않으면서 싼 쪽을 고른다. 둘 다 막히면 그 토막은 그대로 둔다.
        private List<Vector2> Octilinear(List<Vector2> points)
        {
            var result = new List<Vector2> { points[0] };
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 a = result[result.Count - 1], b = points[i];
                Vector2 d = b - a;
                float ax = Mathf.Abs(d.x), az = Mathf.Abs(d.y), diagonal = Mathf.Min(ax, az);
                if (diagonal < 0.6f || Mathf.Abs(ax - az) < 0.6f) { result.Add(b); continue; }

                Vector2 step = new Vector2(Mathf.Sign(d.x), Mathf.Sign(d.y)) * diagonal;
                Vector2 diagonalFirst = a + step, straightFirst = b - step;
                float k1 = SegmentCost(a, diagonalFirst) + SegmentCost(diagonalFirst, b);
                float k2 = SegmentCost(a, straightFirst) + SegmentCost(straightFirst, b);
                if (!float.IsInfinity(k1) || !float.IsInfinity(k2)) result.Add(k1 <= k2 ? diagonalFirst : straightFirst);
                result.Add(b);
            }

            // 같은 방향으로 이어진 점은 하나로.
            var merged = new List<Vector2> { result[0] };
            for (int i = 1; i < result.Count - 1; i++)
            {
                Vector2 before = (result[i] - merged[merged.Count - 1]).normalized;
                Vector2 after = (result[i + 1] - result[i]).normalized;
                if (Vector2.Dot(before, after) < 0.999f) merged.Add(result[i]);
            }
            merged.Add(result[result.Count - 1]);
            return merged;
        }

        // 토막을 따라 걸을 때의 값. 벽에 걸리면 무한대.
        private float SegmentCost(Vector2 a, Vector2 b)
        {
            float length = Vector2.Distance(a, b), sum = 0f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.5f));
            for (int k = 0; k <= steps; k++)
            {
                Vector2 p = Vector2.Lerp(a, b, k / (float)steps);
                if (!ToCell(new Vector3(p.x, 0f, p.y), out int i) || blocked[i]) return float.PositiveInfinity;
                sum += cost[i];
            }
            return sum * length / (steps + 1);
        }
    }

    // 계단 모양으로 꺾인 칸 줄을 곧게 편다(더글러스-포이커).
    private static List<Vector2> Simplify(List<Vector2> points, float epsilon)
    {
        if (points.Count < 3) return new List<Vector2>(points);
        var keep = new bool[points.Count];
        keep[0] = keep[points.Count - 1] = true;
        var stack = new Stack<Vector2Int>();
        stack.Push(new Vector2Int(0, points.Count - 1));
        while (stack.Count > 0)
        {
            Vector2Int span = stack.Pop();
            float worst = 0f;
            int index = -1;
            for (int i = span.x + 1; i < span.y; i++)
            {
                float d = DistanceToSegment(points[i], points[span.x], points[span.y]);
                if (d <= worst) continue;
                worst = d;
                index = i;
            }
            if (index < 0 || worst <= epsilon) continue;
            keep[index] = true;
            stack.Push(new Vector2Int(span.x, index));
            stack.Push(new Vector2Int(index, span.y));
        }
        var result = new List<Vector2>();
        for (int i = 0; i < points.Count; i++) if (keep[i]) result.Add(points[i]);
        return result;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }

    // 다익스트라용 최소 힙(칸 번호, 거리).
    private sealed class MinHeap
    {
        private readonly List<int> items;
        private readonly List<float> keys;

        public MinHeap(int capacity)
        {
            items = new List<int>(capacity);
            keys = new List<float>(capacity);
        }

        public int Count => items.Count;

        public void Push(int item, float key)
        {
            items.Add(item);
            keys.Add(key);
            int i = items.Count - 1;
            while (i > 0)
            {
                int up = (i - 1) / 2;
                if (keys[up] <= keys[i]) break;
                Swap(i, up);
                i = up;
            }
        }

        public void Pop(out int item, out float key)
        {
            item = items[0];
            key = keys[0];
            int last = items.Count - 1;
            items[0] = items[last];
            keys[0] = keys[last];
            items.RemoveAt(last);
            keys.RemoveAt(last);

            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, smallest = i;
                if (l < items.Count && keys[l] < keys[smallest]) smallest = l;
                if (r < items.Count && keys[r] < keys[smallest]) smallest = r;
                if (smallest == i) break;
                Swap(i, smallest);
                i = smallest;
            }
        }

        private void Swap(int a, int b)
        {
            (items[a], items[b]) = (items[b], items[a]);
            (keys[a], keys[b]) = (keys[b], keys[a]);
        }
    }
}
