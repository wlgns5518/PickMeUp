using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

// 바깥 서비스 층(MeshyClient, GeminiClient, 재시도, 요청 본문)을 크레딧 없이 돌려 본다.
// 가짜 통신(FakeTransport)이 준비된 응답을 차례로 돌려주고, 기다림은 건너뛴다.
public class ExternalServiceTests
{
    private sealed class FakeTransport : IWebTransport
    {
        public readonly List<WebCall> Calls = new List<WebCall>();
        private readonly Queue<WebReply> replies = new Queue<WebReply>();

        public FakeTransport Reply(WebReply reply)
        {
            replies.Enqueue(reply);
            return this;
        }

        public Task<WebReply> SendAsync(WebCall call)
        {
            Calls.Add(call);
            return Task.FromResult(replies.Count > 0 ? replies.Dequeue() : WebReply.Fail(404, "no reply"));
        }

        public Task<WebReply> DownloadFileAsync(string url, string destinationPath, int timeoutSeconds)
        {
            Calls.Add(WebCall.ForGet(url));
            return Task.FromResult(replies.Count > 0 ? replies.Dequeue() : WebReply.Fail(404, "no reply"));
        }
    }

    private static readonly Func<TimeSpan, Task> NoWait = _ => Task.CompletedTask;

    private static MeshyClient Meshy(FakeTransport fake, RetryPolicy retry = null, float timeout = 900f) =>
        new MeshyClient(new MeshyClient.Options
        {
            PollIntervalSeconds = 4f,
            TimeoutSeconds = timeout,
            Retry = retry ?? RetryPolicy.None,
            Wait = NoWait,
        }, fake, () => "test-key");

    // ── 요청 본문: 예전에 손으로 짜던 문자열과 한 글자도 다르지 않아야 한다 ─────

    [Test]
    public void 전신_시트_본문이_예전과_같다()
    {
        Assert.AreEqual(
            "{\"ai_model\":\"nano-banana-pro\",\"prompt\":\"a \\\"b\\\"\\n\",\"pose_mode\":\"t-pose\"," +
            "\"generate_multi_view\":true,\"remove_background\":true}",
            MeshyBodyRecipe.SheetBody("a \"b\"\n"));
    }

    [Test]
    public void 메시와_리깅_본문이_예전과_같다()
    {
        Assert.AreEqual(
            "{\"input_task_id\":\"t1\",\"ai_model\":\"meshy-7\",\"pose_mode\":\"t-pose\",\"should_texture\":true," +
            "\"texture_resolution\":\"2k\",\"enable_pbr\":false,\"should_remesh\":true,\"topology\":\"triangle\"," +
            "\"target_polycount\":30000,\"target_formats\":[\"glb\",\"fbx\"],\"auto_size\":true,\"origin_at\":\"bottom\"}",
            MeshyBodyRecipe.MeshBody("t1"));

        Assert.AreEqual("{\"input_task_id\":\"m1\",\"height_meters\":1.7}", MeshyBodyRecipe.RigBody("m1"));
    }

    [Test]
    public void 아이콘_그림_본문은_선택_항목을_넣은_것만_싣는다()
    {
        Assert.AreEqual(
            "{\"ai_model\":\"m\",\"prompt\":\"p\",\"aspect_ratio\":\"16:9\",\"remove_background\":false}",
            MeshyRequests.TextToImage("m", "p", aspectRatio: "16:9", removeBackground: false));
        Assert.AreEqual("{\"ai_model\":\"m\",\"prompt\":\"p\"}", MeshyRequests.TextToImage("m", "p"));
    }

    [Test]
    public void Gemini_본문이_예전과_같다()
    {
        Assert.AreEqual(
            "{\"contents\":[{\"parts\":[{\"text\":\"hi\"}]}]," +
            "\"generationConfig\":{\"temperature\":1.3,\"maxOutputTokens\":36,\"thinkingConfig\":{\"thinkingBudget\":0}}}",
            new GeminiPrompt { Text = "hi", Temperature = 1.3f, MaxOutputTokens = 36 }.ToJson());

        Assert.AreEqual(
            "{\"contents\":[{\"parts\":[{\"text\":\"X\"},{\"inline_data\":{\"mime_type\":\"image/png\",\"data\":\"AQI=\"}}]}]," +
            "\"generationConfig\":{\"temperature\":0.4,\"maxOutputTokens\":500,\"thinkingConfig\":{\"thinkingBudget\":0}}}",
            new GeminiPrompt { Text = "X", Png = new byte[] { 1, 2 }, Temperature = 0.4f, MaxOutputTokens = 500 }.ToJson());
    }

