using System;
using System.Threading.Tasks;
using UnityEngine;

// Gemini에게 글을 받아 오는 유일한 곳. 소환 이름 짓기(CharacterNameGenerator)와 초상화 읽기(CharacterAppearance)가
// 이것을 가져다 쓴다.
//
// 예전에는 초상화 읽기가 에디터(HttpClient)와 빌드(UnityWebRequest) 두 벌이었고, 이름 짓기는 또 따로
// 429 재시도를 들고 있었다. 요청 본문은 셋이 모양까지 같았다.
public sealed class GeminiClient
{
    public sealed class Options
    {
        /// 429에 다시 보낼 횟수. 본문의 retryDelay만큼 기다리고, 못 읽으면 FallbackWaitSeconds.
        public int RateLimitRetries = 2;
        public float FallbackWaitSeconds = 30f;
        public int RequestTimeoutSeconds = 120;

        /// 다시 보낼 때 남길 로그 이름. 비우면 조용히 다시 보낸다.
        public string RetryLogLabel = "Gemini";

        /// 재시도 사이에 기다리는 방법. 테스트에서만 바꾼다.
        public Func<TimeSpan, Task> Wait;
    }

    private static GeminiClient shared;

    public static GeminiClient Shared => shared ?? (shared = new GeminiClient());

    private readonly IWebTransport transport;
    private readonly Func<string> apiKey;
    private readonly Options options;

    public GeminiClient(Options options = null, IWebTransport transport = null, Func<string> apiKey = null)
    {
        this.options = options ?? new Options();
        var retry = new RetryPolicy(this.options.RateLimitRetries,
            (call, reply) => reply.Code == 429,
            (attempt, reply) => TimeSpan.FromSeconds(RateLimitWait(reply.Text, this.options.FallbackWaitSeconds)));
        this.transport = new RetryingWebTransport(transport ?? new UnityWebTransport(), retry, this.options.Wait,
                                                  this.options.RetryLogLabel);
        this.apiKey = apiKey ?? (() => ApiKeys.Gemini);
    }

    public bool HasKey => !string.IsNullOrEmpty(apiKey());

    /// 첫 후보의 첫 글 조각을 돌려준다. 비어 있으면 null. 요청이 실패하면 WebCallException.
    public async Task<string> GenerateAsync(string model, GeminiPrompt prompt)
    {
        string key = apiKey();
        if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("Gemini API 키가 없다.");

        string name = NormalizeModel(model);
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Gemini 모델 이름이 비어 있다.", nameof(model));

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{name}:generateContent?key={key}";
        WebCall call = WebCall.ForPost(url, prompt.ToJson())
            .WithContentType("application/json; charset=utf-8")
            .WithTimeout(options.RequestTimeoutSeconds);

        WebReply reply = await transport.SendAsync(call);
        if (!reply.IsSuccess) throw new WebCallException($"Gemini {name} 실패", reply);

        return FirstText(reply.Text);
    }

    // ── 응답 모양 ────────────────────────────────────────────────────────

    [Serializable] private class Part { public string text; }
    [Serializable] private class Content { public Part[] parts; }
    [Serializable] private class Candidate { public Content content; }
    [Serializable] private class Response { public Candidate[] candidates; }

    public static string FirstText(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;

        var parsed = JsonUtility.FromJson<Response>(json);
        if (parsed?.candidates == null || parsed.candidates.Length == 0) return null;

        Content content = parsed.candidates[0].content;
        if (content?.parts == null || content.parts.Length == 0) return null;

        string text = content.parts[0].text;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    // 인스펙터에 적은 모델 이름에 따옴표나 "models/"가 섞여 들어오는 일이 있었다.
    public static string NormalizeModel(string model)
    {
        string name = (model ?? "").Trim().Trim('"', '\'', '/', ' ');
        return name.StartsWith("models/") ? name.Substring("models/".Length) : name;
    }

    private static float RateLimitWait(string body, float fallback)
    {
        float seconds = RetryPolicy.ParseRetryDelaySeconds(body);
        if (seconds <= 0f) seconds = fallback;
        return Mathf.Clamp(seconds, 1f, 120f) + 1f;
    }
}

// Gemini에게 보낼 질문 한 벌. 글 한 조각과, 있으면 그림 한 장.
public sealed class GeminiPrompt
{
    public string Text;
    public byte[] Png;
    public float Temperature = 1f;
    public int MaxOutputTokens = 500;

    public string ToJson()
    {
        string textPart = new JsonBody().Add("text", Text).ToString();
        string parts = Png == null
            ? JsonBody.Array(textPart)
            : JsonBody.Array(textPart, new JsonBody()
                .Add("inline_data", new JsonBody()
                    .Add("mime_type", "image/png")
                    .Add("data", Convert.ToBase64String(Png))));

        return new JsonBody()
            .AddRaw("contents", JsonBody.Array(new JsonBody().AddRaw("parts", parts)))
            // 생각을 끄지 않으면 사고 토큰이 출력 한도를 먹어 버려 문단이 한 줄에서 잘린다.
            .Add("generationConfig", new JsonBody()
                .Add("temperature", Temperature)
                .Add("maxOutputTokens", MaxOutputTokens)
                .Add("thinkingConfig", new JsonBody().Add("thinkingBudget", 0)))
            .ToString();
    }
}
