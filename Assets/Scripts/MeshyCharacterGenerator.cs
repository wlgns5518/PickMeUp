using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MeshyCharacterGenerator : MonoBehaviour
{
    [Header("Meshy API")]
    [SerializeField] private string apiKey = "msy_YOUR_KEY_HERE";
    [Tooltip("Meshy text-to-image 모델 (nano-banana-pro 등)")]
    [SerializeField] private string aiModel = "nano-banana-pro";

    [Header("Gemini (이름 생성용)")]
    [SerializeField] private string geminiApiKey = "";
    [SerializeField] private string geminiModel = "gemini-2.5-flash";

    [Header("Polling")]
    [SerializeField] private float pollInterval = 3f;
    [SerializeField] private float timeoutSeconds = 600f;

    [Header("Retry")]
    [SerializeField] private int maxRetries = 3;
    [SerializeField] private float baseRetryDelay = 2f;

    [Header("Image to 3D")]
    [Tooltip("이미지 생성 후 image-to-3d로 .glb 모델까지 만든다 (크레딧 추가 소모)")]
    [SerializeField] private bool generateModel = false;
    [Tooltip("lowpoly / standard / etc.")]
    [SerializeField] private string modelType = "lowpoly";
    [SerializeField] private bool moderation = false;
    [Tooltip("on / off / auto")]
    [SerializeField] private string symmetryMode = "on";
    [Tooltip("t-pose / a-pose / etc.")]
    [SerializeField] private string poseMode = "t-pose";

    [Header("Background Removal")]
    [Tooltip("다운로드한 이미지에서 흰색 배경을 투명으로 변환")]
    [SerializeField] private bool removeWhiteBackground = true;
    [Tooltip("이 값 이상의 RGB는 흰색으로 간주 (0~255)")]
    [Range(200, 255)] [SerializeField] private int whiteThreshold = 235;
    [Tooltip("경계 부드럽게 — 임계값 ~ 255 사이는 알파를 점진적으로")]
    [SerializeField] private bool softEdge = true;

    [Header("Asset Save")]
    [Tooltip("에디터에서 생성된 캐릭터를 .asset으로 저장")]
    [SerializeField] private bool saveAsAsset = true;
    [SerializeField] private string imageDir = "Assets/CharacterImage";
    [SerializeField] private string assetDir = "Assets/CharacterSO";
    [SerializeField] private string modelDir = "Assets/CharacterModels";

    private const string TaskUrl = "https://api.meshy.ai/openapi/v1/text-to-image";
    private const string ModelTaskUrl = "https://api.meshy.ai/openapi/v1/image-to-3d";

    // 직업 풀과 프롬프트용 영어 표현이 인덱스로 1:1 매칭됨
    private static readonly JobType[] JobPool = {
        JobType.Melee, JobType.Mage, JobType.Archer, JobType.Assassin, JobType.Tank, JobType.Support,
        JobType.Carpenter, JobType.Cook, JobType.Blacksmith, JobType.Tanner,
    };
    private static readonly string[] JobPromptsEn = {
        "warrior", "mage", "archer", "assassin", "tank knight", "support healer",
        "carpenter", "cook", "blacksmith", "leatherworker",
    };
    // 추첨 가중치 — 전투 6직 합계 78%, 생산 4직 합계 22%
    // 전투직 각 13% / 생산직 각 5.5%
    private static readonly int[] JobWeights = {
        13, 13, 13, 13, 13, 13,  // 전투
         6,  5,  6,  5,          // 생산 (합 22)
    };
    private static readonly string[] Traits = { "냉소적인", "낙천적인", "고독한", "충직한", "야망 있는", "수줍은", "비밀스러운" };
    private static readonly string[] TraitsEn = { "cynical", "cheerful", "lonely", "loyal", "ambitious", "shy", "mysterious" };


    // 1성 63.949% / 2성 30% / 3성 5% / 4성 1% / 5성 0.05% / 6성 0.001% (7성 가챠 제외)
    private static readonly int[] StarWeights = { 63949, 30000, 5000, 1000, 50, 1 };

    // 체질 프리셋: 직업에 어울리는 가중치 + 약간의 랜덤 변동
    private static Constitution RollConstitution(JobType job)
    {
        Constitution c = new Constitution { name = "균형" };
        switch (job)
        {
            case JobType.Melee:      c.name = "근육질"; c.strengthGrowth = 1.6f; c.vitalityGrowth = 1.2f; c.agilityGrowth = 0.8f; c.intelligenceGrowth = 0.4f; break;
            case JobType.Mage:       c.name = "현자";   c.intelligenceGrowth = 1.8f; c.agilityGrowth = 0.6f; c.vitalityGrowth = 0.6f; c.strengthGrowth = 0.4f; break;
            case JobType.Archer:     c.name = "민첩한"; c.agilityGrowth = 1.6f; c.strengthGrowth = 1f;   c.intelligenceGrowth = 0.8f; c.vitalityGrowth = 0.6f; break;
            case JobType.Assassin:   c.name = "그림자"; c.agilityGrowth = 1.8f; c.strengthGrowth = 1.0f; c.intelligenceGrowth = 0.7f; c.vitalityGrowth = 0.5f; break;
            case JobType.Tank:       c.name = "강건한"; c.vitalityGrowth = 1.8f; c.strengthGrowth = 1.2f; c.intelligenceGrowth = 0.5f; c.agilityGrowth = 0.5f; break;
            case JobType.Support:    c.name = "조화";   c.intelligenceGrowth = 1.4f; c.vitalityGrowth = 1.0f; c.agilityGrowth = 0.8f; c.strengthGrowth = 0.8f; break;
            case JobType.Carpenter:  c.name = "근면";   c.strengthGrowth = 1.2f; c.vitalityGrowth = 1.1f; c.agilityGrowth = 0.9f; c.intelligenceGrowth = 0.8f; break;
            case JobType.Cook:       c.name = "온화";   c.vitalityGrowth = 1.2f; c.intelligenceGrowth = 1.1f; c.agilityGrowth = 0.9f; c.strengthGrowth = 0.8f; break;
            case JobType.Blacksmith: c.name = "강인";   c.strengthGrowth = 1.5f; c.vitalityGrowth = 1.3f; c.agilityGrowth = 0.6f; c.intelligenceGrowth = 0.6f; break;
            case JobType.Tanner:     c.name = "손재주"; c.agilityGrowth = 1.3f; c.intelligenceGrowth = 1.1f; c.strengthGrowth = 0.9f; c.vitalityGrowth = 0.7f; break;
        }
        c.strengthGrowth     *= UnityEngine.Random.Range(0.85f, 1.15f);
        c.intelligenceGrowth *= UnityEngine.Random.Range(0.85f, 1.15f);
        c.vitalityGrowth     *= UnityEngine.Random.Range(0.85f, 1.15f);
        c.agilityGrowth      *= UnityEngine.Random.Range(0.85f, 1.15f);
        return c;
    }

    // onUpdate는 두 번 호출됨:
    //   1) 로컬에서 만든 SO 즉시 전달 (portrait는 아직 null)
    //   2) Meshy 이미지 다운로드 완료 후 portrait 채워서 다시 전달
    public IEnumerator GenerateCharacter(Action<CharacterSO> onUpdate)
    {
        int jIdx = RollWeightedIndex(JobWeights);
        int tIdx = UnityEngine.Random.Range(0, Traits.Length);

        CharacterSO so = ScriptableObject.CreateInstance<CharacterSO>();
        so.starCount = RollStars();
        so.level     = 1;
        so.exp       = 0;
        so.expToNext = 10;
        so.job       = JobPool[jIdx];
        yield return GenerateName(name => so.characterName = name);
        so.description  = $"{Traits[tIdx]} 인간 {CharacterRules.Korean(so.job)}";
        so.constitution = RollConstitution(so.job);

        RollInitialStats(so);
        so.currentHP = HpFromStats(so.stats, so.hiddenStats);
        so.name      = $"{so.characterName} ({so.starCount}★)"; // 인스펙터/에셋 표시명

        // 1차: 이름·별·타입·스탯 전달 (이미지 전)
        onUpdate?.Invoke(so);

        string prompt =
            $"A single {TraitsEn[tIdx]} human {JobPromptsEn[jIdx]} character, " +
            "upper body portrait from the waist up, natural relaxed standing pose, " +
            "slight three-quarter view, looking forward, " +
            "semi-realistic mature fantasy illustration, detailed face and costume, " +
            "cinematic lighting, painterly style, " +
            "isolated on a pure plain white background, no scenery, no environment, " +
            "no props, no other characters, no text, " +
            "not cute, not chibi, not childish, adult proportions, " +
            "character centered and fully visible, high quality";

        string imageUrl = null;
        yield return GenerateImageUrl(prompt, url => imageUrl = url);

        if (!string.IsNullOrEmpty(imageUrl))
        {
            Texture2D tex = null;
            yield return DownloadTexture(imageUrl, t => tex = t);
            if (tex != null)
            {
                if (removeWhiteBackground) tex = MakeWhiteTransparent(tex, whiteThreshold, softEdge);
                Sprite sprite = SavePortraitAndLoadSprite(tex, so.characterName, out string assetPath);
                so.portrait = sprite;
                so.portraitAssetPath = assetPath;
            }
        }

        // 3D 모델 생성 (옵션)
        if (generateModel && !string.IsNullOrEmpty(imageUrl))
        {
            string glbUrl = null;
            yield return Generate3DFromImage(imageUrl, url => glbUrl = url);
            if (!string.IsNullOrEmpty(glbUrl))
            {
                string savedPath = null;
                yield return DownloadAndSaveGlb(glbUrl, so.characterName, p => savedPath = p);
                if (!string.IsNullOrEmpty(savedPath)) so.modelUrl = savedPath;
            }
        }

#if UNITY_EDITOR
        if (saveAsAsset) SaveCharacterAsAsset(so);
#endif

        // 2차: 이미지 포함 전달
        onUpdate?.Invoke(so);
    }

    // 초기 스탯 — 별 등급에 따라 가산점
    private static void RollInitialStats(CharacterSO so)
    {
        int baseV = 5 + so.starCount * 2;
        so.stats.strength     = baseV + UnityEngine.Random.Range(0, 4);
        so.stats.intelligence = baseV + UnityEngine.Random.Range(0, 4);
        so.stats.vitality     = baseV + UnityEngine.Random.Range(0, 4);
        so.stats.agility      = baseV + UnityEngine.Random.Range(0, 4);

        int hidden = 5 + so.starCount;
        so.hiddenStats.diligence = hidden + UnityEngine.Random.Range(-2, 3);
        so.hiddenStats.stamina   = hidden + UnityEngine.Random.Range(-2, 3);
        so.hiddenStats.stress    = UnityEngine.Random.Range(0, 10);
        // 1~2성은 멘탈 약함
        so.hiddenStats.mental = CharacterRules.IsFragileMental(so.starCount)
            ? UnityEngine.Random.Range(1, 4)
            : hidden + UnityEngine.Random.Range(-1, 4);
        so.hiddenStats.skill  = hidden + UnityEngine.Random.Range(-2, 3);
        so.hiddenStats.body   = hidden + UnityEngine.Random.Range(-1, 4);
        so.hiddenStats.sanity = hidden + UnityEngine.Random.Range(-1, 4);
    }

    private static int HpFromStats(VisibleStats s, HiddenStats h) => s.vitality * 5 + h.body * 3;

    // 태스크 생성 + 폴링 -----------------------------------------------

    private IEnumerator GenerateImageUrl(string prompt, Action<string> onComplete)
    {
        string body =
            "{\"ai_model\":" + EscapeJson(aiModel) +
            ",\"prompt\":" + EscapeJson(prompt) + "}";

        string createResponse = null;
        yield return SendWithRetry(TaskUrl, "POST", body, "이미지 태스크 생성", r => createResponse = r);

        if (string.IsNullOrEmpty(createResponse)) { onComplete?.Invoke(null); yield break; }

        string immediate = ExtractFirstUrl(createResponse);
        if (!string.IsNullOrEmpty(immediate) && immediate.StartsWith("http"))
        {
            onComplete?.Invoke(immediate);
            yield break;
        }

        string taskId = ExtractStringField(createResponse, "result");
        if (string.IsNullOrEmpty(taskId)) taskId = ExtractStringField(createResponse, "id");
        if (string.IsNullOrEmpty(taskId))
        {
            Debug.LogError($"[Meshy] 태스크 ID/이미지 URL 못 찾음: {createResponse}");
            onComplete?.Invoke(null);
            yield break;
        }

        float elapsed = 0f;
        string statusUrl = $"{TaskUrl}/{taskId}";
        while (elapsed < timeoutSeconds)
        {
            string statusBody = null;
            yield return SendWithRetry(statusUrl, "GET", null, "폴링", r => statusBody = r);

            if (!string.IsNullOrEmpty(statusBody))
            {
                string status = ExtractStringField(statusBody, "status");
                if (string.IsNullOrEmpty(status)) status = ExtractStringField(statusBody, "task_status");

                string upper = status?.ToUpperInvariant() ?? "";
                bool succeeded = upper == "SUCCEEDED" || upper == "SUCCESS" || upper == "COMPLETED" || upper == "DONE" || upper == "FINISHED";
                bool failed = upper == "FAILED" || upper == "CANCELED" || upper == "CANCELLED" || upper == "EXPIRED" || upper == "ERROR";

                string url = ExtractFirstUrl(statusBody);
                if (succeeded || (!failed && !string.IsNullOrEmpty(url) && url != statusUrl))
                {
                    onComplete?.Invoke(url);
                    yield break;
                }
                if (failed)
                {
                    Debug.LogError($"[Meshy] 태스크 종료({status}): {statusBody}");
                    onComplete?.Invoke(null);
                    yield break;
                }
                Debug.Log($"[Meshy] {taskId}: {status}");
            }

            yield return new WaitForSeconds(pollInterval);
            elapsed += pollInterval;
        }

        Debug.LogError($"[Meshy] 폴링 타임아웃: {taskId}");
        onComplete?.Invoke(null);
    }

    private static string ExtractFirstUrl(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        int s = json.IndexOf("https://");
        if (s < 0) return null;
        int e = s;
        while (e < json.Length && json[e] != '"' && json[e] != ' ' && json[e] != '\n' && json[e] != '\r') e++;
        return json.Substring(s, e - s);
    }

    private static string ExtractStringField(string json, string field)
    {
        if (string.IsNullOrEmpty(json)) return null;
        string key = "\"" + field + "\"";
        int i = json.IndexOf(key);
        if (i < 0) return null;
        i = json.IndexOf(':', i + key.Length);
        if (i < 0) return null;
        i++;
        while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
        if (i >= json.Length || json[i] != '"') return null;
        int s = i + 1;
        int e = json.IndexOf('"', s);
        return e < 0 ? null : json.Substring(s, e - s);
    }

    // image-to-3d ------------------------------------------------------

    private IEnumerator Generate3DFromImage(string imageUrl, Action<string> onComplete)
    {
        string body =
            "{" +
            "\"image_url\":" + EscapeJson(imageUrl) +
            ",\"model_type\":" + EscapeJson(modelType) +
            ",\"moderation\":" + (moderation ? "true" : "false") +
            ",\"symmetry_mode\":" + EscapeJson(symmetryMode) +
            ",\"pose_mode\":" + EscapeJson(poseMode) +
            ",\"target_formats\":[\"glb\"]" +
            "}";

        string createResponse = null;
        yield return SendWithRetry(ModelTaskUrl, "POST", body, "3D 태스크 생성", r => createResponse = r);

        if (string.IsNullOrEmpty(createResponse)) { onComplete?.Invoke(null); yield break; }

        string taskId = ExtractStringField(createResponse, "result");
        if (string.IsNullOrEmpty(taskId)) taskId = ExtractStringField(createResponse, "id");
        if (string.IsNullOrEmpty(taskId))
        {
            Debug.LogError($"[Meshy] 3D 태스크 ID 못 찾음: {createResponse}");
            onComplete?.Invoke(null);
            yield break;
        }

        Debug.Log($"[Meshy] 3D 태스크 생성됨: {taskId}");

        float elapsed = 0f;
        string statusUrl = $"{ModelTaskUrl}/{taskId}";
        while (elapsed < timeoutSeconds)
        {
            string statusBody = null;
            yield return SendWithRetry(statusUrl, "GET", null, "3D 폴링", r => statusBody = r);

            if (!string.IsNullOrEmpty(statusBody))
            {
                string status = ExtractStringField(statusBody, "status");
                string upper = status?.ToUpperInvariant() ?? "";
                bool succeeded = upper == "SUCCEEDED" || upper == "SUCCESS" || upper == "COMPLETED" || upper == "DONE" || upper == "FINISHED";
                bool failed = upper == "FAILED" || upper == "CANCELED" || upper == "CANCELLED" || upper == "EXPIRED" || upper == "ERROR";

                if (succeeded)
                {
                    string glb = ExtractStringField(statusBody, "glb");
                    Debug.Log($"[Meshy] glb URL: {glb}");
                    onComplete?.Invoke(glb);
                    yield break;
                }
                if (failed)
                {
                    Debug.LogError($"[Meshy] 3D 태스크 종료({status}): {statusBody}");
                    onComplete?.Invoke(null);
                    yield break;
                }
                Debug.Log($"[Meshy] 3D {taskId}: {status}");
            }

            yield return new WaitForSeconds(pollInterval);
            elapsed += pollInterval;
        }

        Debug.LogError($"[Meshy] 3D 폴링 타임아웃: {taskId}");
        onComplete?.Invoke(null);
    }

    // .glb 다운로드 + Assets/CharacterModels 에 저장
    private IEnumerator DownloadAndSaveGlb(string url, string characterName, Action<string> onComplete)
    {
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Meshy] glb 다운로드 실패({req.responseCode}): {req.error}");
                onComplete?.Invoke(null);
                yield break;
            }

            byte[] bytes = req.downloadHandler.data;
