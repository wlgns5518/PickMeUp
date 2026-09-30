using System;

// Meshy가 무엇을 받고 무엇을 돌려주는지 — 주소, 엔드포인트 이름, 응답 모양, 상태 판정.
//
// "무엇을 만들어 달라"(프롬프트, 자세, 폴리곤 수)는 여기 없다. 그건 주문하는 쪽이 들고 있다
// (몸은 MeshyBodyRecipe, 아이콘은 UiArtBaker, 마을 파츠는 VillagePartBaker, 초상화는 CharacterPortraitGenerator).
// 여기 있는 것은 누가 주문하든 같은, Meshy라는 서비스 자체의 약속이다.
public static class MeshyProtocol
{
    public const string BaseUrl = "https://api.meshy.ai/openapi/v1";

    // ── 엔드포인트 ───────────────────────────────────────────────────────

    public const string TextToImage = "text-to-image";
    public const string ImageTo3D = "image-to-3d";
    public const string MultiImageTo3D = "multi-image-to-3d";
    public const string Rigging = "rigging";
    public const string Balance = "balance";

    public static string Url(string endpoint) => $"{BaseUrl}/{endpoint}";

    public static string TaskUrl(string endpoint, string taskId) => $"{BaseUrl}/{endpoint}/{taskId}";

    // ── 응답 모양 ────────────────────────────────────────────────────────
    //
    // 손으로 문자열을 뒤지지 않고 JsonUtility에 [Serializable] 클래스로 넘긴다 — 몸 굽기는
    // 태스크 셋을 사슬처럼 엮기 때문에, 중간 한 곳에서 필드 하나를 잘못 읽으면 뒤가 통째로
    // 조용히 어긋난다.

    [Serializable]
    public class CreateResponse
    {
        public string result;
        // 몇몇 엔드포인트는 태스크 id를 result가 아니라 id로 돌려준다.
        public string id;

        public string TaskId => !string.IsNullOrEmpty(result) ? result : id;
    }

    [Serializable] public class TaskError { public string message; }
    [Serializable] public class BalanceResponse { public int balance; }

    [Serializable]
    public class ImageTask : IMeshyTask
    {
        public string status;
        public int progress;
        public string[] image_urls;
        public TaskError task_error;

        public string Status => status;
        public int Progress => progress;
        public string ErrorMessage => task_error?.message;

        public string FirstImageUrl => image_urls != null && image_urls.Length > 0 ? image_urls[0] : null;
    }

    [Serializable] public class ModelUrls { public string glb; public string fbx; public string obj; }

    [Serializable]
    public class TextureUrls
    {
        public string base_color;
        public string normal;
        public string metallic;
        public string roughness;
    }

    [Serializable]
    public class ModelTask : IMeshyTask
    {
        public string status;
        public int progress;
        public ModelUrls model_urls;
        public TextureUrls[] texture_urls;
        public string thumbnail_url;
        public TaskError task_error;

        public string Status => status;
        public int Progress => progress;
        public string ErrorMessage => task_error?.message;
    }

    [Serializable]
    public class RigResult
    {
        public string rigged_character_fbx_url;
        public string rigged_character_glb_url;
    }

    [Serializable]
    public class RigTask : IMeshyTask
    {
        public string status;
        public int progress;
        public RigResult result;
        public TaskError task_error;

        public string Status => status;
        public int Progress => progress;
        public string ErrorMessage => task_error?.message;
    }

    // ── 상태 판정 ────────────────────────────────────────────────────────

    public static bool IsDone(string status) =>
        Is(status, "SUCCEEDED") || Is(status, "SUCCESS") || Is(status, "COMPLETED") || Is(status, "DONE") ||
        Is(status, "FINISHED");

    public static bool IsDead(string status) =>
        Is(status, "FAILED") || Is(status, "CANCELED") || Is(status, "CANCELLED") ||
        Is(status, "EXPIRED") || Is(status, "ERROR");

    private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

// 폴링하는 태스크가 공통으로 답하는 것. 그림·메시·리깅 태스크는 결과 모양만 다르고 상태를 읽는 법은 같다.
// 예전에는 이걸 `task is ImageTask i ? … : task is ModelTask m ? …`로 두 곳에서 따로 갈랐다 —
// 새 태스크 모양이 생기면 두 곳을 다 고쳐야 했다.
public interface IMeshyTask
{
    string Status { get; }
    int Progress { get; }
    string ErrorMessage { get; }
}
