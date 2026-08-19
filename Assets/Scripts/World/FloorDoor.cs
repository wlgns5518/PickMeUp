using UnityEngine;
using UnityEngine.EventSystems;

// 메인 씬에 세워둔 문. 누르면 층 선택 창이 열린다.
//
// 클릭은 직접 레이를 쏘지 않고 EventSystem으로 받는다. 그래야 UI 위를 누른 클릭이
// 문까지 새지 않는다(카드 편성 패널이 문 앞을 덮고 있어도 카드가 먼저 먹는다).
// 대신 3D 콜라이더가 이벤트를 받으려면 카메라에 PhysicsRaycaster가 있어야 한다.
[DisallowMultipleComponent]
public class FloorDoor : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI")]
    [Tooltip("문을 눌렀을 때 열 층 선택 창. 비워두면 씬에서 찾는다.")]
    [SerializeField] private FloorSelectUI floorSelect;

    [Header("Highlight")]
    [Tooltip("마우스를 올렸을 때 밝아질 렌더러. 비워두면 자식 전부를 쓴다.")]
    [SerializeField] private Renderer[] highlightTargets;
    [SerializeField, Range(1f, 3f)] private float hoverBrightness = 1.4f;

    // URP Lit은 _BaseColor, 빌트인/구형 셰이더는 _Color를 쓴다. 있는 쪽에만 넣는다.
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private Color[] baseColors;
    private MaterialPropertyBlock block;

    private void Awake()
    {
        if (floorSelect == null) floorSelect = FindAnyObjectByType<FloorSelectUI>(FindObjectsInactive.Include);
        if (floorSelect == null)
            Debug.LogWarning("[FloorDoor] 층 선택 창을 찾지 못해 문을 눌러도 아무 일도 일어나지 않습니다.", this);

        if (highlightTargets == null || highlightTargets.Length == 0)
            highlightTargets = GetComponentsInChildren<Renderer>();

        // 머티리얼을 복제하면 문 하나마다 인스턴스가 생긴다. 색만 덮어쓰는 프로퍼티 블록을 쓴다.
        block = new MaterialPropertyBlock();
        baseColors = new Color[highlightTargets.Length];
        for (int i = 0; i < highlightTargets.Length; i++)
            baseColors[i] = ReadBaseColor(highlightTargets[i]);
    }

    private void Start()
    {
        // 이게 없으면 문은 클릭 이벤트를 아예 받지 못한다. 조용히 안 눌리는 것보다 알려주는 편이 낫다.
        Camera cam = Camera.main;
        if (cam != null && cam.GetComponent<PhysicsRaycaster>() == null)
            Debug.LogWarning("[FloorDoor] 메인 카메라에 PhysicsRaycaster가 없어 문을 누를 수 없습니다.", this);

        if (GetComponentInChildren<Collider>() == null)
            Debug.LogWarning("[FloorDoor] 콜라이더가 없어 문을 누를 수 없습니다.", this);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (floorSelect == null) return;

        // 열려 있을 때 다시 누르면 닫는다. 창을 닫을 방법이 닫기 버튼 하나뿐이면 답답하다.
        floorSelect.Toggle();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        ApplyHighlight(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ApplyHighlight(false);
    }

    // 창이 닫힌 채로 문이 사라지면 강조가 남는다. 꺼질 때 원래 색으로 돌려둔다.
    private void OnDisable()
    {
        ApplyHighlight(false);
    }

    private void ApplyHighlight(bool on)
    {
        if (highlightTargets == null || block == null) return;

        for (int i = 0; i < highlightTargets.Length; i++)
        {
            Renderer target = highlightTargets[i];
            if (target == null) continue;

            Color color = baseColors[i];
            if (on) color = new Color(color.r * hoverBrightness, color.g * hoverBrightness, color.b * hoverBrightness, color.a);

            target.GetPropertyBlock(block);
            Material material = target.sharedMaterial;
            if (material != null && material.HasProperty(BaseColorId)) block.SetColor(BaseColorId, color);
            if (material != null && material.HasProperty(ColorId)) block.SetColor(ColorId, color);
            target.SetPropertyBlock(block);
        }
    }

    private static Color ReadBaseColor(Renderer target)
    {
        Material material = target != null ? target.sharedMaterial : null;
        if (material == null) return Color.white;
        if (material.HasProperty(BaseColorId)) return material.GetColor(BaseColorId);
        if (material.HasProperty(ColorId)) return material.GetColor(ColorId);
        return Color.white;
    }
}
