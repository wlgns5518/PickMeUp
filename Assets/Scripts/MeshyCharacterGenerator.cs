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

    [Header("Asset Save")]
    [Tooltip("에디터에서 생성된 캐릭터를 .asset으로 저장")]
    [SerializeField] private bool saveAsAsset = true;
    [SerializeField] private string imageDir = "Assets/CharacterImage";
    [SerializeField] private string assetDir = "Assets/CharacterSO";
    [SerializeField] private string modelDir = "Assets/CharacterModels";

    private const string TaskUrl = "https://api.meshy.ai/openapi/v1/text-to-image";
    private const string ModelTaskUrl = "https://api.meshy.ai/openapi/v1/image-to-3d";

    private static readonly string[] Races = { "인간", "엘프", "수인", "기계인", "정령", "혼혈" };
    private static readonly string[] RacesEn = { "human", "elf", "beastfolk", "cyborg", "spirit", "halfblood" };
    private static readonly CombatType[] CombatPool = {
        CombatType.Melee, CombatType.Mage, CombatType.Archer,
        CombatType.Assassin, CombatType.Tank, CombatType.Support,
    };
    private static readonly string[] CombatPromptsEn = { "warrior", "mage", "archer", "assassin", "tank", "support" };
    private static readonly SupportType[] SupportPool = {
        SupportType.Carpenter, SupportType.Cook, SupportType.Blacksmith, SupportType.Tanner,
    };
    private static readonly string[] Traits = { "냉소적인", "낙천적인", "고독한", "충직한", "야망 있는", "수줍은", "비밀스러운" };
    private static readonly string[] TraitsEn = { "cynical", "cheerful", "lonely", "loyal", "ambitious", "shy", "mysterious" };

    private static readonly string[] NameA = { "리오", "카엘", "베른", "세인", "유리", "타나", "이그", "솔", "린", "엘", "노바", "비엔" };
    private static readonly string[] NameB = { "하르", "스카", "라프", "엔디", "미르", "셰린", "타엘", "케인", "베일", "도란" };

    // 1성 63.949% / 2성 30% / 3성 5% / 4성 1% / 5성 0.05% / 6성 0.001% (7성 가챠 제외)
    private static readonly int[] StarWeights = { 63949, 30000, 5000, 1000, 50, 1 };

    // 체질 프리셋: 전투 타입에 어울리는 가중치 + 약간의 랜덤 변동
    private static Constitution RollConstitution(CombatType combat)
    {
        Constitution c = new Constitution { name = "균형" };
        switch (combat)
        {
            case CombatType.Melee:    c.name = "근육질"; c.strengthGrowth = 1.6f; c.vitalityGrowth = 1.2f; c.agilityGrowth = 0.8f; c.intelligenceGrowth = 0.4f; break;
            case CombatType.Mage:     c.name = "현자";   c.intelligenceGrowth = 1.8f; c.agilityGrowth = 0.6f; c.vitalityGrowth = 0.6f; c.strengthGrowth = 0.4f; break;
            case CombatType.Archer:   c.name = "민첩한"; c.agilityGrowth = 1.6f; c.strengthGrowth = 1f; c.intelligenceGrowth = 0.8f; c.vitalityGrowth = 0.6f; break;
            case CombatType.Assassin: c.name = "그림자"; c.agilityGrowth = 1.8f; c.strengthGrowth = 1.0f; c.intelligenceGrowth = 0.7f; c.vitalityGrowth = 0.5f; break;
            case CombatType.Tank:     c.name = "강건한"; c.vitalityGrowth = 1.8f; c.strengthGrowth = 1.2f; c.intelligenceGrowth = 0.5f; c.agilityGrowth = 0.5f; break;
            case CombatType.Support:  c.name = "조화";   c.intelligenceGrowth = 1.4f; c.vitalityGrowth = 1.0f; c.agilityGrowth = 0.8f; c.strengthGrowth = 0.8f; break;
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
        int rIdx = UnityEngine.Random.Range(0, Races.Length);
        int cIdx = UnityEngine.Random.Range(0, CombatPool.Length);
        int tIdx = UnityEngine.Random.Range(0, Traits.Length);

        CharacterSO so = ScriptableObject.CreateInstance<CharacterSO>();
        so.starCount    = RollStars();
        so.level        = 1;
        so.exp          = 0;
        so.expToNext    = 10;
        so.combatType   = CombatPool[cIdx];
        so.supportType  = SupportPool[UnityEngine.Random.Range(0, SupportPool.Length)];
        so.characterName = $"{Pick(NameA)} {Pick(NameB)}";
        so.description   = $"{Traits[tIdx]} {Races[rIdx]} {CharacterRules.Korean(so.combatType)} ({CharacterRules.Korean(so.supportType)})";
        so.constitution  = RollConstitution(so.combatType);

        RollInitialStats(so);
        so.currentHP = HpFromStats(so.stats, so.hiddenStats);
        so.name      = $"{so.characterName} ({so.starCount}★)"; // 인스펙터/에셋 표시명

        // 1차: 이름·별·타입·스탯 전달 (이미지 전)
        onUpdate?.Invoke(so);

        string prompt =
            $"Portrait of a {TraitsEn[tIdx]} {RacesEn[rIdx]} {CombatPromptsEn[cIdx]} character, " +
            "webtoon style, soft cel shading, expressive eyes, head and shoulders, " +
            "centered composition, clean background, high quality";

        string imageUrl = null;
        yield return GenerateImageUrl(prompt, url => imageUrl = url);

        if (!string.IsNullOrEmpty(imageUrl))
        {
            Texture2D tex = null;
            yield return DownloadTexture(imageUrl, t => tex = t);
            if (tex != null)
            {
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

    private static int RollStars()
    {
        int total = 0;
        for (int i = 0; i < StarWeights.Length; i++) total += StarWeights[i];
        int roll = UnityEngine.Random.Range(0, total);
        int acc = 0;
        for (int i = 0; i < StarWeights.Length; i++)
        {
            acc += StarWeights[i];
            if (roll < acc) return i + 1;
        }
        return 1;
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
