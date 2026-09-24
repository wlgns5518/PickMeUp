using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

// Meshy를 두드리는 에디터 쪽 통신 층.
//
// 주문 내용(프롬프트, 엔드포인트, 요청 본문, 응답 모양)은 하나도 여기 없다 — 전부
// MeshyBodyRecipe가 들고 있고, 빌드에서 도는 쪽(MeshyBodyService)도 같은 것을 본다.
// 두 길이 갈라지면 같은 캐릭터인데도 에디터에서 구운 몸과 빌드에서 구운 몸이 다른 사람이 된다.
//
// 여기 남는 것은 통신 방식의 차이뿐이다. 에디터는 async/await(Task)로 돌리고,
// 빌드 쪽은 코루틴으로 돈다.
public static class MeshyApi
{
    private const float PollIntervalSeconds = 4f;
    private const float TimeoutSeconds = 900f;

    // ── 태스크 만들기 ────────────────────────────────────────────────────

    /// 전신 T포즈 시트. 3D의 원본이 될 그림이다.
    public static Task<string> CreateModelSheet(string appearance) =>
        CreateTask(MeshyBodyRecipe.SheetEndpoint,
                   MeshyBodyRecipe.SheetBody(MeshyBodyRecipe.SheetPrompt(appearance)));

    /// 시트에서 메시를 뽑는다.
    public static Task<string> CreateMesh(string sheetTaskId) =>
        CreateTask(MeshyBodyRecipe.MeshEndpoint, MeshyBodyRecipe.MeshBody(sheetTaskId));

    /// 메시에 뼈를 넣는다. 여기서 나온 FBX가 Unity에서 사람으로 읽힐 몸이다.
    public static Task<string> CreateRig(string meshTaskId) =>
        CreateTask(MeshyBodyRecipe.RigEndpoint, MeshyBodyRecipe.RigBody(meshTaskId));

    /// 몸과 상관없는 그림 한 장(UI 아이콘 등). 엔드포인트는 시트와 같고, 주문서(본문)는 부르는 쪽이 들고 온다.
    public static Task<string> CreateImage(string body) =>
        CreateTask(MeshyBodyRecipe.SheetEndpoint, body);

    /// 그림 한 장에서 메시를 뽑는다(마을 건물 파츠 등). 주문서는 부르는 쪽이 들고 온다.
    public const string ImageTo3DEndpoint = "image-to-3d";

    public static Task<string> CreateModelFromImage(string body) =>
        CreateTask(ImageTo3DEndpoint, body);

    // ── 기다리기 ─────────────────────────────────────────────────────────

