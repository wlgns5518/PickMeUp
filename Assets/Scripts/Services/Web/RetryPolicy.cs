using System;
using System.Globalization;

// 실패한 요청을 다시 보낼지, 얼마나 기다렸다 보낼지.
//
// 서비스마다 규칙이 다르다. Meshy는 한꺼번에 여러 개를 주문하면(마을 파츠 스무 개) 429를 돌려주는데
// 기다리면 풀린다. Gemini는 429 본문에 "몇 초 뒤에 오라"(retryDelay)를 적어 보낸다.
// 규칙은 여기서 값으로 만들어 넘기고, 다시 보내는 일 자체는 RetryingWebTransport 한 곳이 한다.
public sealed class RetryPolicy
{
    public int MaxRetries { get; }

    private readonly Func<WebCall, WebReply, bool> isRetriable;
    private readonly Func<int, WebReply, TimeSpan> delayFor;

    public RetryPolicy(int maxRetries, Func<WebCall, WebReply, bool> isRetriable, Func<int, WebReply, TimeSpan> delayFor)
    {
        MaxRetries = Math.Max(0, maxRetries);
        this.isRetriable = isRetriable ?? throw new ArgumentNullException(nameof(isRetriable));
        this.delayFor = delayFor ?? throw new ArgumentNullException(nameof(delayFor));
    }

    public static readonly RetryPolicy None = new RetryPolicy(0, (c, r) => false, (a, r) => TimeSpan.Zero);

    public bool ShouldRetry(WebCall call, WebReply reply, int attempt) =>
        !reply.IsSuccess && attempt < MaxRetries && isRetriable(call, reply);

    public TimeSpan DelayFor(int attempt, WebReply reply) => delayFor(attempt, reply);

    // ── 판정 ─────────────────────────────────────────────────────────────

    /// 서버가 "처리하지 않았다"고 분명히 말한 실패. POST도 다시 보내도 된다 —
    /// 429(너무 많다)와 503(지금 못 받는다)은 태스크를 만들지 않고 돌려보낸 것이다.
    public static bool IsRejectedUnprocessed(WebReply reply) => reply.Code == 429 || reply.Code == 503;

    /// 잠깐 탈이 난 것으로 보이는 실패. 조회(GET)만 다시 보낸다 — 태스크를 만드는 POST는 응답만 잃고
    /// 서버에서는 이미 크레딧을 치렀을 수 있어서, 다시 보내면 같은 주문이 두 번 들어간다.
    public static bool IsTransient(WebCall call, WebReply reply)
    {
        if (IsRejectedUnprocessed(reply)) return true;
        if (!call.IsIdempotent) return false;
        return reply.IsNetworkError || (reply.Code >= 500 && reply.Code <= 504);
    }

    // ── 기다림 ───────────────────────────────────────────────────────────

    // 동시에 막힌 요청들이 같은 순간에 다시 몰리지 않게 조금씩 흩뿌린다.
    private static readonly Random Jitter = new Random();

    /// base × 2^attempt 초, cap에서 멈추고 jitter 안에서 흩뿌린다.
    public static TimeSpan Exponential(int attempt, double baseSeconds, double capSeconds, double jitterSeconds)
    {
        double spread;
        lock (Jitter) spread = Jitter.NextDouble() * jitterSeconds;
        double seconds = Math.Min(capSeconds, baseSeconds * Math.Pow(2, attempt)) + spread;
        return TimeSpan.FromSeconds(seconds);
    }

    /// 응답 본문의 "retryDelay": "12s"를 읽는다(Gemini). 없거나 못 읽으면 0.
    public static float ParseRetryDelaySeconds(string json)
    {
        string value = JsonText.ExtractString(json, "retryDelay");
        if (string.IsNullOrEmpty(value)) return 0f;
        if (value.EndsWith("s")) value = value.Substring(0, value.Length - 1);
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds) ? seconds : 0f;
    }
}
