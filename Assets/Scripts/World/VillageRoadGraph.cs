using System.Collections.Generic;
using UnityEngine;

// 마을 길(VillageBlockout.Roads)을 걸어 다닐 수 있는 그래프로 옮긴 것. 마을 주민(VillageResident)은 이 위로만 걷는다.
//
// NavMesh를 굽지 않는 이유: 마을 건물은 플레이할 때 VillageBlockout이 새로 세우고(씬에 저장되지 않는다),
// 시설을 올리면 그 자리에서 다시 세운다. 구워 둔 NavMesh는 그때마다 어긋나고, 런타임에 굽기에는 성벽 안만 해도
// 250m 사방에 삼각형 87만 개다. 길은 이미 건물을 피해 나 있다(입구 길 도구가 건물을 피해 그었다) —
// 길 위로만 걸으면 벽을 뚫고 지나갈 일이 없다.
public sealed class VillageRoadGraph
{
    // 길을 이 간격(미터)으로 잘게 나눠 마디를 둔다. 오른쪽 차선으로 비켜 걷는 것도 마디마다 하므로
    // 너무 성기면 모퉁이에서 차선이 길 밖으로 삐져나간다.
    private const float NodeSpacing = 3f;

    // 다른 길의 마디가 이 안에 있으면 이어 걷는다. 길 끝이 다른 길 한가운데에 닿는 T자 갈림길은
    // 끝점끼리 겹치지 않아 이렇게만 이어진다. 연결 길의 끝은 큰길 끝에서 3.3m 떨어져 있다.
    private const float JunctionRadius = 4.5f;

    // 길 윗면이 지형보다 떠 있는 높이 — VillageBlockout의 groundLift(0.15) + RoadLift(0.10). 길에는 충돌체가 없다.
    public const float RoadSurface = 0.25f;

    private readonly List<Vector3> nodes = new List<Vector3>();
    private readonly List<List<int>> links = new List<List<int>>();

    // 서로 이어진 가장 큰 덩어리. 떨어져 나온 토막 길에 선 주민은 다른 곳으로 갈 수 없으므로 아예 쓰지 않는다.
    private readonly List<int> reachable = new List<int>();
    private bool[] isReachable;

    public int Count => reachable.Count;
    public Vector3 Node(int index) => nodes[index];

