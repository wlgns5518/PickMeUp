using System.Collections.Generic;
using UnityEngine;

// 성벽 안 마을의 임시 배치.
//
// 참고 그림의 열두 각 성벽 안에 광장과 시설들이 놓인 그 배치를 그대로 세운다.
// 지금은 전부 큐브/실린더/스피어 덩어리다. 에셋이 나오면 구역마다 prefab 칸에 넣기만 하면
// 그 자리에 프리팹이 대신 놓이므로, 배치를 다시 잡거나 이 스크립트를 지울 필요가 없다.
//
// 만들어진 오브젝트를 씬에 저장하지 않는 이유는 FloatingIsland/PolygonWall과 같다 —
// 씬 파일에는 구역 목록의 숫자 몇 줄만 남아 병합 충돌이 나지 않고, 각도나 거리를 고치면 바로 반영된다.
// 대신 이 오브젝트 밑에 손으로 붙여둔 자식은 다시 만들 때 같이 지워진다.
[ExecuteAlways]
[DisallowMultipleComponent]
public class VillageBlockout : MonoBehaviour
{
    public enum Kind
    {
        Plaza,      // 광장
        Rift,       // 시공의 틈
        Synthesis,  // 합성소
        Armory,     // 무기창고
        Summoning,  // 소환소
        Alchemy,    // 연금시설
        Airdock,    // 비행선착장
        Training,   // 훈련소
        Housing,    // 숙소
        Workshop,   // 공방시설(장식용 뼈대) — 도형만 쓰고 싶을 때는 지금도 이 kind를 쓴다.
        EquipmentWorkshop // 장비제작소 — 공방시설 자리를 그대로 쓰되 기능(자동/수동 제작)이 붙는다.
    }

    [System.Serializable]
    public class District
    {
        public string label = "구역";
        public Kind kind = Kind.Plaza;

        [TextArea(1, 3), Tooltip("이 구역이 하는 일.")]
        public string role = "";

        [Tooltip("북(+Z)에서 시계 방향으로 잰 각도. 참고 그림의 위쪽이 북이다.")]
        public float bearing;

        [Tooltip("마을 한가운데에서 떨어진 거리.")]
        public float distance;

        [Tooltip("구역이 차지하는 반지름. 안에 놓이는 것들이 통째로 따라 커진다.")]
        [Min(2f)] public float size = 18f;

        [Tooltip("정면을 돌리는 각도. 0이면 마을 한가운데를 바라본다.")]
        public float facing;

        [Tooltip("임시 도형만 다른 시설 것으로 세우고 싶을 때 켠다. 하는 일(kind)은 그대로다.")]
        public bool useOtherBuilding;

        [Tooltip("켰을 때 세울 도형. 예: 소환소인데 연금시설 건물을 쓰는 경우.")]
        public Kind building;

        [Tooltip("에셋이 준비되면 여기에 프리팹을 넣는다. 넣으면 임시 도형 대신 이 프리팹이 놓인다.")]
        public GameObject prefab;

        [Tooltip("잠깐 빼고 보고 싶을 때 끈다.")]
        public bool build = true;
    }

    [Header("구역")]
    [SerializeField] private List<District> districts = new List<District>();


    [Header("바닥")]
    [Tooltip("성벽 안을 통째로 덮는 바닥. 구역들은 이 바닥 위에 놓여 서로 이어진다.")]
    [SerializeField] private bool buildGround = true;
    [Tooltip("변 수. 성벽(PolygonWall)과 같은 값이어야 모서리가 맞는다.")]
    [SerializeField, Range(3, 24)] private int groundSides = 12;
    [Tooltip("모서리까지의 반지름. 성벽과 같은 값을 넣으면 바닥 가장자리가 벽 두께 안으로 들어가 이음매가 보이지 않는다.")]
    [SerializeField, Min(10f)] private float groundRadius = 124f;
    [SerializeField, Min(0.1f)] private float groundThickness = 0.4f;
    [Tooltip("바닥을 돌려놓는 각도. 성벽을 돌렸다면 같은 값을 넣어야 모서리가 맞는다. " +
             "성벽은 면 하나가 정북(시공의 틈)을 보도록 15도 돌려 두었다.")]
    [SerializeField] private float groundYaw = 15f;
    [Tooltip("지면보다 얼마나 띄울지. 지형과 높이가 똑같으면 두 면이 겹쳐 깜빡이고 지형이 뚫고 올라온다.")]
    [SerializeField, Min(0f)] private float groundLift = 0.15f;

    [Header("나무")]
    [Tooltip("구역과 구역 사이 빈 자리에 나무를 심는다. 훈련소 둘레 숲은 이 값과 상관없이 늘 심는다.")]
    [SerializeField] private bool buildTrees = true;
    [Tooltip("마을 빈터에 흩뿌릴 나무 수. 0이면 훈련소 둘레 숲만 남는다.")]
    [SerializeField, Min(0)] private int treeCount;
    [Tooltip("구역 경계에서 이만큼 떨어뜨린다.")]
    [SerializeField, Min(0f)] private float treeMargin = 4f;
    [Tooltip("나무끼리의 최소 간격.")]
    [SerializeField, Min(1f)] private float treeSpacing = 10f;
    [Tooltip("마을 한가운데는 참고 그림처럼 트인 돌바닥으로 비워 둔다.")]
    [SerializeField, Min(0f)] private float treeCenterClear = 34f;
    [Tooltip("시공의 틈으로 걸어 들어가는 길목의 폭. 여기에 나무가 서면 정면에서 틈이 가린다.")]
    [SerializeField, Min(0f)] private float riftApproachWidth = 15f;
    [Tooltip("훈련소 한가운데로 들어가는 길의 폭. 둘레 숲에서 이만큼은 비워 둔다. " +
             "밑동 기준이라 가지가 덮지 않도록 길보다 넉넉히 잡는다.")]
    [SerializeField, Min(0f)] private float trainingPathWidth = 26f;
    [SerializeField] private int treeSeed = 20260819;
    [Tooltip("심을 나무 프리팹. 비워두면 성벽 밖 숲과 같은 나무(지형에 등록된 프리팹)를 골라 쓴다.")]
    [SerializeField] private GameObject[] treePrefabs;
    [Tooltip("지형에서 자동으로 고를 때 쓸 높이 범위. 마을 안이라 너무 큰 나무는 뺀다.")]
    [SerializeField] private Vector2 treeHeightRange = new Vector2(5f, 22f);

    // 색만 다른 임시 머티리얼. 에셋으로 남기지 않고 만들 때마다 새로 찍는다.
    private static readonly Color Stone      = new Color(0.72f, 0.71f, 0.67f);
    private static readonly Color StoneLight = new Color(0.83f, 0.82f, 0.78f);
    private static readonly Color StoneDark  = new Color(0.48f, 0.48f, 0.46f);
    private static readonly Color Wood       = new Color(0.50f, 0.36f, 0.24f);
    private static readonly Color WoodDark   = new Color(0.32f, 0.23f, 0.16f);
    private static readonly Color Roof       = new Color(0.31f, 0.30f, 0.34f);
    private static readonly Color RoofRed    = new Color(0.45f, 0.27f, 0.24f);
    private static readonly Color Metal      = new Color(0.56f, 0.58f, 0.62f);
    private static readonly Color Dirt       = new Color(0.45f, 0.38f, 0.28f);
    private static readonly Color PathColor  = new Color(0.66f, 0.63f, 0.57f);
    private static readonly Color Sand       = new Color(0.78f, 0.72f, 0.57f);
    private static readonly Color Teal       = new Color(0.42f, 0.62f, 0.68f);
    private static readonly Color Accent     = new Color(0.35f, 0.88f, 0.84f);   // 마력이 도는 자리
    private static readonly Color Ember      = new Color(0.95f, 0.55f, 0.22f);   // 불
    private static readonly Color RiftDark   = new Color(0.17f, 0.14f, 0.24f);
    private static readonly Color Bark       = new Color(0.34f, 0.26f, 0.19f);
    private static readonly Color Leaf       = new Color(0.28f, 0.42f, 0.24f);
    private static readonly Color LeafDark   = new Color(0.21f, 0.34f, 0.20f);
    private static readonly Color Turf       = new Color(0.34f, 0.44f, 0.28f);   // 나무 밑 잔디

    private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
    private readonly Dictionary<Color, Material> glowMaterials = new Dictionary<Color, Material>();
    private readonly List<Mesh> meshes = new List<Mesh>();

    private void Reset()
    {
        LoadDefaultLayout();
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // 씬을 여는 도중에 오브젝트를 만들면 유니티가 싫어한다. 한 틱 미룬다.
            ScheduleRebuild();
            return;
        }
#endif
        Rebuild();
    }

    private void OnDestroy()
    {
        ClearGenerated();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        // OnValidate 안에서는 오브젝트를 만들거나 지울 수 없다. 마찬가지로 미룬다.
        ScheduleRebuild();
    }

    // 씬을 여는 중에 걸린 delayCall은 그냥 버려지는 일이 있다. 에디터 틱을 한 번 받아서 만든다.
    private void ScheduleRebuild()
    {
        UnityEditor.EditorApplication.update -= EditorTick;
        UnityEditor.EditorApplication.update += EditorTick;
    }

    private void EditorTick()
    {
        UnityEditor.EditorApplication.update -= EditorTick;

        // 기다리는 사이에 지워졌거나 꺼졌을 수 있다.
        if (this == null || !isActiveAndEnabled) return;
        Rebuild();
    }