    // ── Meshy 폴링 ───────────────────────────────────────────────────────

    [Test]
    public void 태스크를_만들면_인증_헤더를_붙여_id를_돌려준다()
    {
        var fake = new FakeTransport().Reply(WebReply.Ok("{\"result\":\"task-1\"}"));

        string id = Meshy(fake).CreateImageAsync("{}").Result;

        Assert.AreEqual("task-1", id);
        Assert.AreEqual("https://api.meshy.ai/openapi/v1/text-to-image", fake.Calls[0].Url);
        Assert.AreEqual(WebCall.Post, fake.Calls[0].Method);
        Assert.AreEqual("Bearer test-key", fake.Calls[0].Headers[0].Value);
    }

    [Test]
    public void 끝날_때까지_폴링하며_진행률을_흘린다()
    {
        var fake = new FakeTransport()
            .Reply(WebReply.Ok("{\"status\":\"PENDING\",\"progress\":0}"))
            .Reply(WebReply.Ok("{\"status\":\"IN_PROGRESS\",\"progress\":50}"))
            .Reply(WebReply.Ok("{\"status\":\"SUCCEEDED\",\"progress\":100,\"image_urls\":[\"https://x/1.png\"]}"));

        var seen = new List<float>();
        MeshyProtocol.ImageTask task = Meshy(fake)
            .AwaitTaskAsync<MeshyProtocol.ImageTask>(MeshyProtocol.TextToImage, "t", (p, s) => seen.Add(p)).Result;

        Assert.AreEqual("https://x/1.png", task.FirstImageUrl);
        CollectionAssert.AreEqual(new[] { 0f, 0.5f, 1f }, seen);
        Assert.AreEqual("https://api.meshy.ai/openapi/v1/text-to-image/t", fake.Calls[0].Url);
    }

    [Test]
    public void 실패로_끝난_태스크는_서버가_준_이유를_싣고_던진다()
    {
        var fake = new FakeTransport()
            .Reply(WebReply.Ok("{\"status\":\"FAILED\",\"task_error\":{\"message\":\"nsfw\"}}"));

        var e = Assert.Throws<AggregateException>(() =>
            Meshy(fake).AwaitTaskAsync<MeshyProtocol.RigTask>(MeshyProtocol.Rigging, "t").Wait());
        StringAssert.Contains("nsfw", e.InnerException.Message);
    }

    [Test]
    public void 정해진_시간을_넘기면_TimeoutException()
    {
        var fake = new FakeTransport();
        for (int i = 0; i < 5; i++) fake.Reply(WebReply.Ok("{\"status\":\"IN_PROGRESS\"}"));

        var e = Assert.Throws<AggregateException>(() =>
            Meshy(fake, timeout: 8f).AwaitTaskAsync<MeshyProtocol.ModelTask>(MeshyProtocol.ImageTo3D, "t").Wait());
        Assert.IsInstanceOf<TimeoutException>(e.InnerException);
        Assert.AreEqual(2, fake.Calls.Count, "4초 간격으로 8초면 두 번 묻고 끝난다.");
    }

    [Test]
    public void 키가_없으면_보내기_전에_멈춘다()
    {
        var fake = new FakeTransport();
        var client = new MeshyClient(new MeshyClient.Options { Wait = NoWait }, fake, () => "");

        Assert.IsFalse(client.HasKey);
        Assert.Throws<AggregateException>(() => client.BalanceAsync().Wait());
        Assert.AreEqual(0, fake.Calls.Count);
    }

    // ── 재시도 ───────────────────────────────────────────────────────────

