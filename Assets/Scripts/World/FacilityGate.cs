using UnityEngine;
using UnityEngine.EventSystems;

// 눌러서 여닫는 창. 시설을 눌렀을 때 열리는 창들이 이걸 구현한다.
public interface IFacilityWindow
{
    bool IsOpen { get; }
    void Toggle();
}

// 마을에서 눌러 창을 여는 시설. 소환소, 합성소, 시공의 틈이 같은 부품을 쓴다.
//
// 씬에서 손으로 붙이지 않는다. 구역은 VillageBlockout이 실행 중에 세우고 다시 만들 때마다
// 통째로 지워지므로, 붙이는 일도 구역을 세울 때 함께 한다(VillageBlockout.BuildDistrict).
//
// 클릭은 직접 레이를 쏘지 않고 EventSystem으로 받는다. 그래야 UI 위를 누른 클릭이 건물까지
// 새지 않는다. 대신 3D 콜라이더가 이벤트를 받으려면 카메라에 PhysicsRaycaster가 있어야 한다.
[DisallowMultipleComponent]
public class FacilityGate : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("어느 시설인지. 이 값으로 열 창을 찾는다.")]
    [SerializeField] private VillageBlockout.Kind kind;

    [Header("Highlight")]
    [Tooltip("마우스를 올렸을 때 밝아질 렌더러. 비워두면 자식 전부를 쓴다.")]
    [SerializeField] private Renderer[] highlightTargets;
    [SerializeField, Range(1f, 3f)] private float hoverBrightness = 1.35f;

    private IFacilityWindow window;
    private HoverHighlight highlight;

    /// 이 시설을 누르면 열릴 창이 있는지. VillageBlockout이 어느 구역에 이 부품을 붙일지 정할 때 쓴다.
    /// 여는 창이 있는 시설을 여기 한 곳에만 적어 두면 마을 배치 코드는 목록을 몰라도 된다.
    public static bool HasWindow(VillageBlockout.Kind kind)
    {
        switch (kind)
        {
            case VillageBlockout.Kind.Summoning:
            case VillageBlockout.Kind.Synthesis:
            case VillageBlockout.Kind.Rift:
            case VillageBlockout.Kind.EquipmentWorkshop:
                return true;
            default:
                return false;
        }
    }

    // AddComponent 직후에 불린다. Awake는 이미 지났으므로 실제 준비는 Start에서 한다.
    public void Bind(VillageBlockout.Kind facilityKind)
    {
        kind = facilityKind;
    }

    // 창 찾기와 강조는 Awake가 아니라 여기서 한다. VillageBlockout은 구역 루트에 이 부품을 먼저
    // 붙이고 그 뒤에 건물 도형을 만든다 — Awake 시점에는 밝힐 렌더러가 아직 하나도 없고,
    // Bind로 어느 시설인지 듣기도 전이다.
    private void Start()
    {
        window = FindWindow(kind);
        if (window == null)
            Debug.LogWarning($"[FacilityGate] {kind}에 해당하는 창을 찾지 못해 눌러도 아무 일도 일어나지 않습니다.", this);

        if (highlightTargets == null || highlightTargets.Length == 0)
            highlightTargets = GetComponentsInChildren<Renderer>();

        highlight = new HoverHighlight(highlightTargets, hoverBrightness);

        // 이게 없으면 건물은 클릭 이벤트를 아예 받지 못한다. 조용히 안 눌리는 것보다 알려주는 편이 낫다.
        Camera cam = Camera.main;
        if (cam != null && cam.GetComponent<PhysicsRaycaster>() == null)
            Debug.LogWarning("[FacilityGate] 메인 카메라에 PhysicsRaycaster가 없어 시설을 누를 수 없습니다.", this);

        if (GetComponentInChildren<Collider>() == null)
            Debug.LogWarning("[FacilityGate] 콜라이더가 없어 시설을 누를 수 없습니다.", this);
    }

    private static IFacilityWindow FindWindow(VillageBlockout.Kind kind)
    {
        switch (kind)
        {
            case VillageBlockout.Kind.Summoning:
                return FindAnyObjectByType<SummonUI>(FindObjectsInactive.Include);
            case VillageBlockout.Kind.Synthesis:
                return FindAnyObjectByType<SynthesisUI>(FindObjectsInactive.Include);
            // 시공의 틈은 원정을 떠나는 자리다. 누구를 데려갈지부터 고른다.
            case VillageBlockout.Kind.Rift:
                return FindAnyObjectByType<DeckBuildUI>(FindObjectsInactive.Include);
            case VillageBlockout.Kind.EquipmentWorkshop:
                return FindAnyObjectByType<EquipmentWorkshopUI>(FindObjectsInactive.Include);
            default:
                return null;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // 열려 있을 때 다시 누르면 닫는다. 창을 닫을 방법이 닫기 버튼 하나뿐이면 답답하다.
        window?.Toggle();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        highlight?.Set(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        highlight?.Set(false);
    }

    // 창이 열린 채로 건물이 사라지면 강조가 남는다. 꺼질 때 원래 색으로 돌려둔다.
    private void OnDisable()
    {
        highlight?.Set(false);
    }
}