#if UNITY_EDITOR
            try
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string fullDir = Path.Combine(projectRoot, modelDir);
                if (!Directory.Exists(fullDir)) Directory.CreateDirectory(fullDir);

                string safeName = SanitizeFileName(string.IsNullOrEmpty(characterName) ? "character" : characterName);
                string filename = $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}_{UnityEngine.Random.Range(1000, 9999)}.glb";
                string fullPath = Path.Combine(fullDir, filename);
                File.WriteAllBytes(fullPath, bytes);

                string assetPath = $"{modelDir}/{filename}".Replace('\\', '/');
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                Debug.Log($"[Meshy] glb 저장: {assetPath} ({bytes.Length / 1024} KB)");
                onComplete?.Invoke(assetPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Meshy] glb 저장 실패: {e.Message}");
                onComplete?.Invoke(null);
            }
#else
            onComplete?.Invoke(null);
#endif
        }
    }

    // 이미지 다운로드 ---------------------------------------------------

    private IEnumerator DownloadTexture(string url, Action<Texture2D> onComplete)
    {
        using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
                onComplete?.Invoke(DownloadHandlerTexture.GetContent(req));
            else
            {
                Debug.LogError($"[Meshy] 이미지 다운로드 실패({req.responseCode}): {req.error}");
                onComplete?.Invoke(null);
            }
        }
    }

    // 흰색 배경 → 투명 변환 --------------------------------------------

    private static Texture2D MakeWhiteTransparent(Texture2D src, int threshold, bool softEdge)
    {
        try
        {
            Color32[] pixels = src.GetPixels32();
            float t = threshold;
            float range = Mathf.Max(1f, 255f - t);

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 c = pixels[i];
                int minRgb = Mathf.Min(c.r, Mathf.Min(c.g, c.b));

                if (minRgb >= 255)
                {
                    c.a = 0;
                }
                else if (minRgb >= threshold)
                {
                    if (softEdge)
                    {
                        // threshold(불투명 한계)에 가까울수록 ~255(완전 흰색)에 가까울수록 투명
                        float n = (minRgb - t) / range; // 0..1
                        c.a = (byte)Mathf.RoundToInt((1f - n) * 255f);
                    }
                    else
                    {
                        c.a = 0;
                    }
                }
                pixels[i] = c;
            }

            Texture2D result = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            result.SetPixels32(pixels);
            result.Apply();
            return result;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Meshy] 배경 제거 실패, 원본 사용: {e.Message}");
            return src;
        }
    }

    // PNG 저장 후 Sprite로 로드 (에디터 전용) --------------------------

    private Sprite SavePortraitAndLoadSprite(Texture2D tex, string characterName, out string assetPath)
    {
        assetPath = null;
#if UNITY_EDITOR
        try
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string fullDir = Path.Combine(projectRoot, imageDir);
            if (!Directory.Exists(fullDir)) Directory.CreateDirectory(fullDir);

            string safeName = SanitizeFileName(string.IsNullOrEmpty(characterName) ? "character" : characterName);
            string filename = $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}_{UnityEngine.Random.Range(1000, 9999)}.png";
            string fullPath = Path.Combine(fullDir, filename);
            File.WriteAllBytes(fullPath, tex.EncodeToPNG());

            assetPath = $"{imageDir}/{filename}".Replace('\\', '/');
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            // Sprite로 사용하기 위해 임포트 설정
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Meshy] PNG 저장 실패: {e.Message}");
            return null;
        }