    [Test]
    public void 태스크_생성은_429만_다시_보내고_500은_다시_보내지_않는다()
    {
        var retry = new MeshyClient.Options().Retry;

        var limited = new FakeTransport()
            .Reply(WebReply.Fail(429, "slow down"))
            .Reply(WebReply.Ok("{\"result\":\"t\"}"));
        Assert.AreEqual("t", Meshy(limited, retry).CreateImageAsync("{}").Result);
        Assert.AreEqual(2, limited.Calls.Count);

        // 500은 서버에서 이미 태스크를 만들었을 수 있다. 다시 보내면 크레딧을 두 번 낸다.
        var broken = new FakeTransport()
            .Reply(WebReply.Fail(500, "oops"))
            .Reply(WebReply.Ok("{\"result\":\"t\"}"));
        Assert.Throws<AggregateException>(() => Meshy(broken, retry).CreateImageAsync("{}").Wait());
        Assert.AreEqual(1, broken.Calls.Count);
    }

    [Test]
    public void 폴링은_잠깐의_탈을_견딘다()
    {
        var retry = new MeshyClient.Options().Retry;
        var fake = new FakeTransport()
            .Reply(WebReply.Fail(0, null, "connection reset"))
            .Reply(WebReply.Fail(502, "bad gateway"))
            .Reply(WebReply.Ok("{\"status\":\"SUCCEEDED\"}"));

        Meshy(fake, retry).AwaitTaskAsync<MeshyProtocol.ModelTask>(MeshyProtocol.ImageTo3D, "t").Wait();
        Assert.AreEqual(3, fake.Calls.Count);
    }

    [Test]
    public void 재시도_횟수를_넘기면_마지막_실패를_돌려준다()
    {
        var fake = new FakeTransport();
        for (int i = 0; i < 5; i++) fake.Reply(WebReply.Fail(429, "slow"));
        var policy = new RetryPolicy(2, RetryPolicy.IsTransient, (a, r) => TimeSpan.Zero);

        WebReply reply = new RetryingWebTransport(fake, policy, NoWait).SendAsync(WebCall.ForGet("u")).Result;

        Assert.AreEqual(429, reply.Code);
        Assert.AreEqual(3, fake.Calls.Count, "처음 한 번 + 다시 두 번");
    }

    // ── Gemini ───────────────────────────────────────────────────────────

    [Test]
    public void Gemini는_429에_retryDelay만큼_기다렸다_다시_보낸다()
    {
        var waited = new List<double>();
        var fake = new FakeTransport()
            .Reply(WebReply.Fail(429, "{\"error\":{\"details\":[{\"retryDelay\": \"7s\"}]}}"))
            .Reply(WebReply.Ok("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"카엘리온\"}]}}]}"));
        var client = new GeminiClient(new GeminiClient.Options
        {
            RetryLogLabel = null,
            Wait = d => { waited.Add(d.TotalSeconds); return Task.CompletedTask; },
        }, fake, () => "k");

        string text = client.GenerateAsync("models/gemini-2.5-flash", new GeminiPrompt { Text = "x" }).Result;

        Assert.AreEqual("카엘리온", text);
        CollectionAssert.AreEqual(new[] { 8.0 }, waited, "retryDelay 7초 + 여유 1초");
        StringAssert.Contains("/models/gemini-2.5-flash:generateContent", fake.Calls[0].Url);
    }

    [Test]
    public void 한글_이름만_건진다()
    {
        Assert.AreEqual("카엘리온", HangulNames.CleanSingle("THOUGHTS: ...\n**이름**\n카엘리온."));

        var names = new List<string>();
        HangulNames.ExtractMany("1. 아르웬\n2) 드라키엘\nTHOUGHTS 무시\n셀레스티아", 2, names);
        CollectionAssert.AreEqual(new[] { "아르웬", "드라키엘" }, names);
    }

    [Test]
    public void 이스케이프된_문자열_필드를_되돌려_읽는다()
    {
        Assert.AreEqual("가\n나\"다", JsonText.ExtractString("{\"text\": \"가\\n나\\\"다\"}", "text"));
        Assert.IsNull(JsonText.ExtractString("{\"n\":1}", "text"));
    }
}
