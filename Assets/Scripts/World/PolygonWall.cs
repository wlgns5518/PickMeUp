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

    [Header("모듈 에셋")]
    [Tooltip("변마다 되풀이해 세울 성벽 한 칸(발밑 가운데가 원점, 정면 +Z가 마을 안쪽을 본다). " +
             "넣으면 상자 벽은 그리지 않고 충돌용으로만 남는다. 비우면 다시 상자 벽이 보인다.")]
    [SerializeField] private GameObject segmentPrefab;
    [Tooltip("모서리마다 세울 탑.")]
    [SerializeField] private GameObject cornerPrefab;
    [Tooltip("한 변을 몇 칸으로 나눠 세울지. 칸이 길면 돌 무늬가 옆으로 늘어난다.")]
    [SerializeField, Min(1)] private int segmentsPerSide = 3;
    [Tooltip("칸과 탑을 땅에 묻는 깊이. 벽 밖 지면이 낮은 곳에서 밑동이 뜨지 않게 한다.")]
    [SerializeField, Min(0f)] private float moduleSink = 4f;
    [Tooltip("모서리 탑이 벽 위로 더 솟는 높이.")]
    [SerializeField, Min(0f)] private float cornerRise = 5f;

    private Mesh mesh;
    private Transform modules;

    private void OnEnable()
    {
#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseMesh;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseMesh;
#endif
        RebuildMesh();
#if UNITY_EDITOR
        // 씬을 여는 도중에 자식(모듈 칸)을 만들면 유니티가 싫어한다. 에디터 틱을 한 번 받아서 세운다.
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.update -= BuildModulesOnce;
            UnityEditor.EditorApplication.update += BuildModulesOnce;
            return;
        }
#endif
        BuildModules();
    }

#if UNITY_EDITOR
    private void BuildModulesOnce()
    {
        UnityEditor.EditorApplication.update -= BuildModulesOnce;
        if (this != null && isActiveAndEnabled) BuildModules();
    }
#endif

#if UNITY_EDITOR
    private void OnDisable()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseMesh;
        UnityEditor.EditorApplication.update -= BuildModulesOnce;
    }
#endif

    // 메시 정리 규칙은 FloatingIsland와 같다 — DontSave라 직접 지우지 않으면 씬을 불러올 때마다 쌓인다.
    private void OnDestroy()
    {
        ReleaseMesh();
    }

    private void ReleaseMesh()
    {
        if (mesh == null) return;

        if (Application.isPlaying) Destroy(mesh);
        else DestroyImmediate(mesh);
        mesh = null;
    }

    private void OnValidate()
    {
        if (!isActiveAndEnabled) return;
#if UNITY_EDITOR
        // OnValidate 안에서는 오브젝트를 만들거나 지울 수 없다(모듈 칸). 한 틱 미룬다.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled) Rebuild();
        };
#endif
    }

    [ContextMenu("성벽 다시 만들기")]
    public void Rebuild()
    {
        RebuildMesh();
        BuildModules();
    }

    private void RebuildMesh()
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

    // 성벽 한 칸과 모서리 탑을 변을 따라 세운다. 상자 벽 메시는 그대로 두고(충돌·지면 아래 밑동) 그리기만 끈다.
    // 세운 것은 씬에 저장하지 않는다 — VillageBlockout과 같은 이유로, 값이 바뀌면 다시 세운다.
    private void BuildModules()
    {
        if (modules != null) Kill(modules.gameObject);
        modules = null;
        // 리로드 뒤에는 필드가 비어 있어 이름으로 옛 묶음을 찾아 치운다.
        Transform stale = transform.Find(ModulesName);
        if (stale != null) Kill(stale.gameObject);

        bool useModules = segmentPrefab != null;
        GetComponent<MeshRenderer>().enabled = !useModules;
        if (!useModules) return;

        var root = new GameObject(ModulesName);
        root.hideFlags = Application.isPlaying ? HideFlags.None : HideFlags.DontSaveInEditor;
        root.transform.SetParent(transform, false);
        modules = root.transform;

        int count = Mathf.Max(3, sides);
        Bounds segment = MeshBounds(segmentPrefab);
        Bounds corner = cornerPrefab != null ? MeshBounds(cornerPrefab) : default;

        for (int i = 0; i < count; i++)
        {
            Vector3 from = CornerAt(i, count);
            Vector3 to = CornerAt(i + 1, count);
            Vector3 along = to - from;
            Vector3 inward = -((from + to) * 0.5f).normalized;
            Quaternion facing = Quaternion.LookRotation(inward, Vector3.up);   // 칸의 정면(+Z)이 마을 안쪽

            if (i != gateSide)
            {
                float length = along.magnitude / segmentsPerSide;
                // 이음매가 벌어지지 않게 칸끼리 조금 겹친다.
                var scale = new Vector3(length * 1.03f / segment.size.x,
                                        (height + moduleSink) / segment.size.y,
                                        thickness / segment.size.z);
                for (int k = 0; k < segmentsPerSide; k++)
                {
                    Vector3 spot = from + along * ((k + 0.5f) / segmentsPerSide);
                    Place(segmentPrefab, spot + Vector3.down * moduleSink, facing, scale);
                }
            }

            if (cornerPrefab != null)
            {
                float width = (thickness + pillarExtra) * 1.3f;
                var scale = new Vector3(width / corner.size.x,
                                        (height + cornerRise + moduleSink) / corner.size.y,
                                        width / corner.size.z);
                // 기둥처럼 모서리에서 바깥을 본다.
                Place(cornerPrefab, from + Vector3.down * moduleSink, Quaternion.LookRotation(from.normalized, Vector3.up), scale);
            }
        }

        // 같은 메시 예순 개라 플레이 중에는 한 번 묶는다(VillageBlockout.CombineForRendering과 같은 이유).
        if (Application.isPlaying) StaticBatchingUtility.Combine(root);
    }

    private const string ModulesName = "Modules";

    private void Place(GameObject prefab, Vector3 localPosition, Quaternion localRotation, Vector3 scale)
    {
        GameObject piece = Instantiate(prefab, modules);
        piece.name = prefab.name;
        piece.transform.localPosition = localPosition;
        piece.transform.localRotation = localRotation;
        piece.transform.localScale = scale;
        if (!Application.isPlaying)
            foreach (Transform t in piece.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSaveInEditor;
    }

    private static Bounds MeshBounds(GameObject prefab)
    {
        var filter = prefab.GetComponentInChildren<MeshFilter>();
        return filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds : new Bounds(Vector3.up * 0.5f, Vector3.one);
    }

    private static void Kill(Object target)
    {
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
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
