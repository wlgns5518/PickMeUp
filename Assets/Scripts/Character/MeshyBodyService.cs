using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// 소환한 캐릭터의 몸을 뒤에서 굽는 일꾼. 빌드에서도 돈다.
//
// 소환은 즉시 끝나야 한다. 카드가 뒤집히는 데 3분을 기다리게 할 수는 없으므로,
// 몸 굽기는 카드가 나온 뒤에 조용히 시작해서 조용히 끝난다. 다 구워지기 전에 전투에 나가면
// 그 캐릭터는 공용 몸(Y Bot)으로 나가고, 다음 판부터 제 몸으로 나온다.
//
// 한 번에 하나씩만 굽는다. 10연차를 돌리면 열 명이 줄을 서는데, 한꺼번에 보내면 Meshy 쪽에서
// 막히기도 하고 텍스처 열 장이 동시에 메모리에 올라온다.
//
// 굽고 나면 GLB가 디스크에 남는다(CharacterModelStore). 다음에 게임을 켤 때는 크레딧을 쓰지 않고
// 그 파일에서 다시 세운다.
public class MeshyBodyService : MonoBehaviour
{
    public enum BodyState
    {
        None,     // 아직 부탁받은 적 없다
        Queued,   // 줄을 섰다
        Working,  // 굽는 중
        Ready,    // 몸이 섰다
        Failed,   // 못 구웠다. 공용 몸으로 나간다
    }

    private const float PollInterval = 4f;
    private const float TaskTimeout = 900f;
    private const string EnabledKey = "MeshyBodyService.Enabled";

    // 굽기를 끌 수 있게 둔다. 에디터에서 소환을 반복 시험할 때 한 번에 44크레딧씩 나가면 곤란하다.
    public static bool Enabled
    {
        get => PlayerPrefs.GetInt(EnabledKey, 1) != 0;
        set => PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
    }

    private static MeshyBodyService instance;

