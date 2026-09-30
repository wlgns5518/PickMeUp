using System;
using System.Threading.Tasks;
using UnityEngine;

// 다른 통신을 감싸 실패한 요청을 규칙(RetryPolicy)대로 다시 보낸다.
//
// 감싸는 쪽으로 둔 이유: 다시 보내기는 Meshy와 Gemini가 똑같이 필요한데 규칙만 다르다.
// 클라이언트마다 재시도 반복문을 따로 짜 두면(예전 MeshyApi, MeshyCharacterGenerator) 한쪽은 5xx를 다시 보내고
// 다른 쪽은 안 보내는 식으로 갈라진다.
public sealed class RetryingWebTransport : IWebTransport
{
    private readonly IWebTransport inner;
    private readonly RetryPolicy policy;
    private readonly Func<TimeSpan, Task> wait;
    private readonly string logLabel;

    /// wait를 비우면 Task.Delay로 기다린다. 테스트에서는 기다리지 않는 것을 넘긴다.
    /// logLabel을 주면 다시 보낼 때마다 경고를 남긴다. 한꺼번에 스무 개를 주문하는 도구는 비워 둔다.
    public RetryingWebTransport(IWebTransport inner, RetryPolicy policy, Func<TimeSpan, Task> wait = null, string logLabel = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.policy = policy ?? RetryPolicy.None;
        this.wait = wait ?? (delay => Task.Delay(delay));
        this.logLabel = logLabel;
    }

    public Task<WebReply> SendAsync(WebCall call) => Repeat(call, () => inner.SendAsync(call));

    public Task<WebReply> DownloadFileAsync(string url, string destinationPath, int timeoutSeconds) =>
        Repeat(WebCall.ForGet(url), () => inner.DownloadFileAsync(url, destinationPath, timeoutSeconds));

    private async Task<WebReply> Repeat(WebCall call, Func<Task<WebReply>> send)
    {
        for (int attempt = 0; ; attempt++)
        {
            WebReply reply = await send();
            if (!policy.ShouldRetry(call, reply, attempt)) return reply;

            TimeSpan delay = policy.DelayFor(attempt, reply);
            if (logLabel != null)
                Debug.LogWarning($"[{logLabel}] {call.Method} {reply.Code} — {delay.TotalSeconds:0.0}초 뒤 다시 보낸다 " +
                                 $"({attempt + 1}/{policy.MaxRetries})");
            await wait(delay);
        }
    }
}
