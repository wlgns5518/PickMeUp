using System.Globalization;
using System.Text;

// 손으로 짜는 JSON의 글자 단위 도구.
//
// 받는 쪽은 되도록 JsonUtility에 [Serializable] 클래스로 넘긴다. 다만 보내는 본문은 필드 몇 개라
// 직접 짜고(JsonBody), 응답 모양을 모르는 옛 경로 몇 곳은 필드 하나만 뽑아 읽는다.
// 예전에는 이 둘이 부르는 곳마다 따로 있었다(MeshyBodyRecipe.EscapeJson, MeshyCharacterGenerator.EscapeJson …) —
// 한쪽만 고치면 같은 프롬프트가 어느 입구로 들어갔느냐에 따라 다르게 깨진다.
public static class JsonText
{
    /// 문자열을 따옴표로 감싸 JSON 값으로 만든다. null은 null 리터럴이 된다.
    /// 프롬프트에 따옴표나 줄바꿈이 섞이면 요청이 통째로 깨지므로 문자열은 반드시 여기를 거친다.
    public static string Quote(string value)
    {
        if (value == null) return "null";

        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b");  break;
                case '\f': sb.Append("\\f");  break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    /// 소수는 문화권과 무관하게 점으로 적는다. 한국어 윈도우라도 쉼표가 끼면 요청이 깨진다.
    public static string Number(float value, string format = "0.###") =>
        value.ToString(format, CultureInfo.InvariantCulture);

    /// "field": "value" 문자열 필드 하나를 트리 파싱 없이 뽑는다. 없으면 null.
    ///
    /// 값의 이스케이프는 반드시 되돌려야 한다. 배치 이름은 "가\n나\n다"처럼 한 문자열에 담겨 오는데,
    /// 되돌리지 않으면 줄바꿈이 역슬래시+n 두 글자로 남아 전부 한 줄이 된다.
    /// 그러면 이름을 줄 단위로 끊는 쪽이 첫 이름만 건지고 나머지는 "이름없음"으로 떨어졌다.
    /// 닫는 따옴표를 찾을 때도 이스케이프된 따옴표(\")에 걸려 값이 잘리지 않도록 한 글자씩 읽는다.
    public static string ExtractString(string json, string field)
    {
        if (string.IsNullOrEmpty(json)) return null;
        int i = json.IndexOf("\"" + field + "\"");
        if (i < 0) return null;
        i = json.IndexOf(':', i + field.Length + 2);
        if (i < 0) return null;
        i++;
        while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
        if (i >= json.Length || json[i] != '"') return null;

        var value = new StringBuilder();
        for (int p = i + 1; p < json.Length; p++)
        {
            char c = json[p];
            if (c == '"') return value.ToString();
            if (c != '\\') { value.Append(c); continue; }

            if (++p >= json.Length) break;
            switch (json[p])
            {
                case 'n': value.Append('\n'); break;
                case 'r': value.Append('\r'); break;
                case 't': value.Append('\t'); break;
                case 'b': value.Append('\b'); break;
                case 'f': value.Append('\f'); break;
                case 'u':
                    if (p + 4 < json.Length &&
                        int.TryParse(json.Substring(p + 1, 4), NumberStyles.HexNumber,
                                     CultureInfo.InvariantCulture, out int code))
                    {
                        value.Append((char)code);
                        p += 4;
                    }
                    break;
                // \" \\ \/ 는 뒤 글자가 곧 값이다.
                default: value.Append(json[p]); break;
            }
        }
        return null;
    }
}
