using UnityEngine;

// 메인 씬의 배경이 되는 떠 있는 섬.
//
// 윗면은 딛고 설 수 있게 평평하고, 아래로는 바위가 뾰족하게 뻗어 내려간다.
// 예전 배경은 250x250 판때기에 벽 큐브 여덟 개를 둘러 세운 것이라
// 어느 방향을 봐도 회색 상자 안이었다.
//
// 메시를 에셋으로 굽지 않고 코드에서 만드는 이유:
// 크기나 들쭉날쭉한 정도를 바꿀 때마다 모델링 파일을 다시 내보내는 대신 숫자만 고치면 되고,
// 씬 파일에는 값 몇 개만 남아 병합 충돌이 나지 않는다.
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class FloatingIsland : MonoBehaviour
{
    [Header("크기")]
    [Tooltip("윗면의 평균 반지름.")]
    [SerializeField, Min(1f)] private float radius = 80f;
    [Tooltip("가장자리에서 윗면이 떨어지는 깊이. 이 값이 커야 눈높이에서도 지면이 끊기는 게 보인다.")]
    [SerializeField, Min(0f)] private float rimDrop = 14f;
    [Tooltip("가운데 평지가 차지하는 비율. 여기까지는 정확히 y=0이라 그 위에 놓인 것들이 뜨거나 묻히지 않는다.")]
    [SerializeField, Range(0f, 0.95f)] private float plateau = 0.55f;
    [Tooltip("아래로 뻗어 내려가는 바위의 길이.")]
    [SerializeField, Min(1f)] private float depth = 120f;

    [Header("형태")]
    [SerializeField, Range(12, 128)] private int segments = 72;
    [Tooltip("윗면을 나누는 고리 수. 많을수록 조명이 부드럽다.")]
    [SerializeField, Range(1, 8)] private int topRings = 3;
    [Tooltip("옆면을 나누는 고리 수.")]
    [SerializeField, Range(3, 24)] private int sideRings = 12;
    [Tooltip("가장자리 윤곽이 들쭉날쭉한 정도(반지름 비율).")]
    [SerializeField, Range(0f, 0.4f)] private float edgeNoise = 0.13f;
    [Tooltip("바위 옆면이 우툴두툴한 정도(반지름 비율).")]
    [SerializeField, Range(0f, 0.4f)] private float rockNoise = 0.12f;
    [Tooltip("아래로 갈수록 좁아지는 속도. 작을수록 위쪽 절벽이 오래 유지되다 끝에서 급히 뾰족해진다.")]
    [SerializeField, Range(0.3f, 2f)] private float taper = 1.1f;
    [Tooltip("바위에 세로로 파인 골의 깊이(반지름 비율).")]
    [SerializeField, Range(0f, 0.2f)] private float grooveDepth = 0.06f;
    [Tooltip("윗면이 울퉁불퉁한 정도.")]
    [SerializeField, Range(0f, 10f)] private float topBumpiness = 2.5f;
    [Tooltip("면을 각지게 그린다. 끄면 매끈하게 이어진다.")]
    [SerializeField] private bool flatShading = true;
    [SerializeField] private int seed = 20260819;

    [Header("재질")]
    [Tooltip("윗면(풀)에 쓸 머티리얼.")]
    [SerializeField] private Material topMaterial;
    [Tooltip("옆면과 아래 바위에 쓸 머티리얼.")]
    [SerializeField] private Material rockMaterial;

    [Header("충돌")]
    [Tooltip("윗면을 딛고 설 수 있도록 콜라이더를 만든다.")]
    [SerializeField] private bool generateCollider = true;

    private Mesh mesh;

    private void OnEnable()
    {
        Rebuild();
    }

    private void OnValidate()
    {
        // 인스펙터에서 값을 만지면 바로 보이도록. 에디터에서만 불린다.
        if (isActiveAndEnabled) Rebuild();
    }

    [ContextMenu("지형 다시 만들기")]
    public void Rebuild()
    {
        var filter = GetComponent<MeshFilter>();
        var renderer = GetComponent<MeshRenderer>();

        if (mesh == null)
        {
            mesh = new Mesh { name = "FloatingIsland" };
            // 씬 파일에 메시가 통째로 직렬화되지 않도록 한다. 열 때마다 다시 만든다.
            mesh.hideFlags = HideFlags.DontSave;
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        Generate(mesh);
        filter.sharedMesh = mesh;

        var materials = new Material[2];
        materials[0] = topMaterial;
        materials[1] = rockMaterial;
        renderer.sharedMaterials = materials;

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

        int rim = Mathf.Max(12, segments);
        var vertices = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var topTriangles = new System.Collections.Generic.List<int>();
        var rockTriangles = new System.Collections.Generic.List<int>();

        // 윤곽선은 각도에 대한 주기 함수로 만든다. 그래야 한 바퀴 돌아 처음과 정확히 이어진다.
        var random = new System.Random(seed);
        float phaseA = (float)random.NextDouble() * Mathf.PI * 2f;
        float phaseB = (float)random.NextDouble() * Mathf.PI * 2f;
        float phaseC = (float)random.NextDouble() * Mathf.PI * 2f;
        float noiseOffset = (float)random.NextDouble() * 100f;

        // --- 윗면 -------------------------------------------------------
        // 가운데 한 점에서 시작해 고리를 넓혀 나간다.
        // 가운데를 y=0으로 두고 가장자리만 내려앉힌다. 위로 솟게 만들면 그 위에 서 있는
        // 문이나 카메라가 지면에 파묻힌다.
        vertices.Add(Vector3.zero);
        uvs.Add(new Vector2(0.5f, 0.5f));

        for (int ring = 1; ring <= topRings; ring++)
        {
            float t = ring / (float)topRings;
            // 가운데는 평평하게 두고 바깥쪽만 떨어뜨린다.
            // 전체를 완만한 돔으로 만들면 문이 비탈에 서서 밑동이 뜬다.
            float fall = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(plateau, 1f, t));
            float height = -rimDrop * fall * fall;

            for (int s = 0; s < rim; s++)
            {
                float angle = s / (float)rim * Mathf.PI * 2f;
                float r = OutlineRadius(angle, phaseA, phaseB, phaseC) * t;

                // 윗면도 완전히 평평하면 판때기처럼 보인다. 가장자리로 갈수록 굴곡을 준다.
                // 파이는 쪽으로만 흔들어야 가운데 평지가 솟아오르지 않는다.
                float bump = Mathf.PerlinNoise(
                    Mathf.Cos(angle) * 1.6f + noiseOffset,
                    Mathf.Sin(angle) * 1.6f + noiseOffset);

                var position = new Vector3(Mathf.Cos(angle) * r, height - bump * topBumpiness * fall, Mathf.Sin(angle) * r);
                vertices.Add(position);
                uvs.Add(new Vector2(position.x / (radius * 2f) + 0.5f, position.z / (radius * 2f) + 0.5f));
            }
        }

        // 가운데 부채꼴
        for (int s = 0; s < rim; s++)
        {
            int a = 1 + s;
            int b = 1 + (s + 1) % rim;
            topTriangles.Add(0); topTriangles.Add(b); topTriangles.Add(a);
        }

        // 고리 사이 띠
        for (int ring = 1; ring < topRings; ring++)
        {
            int inner = 1 + (ring - 1) * rim;
            int outer = 1 + ring * rim;
            for (int s = 0; s < rim; s++)
            {
                int s1 = (s + 1) % rim;
                AddQuad(topTriangles, inner + s, inner + s1, outer + s1, outer + s);
            }
        }

        // --- 옆면과 아래 바위 -------------------------------------------
        int rimStart = 1 + (topRings - 1) * rim;   // 윗면 바깥 고리 = 옆면 첫 고리
        int previousRing = rimStart;

        for (int ring = 1; ring <= sideRings; ring++)
        {
            float t = ring / (float)sideRings;
            float y = -rimDrop - depth * Mathf.Pow(t, 1.15f);

            // 위쪽은 절벽처럼 폭을 유지하다가 아래에서 급히 뾰족해지는 곡선.
            // 단순한 (1-t) 감쇠는 처음부터 좁아져 원뿔로 보인다.
            float shrink = Mathf.Pow(Mathf.Cos(t * Mathf.PI * 0.5f), taper);
            int ringStart = vertices.Count;

            for (int s = 0; s < rim; s++)
            {
                float angle = s / (float)rim * Mathf.PI * 2f;
                float baseRadius = OutlineRadius(angle, phaseA, phaseB, phaseC);

                // 바위 표면의 굴곡. 각도에 대해 닫힌 곡선이 되도록 원 위의 좌표로 샘플링한다.
                float bump = Mathf.PerlinNoise(
                    Mathf.Cos(angle) * 2.2f + noiseOffset,
                    Mathf.Sin(angle) * 2.2f + noiseOffset + t * 3.5f) - 0.5f;

                // 세로로 흐르는 골. 높이와 무관한 각도 함수라 위아래로 곧게 이어진다.
                float groove = Mathf.Sin(angle * 13f + phaseA) * 0.6f + Mathf.Sin(angle * 21f + phaseC) * 0.4f;

                float r = baseRadius * shrink
                          * (1f + rockNoise * bump * 2f)
                          * (1f + grooveDepth * groove * Mathf.Clamp01(t * 4f));
                r = Mathf.Max(r, 0.05f);

                var position = new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r);
                vertices.Add(position);
                uvs.Add(new Vector2(s / (float)rim, 1f - t));
            }

            for (int s = 0; s < rim; s++)
            {
                int s1 = (s + 1) % rim;
                AddQuad(rockTriangles, previousRing + s, previousRing + s1, ringStart + s1, ringStart + s);
            }
            previousRing = ringStart;
        }

        // 맨 아래 뾰족한 끝. 정확히 가운데가 아니라 살짝 비틀어 자연스럽게 만든다.
        float tipAngle = (float)random.NextDouble() * Mathf.PI * 2f;
        var tip = new Vector3(Mathf.Cos(tipAngle) * radius * 0.06f, -depth, Mathf.Sin(tipAngle) * radius * 0.06f);
        int tipIndex = vertices.Count;
        vertices.Add(tip);
        uvs.Add(new Vector2(0.5f, 0f));

        for (int s = 0; s < rim; s++)
        {
            int s1 = (s + 1) % rim;
            rockTriangles.Add(previousRing + s);
            rockTriangles.Add(previousRing + s1);
            rockTriangles.Add(tipIndex);
        }

        if (flatShading) Flatten(vertices, uvs, topTriangles, rockTriangles);

        target.SetVertices(vertices);
        target.SetUVs(0, uvs);
        target.subMeshCount = 2;
        target.SetTriangles(topTriangles, 0);
        target.SetTriangles(rockTriangles, 1);
        target.RecalculateNormals();
        target.RecalculateBounds();
    }

    // 삼각형마다 꼭짓점을 따로 두면 면 경계가 각지게 보인다.
    // 바위는 매끈하게 이어지는 것보다 면이 서는 편이 레퍼런스에 가깝다.
    private static void Flatten(System.Collections.Generic.List<Vector3> vertices,
        System.Collections.Generic.List<Vector2> uvs,
        System.Collections.Generic.List<int> topTriangles,
        System.Collections.Generic.List<int> rockTriangles)
    {
        var sourceVertices = new System.Collections.Generic.List<Vector3>(vertices);
        var sourceUvs = new System.Collections.Generic.List<Vector2>(uvs);
        vertices.Clear();
        uvs.Clear();

        Split(sourceVertices, sourceUvs, topTriangles, vertices, uvs);
        Split(sourceVertices, sourceUvs, rockTriangles, vertices, uvs);
    }

    private static void Split(System.Collections.Generic.List<Vector3> sourceVertices,
        System.Collections.Generic.List<Vector2> sourceUvs,
        System.Collections.Generic.List<int> triangles,
        System.Collections.Generic.List<Vector3> vertices,
        System.Collections.Generic.List<Vector2> uvs)
    {
        for (int i = 0; i < triangles.Count; i++)
        {
            int source = triangles[i];
            triangles[i] = vertices.Count;
            vertices.Add(sourceVertices[source]);
            uvs.Add(sourceUvs[source]);
        }
    }

    // 각도에 대한 주기 함수라 한 바퀴가 매끄럽게 이어진다.
    private float OutlineRadius(float angle, float phaseA, float phaseB, float phaseC)
    {
        float wave = Mathf.Sin(angle * 3f + phaseA) * 0.5f
                   + Mathf.Sin(angle * 5f + phaseB) * 0.3f
                   + Mathf.Sin(angle * 9f + phaseC) * 0.2f;
        return radius * (1f + edgeNoise * wave);
    }

    private static void AddQuad(System.Collections.Generic.List<int> triangles, int a, int b, int c, int d)
    {
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
        triangles.Add(a); triangles.Add(c); triangles.Add(d);
    }
}
