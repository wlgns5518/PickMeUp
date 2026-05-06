using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;

public class CharacterCard : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image characterImage;
    [SerializeField] private Text characterNameText;

    // Gemini API 설정
    private const string GeminiTextApiUrl =
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent";
    private const string GeminiImageApiUrl =
        "https://generativelanguage.googleapis.com/v1beta/models/imagen-3.0-generate-002:predict";
    private const string ApiKey = "YOUR_API_KEY_HERE"; // 실제 API 키로 교체하세요

    void Start()
    {
        StartCoroutine(GenerateCharacterCard());
    }

    private IEnumerator GenerateCharacterCard()
    {
        // 이름과 이미지를 병렬 요청
        string characterName = null;
        Texture2D characterTexture = null;

        bool nameReady = false;
        bool imageReady = false;

        StartCoroutine(GenerateCharacterName(result =>
        {
            characterName = result;
            nameReady = true;
        }));

        StartCoroutine(GenerateCharacterImage(result =>
        {
            characterTexture = result;
            imageReady = true;
        }));

        // 둘 다 완료될 때까지 대기
        yield return new WaitUntil(() => nameReady && imageReady);

        // UI 적용
        if (!string.IsNullOrEmpty(characterName))
            characterNameText.text = characterName;

        if (characterTexture != null)
            characterImage.sprite = Sprite.Create(
                characterTexture,
                new Rect(0, 0, characterTexture.width, characterTexture.height),
                new Vector2(0.5f, 0.5f));
    }

    // ── 텍스트 생성 (캐릭터 이름) ──────────────────────────────────────────

    private IEnumerator GenerateCharacterName(System.Action<string> onComplete)
    {
        string requestBody = @"{
            ""contents"": [{
                ""parts"": [{
                    ""text"": ""판타지 RPG 게임 캐릭터의 이름을 하나만 생성해줘. 이름만 출력해.""
                }]
            }]
        }";

        using (UnityWebRequest request = new UnityWebRequest(
            $"{GeminiTextApiUrl}?key={ApiKey}", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string name = ParseTextFromGeminiResponse(request.downloadHandler.text);
                onComplete?.Invoke(name);
            }
            else
            {
                Debug.LogError($"[CharacterCard] 이름 생성 실패: {request.error}");
                onComplete?.Invoke("알 수 없는 용사");
            }
        }
    }

    private string ParseTextFromGeminiResponse(string json)
    {
        // "text": "..." 에서 값을 추출하는 간단한 파싱
        const string key = "\"text\": \"";
        int start = json.IndexOf(key);
        if (start < 0) return "이름 없음";
        start += key.Length;
        int end = json.IndexOf("\"", start);
        return end < 0 ? "이름 없음" : json.Substring(start, end - start).Trim();
    }

    // ── 이미지 생성 (Imagen API) ────────────────────────────────────────────

    private IEnumerator GenerateCharacterImage(System.Action<Texture2D> onComplete)
    {
        string requestBody = @"{
            ""instances"": [{
                ""prompt"": ""A fantasy RPG character portrait, detailed, high quality, digital art""
            }],
            ""parameters"": {
                ""sampleCount"": 1
            }
        }";

        using (UnityWebRequest request = new UnityWebRequest(
            $"{GeminiImageApiUrl}?key={ApiKey}", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                byte[] imageBytes = ParseImageBytesFromResponse(request.downloadHandler.text);
                if (imageBytes != null)
                {
                    Texture2D texture = new Texture2D(2, 2);
                    texture.LoadImage(imageBytes);
                    onComplete?.Invoke(texture);
                }
                else
                {
                    Debug.LogError("[CharacterCard] 이미지 파싱 실패");
                    onComplete?.Invoke(null);
                }
            }
            else
            {
                Debug.LogError($"[CharacterCard] 이미지 생성 실패: {request.error}");
                onComplete?.Invoke(null);
            }
        }
    }

    private byte[] ParseImageBytesFromResponse(string json)
    {
        // Imagen 응답에서 base64 인코딩된 이미지 데이터 추출
        const string key = "\"bytesBase64Encoded\": \"";
        int start = json.IndexOf(key);
        if (start < 0) return null;
        start += key.Length;
        int end = json.IndexOf("\"", start);
        if (end < 0) return null;
        string base64 = json.Substring(start, end - start);
        return System.Convert.FromBase64String(base64);
    }
}