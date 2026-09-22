using UnityEngine;

// 네온 HUD 킷(Assets/UI/README.md)의 스프라이트를 한데 묶은 에셋. 이제 전투 화면(파티 상태 패널·게이지·
// 알림 배너·결과창)만 쓴다 — 마을의 시설 화면은 디자인 시스템(Assets/Scripts/UI/Kit)으로 옮겼다.
//
// UI는 전부 코드에서 짓기 때문에 인스펙터로 스프라이트를 꽂아 줄 자리가 없다. 그래서 스프라이트는 이 에셋
// 한 곳에만 걸고 Resources에서 이름으로 부른다(WeaponCatalog와 같은 방식).
//
// 에셋이 없거나 칸이 비어 있으면 HudFactory가 팔레트 색의 단색 판으로 대신 그린다 — 모양만 밋밋해질 뿐
// 게이지는 그대로 동작한다.
[CreateAssetMenu(fileName = ResourceName, menuName = "PickMeUp/UI/Neon UI Skin")]
public class NeonUISkin : ScriptableObject
{
    // Assets/UI/Resources/NeonUISkin.asset
    public const string ResourceName = "NeonUISkin";

    [Header("Party Panel")]
    [Tooltip("btn_secondary — 파티 상태 패널에서 고른 영웅의 줄")]
    public Sprite buttonSecondary;
    [Tooltip("icon_btn — 파티 상태 패널의 초상화 테두리")]
    public Sprite iconButton;

    [Header("Gauges")]
    public Sprite gaugeTrack;
    public Sprite gaugeFillHp;
    public Sprite gaugeFillMp;
    public Sprite gaugeFillXp;

    // 장식 메시지 박스는 한 장짜리(msgbox_frame) 대신 조각으로 짠다. 한 장짜리는 가운데 장식이 위아래·좌우
    // 슬라이스에 걸쳐 있어서, 박스를 늘리면 그 조각이 글자 위로 가로세로 줄을 긋는다.
    [Header("Message Box")]
    [Tooltip("msgbox_corner_* — 90x90 모서리 장식, 크기 고정")]
    public Sprite messageCornerTopLeft;
    public Sprite messageCornerTopRight;
    public Sprite messageCornerBottomLeft;
    public Sprite messageCornerBottomRight;
    [Tooltip("msgbox_edge_* — 모서리 사이를 잇는 테두리(위아래는 가로로, 좌우는 세로로만 늘어난다)")]
    public Sprite messageEdgeTop;
    public Sprite messageEdgeBottom;
    public Sprite messageEdgeLeft;
    public Sprite messageEdgeRight;
    [Tooltip("msgbox_panel — 테두리 안쪽 검은 판")]
    public Sprite messagePanel;

    public bool HasMessageFrame =>
        messageCornerTopLeft != null && messageCornerTopRight != null &&
        messageCornerBottomLeft != null && messageCornerBottomRight != null &&
        messageEdgeTop != null && messageEdgeBottom != null && messageEdgeLeft != null && messageEdgeRight != null;

    private static NeonUISkin cached;
    private static bool searched;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        cached = null;
        searched = false;
    }

    // 없으면 null. 호출하는 쪽(HudFactory)이 단색으로 대신 그린다.
    public static NeonUISkin Current
    {
        get
        {
            if (cached != null) return cached;
            if (searched) return null;

            searched = true;
            cached = Resources.Load<NeonUISkin>(ResourceName);
            if (cached == null)
                Debug.LogWarning($"[NeonUISkin] Assets/UI/Resources/{ResourceName}.asset 이 없어 UI를 단색으로 그립니다.");
            return cached;
        }
    }

#if UNITY_EDITOR
    // 에셋을 새로 만들거나 인스펙터에서 Reset을 누르면 킷 폴더에서 파일 이름으로 채운다.
    private void Reset()
    {
        const string folder = "Assets/UI/Sprites/";
        Sprite Load(string file) => UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(folder + file + ".png");

        buttonSecondary = Load("btn_secondary");
        iconButton = Load("icon_btn");
        gaugeTrack = Load("gauge_track");
        gaugeFillHp = Load("gauge_fill_hp");
        gaugeFillMp = Load("gauge_fill_mp");
        gaugeFillXp = Load("gauge_fill_xp");
        messageCornerTopLeft = Load("msgbox_corner_tl");
        messageCornerTopRight = Load("msgbox_corner_tr");
        messageCornerBottomLeft = Load("msgbox_corner_bl");
        messageCornerBottomRight = Load("msgbox_corner_br");
        messageEdgeTop = Load("msgbox_edge_top");
        messageEdgeBottom = Load("msgbox_edge_bottom");
        messageEdgeLeft = Load("msgbox_edge_left");
        messageEdgeRight = Load("msgbox_edge_right");
        messagePanel = Load("msgbox_panel");
    }
#endif
}
