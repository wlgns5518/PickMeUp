using System.Globalization;

// 숫자를 사람이 읽는 글자로. 화면(UiKit)과 규칙 쪽의 거절 문구가 같은 모양을 쓴다 —
// 규칙 쪽이 이 한 줄 때문에 UI 킷을 끌어오지 않게 여기 둔다.
public static class TextFormat
{
    /// 12345 → "12,345"
    public static string Amount(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
