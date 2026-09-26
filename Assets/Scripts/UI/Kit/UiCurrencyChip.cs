using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 재화 한 가지의 보유량 — 어두운 알약 위에 재화 아이콘과 액수. 화면 머리줄 오른쪽과 마을 상단바가 같은 칩을 쓴다.
// 지갑이 바뀌면 스스로 다시 적는다.
[DisallowMultipleComponent]
public class UiCurrencyChip : MonoBehaviour
{
    public const float Height = 56f;

    private Currency currency;
    private TMP_Text amount;

    public RectTransform Rect => (RectTransform)transform;

    // + 버튼(재화 사기) 지름. 알약 안 오른쪽 끝에 들어간다.
    private const float BuyButtonSize = Height - 12f;

    /// onBuy를 주면 알약 오른쪽 끝에 + 버튼을 단다(젬 사기 — GemShop).
    public static UiCurrencyChip Create(RectTransform parent, string name, Currency currency, float width = 240f,
        System.Action onBuy = null)
    {
        RectTransform rect = UiKit.Node(parent, name);
        rect.sizeDelta = new Vector2(width, Height);

        UiKit.Rounded(rect, "Fill", UiTheme.SurfaceSunken, Mathf.RoundToInt(Height * 0.5f));
        UiKit.Line(rect, "Border", UiTheme.Border, Mathf.RoundToInt(Height * 0.5f), 2);

        // 아이콘은 알약보다 조금 크게 그려 왼쪽 끝에 걸친다.
        const float iconSize = Height + 14f;
        Image icon = UiKit.Image(rect, "Icon", UiIconLibrary.Currency(currency), Color.white);
        UiKit.LeftMiddle(icon.rectTransform, -12f, iconSize, iconSize);

        var chip = rect.gameObject.AddComponent<UiCurrencyChip>();
        chip.currency = currency;
        chip.amount = UiKit.Text(rect, "Amount", string.Empty, UiTheme.FontBody, UiTheme.TextPrimary, TextAlignmentOptions.Right);
        float amountRight = 22f;
        if (onBuy != null)
        {
            UiButton buy = UiButton.CreateIcon(rect, "Buy", UiSprites.Icon(UiSprites.Glyph.Plus), true, BuyButtonSize,
                UiButtonStyle.Primary, () => onBuy());
            UiKit.RightMiddle(buy.Rect, 6f, BuyButtonSize, BuyButtonSize);
            amountRight = 6f + BuyButtonSize + 12f;
        }
        UiKit.Fill(chip.amount.rectTransform, iconSize - 12f + 6f, 0f, amountRight, 0f);
        chip.Refresh();
        return chip;
    }

    private void OnEnable()
    {
        PlayerAccount.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        PlayerAccount.Changed -= Refresh;
    }

    private void Refresh()
    {
        if (amount != null) amount.text = UiKit.Amount(PlayerAccount.Balance(currency));
    }
}
