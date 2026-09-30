using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

// UnityWebRequest로 보내는 통신. 에디터 메뉴(await)와 게임 안(코루틴에서 WaitForTask)이 같은 것을 쓴다.
//
// HttpClient를 쓰지 않는 이유: IL2CPP 빌드와 모바일에서 인증서·프록시 처리가 갈리고, 빌드에서 도는
// 소환 몸 굽기(MeshyBodyService)는 원래부터 UnityWebRequest였다. 에디터만 HttpClient로 따로 두면
// 같은 Meshy 호출이 두 벌이 된다.
//
// 반드시 메인 스레드에서 부른다. UnityWebRequest는 메인 스레드에서만 만들 수 있고, await 뒤의 재개도
// Unity 동기화 문맥을 타고 메인 스레드로 돌아온다 — 그래서 어디에도 ConfigureAwait(false)를 쓰지 않는다.
public sealed class UnityWebTransport : IWebTransport
{
    public async Task<WebReply> SendAsync(WebCall call)
    {
        using (var request = new UnityWebRequest(call.Url, call.Method) { downloadHandler = new DownloadHandlerBuffer() })
        {
            if (call.Body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(call.Body));
                request.SetRequestHeader("Content-Type", call.ContentType);
            }

            foreach (var header in call.Headers) request.SetRequestHeader(header.Key, header.Value);
            request.timeout = call.TimeoutSeconds;

            await request.SendWebRequest().AsTask();
            return Reply(request, request.downloadHandler.text);
        }
    }

    public async Task<WebReply> DownloadFileAsync(string url, string destinationPath, int timeoutSeconds)
    {
        string directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using (var request = UnityWebRequest.Get(url))
        {
            request.downloadHandler = new DownloadHandlerFile(destinationPath) { removeFileOnAbort = true };
            request.timeout = timeoutSeconds;

            await request.SendWebRequest().AsTask();
            return Reply(request, null);
        }
    }

    /// 그림 한 장을 텍스처로 받는다. 실패하면 null. 받은 텍스처를 지우는 것은 부른 쪽의 몫이다.
    public static async Task<Texture2D> DownloadTextureAsync(string url)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
        {
            await request.SendWebRequest().AsTask();
            if (request.result == UnityWebRequest.Result.Success) return DownloadHandlerTexture.GetContent(request);

            Debug.LogError($"[UnityWebTransport] 그림 받기 실패({request.responseCode}): {request.error}");
            return null;
        }
    }

    private static WebReply Reply(UnityWebRequest request, string text) =>
        request.result == UnityWebRequest.Result.Success
            ? WebReply.Ok(text, request.responseCode)
            : WebReply.Fail(request.responseCode, text, request.error);
}

public static class AsyncOperationTaskExtensions
{
    /// 끝나면 완료되는 Task로 바꾼다. completed는 메인 스레드에서 불리고, 이미 끝난 작업에 붙이면 곧바로 불린다.
    public static Task AsTask(this AsyncOperation operation)
    {
        if (operation.isDone) return Task.CompletedTask;

        var done = new TaskCompletionSource<bool>();
        operation.completed += _ => done.TrySetResult(true);
        return done.Task;
    }
}