#endif

    [ContextMenu("마을 다시 만들기")]
    public void Rebuild()
    {
        ClearChildren();
        ClearGenerated();

        if (buildGround) BuildGround();

        if (districts != null)
        {
            foreach (District district in districts)
            {
                if (district == null || !district.build) continue;
                BuildDistrict(district);
            }
        }

        if (buildTrees) BuildTrees();

        CombineForRendering();
    }

    // 런타임에 만든 오브젝트는 "정적"으로 표시할 수 없어 정적 배칭을 받지 못한다.
    // 도형이 수백 개라 그대로 두면 그 수만큼 드로우콜이 나간다.
    // StaticBatchingUtility는 런타임에도 쓸 수 있는 유일한 통로 — 만들어 놓은 뒤 한 번 묶어 준다.
    // (에디터에서는 값을 고칠 때마다 다시 만들어야 하므로 묶지 않는다. 묶으면 메시가 합쳐져
    //  개별 오브젝트를 씬 뷰에서 집어 옮길 수 없다.)
    private void CombineForRendering()
    {
        if (!Application.isPlaying) return;
        StaticBatchingUtility.Combine(gameObject);
    }

    // 씬 뷰에서 구역을 손으로 끌어다 놓은 뒤, 그 자리를 목록의 숫자로 받아 적는다.
    // 만들어진 자식은 저장되지 않으므로 이걸 하지 않으면 다음 번에 다시 만들 때 원래 값으로 돌아간다.
    [ContextMenu("지금 놓인 자리로 값 맞추기")]
    public void CaptureFromScene()
    {
        CaptureValues();
        Rebuild();
    }

    // 자식들의 지금 위치를 목록의 숫자로 옮겨 적는다. 다시 만들지는 않는다.
    private void CaptureValues()
    {
        if (districts == null) return;
        // 플레이 중에는 런타임 위치라 씬 값으로 굳히지 않는다.
        if (Application.isPlaying) return;

        // 자식은 표식(VillageFacility)의 종류와 이름으로 짝짓는다.
        // 순서로 짝지으면 자식 하나만 지워져도 그 뒤가 통째로 한 칸씩 밀려 엉뚱한 값이 들어간다.
        var built = new List<VillageFacility>();
        foreach (Transform child in transform)
        {
            var facility = child.GetComponent<VillageFacility>();
            if (facility != null) built.Add(facility);
        }

#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "구역 값 맞추기");
#endif

        foreach (District district in districts)
        {
            if (district == null || !district.build) continue;

            VillageFacility match = null;
            foreach (VillageFacility facility in built)
            {
                if (facility == null || facility.kind != district.kind || facility.label != district.label) continue;
                match = facility;
                break;
            }

            // 씬에서 지워진 구역은 값을 건드리지 않는다. 지운 채로 두려면 build를 끄면 된다.
            if (match == null) continue;
            built.Remove(match);

            Transform child = match.transform;
            Vector3 local = transform.InverseTransformPoint(child.position);

            district.bearing = Round(Mathf.Repeat(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, 360f));
            district.distance = Round(new Vector2(local.x, local.z).magnitude);
            district.facing = Round(Mathf.DeltaAngle(district.bearing + 180f, child.localEulerAngles.y));
            // 프리팹을 넣은 구역은 루트를 늘리지 않으므로 스케일에서 크기를 되읽을 수 없다.
            // 세울 때 쓴 것과 같은 기준 크기로 되읽어야 한다(도형을 바꿔 쓴 구역이 있다).
            if (district.prefab == null)
                district.size = Mathf.Max(2f, Round(child.localScale.x * DesignSize(district.useOtherBuilding ? district.building : district.kind)));
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    private static float Round(float value)
    {
        return Mathf.Round(value * 10f) * 0.1f;
    }

    [ContextMenu("기본 배치 불러오기")]
    public void LoadDefaultLayout()
    {
        // 참고 그림의 배치를 그대로 옮긴 것.
        // 그림의 위쪽(시공의 틈)이 정북이고, 나머지는 그 그림에서 시계 방향으로 잰 자리다.
        // 성벽은 면 하나가 정북을 보도록 15도 돌려 두었다 — 그래야 틈이 정면에 반듯하게 선다.
        districts = new List<District>
        {
            // 시계 방향으로 12시가 정북. 거리 117.5는 성벽(반지름 124, 열두 각, 두께 4) 안쪽 면이다.
            Make("시공의 틈",  Kind.Rift,        0f, 117.5f, 18f, "탑의 층으로 들어가는 입구. 벽이 갈라진 자리를 눌러 원정을 떠난다."),   // 12시
            Make("소환소",     Kind.Summoning,  60f,  78f, 18f, "새 동료를 불러낸다."),                                                  //  2시
            Make("비행선착장", Kind.Airdock,   150f,  78f, 18f, "비행선이 드나드는 자리."),                                              //  5시
            // 반원이라 곧은 변이 성벽에 닿도록 벽 앞(117.5)에 세운다. 둥근 쪽만 마을로 뻗는다.
            Make("훈련소",     Kind.Training,  180f, 117.5f, 30f, "동료를 훈련시켜 능력을 올린다."),                                     //  6시
            Make("숙소",       Kind.Housing,   210f,  72f, 22f, "동료들이 쉬며 스트레스를 회복한다."),                                   //  7시
            Make("합성소",     Kind.Synthesis, 300f,  72f, 22f, "재료를 합쳐 새 물건을 만든다."),                                        // 10시
            // 합성소의 오른쪽 아래.
            Make("무기창고",   Kind.Armory,    295f,  56f, 13f, "무기와 장비를 넣어 두고 꺼내 쓴다."),
            // 9시에서 3시까지 마을을 가로지르는 한 줄. 가운데(거리 0)에 놓고 좌우로 뻗는다.
            Make("장비제작소", Kind.EquipmentWorkshop, 0f, 0f, 20f, "장비를 만든다. 자동 제작은 등급이 고정이고, 수동 제작은 퍼즐 난이도에 따라 상위 등급이 나올 수 있다."),
            // 아래 둘은 지정에 없어 남은 자리에 넣었다. 옮기려면 방위/거리만 고치면 된다.
            Make("광장",       Kind.Plaza,       0f,  46f, 24f, "시공의 틈 앞, 사람이 모이는 빈터."),
            Make("연금시설",   Kind.Alchemy,   120f,  84f, 19f, "물약과 마력 재료를 다룬다.")                                            //  4시
        };
        Rebuild();
    }

    private static District Make(string label, Kind kind, float bearing, float distance, float size, string role)
    {
        return new District
        {
            label = label,
            kind = kind,
            bearing = bearing,
            distance = distance,
            size = size,
            role = role
        };
    }

    // ---- 배치 -----------------------------------------------------------

    private void BuildDistrict(District district)
    {
        Vector3 local = Dir(district.bearing) * district.distance;
        // 구역은 마을 바닥 위에 올라앉는다.
        local.y = GroundY(local) - transform.position.y + (buildGround ? groundLift : 0f);

        // 구역은 마을 가운데를 바라보게 돌려 놓는다. 그래야 건물 정면이 광장 쪽을 향한다.
        // facing으로 그 기준에서 더 돌릴 수 있다.
        Transform root = NewChild(transform, district.label, local, district.bearing + 180f + district.facing);

        VillageFacility facility = root.gameObject.AddComponent<VillageFacility>();
        facility.kind = district.kind;
        facility.label = district.label;
        facility.role = district.role;

        // 눌러서 창을 여는 구역에는 여기서 클릭 처리를 붙인다. 구역을 다시 만들 때마다 자식이 통째로
        // 지워져 씬에서 손으로 붙여둘 수 없으므로, 세우는 김에 함께 붙인다.
        // 어느 구역에 창이 딸려 있는지는 FacilityGate가 안다 — 여기는 목록을 들고 있지 않는다.
        if (FacilityGate.HasWindow(district.kind))
            root.gameObject.AddComponent<FacilityGate>().Bind(district.kind);

        if (district.prefab != null)
        {
            // 에셋이 들어오면 임시 도형은 만들지 않는다.
            GameObject instance = Instantiate(district.prefab, root);
            instance.name = district.prefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            MarkTree(instance);
            return;
        }

        // 하는 일과 세우는 도형은 따로다. 소환소인데 연금시설 건물을 쓰는 식이 가능하다.
        Kind style = district.useOtherBuilding ? district.building : district.kind;

        // 도형은 기준 크기로 짜 두고 통째로 늘린다. 인스펙터에서 size만 만져도 구역이 커진다.
        root.localScale = Vector3.one * (district.size / DesignSize(style));

        switch (style)
        {
            case Kind.Plaza:     BuildPlaza(root); break;
            case Kind.Rift:      BuildRift(root); break;
            case Kind.Synthesis: BuildSynthesis(root); break;
            case Kind.Armory:    BuildArmory(root); break;
            case Kind.Summoning: BuildSummoning(root); break;
            case Kind.Alchemy:   BuildAlchemy(root); break;
            case Kind.Airdock:   BuildAirdock(root); break;
            case Kind.Training:  BuildTraining(root); break;
            case Kind.Housing:   BuildHousing(root); break;
            case Kind.Workshop:  BuildWorkshop(root); break;
            case Kind.EquipmentWorkshop: BuildWorkshop(root); break; // 도형은 공방시설과 같다.
        }
    }

    // 각 구역을 짤 때 기준으로 삼은 반지름.
    private static float DesignSize(Kind kind)
    {
        switch (kind)
        {
            case Kind.Plaza:     return 30f;
            case Kind.Rift:      return 13f;
            case Kind.Synthesis: return 18f;
            case Kind.Armory:    return 16f;
            case Kind.Summoning: return 16f;
            case Kind.Alchemy:   return 17f;
            case Kind.Airdock:   return 20f;
            case Kind.Training:  return 22f;
            case Kind.Housing:   return 22f;
            case Kind.Workshop:  return 20f;
            case Kind.EquipmentWorkshop: return 20f;
            default:             return 18f;
        }
    }

    // 성벽 안을 통째로 덮는 바닥 한 장.
    // 구역마다 길을 내서 가운데로 모으는 대신, 이 바닥이 구역들을 그대로 이어 준다.
    //
    // 실린더를 눌러 쓰면 둥근 판이라 열두 모서리에 삼각형 틈이 남는다. 성벽과 같은 열두 각으로 직접 만든다.
    private void BuildGround()
    {
        Transform root = NewChild(transform, "바닥", Vector3.zero, groundYaw);

        Vector3 center = new Vector3(0f, GroundY(Vector3.zero) - transform.position.y + groundLift, 0f);
        MeshPiece(root, "마을 바닥", center,
            ArcSlab("VillageGround", groundSides, groundRadius, groundThickness, 0f, 360f), PathColor, true);
    }

    // 메시 하나를 그대로 붙인 조각. 도형으로 못 만드는 다각형/반원 바닥에 쓴다.
    private GameObject MeshPiece(Transform parent, string name, Vector3 center, Mesh mesh, Color color, bool solid)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        Mark(go);

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = Mat(color);
        // 그 위에 서야 하는 바닥에는 콜라이더도 물린다.
        if (solid) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    // 윗면이 y=0에 오는 부채꼴 판. sweep이 360이면 정다각형(성벽과 같은 자리에서 시작),
    // 180이면 반원이다. start는 첫 모서리의 방위.
    private Mesh ArcSlab(string name, int segments, float radius, float thickness, float start, float sweep)
    {
        bool closed = sweep >= 359.9f;
        int steps = Mathf.Max(3, segments);
        int ringCount = closed ? steps : steps + 1;   // 닫히면 마지막 점이 첫 점과 겹친다
        float step = sweep / (closed ? steps : steps);

        var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
        meshes.Add(mesh);

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        vertices.Add(Vector3.zero);
        uvs.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < ringCount; i++)
        {
            Vector3 corner = Dir(start + i * step) * radius;
            vertices.Add(corner);
            uvs.Add(new Vector2(corner.x / (radius * 2f) + 0.5f, corner.z / (radius * 2f) + 0.5f));
        }

        // 윗면. 유니티는 앞에서 봤을 때 시계 방향인 면을 앞면으로 친다.
        // 순서를 뒤집으면 윗면이 컬링돼 바닥 대신 지형이 그대로 보인다.
        for (int i = 0; i < steps; i++)
        {
            triangles.Add(0);
            triangles.Add(1 + i);
            triangles.Add(1 + (i + 1) % ringCount);
        }

        // 아랫면 고리. 지형보다 살짝 떠 있으므로 옆이 보인다.
        int bottomStart = vertices.Count;
        for (int i = 0; i < ringCount; i++)
        {
            Vector3 corner = Dir(start + i * step) * radius;
            vertices.Add(new Vector3(corner.x, -thickness, corner.z));
            uvs.Add(new Vector2(i / (float)ringCount, 0f));
        }

        for (int i = 0; i < steps; i++)
        {
            int next = (i + 1) % ringCount;
            triangles.Add(1 + i);
            triangles.Add(bottomStart + next);
            triangles.Add(1 + next);

            triangles.Add(1 + i);
            triangles.Add(bottomStart + i);
            triangles.Add(bottomStart + next);
        }

        int bottomCenter = vertices.Count;
        vertices.Add(new Vector3(0f, -thickness, 0f));
        uvs.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < steps; i++)
        {
            triangles.Add(bottomCenter);
            triangles.Add(bottomStart + (i + 1) % ringCount);
            triangles.Add(bottomStart + i);
        }

        if (!closed)
        {
            // 잘린 두 끝의 단면. 반원이면 곧은 변 쪽이다.
            AddCap(triangles, 0, 1, bottomStart, bottomCenter, true);
            AddCap(triangles, 0, ringCount, bottomStart + ringCount - 1, bottomCenter, false);
        }

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddCap(List<int> triangles, int topCenter, int topEdge, int bottomEdge, int bottomCenter, bool first)
    {
        if (first)
        {
            triangles.Add(topCenter); triangles.Add(bottomEdge); triangles.Add(topEdge);
            triangles.Add(topCenter); triangles.Add(bottomCenter); triangles.Add(bottomEdge);
        }
        else
        {
            triangles.Add(topCenter); triangles.Add(topEdge); triangles.Add(bottomEdge);
            triangles.Add(topCenter); triangles.Add(bottomEdge); triangles.Add(bottomCenter);
        }
    }

    // 구역 사이 빈 자리에 나무를 심는다. 같은 씨앗이면 언제 다시 만들어도 같은 자리에 선다.
    private void BuildTrees()
    {
        Transform root = NewChild(transform, "나무", Vector3.zero, 0f);

        var random = new System.Random(treeSeed);
        int planted = 0;
        int attempts = 0;
        var placed = new List<Vector3>();

        // 다각형 바닥이라 모서리보다 변 한가운데가 가깝다. 가장 가까운 변까지를 한계로 잡아야 벽을 뚫지 않는다.
        float limit = groundRadius * Mathf.Cos(Mathf.PI / Mathf.Max(3, groundSides)) - 7f;
        List<GameObject> prefabs = ResolveTreePrefabs();
        float spacing = treeSpacing * treeSpacing;

        // 훈련소 둘레부터 심는다. 흩뿌리기보다 먼저 자리를 잡아야 띠가 끊기지 않는다.
        // 이쪽은 숲을 이뤄야 하므로 treeCount와 따로 센다.
        TrainingForest(root, prefabs, random, placed, limit);

        // 자리를 무작위로 찍고 구역과 겹치면 버린다. 몇 번 실패해도 계속 찍되 무한정 돌지는 않는다.
        while (planted < treeCount && attempts < treeCount * 60)
        {
            attempts++;

            float bearing = (float)random.NextDouble() * 360f;
            // 반지름 제곱에 비례해 뽑아야 바깥쪽이 성기지 않다.
            float radius = Mathf.Sqrt((float)random.NextDouble()) * limit;
            Vector3 spot = Dir(bearing) * radius;

            if (Occupied(spot)) continue;

            bool tooClose = false;
            foreach (Vector3 other in placed)
            {
                if ((other - spot).sqrMagnitude < spacing) { tooClose = true; break; }
            }
            if (tooClose) continue;

            placed.Add(spot);
            // 바닥에 살짝 묻어 심는다. 정확히 바닥 높이에 두면 밑동과 바닥 사이가 뜬다.
            spot.y = GroundY(spot) - transform.position.y + (buildGround ? groundLift : 0f) - 0.05f;
            Tree(root, spot, planted, random, prefabs);
            planted++;
        }
    }

    // 훈련소의 둥근 가장자리를 따라 나무를 두 겹으로 심는다.
    // 참고 그림에서 훈련장은 성벽에 등을 대고 나무 띠에 둘러싸인 반원이다.
    private int TrainingForest(Transform root, List<GameObject> prefabs, System.Random random, List<Vector3> placed, float limit)
    {
        District training = Find(Kind.Training);
        if (training == null) return 0;

        Vector3 middle = Dir(training.bearing) * training.distance;
        int planted = 0;

        // 네 겹으로 둘러야 한 줄로 세워둔 것처럼 보이지 않고 숲이 된다.
        const int rings = 5;
        const int perRing = 20;

        for (int i = 0; i < rings * perRing; i++)
        {
            int ringIndex = i % rings;
            float t = (i / rings) / (float)(perRing - 1);
            // 마을 안쪽을 향한 반쪽만. 끝까지 돌리면 성벽 밖으로 나간다.
            // 바깥 겹일수록 조금씩 더 벌려 가장자리가 톱니처럼 보이지 않게 한다.
            float spread = 74f + ringIndex * 3f;
            float offset = Mathf.Lerp(-spread, spread, t) + (ringIndex % 2 == 0 ? 0f : 180f / perRing);
            float ring = training.size + 5f + ringIndex * 4f;

            // 한가운데로 들어가는 길은 비워 둔다. 겹마다 각도를 다시 재야 길이 부채꼴로 벌어지지 않는다.
            float gate = Mathf.Atan2(trainingPathWidth * 0.5f, ring) * Mathf.Rad2Deg;
            if (Mathf.Abs(offset) < gate) continue;

            Vector3 spot = middle + Dir(training.bearing + 180f + offset) * ring;

            if (spot.magnitude > limit) continue;
            if (Occupied(spot)) continue;

            bool tooClose = false;
            foreach (Vector3 other in placed)
            {
                if ((other - spot).sqrMagnitude < 16f) { tooClose = true; break; }   // 숲이라 마을보다 촘촘하게
            }
            if (tooClose) continue;

            placed.Add(spot);
            spot.y = GroundY(spot) - transform.position.y + (buildGround ? groundLift : 0f) - 0.05f;
            Tree(root, spot, i, random, prefabs);
            planted++;
        }
        return planted;
    }

    // 구역이 차지한 자리인지. 비행선착장은 갑판이 구역 밖으로 길게 뻗어 있어 따로 넉넉히 잡는다.
    private bool Occupied(Vector3 spot)
    {
        // 한가운데 트인 자리와 시공의 틈까지 걸어 들어가는 길목은 비워 둔다.
        if (spot.sqrMagnitude < treeCenterClear * treeCenterClear) return true;

        District rift = Find(Kind.Rift);
        if (rift != null && DistanceToSegment(spot, Vector3.zero, Dir(rift.bearing) * rift.distance) < riftApproachWidth) return true;

        if (districts == null) return false;

        foreach (District district in districts)
        {
            if (district == null || !district.build) continue;

            if (district.kind == Kind.Workshop)
            {
                // 공방은 원이 아니라 한 줄이다. 줄에서의 거리로 재야 줄 위에 나무가 서지 않는다.
                Vector3 middle = Dir(district.bearing) * district.distance;
                Vector3 axis = Dir(district.bearing + 270f) * (WorkshopHalfLength * district.size / DesignSize(Kind.Workshop));
                if (DistanceToSegment(spot, middle - axis, middle + axis) < 18f + treeMargin) return true;
                continue;
            }

            if (district.kind == Kind.Airdock)
            {
                // 갑판은 성벽 쪽으로만 길게 뻗는다. 원으로 잡으면 마을 안쪽까지 쓸데없이 비워진다.
                Vector3 dock = Dir(district.bearing) * district.distance;
                Vector3 outward = Dir(district.bearing) * (31f * district.size / DesignSize(Kind.Airdock));
                if (DistanceToSegment(spot, dock - outward * 0.3f, dock + outward) < 14f * district.size / DesignSize(Kind.Airdock) + treeMargin) return true;
                continue;
            }

            float clearance = district.size + treeMargin;
            // 시공의 틈 앞은 걸어 들어가는 자리라 더 비운다.
            if (district.kind == Kind.Rift) clearance += district.size * 1.2f;

            if ((Dir(district.bearing) * district.distance - spot).sqrMagnitude < clearance * clearance) return true;
        }
        return false;
    }

    // 성벽 밖 숲과 같은 나무를 쓴다. 지형에 등록된 프리팹을 그대로 가져오므로 안팎이 따로 놀지 않는다.
    // 인스펙터의 treePrefabs에 직접 넣으면 그게 우선한다.
    private List<GameObject> ResolveTreePrefabs()
    {
        var list = new List<GameObject>();

        if (treePrefabs != null)
        {
            foreach (GameObject prefab in treePrefabs)
                if (prefab != null) list.Add(prefab);
        }
        if (list.Count > 0) return list;

        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null || terrain.terrainData == null) return list;

        foreach (TreePrototype prototype in terrain.terrainData.treePrototypes)
        {
            GameObject prefab = prototype.prefab;
            if (prefab == null) continue;

            string name = prefab.name.ToLowerInvariant();
            if (!name.Contains("pine") && !name.Contains("spruce")) continue;
            // 그루터기와 죽은 나무는 마을 안에 두지 않는다.
            if (name.Contains("stump") || name.Contains("dead")) continue;

            // 숲에는 30m가 넘는 것도 섞여 있다. 마을 건물을 다 가리므로 크기로 거른다.
            float height = PrefabHeight(prefab);
            if (height < treeHeightRange.x || height > treeHeightRange.y) continue;

            list.Add(prefab);
        }
        return list;
    }

    private static float PrefabHeight(GameObject prefab)
    {
        var renderers = prefab.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0f;

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        return bounds.size.y;
    }

    private District Find(Kind kind)
    {
        if (districts == null) return null;
        foreach (District district in districts)
            if (district != null && district.build && district.kind == kind) return district;
        return null;
    }

    private static float DistanceToSegment(Vector3 point, Vector3 from, Vector3 to)
    {
        Vector3 along = to - from;
        float length = along.sqrMagnitude;
        if (length < 0.001f) return Vector3.Distance(point, from);

        float t = Mathf.Clamp01(Vector3.Dot(point - from, along) / length);
        return Vector3.Distance(point, from + along * t);
    }

    private void Tree(Transform parent, Vector3 spot, int index, System.Random random, List<GameObject> prefabs)
    {
        if (prefabs != null && prefabs.Count > 0)
        {
            GameObject prefab = prefabs[random.Next(prefabs.Count)];
            GameObject instance = Instantiate(prefab, parent);
            instance.name = prefab.name;
            instance.transform.localPosition = spot;
            instance.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
            instance.transform.localScale = prefab.transform.localScale * (0.75f + (float)random.NextDouble() * 0.5f);
            MarkTree(instance);
            return;
        }

        // 지형에 나무가 없는 프로젝트를 대비한 임시 도형.
        float scale = 0.8f + (float)random.NextDouble() * 0.7f;
        Transform tree = NewChild(parent, "나무", spot, (float)random.NextDouble() * 360f);
        tree.localScale = Vector3.one * scale;

        // 돌바닥 위에 나무만 서 있으면 붕 뜬다. 밑동에 잔디를 한 장 깔아 준다.
        Cyl(tree, "잔디", new Vector3(0f, 0.05f, 0f), 7f, 0.2f, Turf, false);

        if (index % 3 == 0)
        {
            // 침엽수 — 위로 갈수록 좁아지는 덩어리 셋
            Cyl(tree, "줄기", new Vector3(0f, 1.6f, 0f), 0.9f, 3.2f, Bark);
            Ball(tree, "잎", new Vector3(0f, 4.2f, 0f), 6f, LeafDark, false);
            Ball(tree, "잎", new Vector3(0f, 7f, 0f), 4.4f, LeafDark, false);
            Ball(tree, "잎", new Vector3(0f, 9.2f, 0f), 2.8f, Leaf, false);
        }
        else
        {
            // 활엽수
            Cyl(tree, "줄기", new Vector3(0f, 2.4f, 0f), 1.1f, 4.8f, Bark);
            Ball(tree, "잎", new Vector3(0f, 6.4f, 0f), 7f, Leaf, false);
            Ball(tree, "잎", new Vector3(-1.8f, 5.4f, 1f), 4.6f, LeafDark, false);
            Ball(tree, "잎", new Vector3(1.9f, 5.8f, -0.8f), 4.2f, LeafDark, false);
        }
    }

    // 구역 바닥 색. 참고 그림의 구역 색을 옅게 옮겨 왔다 — 위에서 봤을 때 경계가 읽힌다.
    private static Color ZoneColor(Kind kind)
    {
        switch (kind)
        {
            case Kind.Plaza:     return new Color(0.63f, 0.68f, 0.53f);
            case Kind.Rift:      return new Color(0.43f, 0.41f, 0.52f);
            case Kind.Synthesis: return new Color(0.71f, 0.69f, 0.63f);
            case Kind.Armory:    return new Color(0.60f, 0.58f, 0.55f);
            case Kind.Summoning: return new Color(0.73f, 0.66f, 0.51f);
            case Kind.Alchemy:   return new Color(0.49f, 0.62f, 0.69f);
            case Kind.Airdock:   return new Color(0.58f, 0.51f, 0.45f);
            case Kind.Training:  return new Color(0.55f, 0.45f, 0.55f);
            case Kind.Housing:   return new Color(0.69f, 0.53f, 0.55f);
            case Kind.Workshop:  return new Color(0.68f, 0.62f, 0.43f);
            case Kind.EquipmentWorkshop: return new Color(0.68f, 0.62f, 0.43f);
            default:             return Stone;
        }
    }

    // ---- 도형 만들기 ----------------------------------------------------

    private static Vector3 Dir(float bearing)
    {
        float radians = bearing * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
    }

    private float GroundY(Vector3 local)
    {
        Vector3 world = transform.TransformPoint(new Vector3(local.x, 0f, local.z));

        // 지형이 있으면 지형 높이를 따른다. SampleHeight는 지형 기준 높이라 지형의 y를 더해야 월드가 된다.
        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null) return terrain.SampleHeight(world) + terrain.transform.position.y;

        if (Physics.Raycast(world + Vector3.up * 500f, Vector3.down, out RaycastHit hit, 1000f)) return hit.point.y;
        return transform.position.y;
    }

    private Transform NewChild(Transform parent, string name, Vector3 localPosition, float yaw)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        Mark(go);
        return go.transform;
    }

    // 도형 하나를 세운다.
    //
    // GameObject.CreatePrimitive를 쓰지 않는 이유: 그쪽은 늘 콜라이더를 붙여서 나오는데,
    // 여기서 만드는 것의 상당수는 콜라이더가 필요 없고(난간, 바닥 문양) 실린더는 붙어 나온
    // 캡슐을 버리고 상자를 다시 다는 구조였다. 도형 수백 개를 세울 때마다 컴포넌트를 붙였다 떼는
    // 셈이라, 값을 조금 고칠 때마다 다시 만드는 에디터 작업에서 특히 체감된다.
    // 메시는 어차피 유니티 기본 도형 넷뿐이므로 한 번 꺼내 캐시해 두고 공유한다.
    private GameObject Prim(Transform parent, PrimitiveType type, string name,
        Vector3 center, Vector3 scale, Color color, Vector3 euler = default, bool solid = true, bool glow = false)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;

        go.GetComponent<MeshFilter>().sharedMesh = PrimitiveMesh(type);
        go.GetComponent<MeshRenderer>().sharedMaterial = glow ? GlowMat(color) : Mat(color);

        // 부딪힐 일 없는 것(solid=false)에는 아예 달지 않는다.
        if (solid)
        {
            switch (type)
            {
                case PrimitiveType.Sphere:
                    go.AddComponent<SphereCollider>();
                    break;
                case PrimitiveType.Cylinder:
                    // 실린더에 캡슐을 물리면 납작하게 눌렀을 때 반지름이 지름을 따라가면서
                    // 판때기가 커다란 공이 된다(기단 하나가 반지름 24짜리 돔). 보이는 대로 상자를 문다.
                    go.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);
                    break;
                default:
                    go.AddComponent<BoxCollider>();
                    break;
            }
        }

        Mark(go);
        return go;
    }

    // 유니티 기본 도형의 메시. CreatePrimitive로 한 번만 꺼내 캐시한다.
    // 여기 담기는 것은 유니티 내장 에셋이라 ClearGenerated에서 지우면 안 된다(meshes 목록과 별개).
    private static readonly Dictionary<PrimitiveType, Mesh> PrimitiveMeshes =
        new Dictionary<PrimitiveType, Mesh>();

    private static Mesh PrimitiveMesh(PrimitiveType type)
    {
        if (PrimitiveMeshes.TryGetValue(type, out Mesh cached) && cached != null) return cached;

        GameObject sample = GameObject.CreatePrimitive(type);
        Mesh mesh = sample.GetComponent<MeshFilter>().sharedMesh;
        Kill(sample);

        PrimitiveMeshes[type] = mesh;
        return mesh;
    }

    private GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Color color,
        float yaw = 0f, bool solid = true, bool glow = false)
    {
        return Prim(parent, PrimitiveType.Cube, name, center, size, color, new Vector3(0f, yaw, 0f), solid, glow);
    }

    private GameObject Tilted(Transform parent, string name, Vector3 center, Vector3 size, Color color,
        Vector3 euler, bool solid = true)
    {
        return Prim(parent, PrimitiveType.Cube, name, center, size, color, euler, solid);
    }

    // 실린더는 기본 높이가 2다. 그래서 세로 스케일에는 높이의 절반을 넣는다.
    private GameObject Cyl(Transform parent, string name, Vector3 center, float diameter, float height, Color color,
        bool solid = true, Vector3 euler = default, bool glow = false)
    {
        return Prim(parent, PrimitiveType.Cylinder, name, center,
            new Vector3(diameter, height * 0.5f, diameter), color, euler, solid, glow);
    }

    private GameObject Ball(Transform parent, string name, Vector3 center, float diameter, Color color,
        bool solid = true, bool glow = false)
    {
        return Prim(parent, PrimitiveType.Sphere, name, center, Vector3.one * diameter, color, default, solid, glow);
    }

    // 반구 지붕. center가 지붕이 앉는 높이, height는 그 위로 솟는 높이다.
    private GameObject Dome(Transform parent, string name, Vector3 center, float diameter, float height, Color color)
    {
        return Prim(parent, PrimitiveType.Sphere, name, center, new Vector3(diameter, height * 2f, diameter), color);
    }

    // 박공지붕. 용마루가 x축을 따라 놓인다. center는 벽 꼭대기 높이.
    private void Gable(Transform parent, Vector3 center, float width, float depth, float pitch, Color color)
    {
        float slope = depth * 0.5f / Mathf.Cos(pitch * Mathf.Deg2Rad);
        float lift = Mathf.Tan(pitch * Mathf.Deg2Rad) * depth * 0.25f;
        Tilted(parent, "지붕", center + new Vector3(0f, lift, -depth * 0.25f),
            new Vector3(width, 0.4f, slope), color, new Vector3(-pitch, 0f, 0f), false);
        Tilted(parent, "지붕", center + new Vector3(0f, lift, depth * 0.25f),
            new Vector3(width, 0.4f, slope), color, new Vector3(pitch, 0f, 0f), false);
    }

    // ---- 뒷정리 ---------------------------------------------------------