    /// 태스크가 끝날 때까지 폴링한다. onProgress로 진행률(0~1)과 상태를 흘려보낸다.
    public static async Task<T> Await<T>(string endpoint, string taskId, Action<float, string> onProgress)
        where T : class
    {
        double elapsed = 0;

        while (elapsed < TimeoutSeconds)
        {
            string json = await Get($"{MeshyBodyRecipe.BaseUrl}/{endpoint}/{taskId}");
            T task = JsonUtility.FromJson<T>(json);

            string status = StatusOf(task);
            onProgress?.Invoke(Mathf.Clamp01(ProgressOf(task) / 100f), status);

            if (MeshyBodyRecipe.IsDone(status)) return task;
            if (MeshyBodyRecipe.IsDead(status))
                throw new Exception($"Meshy {endpoint} 태스크가 {status}로 끝났다: {ErrorOf(task) ?? json}");

            await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds));
            elapsed += PollIntervalSeconds;
        }

        throw new TimeoutException($"Meshy {endpoint} 태스크 {taskId}가 {TimeoutSeconds}초 안에 끝나지 않았다.");
    }

    private static string StatusOf(object task) =>
        task is MeshyBodyRecipe.ImageTask i ? i.status :
        task is MeshyBodyRecipe.ModelTask m ? m.status :
        task is MeshyBodyRecipe.RigTask r ? r.status : null;

    private static int ProgressOf(object task) =>
        task is MeshyBodyRecipe.ImageTask i ? i.progress :
        task is MeshyBodyRecipe.ModelTask m ? m.progress :
        task is MeshyBodyRecipe.RigTask r ? r.progress : 0;

    private static string ErrorOf(object task)
    {
        MeshyBodyRecipe.TaskError error =
            task is MeshyBodyRecipe.ImageTask i ? i.task_error :
            task is MeshyBodyRecipe.ModelTask m ? m.task_error :
            task is MeshyBodyRecipe.RigTask r ? r.task_error : null;
        return error?.message;
    }

    // ── HTTP ─────────────────────────────────────────────────────────────

    private static async Task<string> CreateTask(string endpoint, string body)
    {
        string json = await Post($"{MeshyBodyRecipe.BaseUrl}/{endpoint}", body);
        var created = JsonUtility.FromJson<MeshyBodyRecipe.CreateResponse>(json);
        if (created == null || string.IsNullOrEmpty(created.result))
            throw new Exception($"Meshy {endpoint}가 태스크 id를 돌려주지 않았다: {json}");

        return created.result;
    }

    private static HttpClient Client()
    {
        string key = ApiKeys.Meshy;
        if (string.IsNullOrEmpty(key))
            throw new Exception($"Meshy API 키가 없다. {ApiKeys.FilePath} 또는 환경변수 MESHY_API_KEY를 채워라.");

        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return client;
    }

    // 한꺼번에 여러 개를 주문하면(마을 파츠 스무 개) 429(Rate limit exceeded)가 돌아온다.
    // 폴링에서 429가 나서 포기하면 이미 크레딧을 낸 태스크를 버리게 되므로, 429만은 기다렸다가 다시 한다.
    private const int RateLimitRetries = 8;

    private static async Task<string> Post(string url, string body)
    {
        for (int attempt = 0; ; attempt++)
        {
            using (HttpClient client = Client())
            using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await client.PostAsync(url, content))
            {
                string text = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode) return text;
                if (IsRateLimited(response) && attempt < RateLimitRetries) { await Backoff(attempt); continue; }
                throw new Exception($"Meshy POST {url} 실패 ({(int)response.StatusCode}): {text}");
            }
        }
    }

    private static async Task<string> Get(string url)
    {
        for (int attempt = 0; ; attempt++)
        {
            using (HttpClient client = Client())
            using (HttpResponseMessage response = await client.GetAsync(url))
            {
                string text = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode) return text;
                if (IsRateLimited(response) && attempt < RateLimitRetries) { await Backoff(attempt); continue; }
                throw new Exception($"Meshy GET {url} 실패 ({(int)response.StatusCode}): {text}");
            }
        }
    }

    private static bool IsRateLimited(HttpResponseMessage response) => (int)response.StatusCode == 429;

    // 2, 4, 8 … 초에 조금씩 흩뿌린다. 동시에 막힌 요청들이 같은 순간에 다시 몰리지 않게.
    private static readonly System.Random Jitter = new System.Random();

    private static Task Backoff(int attempt)
    {
        double spread;
        lock (Jitter) spread = Jitter.NextDouble() * 1.5;
        double seconds = Math.Min(60, Math.Pow(2, attempt + 1)) + spread;
        return Task.Delay(TimeSpan.FromSeconds(seconds));
    }

    /// 결과 파일을 그대로 디스크에 떨어뜨린다. 서명이 붙은 임시 URL이라 인증 헤더는 붙이지 않는다.
    public static async Task Download(string url, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        using (HttpResponseMessage response = await client.GetAsync(url))
        {
            if (!response.IsSuccessStatusCode)
                throw new Exception($"내려받기 실패 ({(int)response.StatusCode}): {url}");

            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            File.WriteAllBytes(destinationPath, bytes);
        }
    }

    /// 남은 크레딧. 굽기 전에 한 번 물어 봐서 도중에 끊기는 것을 막는다.
    public static async Task<int> Balance()
    {
        string json = await Get($"{MeshyBodyRecipe.BaseUrl}/balance");
        var balance = JsonUtility.FromJson<MeshyBodyRecipe.BalanceResponse>(json);
        return balance != null ? balance.balance : -1;
    }
}
