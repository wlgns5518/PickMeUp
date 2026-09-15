using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 화면 정중앙에 잠깐 떠올랐다 사라지는 알림 배너.
//
// 파티 편성 안내와 동료의 부고가 같은 장식 배너를 쓴다. 둘 다 "잠깐 읽고 넘기는 말"이라
// 창처럼 자리를 차지하고 있을 이유가 없다. 그래서 눌러서 바로 넘기거나, 두면 알아서 사라진다.
//
// 모양은 킷의 장식 메시지 박스(msgbox_*)다. 한 줄짜리 경고와 다섯 줄짜리 합류 알림이 같은 배너를 쓰므로
// 그림 비율에 글자를 끼워 맞추지 않고, 띄울 때마다 글자 크기에 맞춰 박스를 늘린다(README의 OrnateMessageBox와 같은 계산).
//
// BattleResultPanel과 같이 화면 주인(BattleHud, DeckBuildUI)이 만들어 들고 있는 평범한 클래스다.
// MonoBehaviour가 아니라 코루틴을 못 쓰므로, 시간은 주인이 매 프레임 Tick으로 굴려 준다.
public class AnnouncementBanner
{
    public const float DefaultHoldSeconds = 2f;
    private const float FadeInSeconds = 0.28f;
    private const float FadeOutSeconds = 0.32f;
    // 떠오를 때 이 배율에서 1로 커진다. 크게 튀면 읽기 전에 눈이 먼저 따라간다.
    private const float PopInScale = 0.97f;

    // 테두리 장식 안쪽에 글자가 들어가도록 비워두는 여백(README: 좌우 95, 상하 80).
    private const float PaddingX = 95f;
    private const float PaddingY = 80f;
    private const float MinBoxWidth = 360f;

    private const float FontSize = 28f;

    private static readonly StringBuilder Builder = new StringBuilder(128);

    private enum Phase { Idle, FadeIn, Hold, FadeOut }

    private readonly RectTransform root;
    private readonly CanvasGroup group;
    private readonly TMP_Text messageText;
    private readonly Queue<string> pending = new Queue<string>();
    private readonly float holdSeconds;
    private readonly float maxTextWidth;

    private Phase phase = Phase.Idle;
    private float timer;

    // maxWidth는 박스 전체가 넘지 않을 가로 길이다. 넘치는 글은 줄을 바꾸고 박스가 세로로 늘어난다.
    private AnnouncementBanner(RectTransform parent, TMP_FontAsset font, TMP_SpriteAsset starSprites,
        float maxWidth, float holdSeconds)
    {
        this.holdSeconds = Mathf.Max(0.1f, holdSeconds);
        maxTextWidth = Mathf.Max(MinBoxWidth, maxWidth) - PaddingX * 2f;

        // 화면 한가운데. 창 안이 아니라 캔버스에 바로 매달아야 어디서 띄우든 같은 자리에 뜬다.
        root = HudFactory.CreateOrnateBox(parent, "AnnouncementBanner", out Image body);
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = Vector2.zero;

        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        // 눌러서 바로 넘길 수 있어야 한다. HudFactory는 표시 전용이라 레이캐스트가 꺼져 있다.
        body.raycastTarget = true;
        var button = body.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(Dismiss);

        messageText = HudFactory.CreateText(root, "Message", font, FontSize, BattleHudPalette.TextPrimary);
        RectTransform textRect = messageText.rectTransform;
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        messageText.alignment = TextAlignmentOptions.Center;
        messageText.textWrappingMode = TextWrappingModes.Normal;
        messageText.spriteAsset = starSprites != null ? starSprites : HeroLabel.LoadStarSprites();

        root.gameObject.SetActive(false);
    }

    public static AnnouncementBanner Create(RectTransform parent, TMP_FontAsset font,
        TMP_SpriteAsset starSprites, float maxWidth, float holdSeconds = DefaultHoldSeconds)
    {
        return new AnnouncementBanner(parent, font, starSprites, maxWidth, holdSeconds);
    }

    public bool IsVisible => phase != Phase.Idle;

