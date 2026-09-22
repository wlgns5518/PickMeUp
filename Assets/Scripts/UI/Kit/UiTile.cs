using TMPro;
using UnityEngine;

// 목록의 한 칸 — 슬롯 아래에 이름과 한 줄 설명을 붙인 것.
//
// 영웅 목록(이름 / Lv), 장비 목록(이름 / 주요 능력치), 재료 목록(이름 / 보유 수)이 모두 이 모양이다.
// 무엇을 고르든 "그림 → 이름 → 핵심 숫자" 순서로 읽히게 한다.
[DisallowMultipleComponent]
public class UiTile : MonoBehaviour
{
    public const float NameHeight = 30f;
    public const float SubHeight = 26f;
    public const float TextGap = 6f;

    public UiSlot Slot { get; private set; }
    public TMP_Text Name { get; private set; }
    public TMP_Text Sub { get; private set; }
    public RectTransform Rect => (RectTransform)transform;

    // 목록이 칸을 다시 쓸 때 이 칸이 지금 무엇을 보여 주는지. 누르면 이 값을 보고 할 일을 정한다.
    public object Payload { get; set; }

    public static float HeightOf(float slotSize) => slotSize + TextGap + NameHeight + SubHeight;

    public static UiTile Create(RectTransform parent, string name, float slotSize)
    {
        RectTransform rect = UiKit.Node(parent, name);
        rect.sizeDelta = new Vector2(slotSize, HeightOf(slotSize));

        var tile = rect.gameObject.AddComponent<UiTile>();
        tile.Slot = UiSlot.Create(rect, "Slot", slotSize);
        UiKit.TopLeft(tile.Slot.Rect, 0f, 0f, slotSize, slotSize);

        tile.Name = UiKit.Ellipsis(UiKit.Text(rect, "Name", string.Empty, UiTheme.FontLabel, UiTheme.TextPrimary,
            TextAlignmentOptions.Center));
        UiKit.TopStretch(tile.Name.rectTransform, slotSize + TextGap, NameHeight + 2f, -6f, -6f);

        tile.Sub = UiKit.Ellipsis(UiKit.Text(rect, "Sub", string.Empty, UiTheme.FontCaption, UiTheme.TextSecondary,
            TextAlignmentOptions.Center));
        UiKit.TopStretch(tile.Sub.rectTransform, slotSize + TextGap + NameHeight, SubHeight + 2f, -6f, -6f);
        return tile;
    }

    public void SetText(string name, string sub, Color? nameColor = null)
    {
        Name.text = name ?? string.Empty;
        Name.color = nameColor ?? UiTheme.TextPrimary;
        Sub.text = sub ?? string.Empty;
    }
}
