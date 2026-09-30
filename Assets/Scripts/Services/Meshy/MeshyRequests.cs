// 여러 주문자가 같이 쓰는 Meshy 요청 본문.
//
// text-to-image는 소환 초상화, 전신 시트, UI 아이콘, 마을 파츠 컨셉이 모두 쓰는데, 예전에는 네 곳이
// 본문을 각자 문자열 덧셈으로 짰다. 필드 이름 하나가 바뀌면 네 곳을 다 찾아 고쳐야 했다.
public static class MeshyRequests
{
    /// text-to-image 본문. 비워 둔 선택 항목은 본문에 싣지 않는다(서버 기본값을 따른다).
    public static string TextToImage(string aiModel, string prompt, string aspectRatio = null,
        bool? removeBackground = null, string poseMode = null, bool? generateMultiView = null)
    {
        var body = new JsonBody()
            .Add("ai_model", aiModel)
            .Add("prompt", prompt);

        if (poseMode != null) body.Add("pose_mode", poseMode);
        if (generateMultiView.HasValue) body.Add("generate_multi_view", generateMultiView.Value);
        if (aspectRatio != null) body.Add("aspect_ratio", aspectRatio);
        if (removeBackground.HasValue) body.Add("remove_background", removeBackground.Value);

        return body.ToString();
    }
}
