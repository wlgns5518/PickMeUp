using UnityEngine;

/// <summary>
/// OrnateMessageBox 사용 예시. 스프라이트/폰트는 OrnateMessageBox 인스펙터에서 지정하세요.
/// </summary>
public class MessageBoxDemo : MonoBehaviour
{
    public OrnateMessageBox box;

    readonly string[] samples =
    {
        "사인 - 스트레스로 인한 자살.",
        "몰몬트(★★)가 여신의 품으로 돌아갔습니다.\n그의 투지는 영원히 기억될 것입니다.",
        "라일(★)이 ‘1파티’에 합류합니다!\n란토(★)가 ‘1파티’에 합류합니다!\n마를린(★)이 ‘1파티’에 합류합니다!\n켈커드(★)가 ‘1파티’에 합류합니다!\n작센(★)이 ‘1파티’에 합류합니다!",
    };

    int i;

    void Start() => Show();

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))
        {
            i = (i + 1) % samples.Length;
            Show();
        }
    }

    void Show()
    {
        if (box != null) box.SetMessage(samples[i]);
    }
}
