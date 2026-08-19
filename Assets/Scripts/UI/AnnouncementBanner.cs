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
// BattleResultPanel과 같이 화면 주인(BattleHud, DeckBuildUI)이 만들어 들고 있는 평범한 클래스다.
// MonoBehaviour가 아니라 코루틴을 못 쓰므로, 시간은 주인이 매 프레임 Tick으로 굴려 준다.
public class AnnouncementBanner
{
    public const float DefaultHoldSeconds = 2f;
    private const float FadeInSeconds = 0.18f;
    private const float FadeOutSeconds = 0.32f;
    private const float FallbackAspect = 2.5f;

    // 배너 아트의 장식 테두리 안쪽에 글자가 들어가도록 비워두는 비율.
    private const float PaddingRatioX = 0.11f;
    private const float PaddingRatioY = 0.24f;

    // 글자는 자동 축소가 걸려 있어 이 값은 상한이다. 배너에 비해 글씨가 커 보여 한 단계 낮췄다.
    private const float MaxFontSize = 34f;
    private const float MinFontSize = 18f;

    private static readonly StringBuilder Builder = new StringBuilder(128);

    private enum Phase { Idle, FadeIn, Hold, FadeOut }

    private readonly RectTransform root;
    private readonly CanvasGroup group;
    private readonly TMP_Text messageText;
    private readonly Queue<string> pending = new Queue<string>();
    private readonly float holdSeconds;

    private Phase phase = Phase.Idle;
    private float timer;

    private AnnouncementBanner(RectTransform parent, TMP_FontAsset font, Sprite frameSprite,
        TMP_SpriteAsset starSprites, float width, float holdSeconds)
    {
        this.holdSeconds = Mathf.Max(0.1f, holdSeconds);

        float aspect = frameSprite != null && frameSprite.rect.height > 0f
            ? frameSprite.rect.width / frameSprite.rect.height
            : FallbackAspect;
        var size = new Vector2(width, width / aspect);

        // 화면 한가운데. 창 안이 아니라 캔버스에 바로 매달아야 어디서 띄우든 같은 자리에 뜬다.
        root = HudFactory.CreateGroup(parent, "AnnouncementBanner");
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = size;
        root.anchoredPosition = Vector2.zero;

        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        Image frame;
        if (frameSprite != null)
        {
            frame = HudFactory.CreateImage(root, "Frame", Color.white);
            frame.sprite = frameSprite;
            Stretch(frame.rectTransform);
        }
        else
        {
            // 배너 아트를 아직 넣지 않았을 때의 대비책 — 금색 테두리에 검은 판.
            frame = HudFactory.CreateImage(root, "Frame", BattleHudPalette.Mvp);
            Stretch(frame.rectTransform);

            Image body = HudFactory.CreateImage(frame.rectTransform, "Body", new Color(0.03f, 0.03f, 0.04f, 0.97f));
            Stretch(body.rectTransform);
            body.rectTransform.offsetMin = new Vector2(3f, 3f);
            body.rectTransform.offsetMax = new Vector2(-3f, -3f);
        }

        // 눌러서 바로 넘길 수 있어야 한다. HudFactory는 표시 전용이라 레이캐스트가 꺼져 있다.
        frame.raycastTarget = true;
        var button = frame.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(Dismiss);

        messageText = HudFactory.CreateText(root, "Message", font, MaxFontSize, BattleHudPalette.PanelText);
        Stretch(messageText.rectTransform);
        messageText.rectTransform.offsetMin = new Vector2(size.x * PaddingRatioX, size.y * PaddingRatioY);
        messageText.rectTransform.offsetMax = new Vector2(-size.x * PaddingRatioX, -size.y * PaddingRatioY);
        messageText.alignment = TextAlignmentOptions.Center;
        messageText.textWrappingMode = TextWrappingModes.Normal;
        // 이름이 길면 두 줄짜리 문구가 배너를 넘친다. 넘치는 대신 줄어들게 한다.
        messageText.enableAutoSizing = true;
        messageText.fontSizeMin = MinFontSize;
        messageText.fontSizeMax = MaxFontSize;
        messageText.spriteAsset = starSprites != null ? starSprites : HeroLabel.LoadStarSprites();

        root.gameObject.SetActive(false);
    }

    public static AnnouncementBanner Create(RectTransform parent, TMP_FontAsset font, Sprite frameSprite,
        TMP_SpriteAsset starSprites, float width, float holdSeconds = DefaultHoldSeconds)
    {
        return new AnnouncementBanner(parent, font, frameSprite, starSprites, width, holdSeconds);
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
                group.alpha = Mathf.Clamp01(timer / FadeInSeconds);
                if (timer >= FadeInSeconds)
                {
                    group.alpha = 1f;
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
        // 나중에 만들어진 위젯이 배너를 덮지 않도록 띄울 때마다 앞으로 올린다.
        root.SetAsLastSibling();
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
