using UnityEngine;

// 파츠 메시를 텍스처 공간에 펼쳐, 텍스처 칸마다 그 칸이 덮는 표면의 파츠 공간 위치·법선을 준다.
// Meshy 텍스처는 조각난 아틀라스라 텍스처 공간에서 무늬를 그리면 조각마다 끊겨 보인다. 칸마다 3D 위치를 알면
// 무늬(이끼·돌담·나무판)를 3D에서 정해 조각을 넘어 이어지게 칠할 수 있다(VillageWallPalette, VillageGaiaSkin).
internal sealed class VillagePartSurface
{
    public readonly int width, height;
    public readonly Vector3[] positions;
    public readonly Vector3[] normals;
    public readonly bool[] covered;
    public readonly Bounds bounds;

    private VillagePartSurface(int width, int height, Bounds bounds)
    {
        this.width = width; this.height = height; this.bounds = bounds;
        positions = new Vector3[width * height];
        normals = new Vector3[width * height];
        covered = new bool[width * height];
    }

    /// 삼각형을 UV 공간에 그려 칸마다 위치·법선을 보간한다. 삼각형 밖(조각 사이 틈)은 옆 칸 값을 dilate번 번져
    /// 채운다 — 밉맵이 틈을 섞어 조각 가장자리에 테두리가 생기지 않게. UV·법선이 없는 메시면 null.
    public static VillagePartSurface Unwrap(Mesh mesh, int width, int height, int dilate = 4)
    {
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector2[] uvs = mesh.uv;
        int[] triangles = mesh.triangles;
        if (uvs.Length != vertices.Length || normals.Length != vertices.Length) return null;

        var surface = new VillagePartSurface(width, height, mesh.bounds);
        for (int t = 0; t < triangles.Length; t += 3)
        {
            int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
            var a = new Vector2(uvs[i0].x * width, uvs[i0].y * height);
            var b = new Vector2(uvs[i1].x * width, uvs[i1].y * height);
            var c = new Vector2(uvs[i2].x * width, uvs[i2].y * height);
            float area = Cross(b - a, c - a);
            if (Mathf.Abs(area) < 1e-6f) continue;

            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int x1 = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int y1 = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float w0 = Cross(b - p, c - p) / area;
                float w1 = Cross(c - p, a - p) / area;
                float w2 = 1f - w0 - w1;
                if (w0 < 0f || w1 < 0f || w2 < 0f) continue;

                int index = y * width + x;
                surface.positions[index] = vertices[i0] * w0 + vertices[i1] * w1 + vertices[i2] * w2;
                surface.normals[index] = (normals[i0] * w0 + normals[i1] * w1 + normals[i2] * w2).normalized;
                surface.covered[index] = true;
            }
        }

        for (int pass = 0; pass < dilate; pass++) surface.Dilate();
        return surface;
    }

    // 덮이지 않은 칸에 덮인 이웃(좌우상하 중 처음 찾은 것)의 위치·법선을 옮겨 적는다.
    private void Dilate()
    {
        var next = (bool[])covered.Clone();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int index = y * width + x;
            if (covered[index]) continue;
            int from = -1;
            if (x > 0 && covered[index - 1]) from = index - 1;
            else if (x < width - 1 && covered[index + 1]) from = index + 1;
            else if (y > 0 && covered[index - width]) from = index - width;
            else if (y < height - 1 && covered[index + width]) from = index + width;
            if (from < 0) continue;
            positions[index] = positions[from];
            normals[index] = normals[from];
            next[index] = true;
        }
        System.Array.Copy(next, covered, covered.Length);
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}