#else
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
#endif
    }

#if UNITY_EDITOR
    private void SaveCharacterAsAsset(CharacterSO so)
    {
        try
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string fullDir = Path.Combine(projectRoot, assetDir);
            if (!Directory.Exists(fullDir)) Directory.CreateDirectory(fullDir);

            string safeName = SanitizeFileName(so.characterName);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{assetDir}/{safeName}.asset".Replace('\\', '/'));
            AssetDatabase.CreateAsset(so, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Meshy] CharacterSO 저장: {path}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Meshy] CharacterSO 저장 실패: {e.Message}");
        }
    }
#endif

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 || c == ' ' ? '_' : c);
        return sb.ToString();
    }

    // 공통 HTTP --------------------------------------------------------

    private IEnumerator SendWithRetry(string url, string method, string body, string label, Action<string> onComplete)
    {
        int attempt = 0;
        while (true)
        {
            UnityWebRequest req;
            if (method == "GET")
            {
                req = UnityWebRequest.Get(url);
            }
            else
            {
                req = new UnityWebRequest(url, method);
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body ?? ""));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);

            using (req)
            {
                yield return req.SendWebRequest();

                long code = req.responseCode;
                if (req.result == UnityWebRequest.Result.Success)
                {
                    onComplete?.Invoke(req.downloadHandler.text);
                    yield break;
                }

                bool retriable = code == 429 || code == 500 || code == 502 || code == 503 || code == 504;
                if (!retriable || attempt >= maxRetries)
                {
                    Debug.LogError($"[Meshy] {label} 실패({code}): {req.error}\n{req.downloadHandler.text}");
                    onComplete?.Invoke(null);
                    yield break;
                }

                float delay = baseRetryDelay * Mathf.Pow(2f, attempt);
                Debug.LogWarning($"[Meshy] {label} {code} 재시도 #{attempt + 1} ({delay:0.0}s 후)");
                yield return new WaitForSeconds(delay);
                attempt++;
            }
        }
    }

    // 유틸 ------------------------------------------------------------

    private static string Pick(string[] arr) => arr[UnityEngine.Random.Range(0, arr.Length)];

    // Gemini로 이름 생성 -----------------------------------------------

    [Serializable] private class GeminiResponse { public GeminiCandidate[] candidates; }
    [Serializable] private class GeminiCandidate { public GeminiContent content; }
    [Serializable] private class GeminiContent { public GeminiPart[] parts; }
    [Serializable] private class GeminiPart { public string text; }

    private IEnumerator GenerateName(Action<string> onComplete)
    {
        if (string.IsNullOrEmpty(geminiApiKey))
        {
            Debug.LogWarning("[Gemini] API 키가 없어 임시 이름 사용");
            onComplete?.Invoke("이름없음");
            yield break;
        }

        int seed = UnityEngine.Random.Range(1000, 99999);
        string prompt =
            "서양 판타지 RPG 캐릭터 이름 하나만 만들어줘. " +
            "엘프어/고대어 느낌의 외국식 이름. 예: 카엘리온, 아르웬, 발타자르, 셀레스티아, 드라키엘, 리오넬, 에스텔라, 그라엔. " +
            "한글 2~6글자로 음역해서 출력. 이름만 출력하고 다른 설명, 따옴표, 마침표, 괄호 금지. " +
            $"매번 새롭고 고유한 이름. 시드: {seed}";

        string body = "{\"contents\":[{\"parts\":[{\"text\":" + EscapeJson(prompt) + "}]}]}";

        // 모델명 정리: 앞뒤 공백/따옴표/슬래시 제거, models/ 접두어가 들어가 있으면 떼기
        string model = (geminiModel ?? "").Trim().Trim('"', '\'', '/', ' ');
        if (model.StartsWith("models/")) model = model.Substring("models/".Length);
        if (string.IsNullOrEmpty(model))
        {
            Debug.LogError("[Gemini] 모델 이름이 비어있습니다");
            onComplete?.Invoke("이름없음");
            yield break;
        }

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={geminiApiKey}";
        Debug.Log($"[Gemini] 요청 URL: https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");

        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Gemini] 이름 생성 실패({req.responseCode}): {req.error}\n{req.downloadHandler.text}");
                onComplete?.Invoke("이름없음");
                yield break;
            }

            string name = ParseGeminiText(req.downloadHandler.text);
            onComplete?.Invoke(string.IsNullOrEmpty(name) ? "이름없음" : name);
        }
    }

    private static string ParseGeminiText(string json)
    {
        try
        {
            var res = JsonUtility.FromJson<GeminiResponse>(json);
            if (res?.candidates != null && res.candidates.Length > 0
                && res.candidates[0].content?.parts != null
                && res.candidates[0].content.parts.Length > 0)
            {
                string raw = res.candidates[0].content.parts[0].text ?? "";
                // 따옴표/마침표/줄바꿈/공백 정리
                return raw.Trim().Trim('"', '\'', '.', '。', ' ', '\n', '\r', '\t');
            }
        }
        catch (Exception e) { Debug.LogWarning($"[Gemini] 이름 파싱 실패: {e.Message}"); }
        return "";
    }

    private static int RollStars()
    {
        int idx = RollWeightedIndex(StarWeights);
        return idx + 1;
    }

    private static int RollWeightedIndex(int[] weights)
    {
        int total = 0;
        for (int i = 0; i < weights.Length; i++) total += weights[i];
        if (total <= 0) return 0;
        int roll = UnityEngine.Random.Range(0, total);
        int acc = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            acc += weights[i];
            if (roll < acc) return i;
        }
        return weights.Length - 1;
    }

    private static string EscapeJson(string s)
    {
        if (s == null) return "null";
        StringBuilder sb = new StringBuilder(s.Length + 8);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
