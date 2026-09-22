using System.Collections.Generic;
using TMPro;
using UnityEngine;

public enum UiToastKind { Info, Success, Warning, Danger }

// 화면 위쪽(머리줄 바로 아래)에 잠깐 떴다 사라지는 한 줄 알림.
//
// "골드가 부족합니다", "강화 성공! +4" 같은 짧은 말은 창을 막지 않는다. 눌러서 닫을 필요도 없다.
// 왼쪽 막대 색이 종류(정보·성공·경고·위험)를 말한다. 여럿이 한꺼번에 오면 차례로 보여 준다.
//
// 떠 있는 동안에만 Update가 돈다(꺼지면 컴포넌트를 끈다) — AnnouncementBannerTicker와 같은 이유.
[DisallowMultipleComponent]
public class UiToast : MonoBehaviour
{
    private const float FadeSeconds = 0.18f;
    private const float HoldSeconds = 1.8f;
    private const float Height = 64f;

    private CanvasGroup group;
    private RectTransform body;
    private UnityEngine.UI.Image bar;
    private TMP_Text text;
    private readonly Queue<(string, UiToastKind)> pending = new Queue<(string, UiToastKind)>();
    private float timer;

    public static UiToast Create(RectTransform parent, float top)
    {
        RectTransform root = UiKit.Node(parent, "Toast");
        UiKit.TopCenter(root, 0f, top, 720f, Height);

        var toast = root.gameObject.AddComponent<UiToast>();
        toast.group = root.gameObject.AddComponent<CanvasGroup>();
        toast.group.blocksRaycasts = false;
        toast.group.interactable = false;
        toast.body = root;

        UiKit.Surface panel = UiKit.Panel(root, "Panel", UiTheme.SurfaceHover, UiTheme.RadiusM, UiTheme.BorderStrong, 16);
        UiKit.Fill(panel.Rect);

        toast.bar = UiKit.Rounded(panel.Rect, "Bar", UiTheme.Selection, 3);
        UiKit.LeftMiddle(toast.bar.rectTransform, 14f, 6f, Height - 26f);

        toast.text = UiKit.Text(panel.Rect, "Text", string.Empty, UiTheme.FontBody, UiTheme.TextPrimary, TMPro.TextAlignmentOptions.Center);
        UiKit.Fill(toast.text.rectTransform, 36f, 0f, 24f, 0f);

        root.gameObject.SetActive(false);
        return toast;
    }

    public void Show(string message, UiToastKind kind = UiToastKind.Info)
    {
        if (string.IsNullOrEmpty(message)) return;

        pending.Enqueue((message, kind));
        if (!gameObject.activeSelf) Next();
    }

    private void Next()
    {
        if (pending.Count == 0)
        {
            gameObject.SetActive(false);
            return;
        }

        (string message, UiToastKind kind) = pending.Dequeue();
        text.text = message;
        bar.color = kind == UiToastKind.Success ? UiTheme.Success
            : kind == UiToastKind.Warning ? UiTheme.Warning
            : kind == UiToastKind.Danger ? UiTheme.Danger
            : UiTheme.Selection;

        // 글자 길이에 맞춰 폭을 늘린다. 너무 짧은 알림이 넓은 판에 떠 있으면 비어 보인다.
        float width = Mathf.Clamp(text.GetPreferredValues(message).x + 90f, 360f, 1100f);
        body.sizeDelta = new Vector2(width, Height);

        timer = 0f;
        group.alpha = 0f;
        gameObject.SetActive(true);
        body.SetAsLastSibling();
    }

    private void Update()
    {
        timer += Time.unscaledDeltaTime;
        float total = FadeSeconds * 2f + HoldSeconds;

        if (timer < FadeSeconds) group.alpha = timer / FadeSeconds;
        else if (timer < FadeSeconds + HoldSeconds) group.alpha = 1f;
        else if (timer < total) group.alpha = 1f - (timer - FadeSeconds - HoldSeconds) / FadeSeconds;
        else Next();
    }
}
