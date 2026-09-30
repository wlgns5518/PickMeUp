using System.Text;

// 요청 본문 한 벌을 짜는 도구. 필드를 넣은 순서 그대로, 공백 없이 적는다.
//
// 본문을 문자열 덧셈으로 짜면 쉼표 하나, 따옴표 하나가 빠져도 컴파일은 되고 서버에서만 깨진다.
// 문자열 값은 늘 JsonText.Quote를 거치므로 프롬프트에 무엇이 섞여 와도 본문이 깨지지 않는다.
public sealed class JsonBody
{
    private readonly StringBuilder sb = new StringBuilder("{");
    private bool empty = true;

    public JsonBody Add(string key, string value) => AddRaw(key, JsonText.Quote(value));

    public JsonBody Add(string key, bool value) => AddRaw(key, value ? "true" : "false");

    public JsonBody Add(string key, int value) => AddRaw(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public JsonBody Add(string key, float value) => AddRaw(key, JsonText.Number(value));

    public JsonBody Add(string key, JsonBody value) => AddRaw(key, value.ToString());

    /// 문자열 배열. ["glb","fbx"]
    public JsonBody AddStrings(string key, params string[] values)
    {
        var items = new string[values.Length];
        for (int i = 0; i < values.Length; i++) items[i] = JsonText.Quote(values[i]);
        return AddRaw(key, Array(items));
    }

    /// 이미 JSON인 값(객체, 배열)을 그대로 싣는다.
    public JsonBody AddRaw(string key, string json)
    {
        if (!empty) sb.Append(',');
        sb.Append(JsonText.Quote(key)).Append(':').Append(json);
        empty = false;
        return this;
    }

    /// 이미 JSON인 원소들을 배열로 묶는다.
    public static string Array(params object[] items)
    {
        var array = new StringBuilder("[");
        for (int i = 0; i < items.Length; i++)
        {
            if (i > 0) array.Append(',');
            array.Append(items[i]);
        }
        return array.Append(']').ToString();
    }

    public override string ToString() => sb.ToString() + "}";
}