#if UNITY_EDITOR
    // 배치가 굳으면 지금 모습 그대로 프리팹으로 내보낸다.
    //
    // 런타임에 이 프리팹을 대신 쓰는 분기는 일부러 두지 않았다. 생성 경로가 둘이 되면
    // 한쪽만 고쳐진 채 어긋나기 시작한다. 구운 것을 쓰고 싶으면 씬에 프리팹을 놓고
    // 이 컴포넌트를 끄면 된다 — 유니티에서 늘 하던 방식 그대로다.
    //
    // 임시 머티리얼과 직접 만든 메시는 에셋이 아니라서(HideFlags.DontSave) 그냥 저장하면
    // 프리팹의 참조가 전부 끊긴다. 그래서 프리팹 옆에 컨테이너 에셋을 하나 만들어 함께 넣는다.
    [ContextMenu("프리팹으로 굽기")]
    private void BakeToPrefab()
    {
        string path = UnityEditor.EditorUtility.SaveFilePanelInProject(
            "마을 블록아웃 굽기", name + "_Baked", "prefab",
            "지금 놓여 있는 배치를 프리팹으로 저장합니다.");
        if (string.IsNullOrEmpty(path)) return;

        // 화면에 보이는 것과 구워지는 것이 다르면 안 된다. 먼저 새로 만든다.
        Rebuild();

        var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
        try
        {
            // 만들어 둔 머티리얼과 메시를 먼저 에셋으로 만든다.
            // 렌더러가 이미 이 인스턴스들을 물고 있으므로, 이 순서라야 프리팹이 제대로 참조한다.
            SaveGeneratedAssets(path);

            var children = new List<Transform>();
            foreach (Transform child in transform) children.Add(child);
            for (int i = 0; i < children.Count; i++)
            {
                ClearHideFlagsRecursive(children[i].gameObject);
                children[i].SetParent(root.transform, true);
            }

            if (UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path) == null)
            {
                Debug.LogError($"[VillageBlockout] 프리팹 저장에 실패했습니다: {path}", this);
                return;
            }

            Debug.Log($"[VillageBlockout] 구웠습니다: {path}", this);
        }
        finally
        {
            // 임시 루트와 그 안의 사본을 치우고, 편집용 배치를 원래대로 다시 세운다.
            Kill(root);
            Rebuild();
        }
    }

    // 색 머티리얼과 직접 만든 메시를 프리팹 옆 컨테이너 에셋에 담는다.
    // 첫 머티리얼이 대표 에셋이 되고 나머지는 그 안의 하위 에셋으로 들어간다 —
    // 파일이 수십 개로 흩어지지 않고, 지울 때도 한 번에 지워진다.
    private void SaveGeneratedAssets(string prefabPath)
    {
        string directory = System.IO.Path.GetDirectoryName(prefabPath);
        string baseName = System.IO.Path.GetFileNameWithoutExtension(prefabPath);
        string containerPath = (directory + "/" + baseName + "_Assets.asset").Replace('\\', '/');

        UnityEditor.AssetDatabase.DeleteAsset(containerPath);

        Object container = null;
        foreach (Material material in materials.Values) AddGenerated(material, containerPath, ref container);
        foreach (Material material in glowMaterials.Values) AddGenerated(material, containerPath, ref container);
        for (int i = 0; i < meshes.Count; i++) AddGenerated(meshes[i], containerPath, ref container);

        // 이제 이것들은 에셋이다. 캐시에 그대로 두면 다음 Rebuild의 ClearGenerated가
        // DestroyImmediate로 지우려 들고, 에셋은 그렇게 지울 수 없어 예외가 난다.
        // 목록에서 놓아주면 Rebuild가 편집용 임시 머티리얼을 새로 만들어 쓴다.
        ForgetGenerated();

        if (container == null) return;

        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.ImportAsset(containerPath);
    }

    private static void AddGenerated(Object generated, string containerPath, ref Object container)
    {
        if (generated == null) return;

        // 에셋이 되려면 "저장하지 않음" 표시를 먼저 떼야 한다.
        generated.hideFlags = HideFlags.None;

        if (container == null)
        {
            UnityEditor.AssetDatabase.CreateAsset(generated, containerPath);
            container = generated;
            return;
        }

        UnityEditor.AssetDatabase.AddObjectToAsset(generated, container);
    }

    private static void ClearHideFlagsRecursive(GameObject go)
    {
        go.hideFlags = HideFlags.None;
        foreach (Transform child in go.transform) ClearHideFlagsRecursive(child.gameObject);
    }