    private static MeshyBodyService Instance
    {
        get
        {
            if (instance != null) return instance;

            var go = new GameObject("[MeshyBodyService]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<MeshyBodyService>();
            return instance;
        }
    }

    private readonly Dictionary<string, BodyState> states = new Dictionary<string, BodyState>();
    private readonly Dictionary<string, float> progress = new Dictionary<string, float>();
    private readonly Queue<CharacterSO> queue = new Queue<CharacterSO>();
    private bool working;

    // ── 바깥에서 부르는 것 ───────────────────────────────────────────────

    /// 아무 코루틴이나 여기서 돌린다. 씬이 갈려도 죽지 않는 호스트가 필요할 때.
    public static Coroutine Run(IEnumerator routine) => Instance.StartCoroutine(routine);

    public static BodyState StateOf(CharacterSO character)
    {
        if (character == null) return BodyState.None;
        if (CharacterBodyFactory.Ready(character) != null) return BodyState.Ready;

        return instance != null && instance.states.TryGetValue(character.Id, out BodyState state)
            ? state
            : BodyState.None;
    }

    /// 굽는 중일 때의 진행률(0~1). 다른 상태에서는 의미가 없다.
    public static float ProgressOf(CharacterSO character)
    {
        if (character == null || instance == null) return 0f;
        return instance.progress.TryGetValue(character.Id, out float value) ? value : 0f;
    }

    /// 이 캐릭터의 몸을 만들어 달라. 이미 있거나 이미 굽는 중이면 아무것도 하지 않는다.
    /// 소환이 끝난 직후에 부른다(CardSpawner).
    public static void Request(CharacterSO character)
    {
        if (character == null) return;

        BodyState state = StateOf(character);
        if (state == BodyState.Ready || state == BodyState.Queued || state == BodyState.Working) return;

        // 지난번에 구워 둔 GLB가 디스크에 있으면 크레딧을 쓰지 않는다. 읽어 세우기만 하면 된다.
        if (CharacterModelStore.Exists(character.Id))
        {
            Instance.Enqueue(character);
            return;
        }

        if (!Enabled)
        {
            Debug.Log($"[MeshyBodyService] 몸 굽기가 꺼져 있어 {character.characterName}은 공용 몸으로 나간다.");
            return;
        }

        Instance.Enqueue(character);
    }

    /// 이미 받아 둔 GLB가 있는 캐릭터만 세운다. Meshy를 두드리지 않으므로 크레딧이 들지 않는다.
    ///
    /// 전투에 들어갈 때 부르는 쪽이 이것이다. Request를 부르면 아직 몸이 없는 캐릭터까지
    /// 굽기 시작해서, 던전에 한 번 들어가는 것만으로 파티 전원분 크레딧이 나가 버린다.
    /// 굽기를 시작하는 것은 소환하는 순간뿐이어야 한다.
    public static void RebuildIfDownloaded(CharacterSO character)
    {
        if (character == null) return;
        if (!CharacterModelStore.Exists(character.Id)) return;

        BodyState state = StateOf(character);
        if (state == BodyState.Ready || state == BodyState.Queued || state == BodyState.Working) return;

        Instance.Enqueue(character);
    }

    /// 이 명단의 몸이 다 설 때까지 기다린다. 정해진 시간을 넘기면 그대로 돌아온다 —
    /// 아직 안 된 캐릭터는 공용 몸으로 나가면 되지, 전투를 막을 일은 아니다.
    public static IEnumerator WaitUntilReady(IReadOnlyList<CharacterSO> lineup, float timeoutSeconds)
    {
        if (lineup == null || lineup.Count == 0) yield break;

        // 디스크에 있는데 아직 안 세운 몸이 있으면 여기서 세운다(크레딧 안 씀).
        for (int i = 0; i < lineup.Count; i++) RebuildIfDownloaded(lineup[i]);

        float waited = 0f;
        while (waited < timeoutSeconds)
        {
            bool pending = false;
            for (int i = 0; i < lineup.Count; i++)
            {
                CharacterSO c = lineup[i];
                if (c == null) continue;

                // 아직 API를 두드리는 중인 몸은 기다리지 않는다 — 몇 분이 걸린다.
                // 디스크에서 읽어 세우는 것(몇 초)만 기다린다.
                if (StateOf(c) != BodyState.Ready && CharacterModelStore.Exists(c.Id)) pending = true;
            }
            if (!pending) yield break;

            waited += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    // ── 줄 세우기 ────────────────────────────────────────────────────────

    private void Enqueue(CharacterSO character)
    {
        states[character.Id] = BodyState.Queued;
        queue.Enqueue(character);
        if (!working) StartCoroutine(WorkThroughQueue());
    }

    private IEnumerator WorkThroughQueue()
    {
        working = true;
        while (queue.Count > 0)
        {
            CharacterSO character = queue.Dequeue();
            if (character == null) continue;
            yield return MakeBody(character);
        }
        working = false;
    }

    // ── 몸 하나 만들기 ───────────────────────────────────────────────────

    private IEnumerator MakeBody(CharacterSO character)
    {
        string id = character.Id;
        states[id] = BodyState.Working;
        progress[id] = 0f;

        // 파일이 이미 있으면 굽지 않고 세우기만 한다.
        if (!CharacterModelStore.Exists(id))
        {
            bool downloaded = false;
            yield return Bake(character, ok => downloaded = ok);
            if (!downloaded)
            {
                states[id] = BodyState.Failed;
                yield break;
            }
        }

        progress[id] = 0.95f;
        GameObject body = null;
        yield return CharacterBodyFactory.Build(character, b => body = b);

        states[id] = body != null ? BodyState.Ready : BodyState.Failed;
        progress[id] = 1f;

        if (body != null)
            Debug.Log($"[MeshyBodyService] {character.characterName}의 몸이 준비됐다.");
        else
            Debug.LogWarning($"[MeshyBodyService] {character.characterName}의 몸을 세우지 못했다. 공용 몸으로 나간다.");
    }

    // 초상화 → 외형 설명 → 전신 시트 → 메시 → 리깅 → GLB 저장.
    // 세 번의 Meshy 태스크는 서버 안에서 태스크 id로 이어진다(MeshyBodyRecipe 참조).
    private IEnumerator Bake(CharacterSO character, Action<bool> onDone)
    {
        string id = character.Id;
        string key = ApiKeys.Meshy;
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogWarning($"[MeshyBodyService] Meshy 키가 없어 {character.characterName}의 몸을 굽지 못한다. " +
                             $"{ApiKeys.FilePath}를 채워라.");
            onDone(false);
            yield break;
        }

        string appearance = null;
        yield return DescribeAppearance(character, a => appearance = a);
        progress[id] = 0.05f;

        string sheetTask = null;
        yield return CreateTask(MeshyBodyRecipe.SheetEndpoint,
                                MeshyBodyRecipe.SheetBody(MeshyBodyRecipe.SheetPrompt(appearance)),
                                t => sheetTask = t);
        if (sheetTask == null) { onDone(false); yield break; }

        MeshyBodyRecipe.ImageTask sheet = null;
        yield return Await<MeshyBodyRecipe.ImageTask>(MeshyBodyRecipe.SheetEndpoint, sheetTask,
            p => progress[id] = 0.05f + p * 0.15f, t => sheet = t);
        if (sheet == null || sheet.image_urls == null || sheet.image_urls.Length == 0) { onDone(false); yield break; }

        string meshTask = null;
        yield return CreateTask(MeshyBodyRecipe.MeshEndpoint, MeshyBodyRecipe.MeshBody(sheetTask), t => meshTask = t);
        if (meshTask == null) { onDone(false); yield break; }

        MeshyBodyRecipe.ModelTask mesh = null;
        yield return Await<MeshyBodyRecipe.ModelTask>(MeshyBodyRecipe.MeshEndpoint, meshTask,
            p => progress[id] = 0.20f + p * 0.40f, t => mesh = t);
        if (mesh == null) { onDone(false); yield break; }

        string rigTask = null;
        yield return CreateTask(MeshyBodyRecipe.RigEndpoint, MeshyBodyRecipe.RigBody(meshTask), t => rigTask = t);
        if (rigTask == null) { onDone(false); yield break; }

        MeshyBodyRecipe.RigTask rig = null;
        yield return Await<MeshyBodyRecipe.RigTask>(MeshyBodyRecipe.RigEndpoint, rigTask,
            p => progress[id] = 0.60f + p * 0.30f, t => rig = t);

        if (rig == null || rig.result == null || string.IsNullOrEmpty(rig.result.rigged_character_glb_url))
        {
            Debug.LogError($"[MeshyBodyService] {character.characterName}: 리깅이 끝났는데 GLB 주소가 없다.");
            onDone(false);
            yield break;
        }

        // 6MB가 넘는다. 메모리에 통째로 올리지 않고 파일로 바로 흘려 넣는다.
        bool saved = false;
        yield return DownloadToStore(rig.result.rigged_character_glb_url, id, ok => saved = ok);
        onDone(saved);
    }

    // ── Gemini: 초상화 읽기 ──────────────────────────────────────────────

    [Serializable] private class GeminiPart { public string text; }
    [Serializable] private class GeminiContent { public GeminiPart[] parts; }
    [Serializable] private class GeminiCandidate { public GeminiContent content; }
    [Serializable] private class GeminiResponse { public GeminiCandidate[] candidates; }

    private IEnumerator DescribeAppearance(CharacterSO character, Action<string> onDone)
    {
        string key = ApiKeys.Gemini;
        byte[] png = PortraitPng(character.portrait);

        if (string.IsNullOrEmpty(key) || png == null)
        {
            Debug.LogWarning($"[MeshyBodyService] {character.characterName}: " +
                             (png == null ? "초상화를 읽지 못해" : "Gemini 키가 없어") +
                             " 직업만 보고 외형을 짓는다. 3D가 카드 그림과 다른 사람이 된다.");
            onDone(MeshyBodyRecipe.AppearanceFromJob(character.job));
            yield break;
        }

        string body = "{\"contents\":[{\"parts\":[" +
                      "{\"text\":" + MeshyBodyRecipe.EscapeJson(MeshyBodyRecipe.AppearanceInstruction) + "}," +
                      "{\"inline_data\":{\"mime_type\":\"image/png\",\"data\":\"" + Convert.ToBase64String(png) + "\"}}" +
                      "]}]," +
                      // 생각을 끄지 않으면 사고 토큰이 출력 한도를 먹어 문단이 한 줄에서 잘린다.
                      "\"generationConfig\":{\"temperature\":0.4,\"maxOutputTokens\":500," +
                      "\"thinkingConfig\":{\"thinkingBudget\":0}}}";

        string url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key=" + key;

        using (UnityWebRequest request = Json(url, "POST", body))
        {
            yield return request.SendWebRequest();

            string described = null;
            if (request.result == UnityWebRequest.Result.Success)
            {
                var parsed = JsonUtility.FromJson<GeminiResponse>(request.downloadHandler.text);
                if (parsed != null && parsed.candidates != null && parsed.candidates.Length > 0 &&
                    parsed.candidates[0].content != null && parsed.candidates[0].content.parts != null &&
                    parsed.candidates[0].content.parts.Length > 0)
                {
                    described = parsed.candidates[0].content.parts[0].text;
                }
            }
            else
            {
                Debug.LogWarning($"[MeshyBodyService] Gemini 실패({request.responseCode}): {request.downloadHandler.text}");
            }

            if (string.IsNullOrWhiteSpace(described))
            {
                onDone(MeshyBodyRecipe.AppearanceFromJob(character.job));
                yield break;
            }

            Debug.Log($"[MeshyBodyService] {character.characterName} 외형: {described.Trim()}");
            onDone(described.Trim());
        }
    }

    // 스프라이트의 텍스처는 읽기 허용이 아닌 경우가 많다(에디터에서 임포트한 것이 그렇다).
    // 렌더 텍스처에 한 번 그려서 읽을 수 있는 복사본을 뜬다 — 임포트 설정과 무관하게 통한다.
    private static byte[] PortraitPng(Sprite portrait)
    {
        if (portrait == null || portrait.texture == null) return null;

        Texture2D source = portrait.texture;
        RenderTexture buffer = RenderTexture.GetTemporary(
            source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

        RenderTexture previous = RenderTexture.active;
        Texture2D readable = null;
        try
        {
            Graphics.Blit(source, buffer);
            RenderTexture.active = buffer;

            readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply();
            return readable.EncodeToPNG();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(buffer);
            if (readable != null) Destroy(readable);
        }
    }

    // ── Meshy 통신 ───────────────────────────────────────────────────────

    private IEnumerator CreateTask(string endpoint, string body, Action<string> onDone)
    {
        using (UnityWebRequest request = Meshy($"{MeshyBodyRecipe.BaseUrl}/{endpoint}", "POST", body))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[MeshyBodyService] {endpoint} 태스크 생성 실패({request.responseCode}): " +
                               request.downloadHandler.text);
                onDone(null);
                yield break;
            }

            var created = JsonUtility.FromJson<MeshyBodyRecipe.CreateResponse>(request.downloadHandler.text);
            if (created == null || string.IsNullOrEmpty(created.result))
            {
                Debug.LogError($"[MeshyBodyService] {endpoint}가 태스크 id를 돌려주지 않았다: {request.downloadHandler.text}");
                onDone(null);
                yield break;
            }

            Debug.Log($"[MeshyBodyService] {endpoint} 태스크 생성: {created.result}");
            onDone(created.result);
        }
    }

