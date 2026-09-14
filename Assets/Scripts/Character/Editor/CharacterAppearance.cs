using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

// 카드에 걸린 초상화를 읽어, 3D 생성기에 넘길 외형 설명을 영어 한 문단으로 받아 온다.
//
// 왜 그림을 그대로 넘기지 않는가: Meshy의 text-to-image는 참조 이미지를 받지 않는다.
// 초상화 자체를 3D에 밀어 넣을 방법이 없으니, 그림과 3D를 잇는 다리는 결국 말뿐이다.
// 그래서 Gemini에게 초상화를 보여 주고 "이 사람"을 글로 받아, 그 글로 전신 시트를 새로 굽는다.
//
// 초상화는 허리 위만 있다. 하체는 존재하지 않으므로 읽어 올 수 없고, 지어내는 수밖에 없다 —
// 다만 아무렇게나 지어내면 상의는 중세 가죽인데 하의는 청바지가 되므로,
// "위와 같은 시대·재질로" 라는 조건을 붙여서 짓게 한다.
public static class CharacterAppearance
{
    private const string Model = "gemini-2.5-flash";
    private const int MaxOutputTokens = 500;

    // 무엇을 물어보는지는 MeshyBodyRecipe.AppearanceInstruction에 있다.
    // 빌드에서 도는 쪽(MeshyBodyService)도 같은 문구를 써야 같은 사람이 나온다.

    // ── 응답 모양 ────────────────────────────────────────────────────────

    [Serializable] private class Part { public string text; }
    [Serializable] private class Content { public Part[] parts; }
    [Serializable] private class Candidate { public Content content; public string finishReason; }
    [Serializable] private class Response { public Candidate[] candidates; }

    /// 초상화에서 외형을 읽는다. 초상화도 키도 없으면 직업만 가지고 만든 설명으로 떨어진다.
    public static async Task<string> Describe(CharacterSO character)
    {
        string portraitPath = PortraitPath(character);
        string key = ApiKeys.Gemini;

        if (string.IsNullOrEmpty(portraitPath) || string.IsNullOrEmpty(key))
        {
            Debug.LogWarning($"[CharacterAppearance] {character.characterName}: " +
                             (string.IsNullOrEmpty(key) ? "Gemini 키가 없어" : "초상화 파일을 찾지 못해") +
                             " 직업만 보고 외형을 짓는다. 3D가 카드 그림과 다른 사람이 된다.");
            return FromJobOnly(character);
        }

        string base64 = Convert.ToBase64String(File.ReadAllBytes(portraitPath));
        string body = "{\"contents\":[{\"parts\":[" +
                      "{\"text\":" + MeshyBodyRecipe.EscapeJson(MeshyBodyRecipe.AppearanceInstruction) + "}," +
                      "{\"inline_data\":{\"mime_type\":\"image/png\",\"data\":\"" + base64 + "\"}}" +
                      "]}]," +
                      // 생각을 끄지 않으면 사고 토큰이 출력 한도를 먹어 버려 문단이 한 줄에서 잘린다.
                      "\"generationConfig\":{\"temperature\":0.4,\"maxOutputTokens\":" + MaxOutputTokens +
                      ",\"thinkingConfig\":{\"thinkingBudget\":0}}}";

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={key}";

        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
        using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
        using (HttpResponseMessage response = await client.PostAsync(url, content))
        {
            string text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Debug.LogWarning($"[CharacterAppearance] Gemini 실패 ({(int)response.StatusCode}): {text}");
                return FromJobOnly(character);
            }

            string described = FirstText(text);
            if (string.IsNullOrWhiteSpace(described))
            {
                Debug.LogWarning($"[CharacterAppearance] Gemini가 빈 답을 줬다: {text}");
                return FromJobOnly(character);
            }

            Debug.Log($"[CharacterAppearance] {character.characterName}: {described}");
            return described.Trim();
        }
    }

    private static string FirstText(string json)
    {
        var parsed = JsonUtility.FromJson<Response>(json);
        if (parsed?.candidates == null || parsed.candidates.Length == 0) return null;

        Content content = parsed.candidates[0].content;
        if (content?.parts == null || content.parts.Length == 0) return null;
        return content.parts[0].text;
    }

    // 초상화를 못 읽었을 때의 최소한. 같은 직업이면 다 같이 생기게 되지만,
    // 적어도 하체와 직업 복장은 갖춘 사람이 나온다.
    private static string FromJobOnly(CharacterSO character) =>
        MeshyBodyRecipe.AppearanceFromJob(character.job);

    // 스프라이트가 물려 있으면 그쪽이 정답이다. 에셋을 옮기면 문자열 경로는 낡지만 참조는 따라간다.
    private static string PortraitPath(CharacterSO character)
    {
        string path = character.portrait != null
            ? UnityEditor.AssetDatabase.GetAssetPath(character.portrait)
            : character.portraitAssetPath;

        if (string.IsNullOrEmpty(path)) return null;

        string full = Path.GetFullPath(path);
        return File.Exists(full) ? full : null;
    }
}
