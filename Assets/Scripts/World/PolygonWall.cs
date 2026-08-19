using System.Collections.Generic;
using UnityEngine;

// 섬 위 생활 공간을 둘러싸는 다각형 성벽.
//
// 멀리서 보면 떠 있는 섬이고, 그 위 사람들이 사는 자리는 이 벽 안이다.
// 예전 배경의 벽은 큐브 여덟 개를 손으로 배치한 것이라 각도와 간격이 어긋나 있었다.
// 변 수나 반지름을 바꾸면 모서리 기둥까지 알아서 다시 놓이도록 코드에서 만든다.
//
// 메시를 에셋으로 굽지 않는 이유는 FloatingIsland와 같다 — 값만 고치면 바로 반영되고
// 씬 파일에는 숫자 몇 개만 남는다.
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PolygonWall : MonoBehaviour
{
    [Header("형태")]
    [SerializeField, Range(3, 24)] private int sides = 12;
    [Tooltip("모서리 기둥이 놓이는 원의 반지름.")]
    [SerializeField, Min(2f)] private float radius = 29f;
    [Tooltip("지면 위로 올라오는 벽 높이.")]
    [SerializeField, Min(0.5f)] private float height = 9f;
    [SerializeField, Min(0.2f)] private float thickness = 1.6f;
    [Tooltip("지면 아래로 묻어 넣는 깊이. 지면이 기울어 있어도 벽 밑이 뜨지 않게 한다.")]
    [SerializeField, Min(0f)] private float baseSink = 9f;

    [Header("모서리 기둥")]
    [Tooltip("기둥이 벽보다 두꺼운 정도.")]
    [SerializeField, Min(0f)] private float pillarExtra = 1.5f;
    [Tooltip("기둥이 벽보다 높은 정도.")]
    [SerializeField, Min(0f)] private float pillarHeightExtra = 1.8f;

    [Header("출입구")]
    [Tooltip("벽 한 면을 비워 출입구로 쓴다. -1이면 완전히 막는다.")]
    [SerializeField] private int gateSide = -1;

    [Header("재질")]
    [SerializeField] private Material wallMaterial;
    [SerializeField] private Material pillarMaterial;

    [Header("충돌")]
    [SerializeField] private bool generateCollider = true;

    private Mesh mesh;

    private void OnEnable()
    {
        Rebuild();
    }

    private void OnValidate()
    {
        if (isActiveAndEnabled) Rebuild();
    }

    [ContextMenu("성벽 다시 만들기")]
    public void Rebuild()
    {
        if (mesh == null)
        {
            mesh = new Mesh { name = "PolygonWall" };
            // 씬 파일에 메시가 통째로 직렬화되지 않도록 한다. 열 때마다 다시 만든다.
            mesh.hideFlags = HideFlags.DontSave;
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        Generate(mesh);
        GetComponent<MeshFilter>().sharedMesh = mesh;
        GetComponent<MeshRenderer>().sharedMaterials = new[] { wallMaterial, pillarMaterial };

        var collider = GetComponent<MeshCollider>();
        if (generateCollider)
        {
            if (collider == null) collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = null;   // 같은 메시를 다시 물려야 갱신된다.
            collider.sharedMesh = mesh;
        }
        else if (collider != null)
        {
            collider.sharedMesh = null;
        }
    }

    private void Generate(Mesh target)
    {
        target.Clear();

        int count = Mathf.Max(3, sides);
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var wallTriangles = new List<int>();
        var pillarTriangles = new List<int>();

        float totalHeight = height + baseSink;
        float centerY = height - totalHeight * 0.5f;   // 윗면이 height, 밑면이 -baseSink

        for (int i = 0; i < count; i++)
        {
            Vector3 corner = CornerAt(i, count);
            Vector3 next = CornerAt(i + 1, count);

            // 모서리 기둥
            float pillarSize = thickness + pillarExtra;
            float pillarHeight = totalHeight + pillarHeightExtra;
            float pillarCenterY = height + pillarHeightExtra - pillarHeight * 0.5f;
            // 기둥은 모서리에서 바깥을 향하도록 돌려 놓는다.
            // 반 칸 어긋나면 양옆 벽과 각도가 맞지 않아 이음매가 벌어진다.
            float pillarAngle = i / (float)count * 360f;
            AddBox(vertices, uvs, pillarTriangles,
                new Vector3(corner.x, pillarCenterY, corner.z),
                Quaternion.Euler(0f, pillarAngle, 0f),
                new Vector3(pillarSize, pillarHeight, pillarSize));

            if (i == gateSide) continue;   // 이 면은 비워 둔다

            // 벽 한 판. 모서리 기둥 안쪽으로 살짝 파고들게 해서 이음매가 벌어지지 않게 한다.
            Vector3 mid = (corner + next) * 0.5f;
            Vector3 along = next - corner;
            float length = along.magnitude + thickness;
            float yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;

            AddBox(vertices, uvs, wallTriangles,
                new Vector3(mid.x, centerY, mid.z),
                Quaternion.Euler(0f, yaw, 0f),
                new Vector3(thickness, totalHeight, length));
        }

        target.SetVertices(vertices);
        target.SetUVs(0, uvs);
        target.subMeshCount = 2;
        target.SetTriangles(wallTriangles, 0);
        target.SetTriangles(pillarTriangles, 1);
        target.RecalculateNormals();
        target.RecalculateBounds();
    }

    private Vector3 CornerAt(int index, int count)
    {
        float angle = index / (float)count * Mathf.PI * 2f;
        return new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
    }

    // 면마다 꼭짓점을 따로 둬서 모서리가 각지게 보이도록 상자 하나를 붙인다.
    private static void AddBox(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        Vector3 center, Quaternion rotation, Vector3 size)
    {
        Vector3 half = size * 0.5f;

        // 면: 앞/뒤/오른/왼/위/아래
        AddFace(vertices, uvs, triangles, center, rotation,
            new Vector3(-half.x, -half.y, half.z), new Vector3(half.x, -half.y, half.z),
            new Vector3(half.x, half.y, half.z), new Vector3(-half.x, half.y, half.z));
        AddFace(vertices, uvs, triangles, center, rotation,
            new Vector3(half.x, -half.y, -half.z), new Vector3(-half.x, -half.y, -half.z),
            new Vector3(-half.x, half.y, -half.z), new Vector3(half.x, half.y, -half.z));
        AddFace(vertices, uvs, triangles, center, rotation,
            new Vector3(half.x, -half.y, half.z), new Vector3(half.x, -half.y, -half.z),
            new Vector3(half.x, half.y, -half.z), new Vector3(half.x, half.y, half.z));
        AddFace(vertices, uvs, triangles, center, rotation,
            new Vector3(-half.x, -half.y, -half.z), new Vector3(-half.x, -half.y, half.z),
            new Vector3(-half.x, half.y, half.z), new Vector3(-half.x, half.y, -half.z));
        AddFace(vertices, uvs, triangles, center, rotation,
            new Vector3(-half.x, half.y, half.z), new Vector3(half.x, half.y, half.z),
            new Vector3(half.x, half.y, -half.z), new Vector3(-half.x, half.y, -half.z));
        AddFace(vertices, uvs, triangles, center, rotation,
            new Vector3(-half.x, -half.y, -half.z), new Vector3(half.x, -half.y, -half.z),
            new Vector3(half.x, -half.y, half.z), new Vector3(-half.x, -half.y, half.z));
    }

    private static void AddFace(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        Vector3 center, Quaternion rotation, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int start = vertices.Count;
        vertices.Add(center + rotation * a);
        vertices.Add(center + rotation * b);
        vertices.Add(center + rotation * c);
        vertices.Add(center + rotation * d);

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(1f, 0f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(0f, 1f));

        // 유니티는 앞에서 봤을 때 시계 방향인 면을 앞면으로 친다.
        // 순서를 뒤집으면 겉면이 컬링돼 안쪽이 보이고, 레이캐스트도 윗면을 그냥 통과한다.
        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
    }
}