    public static VillageRoadGraph Build(VillageBlockout village)
    {
        var graph = new VillageRoadGraph();
        if (village == null || village.Roads == null) return graph;

        var roadOf = new List<int>();
        Transform frame = village.transform;
        for (int r = 0; r < village.Roads.Count; r++)
        {
            VillageBlockout.Road road = village.Roads[r];
            if (road == null || road.points == null || road.points.Count < 2) continue;

            int previous = -1;
            Vector3 from = Flat(frame.TransformPoint(new Vector3(road.points[0].x, 0f, road.points[0].y)));
            for (int i = 1; i < road.points.Count; i++)
            {
                Vector3 to = Flat(frame.TransformPoint(new Vector3(road.points[i].x, 0f, road.points[i].y)));
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / NodeSpacing));
                // 앞 토막의 끝이 이 토막의 시작이므로 첫 토막만 시작점을 넣는다.
                for (int s = previous < 0 ? 0 : 1; s <= steps; s++)
                {
                    int index = graph.Add(Vector3.Lerp(from, to, s / (float)steps));
                    roadOf.Add(r);
                    if (previous >= 0) graph.Link(previous, index);
                    previous = index;
                }
                from = to;
            }
        }

        float junction = JunctionRadius * JunctionRadius;
        for (int a = 0; a < graph.nodes.Count; a++)
        for (int b = a + 1; b < graph.nodes.Count; b++)
        {
            if (roadOf[a] == roadOf[b]) continue;
            if ((graph.nodes[a] - graph.nodes[b]).sqrMagnitude <= junction) graph.Link(a, b);
        }

        graph.KeepLargestComponent();
        for (int i = 0; i < graph.nodes.Count; i++) graph.nodes[i] = GroundAt(graph.nodes[i]);
        return graph;
    }

    private int Add(Vector3 position)
    {
        nodes.Add(position);
        links.Add(new List<int>(3));
        return nodes.Count - 1;
    }

    private void Link(int a, int b)
    {
        if (a == b || links[a].Contains(b)) return;
        links[a].Add(b);
        links[b].Add(a);
    }

    private void KeepLargestComponent()
    {
        var component = new int[nodes.Count];
        for (int i = 0; i < component.Length; i++) component[i] = -1;

        int best = -1, bestSize = 0, id = 0;
        var stack = new Stack<int>();
        for (int start = 0; start < nodes.Count; start++)
        {
            if (component[start] >= 0) continue;
            int size = 0;
            component[start] = id;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int n = stack.Pop();
                size++;
                foreach (int next in links[n])
                {
                    if (component[next] >= 0) continue;
                    component[next] = id;
                    stack.Push(next);
                }
            }
            if (size > bestSize) { bestSize = size; best = id; }
            id++;
        }

        isReachable = new bool[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            if (component[i] != best) continue;
            isReachable[i] = true;
            reachable.Add(i);
        }
    }

    public int RandomNode() => reachable.Count > 0 ? reachable[Random.Range(0, reachable.Count)] : -1;

    public int Nearest(Vector3 position)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        foreach (int i in reachable)
        {
            Vector3 d = nodes[i] - position;
            float distance = d.x * d.x + d.z * d.z;
            if (distance < bestDistance) { bestDistance = distance; best = i; }
        }
        return best;
    }

    // from에서 to까지 마디를 따라가는 가장 짧은 길(A*). 마디가 수백 개뿐이고 주민이 몇 초에 한 번 부르는 것이라
    // 열린 목록을 줄 세우지 않고 매번 훑는다.
    public bool FindPath(int from, int to, List<Vector3> path)
    {
        path.Clear();
        if (from < 0 || to < 0 || !isReachable[from] || !isReachable[to]) return false;

        int count = nodes.Count;
        var cost = new float[count];
        var came = new int[count];
        var closed = new bool[count];
        for (int i = 0; i < count; i++) { cost[i] = float.MaxValue; came[i] = -1; }

        var open = new List<int> { from };
        cost[from] = 0f;
        while (open.Count > 0)
        {
            int bestSlot = 0;
            float bestScore = float.MaxValue;
            for (int i = 0; i < open.Count; i++)
            {
                float score = cost[open[i]] + Vector3.Distance(nodes[open[i]], nodes[to]);
                if (score < bestScore) { bestScore = score; bestSlot = i; }
            }

            int current = open[bestSlot];
            open.RemoveAt(bestSlot);
            if (current == to) break;
            closed[current] = true;

            foreach (int next in links[current])
            {
                if (closed[next]) continue;
                float g = cost[current] + Vector3.Distance(nodes[current], nodes[next]);
                if (g >= cost[next]) continue;
                if (cost[next] == float.MaxValue) open.Add(next);
                cost[next] = g;
                came[next] = current;
            }
        }

        if (from != to && came[to] < 0) return false;
        for (int n = to; n >= 0; n = came[n]) path.Add(nodes[n]);
        path.Reverse();
        return true;
    }

    // 발을 딛는 높이. 길은 충돌체가 없어 지형 높이에 길 두께를 얹고, 훈련장 마당처럼 충돌체가 있는 바닥은 그 윗면을 쓴다.
    // 위에서 2.5m만 내려다보는 이유: 가로등·나무는 그보다 높아 광선이 그 속에서 출발하므로 맞지 않는다(속에서 쏜 광선은 그 충돌체를 못 맞힌다).
    public static Vector3 GroundAt(Vector3 position)
    {
        Terrain terrain = Terrain.activeTerrain;
        float y = terrain != null
            ? terrain.SampleHeight(position) + terrain.transform.position.y + RoadSurface
            : position.y;

        if (Physics.Raycast(new Vector3(position.x, y + 2.5f, position.z), Vector3.down, out RaycastHit hit, 5f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            y = Mathf.Max(y, hit.point.y);

        position.y = y;
        return position;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