#endif

    private void Mark(GameObject go)
    {
        // 씬 파일에 임시 배치가 통째로 들어가지 않게 한다. 씬을 열 때마다 다시 만든다.
        // DontSave가 아니라 DontSaveInEditor인 이유: DontSave는 씬을 바꿔도 안 지워져 전투 씬까지 따라간다.
        go.hideFlags = Application.isPlaying ? HideFlags.None : HideFlags.DontSaveInEditor;
    }

    private void MarkTree(GameObject go)
    {
        Mark(go);
        foreach (Transform child in go.transform) MarkTree(child.gameObject);
    }

    private void ClearChildren()
    {
        // 이 오브젝트 밑은 전부 이 스크립트가 만든 것으로 본다.
        for (int i = transform.childCount - 1; i >= 0; i--)
            Kill(transform.GetChild(i).gameObject);
    }

    // 임시로 찍어낸 머티리얼과 메시. 에셋이 아니라서 직접 지우지 않으면 그대로 쌓인다.
    private void ClearGenerated()
    {
        foreach (Material material in materials.Values) Kill(material);
        foreach (Material material in glowMaterials.Values) Kill(material);
        foreach (Mesh mesh in meshes) Kill(mesh);
        ForgetGenerated();
    }

    // 지우지 않고 목록만 비운다. 구워서 에셋이 된 것들을 놓아줄 때 쓴다 —
    // 에셋은 DestroyImmediate로 지울 수 없어서, 캐시에 남겨 두면 다음 Rebuild가 예외를 낸다.
    private void ForgetGenerated()
    {
        materials.Clear();
        glowMaterials.Clear();
        meshes.Clear();
    }

    private Material Mat(Color color)
    {
        if (materials.TryGetValue(color, out Material cached) && cached != null) return cached;

        Material material = NewMaterial(color);
        materials[color] = material;
        return material;
    }

    private Material GlowMat(Color color)
    {
        if (glowMaterials.TryGetValue(color, out Material cached) && cached != null) return cached;

        Material material = NewMaterial(color);
        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 1.6f);
        glowMaterials[color] = material;
        return material;
    }

    private static Material NewMaterial(Color color)
    {
        // URP가 없는 프로젝트에서도 색은 나오도록 빌트인 셰이더로 떨어진다.
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        var material = new Material(shader)
        {
            name = "Blockout " + ColorUtility.ToHtmlStringRGB(color),
            hideFlags = HideFlags.DontSave   // 에셋으로 남기지 않는다
        };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.12f);
        return material;
    }

    private static void Kill(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    // ---- 구역별 임시 도형 ------------------------------------------------
    // 아래 숫자들은 전부 DesignSize 기준이다. 구역 루트가 통째로 늘어나므로 여기서는 비율만 맞추면 된다.

    // 광장 — 마을 한가운데. 길이 여기서 갈라진다.
    private void BuildPlaza(Transform root)
    {
        Cyl(root, "바닥", new Vector3(0f, 0.1f, 0f), 60f, 0.2f, ZoneColor(Kind.Plaza), false);
        Cyl(root, "안쪽 원", new Vector3(0f, 0.24f, 0f), 40f, 0.12f, StoneDark, false);

        // 광장 무늬는 겹친 원으로만 만든다.
        // 가운데로 뻗는 살을 두면 길이 광장으로 모여드는 것처럼 보인다.
        Cyl(root, "무늬 바깥", new Vector3(0f, 0.33f, 0f), 26f, 0.14f, StoneLight, false);
        Cyl(root, "무늬 안", new Vector3(0f, 0.42f, 0f), 21f, 0.14f, ZoneColor(Kind.Plaza), false);
        Cyl(root, "가운데 무늬", new Vector3(0f, 0.51f, 0f), 9f, 0.12f, StoneLight, false);

        // 기념비와 가로등은 두지 않는다. 시공의 틈 바로 앞이라 시야를 막는다.
        // 바닥 무늬와 화단만 남긴다.
        for (int i = 0; i < 4; i++)
        {
            float bearing = 45f + i * 90f;
            Box(root, "화단", Dir(bearing) * 17f + new Vector3(0f, 0.5f, 0f),
                new Vector3(7f, 1f, 2.4f), StoneDark, bearing + 90f);
        }
    }

    // 시공의 틈 — 탑의 층으로 들어가는 입구.
    // 성벽 안쪽 면에 붙여 세운다. 그래서 뒷면이 로컬 z=0이고, 단과 계단은 마을 쪽(+Z)으로만 뻗는다.
    // 문틀은 성벽(높이 16)보다 높아 밖에서도 이 자리가 보인다.
    private void BuildRift(Transform root)
    {
        // 단과 계단은 두지 않는다. 마을 바닥이 벽까지 이어져 있어 문틀만 세우는 편이 깔끔하다.

        // 벽에 기대 세운 문틀
        Box(root, "기둥", new Vector3(-7.6f, 7.5f, 1.6f), new Vector3(2.4f, 15f, 3.2f), StoneLight);
        Box(root, "기둥", new Vector3(7.6f, 7.5f, 1.6f), new Vector3(2.4f, 15f, 3.2f), StoneLight);
        Box(root, "상인방", new Vector3(0f, 16.1f, 1.6f), new Vector3(19f, 2.2f, 3.6f), StoneLight);
        Box(root, "종석", new Vector3(0f, 17.8f, 1.6f), new Vector3(3.4f, 1.4f, 3.8f), StoneDark);

        // 벽이 갈라진 자리. 여기를 눌러 원정을 떠난다 — 클릭은 구역 루트의 FacilityGate가 받는다.
        // 여기에 따로 클릭 부품을 붙이면 안 된다. EventSystem은 콜라이더에서 위로 올라가며 가장 먼저
        // 만나는 처리기에 클릭을 주므로, 자식에 붙이는 순간 구역 루트의 게이트가 영영 안 불린다.
        // 어두운 판만 단단하게 두고 앞에 겹치는 빛나는 판에는 콜라이더를 두지 않는다.
        Transform gate = NewChild(root, "시공의 틈 입구", new Vector3(0f, 0f, 0.5f), 0f);
        Box(gate, "틈", new Vector3(0f, 7f, 0f), new Vector3(12.6f, 14.4f, 1f), RiftDark);
        Box(gate, "틈 빛", new Vector3(0f, 7f, 0.7f), new Vector3(10f, 11.8f, 0.4f), Accent, 0f, false, true);

        // 틈에서 떨어져 나와 떠 있는 파편. 벽을 파고들지 않게 전부 앞쪽에 둔다.
        for (int i = 0; i < 7; i++)
        {
            var spot = new Vector3(-9f + i * 3f, 3.5f + (i % 4) * 3.4f, 3f + (i % 3) * 2.5f);
            Tilted(root, "파편", spot, new Vector3(1.4f, 2.2f, 1f), RiftDark,
                new Vector3(20f + i * 13f, i * 47f, 35f - i * 9f), false);
        }

        // 단 양옆을 지키는 결계석
        for (int i = 0; i < 4; i++)
        {
            var spot = new Vector3(i < 2 ? -11f : 11f, 0f, i % 2 == 0 ? 6f : 15f);
            Cyl(root, "결계석", spot + new Vector3(0f, 1.4f, 0f), 1.6f, 2.8f, StoneDark);
            Ball(root, "결계 불빛", spot + new Vector3(0f, 3.2f, 0f), 1f, Accent, false, true);
        }
    }

    // 합성소 — 재료를 합쳐 새 물건을 만든다. 참고 그림에서 바퀴 무늬 지붕을 얹은 둥근 건물.
    private void BuildSynthesis(Transform root)
    {
        Cyl(root, "기단", new Vector3(0f, 0.6f, 0f), 40f, 1.2f, ZoneColor(Kind.Synthesis));
        Box(root, "계단", new Vector3(0f, 0.9f, 20.5f), new Vector3(14f, 0.5f, 3f), StoneDark, 0f, false);
        Box(root, "계단", new Vector3(0f, 0.4f, 23f), new Vector3(14f, 0.5f, 3f), StoneDark, 0f, false);

        Cyl(root, "본채", new Vector3(0f, 6.2f, 0f), 26f, 10f, StoneLight);

        // 처마를 받치는 기둥 열두 개
        for (int i = 0; i < 12; i++)
        {
            float bearing = i * 30f;
            Cyl(root, "기둥", Dir(bearing) * 15.5f + new Vector3(0f, 6.2f, 0f), 1.8f, 10f, Stone);
        }

        Cyl(root, "처마", new Vector3(0f, 11.7f, 0f), 36f, 1.2f, StoneDark, false);
        Dome(root, "돔", new Vector3(0f, 12.2f, 0f), 26f, 9f, StoneLight);

        // 돔 위 바퀴 무늬
        Cyl(root, "무늬 테", new Vector3(0f, 20.6f, 0f), 13f, 0.5f, StoneDark, false);
        for (int i = 0; i < 8; i++)
        {
            float bearing = i * 45f;
            Box(root, "무늬 살", Dir(bearing) * 3.2f + new Vector3(0f, 20.9f, 0f),
                new Vector3(0.7f, 0.3f, 6.4f), Accent, bearing, false, true);
        }

        Cyl(root, "첨탑", new Vector3(0f, 22.8f, 0f), 3.2f, 4f, StoneDark);
        Ball(root, "첨탑 구슬", new Vector3(0f, 26f, 0f), 3f, Accent, true, true);

        Box(root, "문", new Vector3(0f, 4.5f, 13.2f), new Vector3(6f, 7f, 1f), WoodDark);
    }

    // 무기창고 — 무기와 장비를 넣어 두고 꺼내 쓴다.
    private void BuildArmory(Transform root)
    {
        Box(root, "바닥", new Vector3(0f, 0.15f, 0f), new Vector3(36f, 0.3f, 26f), ZoneColor(Kind.Armory), 0f, false);
        Box(root, "본채", new Vector3(0f, 5.2f, 0f), new Vector3(28f, 10f, 15f), StoneDark);
        Gable(root, new Vector3(0f, 10.2f, 0f), 30f, 17f, 30f, Roof);

        // 벽을 받치는 부벽
        for (int i = 0; i < 4; i++)
        {
            float x = (i < 2 ? -14.6f : 14.6f);
            float z = (i % 2 == 0 ? -4.5f : 4.5f);
            Box(root, "부벽", new Vector3(x, 4f, z), new Vector3(1.8f, 8f, 2.6f), Stone);
        }

        Box(root, "문틀", new Vector3(0f, 4.2f, 7.7f), new Vector3(7.6f, 8.4f, 0.6f), Stone);
        Box(root, "문", new Vector3(0f, 3.6f, 8.1f), new Vector3(6f, 7f, 0.5f), WoodDark);

        // 밖에 세워 둔 무기 걸이
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 rack = new Vector3(11.5f * side, 0f, 11f);
            Cyl(root, "걸이 기둥", rack + new Vector3(-2.5f, 1.6f, 0f), 0.4f, 3.2f, Wood);
            Cyl(root, "걸이 기둥", rack + new Vector3(2.5f, 1.6f, 0f), 0.4f, 3.2f, Wood);
            Box(root, "걸이 가로대", rack + new Vector3(0f, 3f, 0f), new Vector3(6f, 0.3f, 0.3f), Wood, 0f, false);
            for (int i = 0; i < 5; i++)
            {
                Box(root, "무기", rack + new Vector3(-2f + i, 1.6f, 0f),
                    new Vector3(0.25f, 3.2f, 0.25f), Metal, 0f, false);
            }
        }

        // 짐
        for (int i = 0; i < 5; i++)
        {
            Vector3 spot = new Vector3(-13f + i * 6.5f, 1f, 15f);
            Box(root, "상자", spot, new Vector3(2.4f, 2f, 2.4f), Wood, 12f * i);
        }
        Cyl(root, "통", new Vector3(15f, 1.3f, 15f), 2.2f, 2.6f, WoodDark);
        Cyl(root, "통", new Vector3(-15f, 1.3f, 15f), 2.2f, 2.6f, WoodDark);
    }

    // 소환소 — 새 동료를 불러낸다. 바닥에 새겨진 큰 마법진이 이 구역의 전부다.
    private void BuildSummoning(Transform root)
    {
        Cyl(root, "바닥", new Vector3(0f, 0.25f, 0f), 32f, 0.5f, ZoneColor(Kind.Summoning), false);
        Cyl(root, "테두리", new Vector3(0f, 0.55f, 0f), 24f, 0.16f, StoneDark, false);
        Cyl(root, "마법진", new Vector3(0f, 0.64f, 0f), 20f, 0.1f, Accent, false, default, true);

        for (int i = 0; i < 8; i++)
        {
            float bearing = i * 45f;
            Box(root, "룬", Dir(bearing) * 7f + new Vector3(0f, 0.72f, 0f),
                new Vector3(0.8f, 0.1f, 9f), StoneDark, bearing, false);
        }

        // 마법진을 둘러싼 선돌
        for (int i = 0; i < 8; i++)
        {
            float bearing = 22.5f + i * 45f;
            Vector3 spot = Dir(bearing) * 14f;
            Prim(root, PrimitiveType.Cube, "선돌", spot + new Vector3(0f, 4f, 0f),
                new Vector3(2.6f, 8f, 1.8f), Stone, new Vector3(3f, bearing, i % 2 == 0 ? 2.5f : -2.5f));
        }

        // 가운데 제단
        Box(root, "제단", new Vector3(0f, 0.9f, 0f), new Vector3(4.6f, 1.8f, 4.6f), StoneLight);
        Ball(root, "제물 구슬", new Vector3(0f, 3.1f, 0f), 2.4f, Accent, true, true);

        // 화톳불
        for (int i = 0; i < 4; i++)
        {
            float bearing = 45f + i * 90f;
            Vector3 spot = Dir(bearing) * 9.5f;
            Cyl(root, "화톳불", spot + new Vector3(0f, 1.3f, 0f), 1.8f, 2.6f, StoneDark);
            Ball(root, "불", spot + new Vector3(0f, 3f, 0f), 1.4f, Ember, false, true);
        }
    }

    // 연금시설 — 물약과 마력 재료를 다룬다. 참고 그림에서 큰 구슬을 품은 돔 건물.
    private void BuildAlchemy(Transform root)
    {
        Cyl(root, "기단", new Vector3(0f, 0.4f, 0f), 34f, 0.8f, ZoneColor(Kind.Alchemy));
        Cyl(root, "본채", new Vector3(0f, 4.8f, 0f), 20f, 8f, Teal);
        Cyl(root, "처마", new Vector3(0f, 9.2f, 0f), 23f, 0.8f, StoneDark, false);
        Dome(root, "돔", new Vector3(0f, 9.4f, 0f), 20f, 7f, StoneLight);
        Cyl(root, "환기탑", new Vector3(0f, 17f, 0f), 2.6f, 3f, StoneDark);

        // 기둥이 받치고 있는 큰 구슬. 참고 그림에서 가장 눈에 띄는 것.
        Cyl(root, "구슬 받침", new Vector3(0f, 1.4f, 11f), 10f, 1.2f, StoneDark);
        for (int i = 0; i < 4; i++)
        {
            float bearing = 45f + i * 90f;
            Prim(root, PrimitiveType.Cube, "받침 발", Dir(bearing) * 3.6f + new Vector3(0f, 3f, 11f),
                new Vector3(0.8f, 4.2f, 0.8f), Metal, new Vector3(12f * Mathf.Cos(bearing * Mathf.Deg2Rad), bearing, 12f * Mathf.Sin(bearing * Mathf.Deg2Rad)));
        }
        Ball(root, "큰 구슬", new Vector3(0f, 8f, 11f), 8f, Accent, true, true);

        // 증류 통과 배관
        for (int i = 0; i < 3; i++)
        {
            Vector3 spot = new Vector3(-12f + i * 12f, 0f, -12f);
            Cyl(root, "증류통", spot + new Vector3(0f, 2.6f, 0f), 5f, 5.2f, Metal);
            Cyl(root, "뚜껑", spot + new Vector3(0f, 5.4f, 0f), 5.6f, 0.5f, StoneDark, false);
            Cyl(root, "배관", spot + new Vector3(0f, 4.6f, 5f), 0.7f, 9f, Metal, false, new Vector3(90f, 0f, 0f));
        }
        Cyl(root, "굴뚝", new Vector3(9f, 12f, -6f), 2f, 8f, StoneDark);

        Box(root, "문", new Vector3(0f, 3.4f, 10.2f), new Vector3(5f, 6f, 0.8f), WoodDark);
    }

    // 비행선착장 — 비행선이 드나드는 자리. 성벽 쪽(-Z)으로 갑판이 뻗는다.
    private void BuildAirdock(Transform root)
    {
        // 갑판이 성벽 쪽으로 길게 뻗으므로 구역 바닥도 그 밑까지 깔아 둔다.
        Box(root, "바닥", new Vector3(0f, 0.15f, -6f), new Vector3(28f, 0.3f, 50f), ZoneColor(Kind.Airdock), 0f, false);

        // 갑판을 받치는 다리
        for (int i = 0; i < 3; i++)
        {
            float z = -6f - i * 12f;
            Cyl(root, "다리", new Vector3(-9f, 4.6f, z), 2.6f, 9.2f, StoneDark);
            Cyl(root, "다리", new Vector3(9f, 4.6f, z), 2.6f, 9.2f, StoneDark);
        }

        Box(root, "갑판", new Vector3(0f, 9.6f, -12f), new Vector3(24f, 1f, 46f), Wood);

        // 난간
        for (int side = -1; side <= 1; side += 2)
        {
            Box(root, "난간", new Vector3(11.8f * side, 10.9f, -12f), new Vector3(0.4f, 1.4f, 46f), WoodDark, 0f, false);
            for (int i = 0; i < 8; i++)
                Box(root, "난간 기둥", new Vector3(11.8f * side, 10.9f, -34f + i * 6.4f),
                    new Vector3(0.7f, 1.8f, 0.7f), WoodDark, 0f, false);
        }

        // 광장에서 갑판으로 올라가는 경사로
        Tilted(root, "경사로", new Vector3(0f, 5.5f, 16f), new Vector3(9f, 0.7f, 19f), Wood,
            new Vector3(30f, 0f, 0f));

        // 계류탑
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 mast = new Vector3(8.5f * side, 0f, -28f);
            Cyl(root, "계류탑", mast + new Vector3(0f, 22f, 0f), 2.4f, 24f, Metal);
            Box(root, "계류 가로대", mast + new Vector3(0f, 31f, 0f), new Vector3(7f, 0.5f, 0.5f), Metal, 0f, false);
            Box(root, "계류 가로대", mast + new Vector3(0f, 27f, 0f), new Vector3(5.5f, 0.5f, 0.5f), Metal, 0f, false);
        }

        // 관제실과 짐
        Box(root, "관제실", new Vector3(8f, 12.6f, 2f), new Vector3(6f, 5f, 6f), StoneLight);
        Gable(root, new Vector3(8f, 15.1f, 2f), 7f, 7f, 28f, Roof);
        for (int i = 0; i < 4; i++)
            Box(root, "짐", new Vector3(-8f + (i % 2) * 3f, 11.3f, -4f - i * 3.4f),
                new Vector3(2.4f, 2.4f, 2.4f), Wood, 15f * i);

        // 정박한 비행선. 성벽 높이(16)보다 위에 떠 있어 곧 날아갈 것처럼 보인다.
        Prim(root, PrimitiveType.Sphere, "비행선 선체", new Vector3(0f, 30f, -20f),
            new Vector3(11f, 8f, 28f), StoneLight, default, false);
        Box(root, "비행선 곤돌라", new Vector3(0f, 24.6f, -20f), new Vector3(4.5f, 3f, 12f), Wood, 0f, false);
        Tilted(root, "비행선 날개", new Vector3(0f, 30f, -33f), new Vector3(12f, 0.5f, 5f), WoodDark,
            new Vector3(0f, 0f, 18f), false);
        Tilted(root, "비행선 꼬리", new Vector3(0f, 33f, -33f), new Vector3(0.5f, 6f, 5f), WoodDark,
            default, false);
    }

    // 훈련소 — 동료를 훈련시켜 능력을 올린다.
    // 곧은 변이 성벽에 닿고 둥근 쪽이 마을을 향하는 반원. 둘레는 BuildTrees가 숲으로 두른다.
    private void BuildTraining(Transform root)
    {
        // 로컬 +Z가 마을 안쪽이므로 -90도에서 +90도까지가 마을을 향한 반쪽이다.
        MeshPiece(root, "구역 바닥", new Vector3(0f, 0.15f, 0f),
            ArcSlab("TrainingGround", 24, 22f, 0.3f, -90f, 180f), ZoneColor(Kind.Training), false);
        MeshPiece(root, "흙바닥", new Vector3(0f, 0.32f, 0f),
            ArcSlab("TrainingDirt", 24, 18f, 0.2f, -90f, 180f), Dirt, false);

        // 둥근 가장자리를 따라 놓은 경계석. 숲이 시작되는 자리를 알려 준다.
        // 한가운데 들어오는 길목(0도 언저리)은 비워 둔다.
        for (int i = 0; i <= 10; i++)
        {
            float angle = -85f + i * 17f;
            if (Mathf.Abs(angle) < 12f) continue;
            Cyl(root, "경계석", Dir(angle) * 21f + new Vector3(0f, 0.5f, 0f), 1.4f, 1f, StoneDark);
        }

        // 숲을 가로질러 마당 한가운데로 들어오는 길.
        Box(root, "들어오는 길", new Vector3(0f, 0.2f, 28f), new Vector3(11f, 0.25f, 21f), PathColor, 0f, false);

        // 곧은 변(성벽) 쪽에 붙인 과녁
        for (int i = 0; i < 3; i++)
        {
            var spot = new Vector3(-9f + i * 9f, 0f, 3f);
            Box(root, "과녁판", spot + new Vector3(0f, 2.4f, 0f), new Vector3(3.4f, 3.4f, 0.4f), Wood);
            Cyl(root, "과녁 무늬", spot + new Vector3(0f, 2.4f, 0.3f), 2.2f, 0.2f, RoofRed, false, new Vector3(90f, 0f, 0f));
            Tilted(root, "과녁 받침", spot + new Vector3(0f, 1.2f, -1.2f), new Vector3(0.4f, 3f, 0.4f), Wood,
                new Vector3(-20f, 0f, 0f), false);
        }

        // 마당 가운데 늘어선 허수아비
        for (int i = 0; i < 5; i++)
        {
            var spot = new Vector3(-12f + i * 6f, 0f, 11f);
            Cyl(root, "허수아비 기둥", spot + new Vector3(0f, 1.8f, 0f), 0.7f, 3.6f, Wood);
            Box(root, "허수아비 몸", spot + new Vector3(0f, 2.6f, 0f), new Vector3(1.8f, 1.8f, 0.9f), WoodDark);
            Box(root, "허수아비 팔", spot + new Vector3(0f, 3f, 0f), new Vector3(3.6f, 0.4f, 0.4f), Wood, 0f, false);
            Ball(root, "허수아비 머리", spot + new Vector3(0f, 4.1f, 0f), 1.1f, Sand);
        }

        // 교관이 서는 단
        Box(root, "단", new Vector3(14f, 0.6f, 7f), new Vector3(7f, 1.2f, 7f), Stone);
        Box(root, "계단", new Vector3(14f, 0.3f, 11f), new Vector3(4f, 0.6f, 2f), StoneDark, 0f, false);

        // 무기 걸이와 짐
        Cyl(root, "걸이 기둥", new Vector3(-16f, 1.4f, 7f), 0.4f, 2.8f, Wood);
        Cyl(root, "걸이 기둥", new Vector3(-11f, 1.4f, 7f), 0.4f, 2.8f, Wood);
        Box(root, "걸이 가로대", new Vector3(-13.5f, 2.6f, 7f), new Vector3(5.6f, 0.3f, 0.3f), Wood, 0f, false);
        for (int i = 0; i < 4; i++)
            Box(root, "훈련용 무기", new Vector3(-15.4f + i * 1.3f, 1.4f, 7f), new Vector3(0.3f, 2.8f, 0.3f), WoodDark, 0f, false);
    }

    // 숙소 — 동료들이 쉬며 스트레스를 회복한다. 작은 집이 골목을 사이에 두고 늘어선다.
    private void BuildHousing(Transform root)
    {
        Box(root, "바닥", new Vector3(0f, 0.12f, 0f), new Vector3(44f, 0.25f, 34f), ZoneColor(Kind.Housing), 0f, false);

        for (int column = 0; column < 3; column++)
        {
            for (int row = 0; row < 3; row++)
            {
                if (column == 1 && row == 1) continue;   // 가운데는 우물 자리

                var spot = new Vector3(-14f + column * 14f, 0f, -11f + row * 11f);
                Color wall = (column + row) % 2 == 0 ? Sand : StoneLight;
                Color roof = (column + row) % 2 == 0 ? Roof : RoofRed;

                Box(root, "집", spot + new Vector3(0f, 2.8f, 0f), new Vector3(8f, 5f, 7f), wall);
                Gable(root, spot + new Vector3(0f, 5.3f, 0f), 9f, 8.4f, 32f, roof);
                Cyl(root, "굴뚝", spot + new Vector3(2.6f, 7.4f, -2f), 1f, 3f, StoneDark, false);
                Box(root, "문", spot + new Vector3(0f, 1.6f, 3.6f), new Vector3(1.6f, 2.6f, 0.4f), WoodDark, 0f, false);
                Box(root, "창", spot + new Vector3(-2.4f, 3.4f, 3.6f), new Vector3(1.4f, 1.4f, 0.4f), Teal, 0f, false);
                Box(root, "창", spot + new Vector3(2.4f, 3.4f, 3.6f), new Vector3(1.4f, 1.4f, 0.4f), Teal, 0f, false);
            }
        }

        // 가운데 우물
        Cyl(root, "우물", new Vector3(0f, 0.8f, 0f), 4.2f, 1.6f, Stone);
        Cyl(root, "우물 구멍", new Vector3(0f, 1.6f, 0f), 3.2f, 0.2f, StoneDark, false);
        Cyl(root, "우물 기둥", new Vector3(-1.8f, 2.6f, 0f), 0.4f, 4f, Wood, false);
        Cyl(root, "우물 기둥", new Vector3(1.8f, 2.6f, 0f), 0.4f, 4f, Wood, false);
        Gable(root, new Vector3(0f, 4.6f, 0f), 5f, 4f, 30f, Roof);

        // 골목 등불
        for (int i = 0; i < 4; i++)
        {
            var spot = new Vector3(-7f + (i % 2) * 14f, 0f, -5.5f + (i / 2) * 11f);
            Cyl(root, "등불 기둥", spot + new Vector3(0f, 1.8f, 0f), 0.3f, 3.6f, WoodDark, false);
            Ball(root, "등불", spot + new Vector3(0f, 3.9f, 0f), 0.9f, Ember, false, true);
        }
    }

    // 공방시설 — 9시에서 3시까지 마을을 가로지르는 한 줄.
    // 다른 구역처럼 뭉쳐 있지 않고 가운뎃길을 사이에 두고 작업장이 마주 본다.
    // 길이는 size에 비례한다(기준 20에서 좌우로 96씩).
    private const float WorkshopHalfLength = 108f;

    private void BuildWorkshop(Transform root)
    {
        Box(root, "바닥", new Vector3(0f, 0.15f, 0f),
            new Vector3(WorkshopHalfLength * 2f, 0.3f, 28f), ZoneColor(Kind.Workshop), 0f, false);

        for (int i = 0; i < 9; i++)
        {
            float x = -96f + i * 24f;
            float side = i % 2 == 0 ? -8.5f : 8.5f;   // 길 양옆으로 번갈아 앉는다
            float width = 12f + (i % 3) * 2f;
            float height = 5f + (i % 2) * 1.2f;
            Color wall = i % 2 == 0 ? StoneDark : Wood;
            Color roof = i % 3 == 0 ? RoofRed : Roof;

            var spot = new Vector3(x, 0f, side);
            Box(root, "작업장", spot + new Vector3(0f, height * 0.5f + 0.3f, 0f), new Vector3(width, height, 9f), wall);
            Gable(root, spot + new Vector3(0f, height + 0.3f, 0f), width + 1f, 10.5f, 26f, roof);
            Cyl(root, "굴뚝", spot + new Vector3(width * 0.3f, height + 2.4f, side > 0f ? 2.5f : -2.5f), 1.6f, 5f, StoneDark, false);
        }

        // 줄 한가운데의 대장간. 지붕만 얹어 안이 보인다.
        for (int i = 0; i < 4; i++)
            Cyl(root, "기둥", new Vector3(-6f + (i % 2) * 12f, 2.2f, (i / 2) * 7f - 3.5f), 0.6f, 4.4f, Wood);
        Tilted(root, "대장간 지붕", new Vector3(0f, 5.2f, 0f), new Vector3(15f, 0.4f, 11f), Roof,
            new Vector3(14f, 0f, 0f), false);
        Box(root, "화덕", new Vector3(-2f, 1.5f, -2f), new Vector3(5f, 3f, 4f), StoneDark);
        Box(root, "불", new Vector3(-2f, 2.6f, -2f), new Vector3(2.6f, 1.2f, 2f), Ember, 0f, false, true);
        Cyl(root, "화덕 연통", new Vector3(-2f, 5.5f, -2f), 1.2f, 5f, Metal, false);

        // 길을 따라 늘어놓은 살림살이
        for (int i = 0; i < 4; i++)
        {
            float x = -60f + i * 40f;
            Cyl(root, "그루터기", new Vector3(x, 0.6f, 2.5f), 1.8f, 1.2f, WoodDark);
            Box(root, "모루", new Vector3(x, 1.6f, 2.5f), new Vector3(2.2f, 0.9f, 1.1f), Metal);
            Cyl(root, "물통", new Vector3(x + 6f, 1.1f, -2.5f), 3f, 2.2f, WoodDark);

            for (int log = 0; log < 3; log++)
                Cyl(root, "통나무", new Vector3(x - 8f + (log % 2) * 1.6f, 0.8f + (log / 2) * 1.4f, -3f),
                    1.4f, 6f, Wood, false, new Vector3(0f, 90f, 90f));
        }

        Cyl(root, "숫돌", new Vector3(24f, 1.6f, 3f), 3f, 0.6f, Stone, true, new Vector3(90f, 0f, 0f));
        Box(root, "숫돌 틀", new Vector3(24f, 0.8f, 3f), new Vector3(3.4f, 1.6f, 0.4f), Wood, 0f, false);
    }

#if UNITY_EDITOR
    // 씬 뷰에서 구역 이름과 차지하는 넓이를 보여준다. 배치를 옮길 때 눈으로 맞추기 쉬우라고.
    private void OnDrawGizmosSelected()
    {
        if (districts == null) return;

        foreach (District district in districts)
        {
            if (district == null) continue;

            Vector3 center = transform.TransformPoint(Dir(district.bearing) * district.distance);
            center.y = GroundY(Dir(district.bearing) * district.distance);

            UnityEditor.Handles.color = district.build ? new Color(0.4f, 0.9f, 0.9f, 0.9f) : new Color(1f, 0.5f, 0.4f, 0.6f);
            UnityEditor.Handles.DrawWireDisc(center, Vector3.up, district.size);
            UnityEditor.Handles.Label(center + Vector3.up * 4f, district.label);
        }
    }
#endif
}
