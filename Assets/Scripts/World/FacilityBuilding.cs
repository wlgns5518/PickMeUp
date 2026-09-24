using System;
using System.Collections.Generic;
using UnityEngine;

// 마을 시설 건물 한 채. 통짜 메시가 아니라 자리(Slot)마다 따로 놓인 파츠 묶음이고,
// 레벨에 따라 파츠를 켜고 끈다.
//
//   Facility_xxx (이 부품)
//   ├── Foundation / MainBody / Wall / Roof / Entrance / Window
//   └── Stairs / SideModule / Decoration / Lighting / UpgradeModule
//
// 파츠마다 "몇 레벨부터 몇 레벨까지 보이는가"를 들고 있다.
//   - 덧붙이기:   2레벨부터 끝까지 보이는 부벽
//   - 갈아 끼우기: 1~2레벨 지붕과 3레벨 지붕(한 층 위로 올라간 큰 지붕)을 같은 자리(Roof)에 둘 다 둔다
// 기초·본체·입구의 자리는 레벨이 올라도 그대로다. 업그레이드는 위로(층·지붕·탑)와 벽 둘레(부벽·장식·조명)로만
// 자란다 — 같은 자리의 시설이 발전했다고 읽혀야 하므로 발자국을 넓히지 않는다.
//
// 파츠를 새로 만들거나 지우지 않고 켜고 끄기만 한다. 모든 레벨의 파츠가 프리팹 안에 이미 들어 있어서
// 레벨이 바뀌어도 다른 스크립트가 잡고 있는 Transform이 끊기지 않는다.
// 파츠 목록은 에디터의 VillagePrefabAssembler가 채운다.
[DisallowMultipleComponent]
public class FacilityBuilding : MonoBehaviour
{
    public const int MaxLevel = 3;

    public enum Slot
    {
        Foundation,
        MainBody,
        Wall,
        Roof,
        Entrance,
        Window,
        Stairs,
        SideModule,
        Decoration,
        Lighting,
        UpgradeModule
    }

    [Serializable]
    public class Part
    {
        public Slot slot;
        public GameObject target;
        [Range(1, MaxLevel)] public int minLevel = 1;
        [Range(1, MaxLevel)] public int maxLevel = MaxLevel;

        public bool VisibleAt(int level) => level >= minLevel && level <= maxLevel;
    }

    [SerializeField, Range(1, MaxLevel)] private int level = 1;

    [Tooltip("이 건물을 짤 때 기준으로 삼은 구역 반지름. 마을 구역의 size가 이와 다르면 VillageBlockout이 통째로 늘린다.")]
    [SerializeField, Min(1f)] private float designRadius = 18f;

    [SerializeField] private List<Part> parts = new List<Part>();

    public int Level => level;
    public float DesignRadius => designRadius;
    public IReadOnlyList<Part> Parts => parts;

    public event Action<int> LevelChanged;

    private void Awake()
    {
        Apply();
    }

    public void SetLevel(int value)
    {
        value = Mathf.Clamp(value, 1, MaxLevel);
        bool changed = value != level;
        level = value;
        Apply();
        if (changed) LevelChanged?.Invoke(level);
    }

    /// 자리 이름으로 된 자식. 업그레이드 파츠를 코드에서 더 붙일 때 여기에 넣는다.
    public Transform SlotRoot(Slot slot) => transform.Find(slot.ToString());

    private void Apply()
    {
        foreach (Part part in parts)
        {
            if (part == null || part.target == null) continue;
            bool visible = part.VisibleAt(level);
            if (part.target.activeSelf != visible) part.target.SetActive(visible);
        }
    }

#if UNITY_EDITOR
    // 조립기가 파츠를 등록할 때 쓴다. 런타임에는 부르지 않는다.
    public void EditorSetup(float radius, List<Part> list)
    {
        designRadius = radius;
        parts = list;
        Apply();
    }

    // 인스펙터에서 레벨을 끌어 보며 확인할 수 있게. OnValidate 안에서는 SetActive를 부를 수 없어 한 틱 미룬다.
    private void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null) Apply();
        };
    }
#endif
}
