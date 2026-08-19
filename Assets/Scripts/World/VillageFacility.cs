using UnityEngine;

// 마을 구역 하나에 붙는 표식.
//
// 지금은 임시 도형 덩어리지만 나중에 에셋 프리팹으로 바뀌어도 구역 루트에는 항상 이게 붙는다.
// 상호작용이나 UI가 "연금시설이 어디냐"고 물을 때 오브젝트 이름 문자열 대신 이걸로 찾게 하려는 것.
// 이름은 인스펙터에서 바꿔도 코드가 안 깨지지만, 이름으로 찾으면 바로 깨진다.
[DisallowMultipleComponent]
public class VillageFacility : MonoBehaviour
{
    [Tooltip("어떤 시설인지.")]
    public VillageBlockout.Kind kind;

    [Tooltip("화면에 보여줄 이름.")]
    public string label;

    [TextArea(1, 3), Tooltip("이 구역이 하는 일.")]
    public string role;
}
