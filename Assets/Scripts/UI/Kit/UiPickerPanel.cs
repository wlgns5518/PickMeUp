using System.Collections.Generic;
using TMPro;
using UnityEngine;

// 고르는 목록 패널 — 제목과 개수, (있으면) 분류 탭, 그 아래 칸 격자.
//
//   ┌ 보유 장비 ────────── 12개 ┐
//   │ [ 전체 | 무기 | 방어구 ]   │
//   │ [칸][칸][칸][칸][칸]       │
//
// 보유 영웅(합성소·장비창), 보유 장비(장비창·장비 합성), 보유 재료(장비 제작)가 모두 이 패널이다.
// 흐름(UiFlowPanel)의 오른쪽에 붙어 "여기서 골라 왼쪽에 넣는다"는 자리가 모든 화면에서 같다.
[DisallowMultipleComponent]
public class UiPickerPanel : MonoBehaviour
{
    private const float HeaderHeight = 64f;

    public TMP_Text Title { get; private set; }
    public TMP_Text Count { get; private set; }
    public UiTabs Tabs { get; private set; }
    public UiTileGrid Grid { get; private set; }
    public RectTransform Rect => (RectTransform)transform;

    /// 패널을 짓는다. 자리는 돌려받은 Rect에 잡는다(보통 남는 폭을 모두 쓰도록 늘인다). width는 격자가 한 줄에
    /// 몇 칸을 둘지 처음 정할 때의 어림값이다 — 실제 폭은 목록을 깔 때마다 다시 잰다(UiTileGrid.Show).
    /// tabs가 null이면 탭 줄이 없다.
    public static UiPickerPanel Create(RectTransform parent, string name, float width, string title,
        IReadOnlyList<string> tabs, float slotSize)
    {
        UiKit.Surface panel = UiKit.Panel(parent, name, UiTheme.Surface, UiTheme.RadiusL, UiTheme.Border);

        var picker = panel.Rect.gameObject.AddComponent<UiPickerPanel>();
        float pad = UiTheme.Space5;

        picker.Title = UiKit.SectionTitle(panel.Rect, "Title", title);
        UiKit.TopStretch(picker.Title.rectTransform, 0f, HeaderHeight, pad, 200f);
        picker.Count = UiKit.Text(panel.Rect, "Count", string.Empty, UiTheme.FontBody, UiTheme.TextSecondary,
            TextAlignmentOptions.Right);
        UiKit.TopStretch(picker.Count.rectTransform, 0f, HeaderHeight, 240f, pad);

        float top = HeaderHeight;
        if (tabs != null && tabs.Count > 0)
        {
            picker.Tabs = UiTabs.Create(panel.Rect, "Tabs", tabs, UiTheme.FontLabel);
            UiKit.TopStretch(picker.Tabs.Rect, top, UiTheme.TabHeight, pad, pad);
            top += UiTheme.TabHeight + UiTheme.Space3;
        }

        RectTransform well = UiKit.Node(panel.Rect, "Well");
        UiKit.Fill(well, pad * 0.5f, top, pad * 0.5f, pad * 0.5f);
        UiKit.Rounded(well, "Fill", UiTheme.SurfaceSunken, UiTheme.RadiusM);

        RectTransform gridRoot = UiKit.Node(well, "Grid");
        UiKit.Fill(gridRoot, UiTheme.Space3, UiTheme.Space2, UiTheme.Space3, UiTheme.Space2);
        float gridWidth = width - pad - UiTheme.Space3 * 2f;
        picker.Grid = UiTileGrid.Create(gridRoot, "Tiles", gridWidth, slotSize);
        return picker;
    }
}
