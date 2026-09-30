using System;
using System.Threading.Tasks;
using UnityEngine;

// Meshy를 두드리는 유일한 곳. 에디터 메뉴(몸·아이콘·마을 파츠 굽기)와 게임 안(소환 초상화, 소환 몸 굽기)이
// 모두 이 클라이언트를 가져다 쓴다.
//
// 예전에는 같은 일을 세 군데서 따로 했다 — 에디터의 MeshyApi(HttpClient), 소환 몸 굽기의 MeshyBodyService,
// 초상화를 그리는 MeshyCharacterGenerator(둘 다 UnityWebRequest). 인증, 폴링, 429 처리, 상태 판정이 셋 다
// 조금씩 달라서, 에디터에서 되는 주문이 게임 안에서는 폴링 도중 포기하는 식으로 갈렸다.
//
// 하는 일은 넷뿐이다: 태스크 만들기, 끝날 때까지 기다리기, 결과 파일 받기, 잔액 묻기.
// 무엇을 주문할지(본문)는 부르는 쪽이 들고 온다.
public sealed class MeshyClient
{
    public sealed class Options
    {
        public float PollIntervalSeconds = 4f;
        public float TimeoutSeconds = 900f;
        public int RequestTimeoutSeconds = 120;
        public int DownloadTimeoutSeconds = 300;

        /// 한꺼번에 여러 개를 주문하면(마을 파츠 스무 개) 429가 돌아온다. 폴링에서 429로 포기하면
        /// 이미 크레딧을 낸 태스크를 버리게 되므로, 기다렸다가 다시 한다(2, 4, 8 … 60초).
        public RetryPolicy Retry = new RetryPolicy(8, RetryPolicy.IsTransient,
            (attempt, reply) => RetryPolicy.Exponential(attempt, 2, 60, 1.5));

        /// 다시 보낼 때 남길 로그 이름. 비우면 조용히 다시 보낸다.
        public string RetryLogLabel;

        /// 폴링과 재시도 사이에 기다리는 방법. 테스트에서만 바꾼다.
        public Func<TimeSpan, Task> Wait;
    }

    private static MeshyClient shared;

    /// 기본 설정의 클라이언트. 키는 ApiKeys에서 매번 읽는다(키 파일을 고치면 다음 요청부터 바로 쓴다).
    public static MeshyClient Shared => shared ?? (shared = new MeshyClient());

    private readonly IWebTransport transport;
    private readonly Func<string> apiKey;
    private readonly Options options;
    private readonly Func<TimeSpan, Task> wait;

    public MeshyClient(Options options = null, IWebTransport transport = null, Func<string> apiKey = null)
    {
        this.options = options ?? new Options();
        wait = this.options.Wait ?? (delay => Task.Delay(delay));
        this.transport = new RetryingWebTransport(transport ?? new UnityWebTransport(), this.options.Retry, wait,
                                                  this.options.RetryLogLabel);
        this.apiKey = apiKey ?? (() => ApiKeys.Meshy);
    }

    public bool HasKey => !string.IsNullOrEmpty(apiKey());

    public static string MissingKeyMessage =>
        $"Meshy API 키가 없다. {ApiKeys.FilePath} 또는 환경변수 MESHY_API_KEY를 채워라.";

    // ── 태스크 만들기 ────────────────────────────────────────────────────

    /// 태스크를 만들고 id를 돌려받는다.
    public async Task<string> CreateTaskAsync(string endpoint, string body)
    {
        WebReply reply = await transport.SendAsync(Authorized(WebCall.ForPost(MeshyProtocol.Url(endpoint), body)));
        if (!reply.IsSuccess) throw new WebCallException($"Meshy {endpoint} 태스크 생성 실패", reply);

        var created = JsonUtility.FromJson<MeshyProtocol.CreateResponse>(reply.Text);
        string taskId = created?.TaskId;
        if (string.IsNullOrEmpty(taskId))
            throw new Exception($"Meshy {endpoint}가 태스크 id를 돌려주지 않았다: {reply.Text}");

        return taskId;
    }

    /// 그림 한 장(text-to-image). 주문서(본문)는 부르는 쪽이 들고 온다.
    public Task<string> CreateImageAsync(string body) => CreateTaskAsync(MeshyProtocol.TextToImage, body);

    /// 그림 한 장에서 메시를 뽑는다(image-to-3d).
    public Task<string> CreateModelFromImageAsync(string body) => CreateTaskAsync(MeshyProtocol.ImageTo3D, body);

    // ── 기다리기 ─────────────────────────────────────────────────────────

    /// 태스크가 끝날 때까지 폴링한다. onProgress로 진행률(0~1)과 상태를 흘려보낸다.
    /// 실패로 끝나거나 시간을 넘기면 예외를 던진다.
    public async Task<T> AwaitTaskAsync<T>(string endpoint, string taskId, Action<float, string> onProgress = null)
        where T : class, IMeshyTask
    {
        double elapsed = 0;

        while (elapsed < options.TimeoutSeconds)
        {
            WebReply reply = await transport.SendAsync(Authorized(WebCall.ForGet(MeshyProtocol.TaskUrl(endpoint, taskId))));
            if (!reply.IsSuccess) throw new WebCallException($"Meshy {endpoint} 태스크 조회 실패", reply);

            T task = JsonUtility.FromJson<T>(reply.Text);
            string status = task?.Status;
            onProgress?.Invoke(Mathf.Clamp01((task?.Progress ?? 0) / 100f), status);

            if (MeshyProtocol.IsDone(status)) return task;
            if (MeshyProtocol.IsDead(status))
                throw new Exception($"Meshy {endpoint} 태스크가 {status}로 끝났다: {task?.ErrorMessage ?? reply.Text}");

            await wait(TimeSpan.FromSeconds(options.PollIntervalSeconds));
            elapsed += options.PollIntervalSeconds;
        }

        throw new TimeoutException($"Meshy {endpoint} 태스크 {taskId}가 {options.TimeoutSeconds}초 안에 끝나지 않았다.");
    }

    // ── 받기, 잔액 ───────────────────────────────────────────────────────

    /// 결과 파일을 그대로 디스크에 떨어뜨린다. 서명이 붙은 임시 URL이라 인증 헤더는 붙이지 않는다.
    public async Task DownloadAsync(string url, string destinationPath)
    {
        WebReply reply = await transport.DownloadFileAsync(url, destinationPath, options.DownloadTimeoutSeconds);
        if (!reply.IsSuccess) throw new WebCallException($"내려받기 실패: {url}", reply);
    }

    /// 남은 크레딧. 굽기 전에 한 번 물어 봐서 도중에 끊기는 것을 막는다. 모르면 -1.
    public async Task<int> BalanceAsync()
    {
        WebReply reply = await transport.SendAsync(Authorized(WebCall.ForGet(MeshyProtocol.Url(MeshyProtocol.Balance))));
        if (!reply.IsSuccess) throw new WebCallException("Meshy 잔액 조회 실패", reply);

        var balance = JsonUtility.FromJson<MeshyProtocol.BalanceResponse>(reply.Text);
        return balance != null ? balance.balance : -1;
    }

    private WebCall Authorized(WebCall call)
    {
        string key = apiKey();
        if (string.IsNullOrEmpty(key)) throw new InvalidOperationException(MissingKeyMessage);

        return call.WithHeader("Authorization", "Bearer " + key).WithTimeout(options.RequestTimeoutSeconds);
    }
}
