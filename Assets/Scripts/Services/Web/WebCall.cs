using System;
using System.Collections.Generic;

// 보낼 요청 한 건. 어떻게 보내는지(IWebTransport)와 떼어 두어야 바깥 서비스(Meshy, Gemini)가
// 통신 방식을 몰라도 되고, 테스트에서는 가짜 통신을 끼워 크레딧 없이 흐름을 돌려 볼 수 있다.
public sealed class WebCall
{
    public const string Get = "GET";
    public const string Post = "POST";

    public string Method { get; }
    public string Url { get; }
    public string Body { get; }
    public string ContentType { get; private set; } = "application/json";
    public int TimeoutSeconds { get; private set; } = 120;

    private readonly List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();
    public IReadOnlyList<KeyValuePair<string, string>> Headers => headers;

    /// 같은 요청을 두 번 보내도 서버 상태가 달라지지 않는가. 다시 보낼지 정할 때 본다 —
    /// 태스크를 만드는 POST는 응답만 잃었을 뿐 서버에서는 이미 크레딧을 치렀을 수 있다.
    public bool IsIdempotent => Method == Get;

    private WebCall(string method, string url, string body)
    {
        Method = method;
        Url = url ?? throw new ArgumentNullException(nameof(url));
        Body = body;
    }

    public static WebCall ForGet(string url) => new WebCall(Get, url, null);

    public static WebCall ForPost(string url, string body) => new WebCall(Post, url, body ?? "");

    public WebCall WithHeader(string name, string value)
    {
        headers.Add(new KeyValuePair<string, string>(name, value));
        return this;
    }

    public WebCall WithContentType(string contentType)
    {
        ContentType = contentType;
        return this;
    }

    public WebCall WithTimeout(int seconds)
    {
        TimeoutSeconds = seconds;
        return this;
    }
}

// 돌아온 응답 한 건. 실패도 예외가 아니라 값으로 돌려준다 — 다시 보낼지 말지는 윗단(RetryingWebTransport)이 정한다.
public readonly struct WebReply
{
    /// HTTP 상태 코드. 서버에 닿지도 못했으면(연결 실패, 시간 초과) 0이다.
    public long Code { get; }
    public string Text { get; }
    public string Error { get; }
    public bool IsSuccess { get; }

    public bool IsNetworkError => Code == 0 && !IsSuccess;

    public WebReply(long code, string text, bool isSuccess, string error)
    {
        Code = code;
        Text = text;
        IsSuccess = isSuccess;
        Error = error;
    }

    public static WebReply Ok(string text, long code = 200) => new WebReply(code, text, true, null);

    public static WebReply Fail(long code, string text, string error = null) => new WebReply(code, text, false, error);
}

// 실패한 요청을 윗단으로 올릴 때 쓴다. 상태 코드와 본문을 함께 실어야 로그에서 원인이 보인다.
public sealed class WebCallException : Exception
{
    public long Code { get; }
    public string ResponseText { get; }

    public WebCallException(string message, WebReply reply)
        : base($"{message} ({reply.Code}): {(string.IsNullOrEmpty(reply.Text) ? reply.Error : reply.Text)}")
    {
        Code = reply.Code;
        ResponseText = reply.Text;
    }
}
