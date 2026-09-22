using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// 디자인 시스템의 팝업 한 장 — 배경막, 판, 제목줄(제목 + 닫기), 본문, 아래 버튼 줄.
//
// 상세 정보(소환 확률, 장비 상세·비교, 강화)와 되돌릴 수 없는 일의 확인은 전부 팝업으로 뺀다.
// 메인 화면에는 "무엇을 하는 화면인지 · 무엇이 필요한지 · 결과 · 실행 버튼"만 남긴다.
//
// 배경막을 누르면 닫힌다. 판과 배경막은 형제로 둔다 — 판을 배경막의 자식으로 두면 판 안을 누른 클릭이
// 배경막까지 올라가 팝업이 곧바로 닫힌다.
[DisallowMultipleComponent]
public class UiPopup : MonoBehaviour
{
    public const float TitleHeight = 84f;
    public const float FooterHeight = UiTheme.ButtonMedium + UiTheme.Space5 * 2f;
    public const float Padding = UiTheme.Space5;

    private TMP_Text title;
    private RectTransform footer;
    private int footerButtons;

    public RectTransform Panel { get; private set; }
    public RectTransform Body { get; private set; }
    public bool IsOpen => gameObject.activeSelf;

    public event Action Closed;

    /// 부모(캔버스)를 덮는 팝업을 만든다. 닫힌 채로 돌려준다. hasFooter가 false면 아래 버튼 줄이 없다.
    public static UiPopup Create(RectTransform parent, string name, string titleText, Vector2 size, bool hasFooter = true)
    {
        RectTransform root = UiKit.Node(parent, name);
        UiKit.Fill(root);

        var popup = root.gameObject.AddComponent<UiPopup>();

        Image backdrop = UiKit.Image(root, "Backdrop", null, UiTheme.Backdrop, false);
        backdrop.raycastTarget = true;
        var backdropButton = backdrop.gameObject.AddComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(popup.Hide);

        UiKit.Surface panel = UiKit.Panel(root, "Panel", UiTheme.Surface, UiTheme.RadiusL, UiTheme.BorderStrong, 28);
        UiKit.Center(panel.Rect, 0f, 0f, size.x, size.y);
        // 판 안을 누른 클릭이 뒤로 새지 않게 판이 받는다.
        panel.Fill.raycastTarget = true;
        popup.Panel = panel.Rect;

        popup.title = UiKit.Text(panel.Rect, "Title", titleText, UiTheme.FontTitle, UiTheme.TextPrimary);
        UiKit.TopStretch(popup.title.rectTransform, 0f, TitleHeight, Padding + 4f, Padding + 64f);

        UiButton close = UiButton.CreateIcon(panel.Rect, "Close", UiSprites.Icon(UiSprites.Glyph.Close), true, 52f,
            UiButtonStyle.Ghost, popup.Hide);
        UiKit.TopRight(close.Rect, Padding - 4f, (TitleHeight - 52f) * 0.5f, 52f, 52f);

        Image divider = UiKit.Image(panel.Rect, "Divider", null, UiTheme.Border, false);
        UiKit.TopStretch(divider.rectTransform, TitleHeight, 2f, Padding, Padding);

        popup.Body = UiKit.Node(panel.Rect, "Body");
        UiKit.Fill(popup.Body, Padding, TitleHeight + Padding, Padding, hasFooter ? FooterHeight : Padding);

        if (hasFooter)
        {
            popup.footer = UiKit.Node(panel.Rect, "Footer");
            UiKit.BottomStretch(popup.footer, UiTheme.Space5, UiTheme.ButtonMedium, Padding, Padding);
        }

        root.gameObject.SetActive(false);
        return popup;
    }

    public void SetTitle(string text) => title.text = text;

    /// 아래 줄에 버튼을 오른쪽부터 채운다. 가장 중요한 버튼을 먼저 넣으면 오른쪽 끝에 선다.
    public UiButton AddFooterButton(string label, UiButtonStyle style, UnityAction onClick, float width = 240f)
    {
        UiButton button = UiButton.Create(footer, "Button_" + footerButtons, label, style, UiButtonSize.Medium, onClick);
        float right = 0f;
        for (int i = 0; i < footer.childCount - 1; i++) right += ((RectTransform)footer.GetChild(i)).sizeDelta.x + UiTheme.Space3;
        UiKit.RightMiddle(button.Rect, right, width, UiTheme.ButtonMedium);
        footerButtons++;
        return button;
    }

    public void Show()
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void Hide()
    {
        if (!gameObject.activeSelf) return;
        gameObject.SetActive(false);
        Closed?.Invoke();
    }
}

// 되돌릴 수 없는 일 앞의 확인 팝업. 영웅·장비를 재료로 태우기 전, 파괴 확률이 있는 강화 전에 쓴다.
// 한 화면에 하나를 만들어 두고 문구와 할 일만 갈아 끼운다.
public class UiConfirm
{
    private readonly UiPopup popup;
    private readonly TMP_Text message;
    private readonly UiButton confirm;
    private Action onConfirm;

    public UiConfirm(RectTransform parent)
    {
        popup = UiPopup.Create(parent, "Confirm", "확인", new Vector2(760f, 420f));

        message = UiKit.Wrap(UiKit.Text(popup.Body, "Message", string.Empty, UiTheme.FontBody, UiTheme.TextSecondary,
            TextAlignmentOptions.Center));
        message.lineSpacing = 8f;

        confirm = popup.AddFooterButton("확인", UiButtonStyle.Primary, Confirm);
        popup.AddFooterButton("취소", UiButtonStyle.Ghost, popup.Hide);
    }

    /// danger면 확인 버튼이 경고색이 된다(재료가 사라지는 확인 등).
    public void Ask(string title, string text, string confirmLabel, bool danger, Action onYes)
    {
        popup.SetTitle(title);
        message.text = text;
        confirm.SetLabel(confirmLabel);
        confirm.SetStyle(danger ? UiButtonStyle.Danger : UiButtonStyle.Primary);
        onConfirm = onYes;
        popup.Show();
    }

    private void Confirm()
    {
        Action action = onConfirm;
        onConfirm = null;
        popup.Hide();
        action?.Invoke();
    }
}