    private IEnumerator Await<T>(string endpoint, string taskId, Action<float> onProgress, Action<T> onDone)
        where T : class
    {
        float elapsed = 0f;
        while (elapsed < TaskTimeout)
        {
            using (UnityWebRequest request = Meshy($"{MeshyBodyRecipe.BaseUrl}/{endpoint}/{taskId}", "GET", null))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string json = request.downloadHandler.text;
                    T task = JsonUtility.FromJson<T>(json);
                    string status = StatusOf(task);
                    onProgress?.Invoke(Mathf.Clamp01(ProgressOf(task) / 100f));

                    if (MeshyBodyRecipe.IsDone(status)) { onDone(task); yield break; }
                    if (MeshyBodyRecipe.IsDead(status))
                    {
                        Debug.LogError($"[MeshyBodyService] {endpoint} 태스크가 {status}로 끝났다: {json}");
                        onDone(null);
                        yield break;
                    }
                }
            }

            yield return new WaitForSecondsRealtime(PollInterval);
            elapsed += PollInterval;
        }

        Debug.LogError($"[MeshyBodyService] {endpoint} 태스크 {taskId}가 {TaskTimeout}초 안에 끝나지 않았다.");
        onDone(null);
    }

    private static string StatusOf(object task) =>
        task is MeshyBodyRecipe.ImageTask i ? i.status :
        task is MeshyBodyRecipe.ModelTask m ? m.status :
        task is MeshyBodyRecipe.RigTask r ? r.status : null;

    private static int ProgressOf(object task) =>
        task is MeshyBodyRecipe.ImageTask i ? i.progress :
        task is MeshyBodyRecipe.ModelTask m ? m.progress :
        task is MeshyBodyRecipe.RigTask r ? r.progress : 0;

    private IEnumerator DownloadToStore(string url, string characterId, Action<bool> onDone)
    {
        string temporary = CharacterModelStore.TempPathFor(characterId);
        System.IO.Directory.CreateDirectory(CharacterModelStore.Root);

        using (var request = UnityWebRequest.Get(url))
        {
            // 서명이 붙은 임시 주소라 인증 헤더는 붙이지 않는다.
            request.downloadHandler = new DownloadHandlerFile(temporary) { removeFileOnAbort = true };
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[MeshyBodyService] GLB 내려받기 실패({request.responseCode}): {url}");
                onDone(false);
                yield break;
            }
        }

        onDone(CharacterModelStore.Commit(characterId));
    }

    private static UnityWebRequest Meshy(string url, string method, string body)
    {
        UnityWebRequest request = Json(url, method, body);
        request.SetRequestHeader("Authorization", "Bearer " + ApiKeys.Meshy);
        return request;
    }

    private static UnityWebRequest Json(string url, string method, string body)
    {
        var request = new UnityWebRequest(url, method) { downloadHandler = new DownloadHandlerBuffer() };
        if (!string.IsNullOrEmpty(body))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.SetRequestHeader("Content-Type", "application/json");
        }
        return request;
    }
}
