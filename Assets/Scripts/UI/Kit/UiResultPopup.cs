using TMPro;
using UnityEngine;

// 실행 결과를 보여 주는 팝업 — 가운데 큰 슬롯, 그 아래 이름과 설명, [확인].
//
// 합성 완료, 제작 완료, 장비 합성 완료, 강화 결과가 모두 이 한 모양이다. 무엇을 했든 "무엇을 얻었나"는 같은
// 자리에서 같은 크기로 보여야 한다. 한 화면에 하나를 만들어 두고 내용만 갈아 끼운다.
public class UiResultPopup
{
    private const float SlotSize = 200f;

    private readonly UiPopup popup;
    private readonly UiSlot slot;
    private readonly TMP_Text name;
    private readonly TMP_Text lines;

    public UiResultPopup(RectTransform parent)
    {
        popup = UiPopup.Create(parent, "Result", "결과", new Vector2(760f, 660f));

        slot = UiSlot.Create(popup.Body, "Slot", SlotSize);
        UiKit.TopCenter(slot.Rect, 0f, UiTheme.Space3, SlotSize, SlotSize);
        slot.SetInteractable(false);

        name = UiKit.Ellipsis(UiKit.Text(popup.Body, "Name", string.Empty, UiTheme.FontTitle, UiTheme.TextPrimary,
            TextAlignmentOptions.Center));
        UiKit.TopStretch(name.rectTransform, SlotSize + UiTheme.Space5, 54f);

        lines = UiKit.Wrap(UiKit.Text(popup.Body, "Lines", string.Empty, UiTheme.FontBody, UiTheme.TextSecondary,
            TextAlignmentOptions.Top));
        lines.lineSpacing = 8f;
        UiKit.Fill(lines.rectTransform, 0f, SlotSize + UiTheme.Space5 + 62f, 0f, 0f);

        popup.AddFooterButton("확인", UiButtonStyle.Primary, popup.Hide);
    }

    public void Show(string title, UiSlotContent content, string itemName, string description)
    {
        popup.SetTitle(title);
        slot.SetContent(content);
        name.text = itemName ?? string.Empty;
        name.color = content.Tier > 0 ? UiTheme.Tier(content.Tier) : UiTheme.TextPrimary;
        lines.text = description ?? string.Empty;
        popup.Show();
    }
}