    // 같은 순간에 여럿이 들어와도 한 줄씩 차례로 보여준다. 겹쳐 띄우면 아무것도 읽히지 않는다.
    public void Show(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        pending.Enqueue(message);
        if (phase == Phase.Idle) BeginNext();
    }

    public void ShowDeath(CharacterSO character)
    {
        if (character == null) return;
        Show(DeathMessage(character, messageText.spriteAsset != null));
    }

    // 클릭했을 때. 남은 시간을 기다리지 않고 곧바로 걷어낸다.
    public void Dismiss()
    {
        if (phase == Phase.Idle || phase == Phase.FadeOut) return;

        phase = Phase.FadeOut;
        timer = 0f;
    }

    public void Tick(float deltaTime)
    {
        switch (phase)
        {
            case Phase.Idle:
                if (pending.Count > 0) BeginNext();
                break;

            case Phase.FadeIn:
                timer += deltaTime;
                float t = Mathf.Clamp01(timer / FadeInSeconds);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                group.alpha = eased;
                root.localScale = Vector3.one * Mathf.Lerp(PopInScale, 1f, eased);
                if (timer >= FadeInSeconds)
                {
                    group.alpha = 1f;
                    root.localScale = Vector3.one;
                    phase = Phase.Hold;
                    timer = 0f;
                }
                break;

            case Phase.Hold:
                timer += deltaTime;
                if (timer >= holdSeconds)
                {
                    phase = Phase.FadeOut;
                    timer = 0f;
                }
                break;

            case Phase.FadeOut:
                timer += deltaTime;
                group.alpha = 1f - Mathf.Clamp01(timer / FadeOutSeconds);
                if (timer >= FadeOutSeconds)
                {
                    group.alpha = 0f;
                    phase = Phase.Idle;
                    timer = 0f;
                    root.gameObject.SetActive(false);
                    if (pending.Count > 0) BeginNext();
                }
                break;
        }
    }

    // 화면을 떠날 때. 남은 줄까지 통째로 버린다.
    public void Clear()
    {
        pending.Clear();
        phase = Phase.Idle;
        timer = 0f;
        group.alpha = 0f;
        root.gameObject.SetActive(false);
    }

    // "몰몬트(★★)가 여신의 품으로 돌아갔습니다."
    public static string DeathMessage(CharacterSO character, bool useStarSprites)
    {
        Builder.Clear();
        Builder.Append(HeroLabel.NameWithStars(character, useStarSprites));
        Builder.Append(HeroLabel.SubjectParticle(HeroLabel.Name(character)));
        Builder.Append(" 여신의 품으로 돌아갔습니다.\n그의 투지는 영원히 기억될 것입니다.");
        return Builder.ToString();
    }

    private void BeginNext()
    {
        messageText.text = pending.Dequeue();
        group.alpha = 0f;
        phase = Phase.FadeIn;
        timer = 0f;

        root.gameObject.SetActive(true);
        FitToText();
        // 나중에 만들어진 위젯이 배너를 덮지 않도록 띄울 때마다 앞으로 올린다.
        root.SetAsLastSibling();
    }

    // 줄바꿈 없이 잰 길이가 최대 폭보다 짧으면 그 길이로, 길면 최대 폭에서 줄을 바꾼 높이로 박스를 잡는다.
    // 짧은 경고 한 줄에 넓은 박스를 띄우면 테두리 장식만 크게 보이고 글은 가운데에 콕 박힌다.
    private void FitToText()
    {
        string text = messageText.text;
        Vector2 natural = messageText.GetPreferredValues(text, Mathf.Infinity, Mathf.Infinity);
        // 잰 폭에 딱 맞추면 반올림 차이로 마지막 글자가 다음 줄로 밀린다. 조금 여유를 준다.
        float width = Mathf.Min(Mathf.Ceil(natural.x) + 2f, maxTextWidth);
        Vector2 wrapped = messageText.GetPreferredValues(text, width, Mathf.Infinity);

        messageText.rectTransform.sizeDelta = new Vector2(width, wrapped.y);
        root.sizeDelta = new Vector2(Mathf.Max(MinBoxWidth, width + PaddingX * 2f), wrapped.y + PaddingY * 2f);
    }
}
