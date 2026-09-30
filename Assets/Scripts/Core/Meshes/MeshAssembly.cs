using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 정점·UV·삼각형 목록을 메시에 써 넣는 곳. 코드로 모양을 짜는 곳(성벽, 섬 밑면, 잘린 기단, 반원 판 …)은
// 목록만 채우고 이걸 부른다.
//
// 예전에는 곳마다 SetVertices → SetUVs → SetTriangles → Recalculate…를 손으로 늘어놓았는데, 순서나
// 빠진 줄이 조금씩 달랐다(노멀맵 재질에 탄젠트를 안 구한 곳, 다시 쓰면서 Clear를 안 한 곳).
public static class MeshAssembly
{
    [Flags]
    public enum Recalculate
    {
        None = 0,
        Normals = 1,
        // 노멀맵이 있는 재질에 쓸 메시는 탄젠트가 있어야 음영이 맞는다.
        Tangents = 2,
        Bounds = 4,

        Default = Normals | Bounds,
        ForNormalMap = Normals | Tangents | Bounds,
    }

    /// 새 메시를 만들어 채운다. 정점이 65,535개를 넘으면 32비트 인덱스로 만든다.
    public static Mesh Create(string name, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        Recalculate recalculate = Recalculate.Default, HideFlags hideFlags = HideFlags.None)
    {
        var mesh = new Mesh { name = name, hideFlags = hideFlags };
        Write(mesh, vertices, uvs, recalculate, triangles);
        return mesh;
    }

    /// 이미 있는 메시를 비우고 다시 채운다. 서브메시마다 삼각형 목록 하나.
    ///
    /// 비우는 것이 먼저다. 예전 삼각형이 남은 채 더 적은 정점을 쓰면 범위를 벗어난 인덱스로 유니티가 거부한다.
    /// 에셋 메시에 내용만 다시 쓰는 것도 이 길로 한다 — 새 메시를 만들어 덮으면 GPU 버퍼가 다시 올라가지 않아
    /// 에디터를 다시 열 때까지 옛 모양이 그려졌다(VillageIsland).
    public static void Write(Mesh mesh, List<Vector3> vertices, List<Vector2> uvs, Recalculate recalculate,
        params List<int>[] submeshTriangles)
    {
        mesh.Clear();
        if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;

        mesh.SetVertices(vertices);
        if (uvs != null) mesh.SetUVs(0, uvs);

        mesh.subMeshCount = Mathf.Max(1, submeshTriangles.Length);
        for (int i = 0; i < submeshTriangles.Length; i++) mesh.SetTriangles(submeshTriangles[i], i);

        RecalculateNow(mesh, recalculate);
    }

    public static void RecalculateNow(Mesh mesh, Recalculate recalculate)
    {
        if ((recalculate & Recalculate.Normals) != 0) mesh.RecalculateNormals();
        if ((recalculate & Recalculate.Tangents) != 0) mesh.RecalculateTangents();
        if ((recalculate & Recalculate.Bounds) != 0) mesh.RecalculateBounds();
    }
}
