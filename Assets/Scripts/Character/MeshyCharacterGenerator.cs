using System;
using System.Collections;
using System.Collections.Generic;
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
    [Tooltip("gemini-2.5-flash-lite (RPM 15) / gemini-2.5-flash (RPM 5)")]
    [SerializeField] private string geminiModel = "gemini-2.5-flash-lite";
    [Tooltip("429 응답 시 응답 본문의 retryDelay를 읽어 자동 재시도")]
    [SerializeField] private int gemini429MaxRetries = 2;
    [Tooltip("retryDelay 파싱 실패 시 대기 (초)")]
    [SerializeField] private float gemini429FallbackWait = 30f;

    [Header("Polling")]
    [SerializeField] private float pollInterval = 3f;
    [SerializeField] private float timeoutSeconds = 600f;

    [Header("Retry")]
    [SerializeField] private int maxRetries = 3;
    [SerializeField] private float baseRetryDelay = 2f;

    [Header("Background")]
    [Tooltip("거의 흰색인 픽셀을 완전 투명으로 변환")]
    [SerializeField] private bool transparentBackground = true;
    [Tooltip("이 값 이상의 RGB는 흰색 배경으로 간주 (0~255)")]
    [Range(200, 255)] [SerializeField] private int whiteThreshold = 235;
    [Tooltip("경계 부드럽게 — 임계값 ~ 255 사이는 알파를 점진적으로")]
    [SerializeField] private bool softEdge = true;

    [Header("Asset Save")]
    [Tooltip("에디터에서 생성된 캐릭터를 .asset으로 저장")]
    [SerializeField] private bool saveAsAsset = true;
    [SerializeField] private string imageDir = "Assets/CharacterImage";
    [SerializeField] private string assetDir = "Assets/CharacterSO";

    // ─────────────────────────────────────────────────────────────────────────
    // 상수 / 데이터 테이블
    // ─────────────────────────────────────────────────────────────────────────

    private const string TaskUrl = "https://api.meshy.ai/openapi/v1/text-to-image";

    // 직업: enum, 영어 프롬프트, 가중치를 1:1 매칭으로 묶음
    private static readonly JobType[]  JobPool = {
        JobType.Melee, JobType.Mage, JobType.Archer, JobType.Assassin, JobType.Tank, JobType.Support,
        JobType.Carpenter, JobType.Cook, JobType.Blacksmith, JobType.Tanner,
    };
    private static readonly string[]   JobPromptsEn = {
        "warrior", "mage", "archer", "assassin", "tank knight", "support healer",
        "carpenter", "cook", "blacksmith", "leatherworker",
    };
    private static readonly int[]      JobWeights = { 13, 13, 13, 13, 13, 13,  6, 5, 6, 5 };

    private static readonly string[]   Traits   = { "냉소적인", "낙천적인", "고독한", "충직한", "야망 있는", "수줍은", "비밀스러운" };
    private static readonly string[]   TraitsEn = { "cynical", "cheerful", "lonely", "loyal", "ambitious", "shy", "mysterious" };

    // 1성 63.949% / 2성 30% / 3성 5% / 4성 1% / 5성 0.05% / 6성 0.001% (7성 가챠 제외)
    private static readonly int[]      StarWeights = { 63949, 30000, 5000, 1000, 50, 1 };

    // 폴링 상태 키워드 (소문자) — 매 폴링에서 HashSet 조회 O(1)
    private static readonly HashSet<string> SucceededStatus = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "succeeded", "success", "completed", "done", "finished" };
    private static readonly HashSet<string> FailedStatus = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "failed", "canceled", "cancelled", "expired", "error" };

    // ─────────────────────────────────────────────────────────────────────────
    // 공개 API
    // ─────────────────────────────────────────────────────────────────────────

    /// onUpdate 두 번 호출: (1) 메타데이터 즉시, (2) 이미지 완료 후.
    /// presetName이 있으면 Gemini 호출 생략.
    public IEnumerator GenerateCharacter(Action<CharacterSO> onUpdate, string presetName = null)
    {
        int jIdx = RollWeightedIndex(JobWeights);
        int tIdx = UnityEngine.Random.Range(0, Traits.Length);

        CharacterSO so = ScriptableObject.CreateInstance<CharacterSO>();
        so.starCount = RollStars();
        so.level = 1; so.exp = 0; so.expToNext = 10;
        so.job = JobPool[jIdx];
        so.gender = UnityEngine.Random.value < 0.5f ? Gender.Male : Gender.Female;
        so.hairIndex = UnityEngine.Random.Range(0, 2);
        so.outfit = CharacterRules.OutfitFor(so.job);

        if (!string.IsNullOrEmpty(presetName))
            so.characterName = presetName;
        else
            yield return GenerateName(name => so.characterName = name);

        so.description  = $"{Traits[tIdx]} 인간 {CharacterRules.Korean(so.job)}";
        so.constitution = RollConstitution(so.job);
        RollInitialStats(so);
        so.currentHP = so.stats.vitality * 5 + so.hiddenStats.body * 3;
        so.name = $"{so.characterName} ({so.starCount}★)";

        onUpdate?.Invoke(so);

        // Meshy text-to-image
        string imageUrl = null;
        yield return GenerateImageUrl(BuildPortraitPrompt(jIdx, tIdx, so), u => imageUrl = u);

        if (!string.IsNullOrEmpty(imageUrl))
            yield return DownloadAndApplyPortrait(imageUrl, so);

#if UNITY_EDITOR
        if (saveAsAsset) SaveCharacterAsAsset(so);
#endif

        Debug.Log($"[Meshy] 완료: {so.characterName} ({so.starCount}★)");
        onUpdate?.Invoke(so);
    }

    /// 한 번 호출로 N개 이름 받기 — RPM 부담 회피
    public IEnumerator GenerateNames(int count, Action<List<string>> onComplete)
    {
        var result = new List<string>(count);
        if (count <= 0) { onComplete?.Invoke(result); yield break; }

        if (string.IsNullOrEmpty(geminiApiKey))
        {
            FillFallback(result, count);
            onComplete?.Invoke(result);
            yield break;
        }

        int seed = UnityEngine.Random.Range(1000, 99999);
        string prompt =
            $"서양 판타지 RPG 캐릭터 이름 {count}개를 만들어줘. " +
            "엘프어/고대어 느낌의 외국식 이름. 예: 카엘리온, 아르웬, 드라키엘, 셀레스티아. " +
            "한 줄에 하나씩 한글 2~6글자로 음역. " +
            "번호, 따옴표, 마침표, 괄호, 설명 절대 금지. 이름 외 다른 텍스트 금지. " +
            $"모두 서로 다른 이름. 시드: {seed}";

        int maxTokens = Mathf.Clamp(count * 12, 30, 400);
        string response = null;
        yield return SendGemini(prompt, 1.3f, maxTokens, "배치 이름", r => response = r);

        if (!string.IsNullOrEmpty(response))
            ExtractHangulNames(ExtractGeminiText(response), count, result);

        FillFallback(result, count);
        Debug.Log($"[Gemini] 배치 이름 {result.Count}개 수신");
        onComplete?.Invoke(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 캐릭터 데이터 롤
    // ─────────────────────────────────────────────────────────────────────────

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
        so.hiddenStats.mental = CharacterRules.IsFragileMental(so.starCount)
            ? UnityEngine.Random.Range(1, 4)
            : hidden + UnityEngine.Random.Range(-1, 4);
        so.hiddenStats.skill  = hidden + UnityEngine.Random.Range(-2, 3);
        so.hiddenStats.body   = hidden + UnityEngine.Random.Range(-1, 4);
        so.hiddenStats.sanity = hidden + UnityEngine.Random.Range(-1, 4);
    }

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

    private static int RollStars() => RollWeightedIndex(StarWeights) + 1;

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

    // ─────────────────────────────────────────────────────────────────────────
    // Meshy: text-to-image
    // ─────────────────────────────────────────────────────────────────────────

    private string BuildPortraitPrompt(int jIdx, int tIdx, CharacterSO so)
    {
        string genderEn = so.gender == Gender.Male ? "male" : "female";
        string hairEn = so.hairIndex == 0
            ? (so.gender == Gender.Male ? "short messy hair" : "shoulder-length hair")
            : (so.gender == Gender.Male ? "long tied back hair" : "long flowing hair");
        string beardEn = so.HasBeard ? "with a short beard, " : "clean shaven, ";
        if (so.gender == Gender.Female) beardEn = "no facial hair, ";
        string outfitEn = CharacterRules.OutfitPromptEn(so.outfit);

        return
            $"A single {TraitsEn[tIdx]} {genderEn} human {JobPromptsEn[jIdx]} character, " +
            $"{outfitEn}, {hairEn}, {beardEn}" +
            "upper body portrait from the waist up, natural relaxed standing pose, " +
            "slight three-quarter view, looking forward, " +
            "semi-realistic mature fantasy illustration, detailed face and costume, " +
            "cinematic lighting, painterly style, " +
            "PURE SOLID WHITE BACKGROUND #FFFFFF, completely white background, " +
            "no scenery, no environment, no gradient, no shadow on the ground, " +
            "no props, no other characters, no text, no logo, " +
            "not cute, not chibi, not childish, adult proportions, " +
            "character centered and fully visible, high quality";
    }

    private IEnumerator GenerateImageUrl(string prompt, Action<string> onComplete)
    {
        string body = "{\"ai_model\":" + EscapeJson(aiModel) + ",\"prompt\":" + EscapeJson(prompt) + "}";

        string createResponse = null;
        yield return SendMeshyWithRetry(TaskUrl, "POST", body, "이미지 태스크 생성", r => createResponse = r);
        if (string.IsNullOrEmpty(createResponse)) { onComplete?.Invoke(null); yield break; }

        // 동기 응답에 URL이 바로 있으면 사용
        string immediate = ExtractFirstUrl(createResponse);
        if (!string.IsNullOrEmpty(immediate)) { onComplete?.Invoke(immediate); yield break; }

        string taskId = ExtractStringField(createResponse, "result")
                     ?? ExtractStringField(createResponse, "id");
        if (string.IsNullOrEmpty(taskId))
        {
            Debug.LogError($"[Meshy] 태스크 ID/이미지 URL 못 찾음: {createResponse}");
            onComplete?.Invoke(null);
            yield break;
        }

        yield return PollTaskUntilDone($"{TaskUrl}/{taskId}", "이미지", onComplete);
    }

    // 폴링 — text-to-image / 향후 다른 비동기 태스크에도 재사용 가능
    private IEnumerator PollTaskUntilDone(string statusUrl, string label, Action<string> onComplete)
    {
        float elapsed = 0f;
        string lastStatus = null;
        float lastLogAt = -10f;

        while (elapsed < timeoutSeconds)
        {
            string body = null;
            yield return SendMeshyWithRetry(statusUrl, "GET", null, $"{label} 폴링", r => body = r);

            if (!string.IsNullOrEmpty(body))
            {
                string status = ExtractStringField(body, "status") ?? ExtractStringField(body, "task_status");
                string url = ExtractFirstUrl(body);

                if (SucceededStatus.Contains(status ?? "") ||
                    (status != null && !FailedStatus.Contains(status) && !string.IsNullOrEmpty(url) && url != statusUrl))
                {
                    Debug.Log($"[Meshy] {label} 완료 ({elapsed:0}s)");
                    onComplete?.Invoke(url);
                    yield break;
                }
                if (FailedStatus.Contains(status ?? ""))
                {
                    Debug.LogError($"[Meshy] {label} 종료({status}): {body}");
                    onComplete?.Invoke(null);
                    yield break;
                }

                if (status != lastStatus)
                {
                    Debug.Log($"[Meshy] {label} 상태: {lastStatus ?? "(시작)"} → {status} ({elapsed:0}s)");
                    lastStatus = status;
                    lastLogAt = elapsed;
                }
                else if (elapsed - lastLogAt >= 30f)
                {
                    Debug.Log($"[Meshy] {label} {status} 진행 중... ({elapsed:0}s)");
                    lastLogAt = elapsed;
                }
            }

            yield return new WaitForSeconds(pollInterval);
            elapsed += pollInterval;
        }

        Debug.LogError($"[Meshy] {label} 폴링 타임아웃");
        onComplete?.Invoke(null);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 이미지 다운로드 / 배경 처리 / 저장
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator DownloadAndApplyPortrait(string imageUrl, CharacterSO so)
    {
        Texture2D tex = null;
        yield return DownloadTexture(imageUrl, t => tex = t);
        if (tex == null) { Debug.LogWarning("[Meshy] 이미지 다운로드 null"); yield break; }

        Debug.Log($"[Meshy] 이미지 {tex.width}x{tex.height}");

        if (transparentBackground)
            tex = MakeWhiteTransparent(tex, whiteThreshold, softEdge);

        so.portrait = SavePortraitAndLoadSprite(tex, so.characterName, out string assetPath);
        so.portraitAssetPath = assetPath;
        Debug.Log($"[Meshy] 초상화 저장: {assetPath}");
    }

    private static IEnumerator DownloadTexture(string url, Action<Texture2D> onComplete)
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
                int minRgb = c.r < c.g ? (c.r < c.b ? c.r : c.b) : (c.g < c.b ? c.g : c.b);
                if (minRgb >= 255)        c.a = 0;
                else if (minRgb >= threshold)
                    c.a = softEdge ? (byte)Mathf.RoundToInt((1f - (minRgb - t) / range) * 255f) : (byte)0;
                pixels[i] = c;
            }

            // 원본이 RGBA32면 그 자리에서 적용해서 추가 alloc 피함
            if (src.format == TextureFormat.RGBA32)
            {
                src.SetPixels32(pixels);
                src.Apply();
                return src;
            }

            var result = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            result.SetPixels32(pixels);
            result.Apply();
            return result;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Meshy] 배경 투명 처리 실패, 원본 사용: {e.Message}");
            return src;
        }
    }

    private Sprite SavePortraitAndLoadSprite(Texture2D tex, string characterName, out string assetPath)
    {
        assetPath = null;
#if UNITY_EDITOR
        try
        {
            string fullDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), imageDir);
            Directory.CreateDirectory(fullDir);

            string filename = $"{SanitizeFileName(characterName ?? "character")}_{DateTime.Now:yyyyMMdd_HHmmss}_{UnityEngine.Random.Range(1000, 9999)}.png";
            File.WriteAllBytes(Path.Combine(fullDir, filename), tex.EncodeToPNG());

            assetPath = $"{imageDir}/{filename}".Replace('\\', '/');
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
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
            string fullDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetDir);
            Directory.CreateDirectory(fullDir);

            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{assetDir}/{SanitizeFileName(so.characterName)}.asset".Replace('\\', '/'));
            AssetDatabase.CreateAsset(so, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Meshy] CharacterSO 저장: {path}");
        }
        catch (Exception e) { Debug.LogError($"[Meshy] CharacterSO 저장 실패: {e.Message}"); }
    }
