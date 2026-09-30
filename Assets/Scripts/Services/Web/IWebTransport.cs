using System.Threading.Tasks;

// 요청을 실제로 보내는 쪽. 바깥 서비스 클라이언트(MeshyClient, GeminiClient)는 이것만 안다.
//
// 에디터 도구와 빌드가 같은 구현(UnityWebTransport)을 쓴다. 예전에는 에디터는 HttpClient,
// 빌드는 UnityWebRequest로 같은 Meshy 호출을 두 벌 짜 두었고, 재시도 규칙도 폴링 규칙도 서로 달랐다.
public interface IWebTransport
{
    /// 요청을 보내고 본문을 문자열로 받는다. 실패해도 예외를 던지지 않고 WebReply로 돌려준다.
    Task<WebReply> SendAsync(WebCall call);

    /// 본문을 메모리에 올리지 않고 파일로 바로 흘려 넣는다(GLB 한 장이 6MB가 넘는다).
    /// 서명이 붙은 임시 주소라 인증 헤더는 붙이지 않는다.
    Task<WebReply> DownloadFileAsync(string url, string destinationPath, int timeoutSeconds);
}