#endif

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "character";
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 || c == ' ' ? '_' : c);
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gemini
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator GenerateName(Action<string> onComplete)
    {
        if (string.IsNullOrEmpty(geminiApiKey))
        {
            Debug.LogWarning("[Gemini] API 키 없음 → 이름없음");
            onComplete?.Invoke("이름없음");
            yield break;
        }

        int seed = UnityEngine.Random.Range(1000, 99999);
        string prompt =
            "서양 판타지 RPG 캐릭터 이름 하나만 만들어줘. " +
            "엘프어/고대어 느낌의 외국식 이름. 예: 카엘리온, 아르웬, 발타자르, 셀레스티아, 드라키엘. " +
            "한글 2~6글자로 음역해서 출력. 이름만 출력, 다른 설명/따옴표/마침표/괄호 금지. " +
            $"시드: {seed}";

        string response = null;
        yield return SendGemini(prompt, 1.2f, 20, "이름", r => response = r);

        string name = string.IsNullOrEmpty(response) ? "" : CleanName(ExtractGeminiText(response));
        onComplete?.Invoke(string.IsNullOrEmpty(name) ? "이름없음" : name);
    }

    /// 공통 Gemini POST + 429 재시도. 성공 시 응답 본문, 실패 시 null.
    private IEnumerator SendGemini(string prompt, float temperature, int maxTokens, string label, Action<string> onComplete)
    {
        string model = (geminiModel ?? "").Trim().Trim('"', '\'', '/', ' ');
        if (model.StartsWith("models/")) model = model.Substring("models/".Length);
        if (string.IsNullOrEmpty(model))
        {
            Debug.LogError("[Gemini] 모델 이름 비어있음");
            onComplete?.Invoke(null);
            yield break;
        }

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={geminiApiKey}";
        string body =
            "{\"contents\":[{\"parts\":[{\"text\":" + EscapeJson(prompt) + "}]}]," +
            "\"generationConfig\":{" +
                $"\"temperature\":{temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                $"\"maxOutputTokens\":{maxTokens}," +
                "\"thinkingConfig\":{\"thinkingBudget\":0}" +
            "}}";

        int attempt = 0;
        while (true)
        {
            string responseText = null;
            long code = 0;
            bool success = false;

            using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

                yield return req.SendWebRequest();
                code = req.responseCode;
                responseText = req.downloadHandler.text;
                success = req.result == UnityWebRequest.Result.Success;
            }

            if (success) { onComplete?.Invoke(responseText); yield break; }

            if (code == 429 && attempt < gemini429MaxRetries)
            {
                float wait = ParseRetryDelaySeconds(responseText);
                if (wait <= 0f) wait = gemini429FallbackWait;
                wait = Mathf.Clamp(wait, 1f, 120f) + 1f;
                Debug.LogWarning($"[Gemini] {label} 429. {wait:0}s 후 재시도 ({attempt + 1}/{gemini429MaxRetries})");
                yield return new WaitForSeconds(wait);
                attempt++;
                continue;
            }

            Debug.LogWarning($"[Gemini] {label} 실패({code}):\n{responseText}");
            onComplete?.Invoke(null);
            yield break;
        }
    }

    // Gemini 응답 본문에서 candidates[0].content.parts[0].text 추출 — 정규식/JsonUtility 없이
    private static string ExtractGeminiText(string json) =>
        ExtractStringField(json, "text") ?? "";

    // 여러 줄 응답에서 마지막 한글 라인만 단일 이름으로
    private static string CleanName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";

        string[] lines = raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        string picked = null;
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string t = lines[i].Trim();
            if (t.Length == 0 || t.StartsWith("**") || t.StartsWith("##")) continue;
            if (t.StartsWith("THOUGHTS", StringComparison.OrdinalIgnoreCase)) continue;
            if (ContainsHangul(t)) { picked = t; break; }
        }
        if (picked == null) return "";

        int s = -1, e = -1;
        for (int i = 0; i < picked.Length; i++)
        {
            if (IsHangul(picked[i])) { if (s < 0) s = i; e = i; }
            else if (s >= 0) break;
        }
        return s < 0 ? "" : picked.Substring(s, e - s + 1);
    }

    private static void ExtractHangulNames(string raw, int wanted, List<string> outList)
    {
        if (string.IsNullOrEmpty(raw)) return;
        foreach (string line in raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (outList.Count >= wanted) break;
            string t = line.Trim();
            if (t.StartsWith("THOUGHTS", StringComparison.OrdinalIgnoreCase)) continue;

            int s = -1, e = -1;
            for (int i = 0; i < t.Length; i++)
            {
                if (IsHangul(t[i])) { if (s < 0) s = i; e = i; }
                else if (s >= 0) break;
            }
            if (s >= 0) outList.Add(t.Substring(s, e - s + 1));
        }
    }

    private static void FillFallback(List<string> list, int count)
    {
        while (list.Count < count) list.Add("이름없음");
    }

    private static float ParseRetryDelaySeconds(string json)
    {
        if (string.IsNullOrEmpty(json)) return 0f;
        int i = json.IndexOf("\"retryDelay\"");
        if (i < 0) return 0f;
        i = json.IndexOf('"', i + 12);
        if (i < 0) return 0f;
        i = json.IndexOf('"', i + 1) + 1;
        int e = json.IndexOf('"', i);
        if (e < 0) return 0f;
        string v = json.Substring(i, e - i);
        if (v.EndsWith("s")) v = v.Substring(0, v.Length - 1);
        return float.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float seconds) ? seconds : 0f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 공통 HTTP / 파싱 유틸
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator SendMeshyWithRetry(string url, string method, string body, string label, Action<string> onComplete)
    {
        int attempt = 0;
        while (true)
        {
            UnityWebRequest req;
            if (method == "GET") req = UnityWebRequest.Get(url);
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

                bool retriable = code == 429 || (code >= 500 && code <= 504);
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

    // 응답에서 첫 번째 https://... URL 토큰만 잡아냄
    private static string ExtractFirstUrl(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        int s = json.IndexOf("https://");
        if (s < 0) return null;
        int e = s;
        while (e < json.Length && json[e] != '"' && json[e] != ' ' && json[e] != '\n' && json[e] != '\r') e++;
        return json.Substring(s, e - s);
    }

    // "field": "value" 문자열 필드 추출 (JSON 트리 파싱 없이)
    private static string ExtractStringField(string json, string field)
    {
        if (string.IsNullOrEmpty(json)) return null;
        int i = json.IndexOf("\"" + field + "\"");
        if (i < 0) return null;
        i = json.IndexOf(':', i + field.Length + 2);
        if (i < 0) return null;
        i++;
        while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
        if (i >= json.Length || json[i] != '"') return null;
        int s = i + 1;
        int e = json.IndexOf('"', s);
        return e < 0 ? null : json.Substring(s, e - s);
    }

    private static bool ContainsHangul(string s)
    {
        for (int i = 0; i < s.Length; i++) if (IsHangul(s[i])) return true;
        return false;
    }

    private static bool IsHangul(char c) => c >= 0xAC00 && c <= 0xD7A3;

    private static string EscapeJson(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder(s.Length + 8);
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
