using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

// 소환한 캐릭터의 이름을 Gemini에게 받아 온다. 실패해도 예외를 던지지 않는다 — 이름을 못 받았다고
// 소환이 멈출 일은 아니므로 "이름없음"으로 채워 돌려준다.
public sealed class CharacterNameGenerator
{
    public const string Fallback = "이름없음";

    private readonly GeminiClient gemini;
    private readonly string model;

    public CharacterNameGenerator(GeminiClient gemini, string model)
    {
        this.gemini = gemini ?? GeminiClient.Shared;
        this.model = model;
    }

    public async Task<string> GenerateOneAsync()
    {
        if (!gemini.HasKey)
        {
            Debug.LogWarning("[Gemini] API 키 없음 → " + Fallback);
            return Fallback;
        }

        int seed = UnityEngine.Random.Range(1000, 99999);
        string prompt =
            "서양 판타지 RPG 캐릭터 이름 하나만 만들어줘. " +
            "엘프어/고대어 느낌의 외국식 이름. 예: 카엘리온, 아르웬, 발타자르, 셀레스티아, 드라키엘. " +
            "한글 2~6글자로 음역해서 출력. 이름만 출력, 다른 설명/따옴표/마침표/괄호 금지. " +
            $"시드: {seed}";

        string text = await Ask(prompt, 1.2f, 20, "이름");
        string name = HangulNames.CleanSingle(text);
        return string.IsNullOrEmpty(name) ? Fallback : name;
    }

    /// 한 번 호출로 N개 이름 받기 — RPM 부담 회피. 모자란 자리는 Fallback으로 채운다.
    public async Task<List<string>> GenerateManyAsync(int count)
    {
        var result = new List<string>(Math.Max(0, count));
        if (count <= 0) return result;

        if (gemini.HasKey)
        {
            int seed = UnityEngine.Random.Range(1000, 99999);
            string prompt =
                $"서양 판타지 RPG 캐릭터 이름 {count}개를 만들어줘. " +
                "엘프어/고대어 느낌의 외국식 이름. 예: 카엘리온, 아르웬, 드라키엘, 셀레스티아. " +
                "한 줄에 하나씩 한글 2~6글자로 음역. " +
                "번호, 따옴표, 마침표, 괄호, 설명 절대 금지. 이름 외 다른 텍스트 금지. " +
                $"모두 서로 다른 이름. 시드: {seed}";

            string text = await Ask(prompt, 1.3f, Mathf.Clamp(count * 12, 30, 400), "배치 이름");
            HangulNames.ExtractMany(text, count, result);
        }

        while (result.Count < count) result.Add(Fallback);
        return result;
    }

    private async Task<string> Ask(string prompt, float temperature, int maxTokens, string label)
    {
        try
        {
            return await gemini.GenerateAsync(model, new GeminiPrompt
            {
                Text = prompt,
                Temperature = temperature,
                MaxOutputTokens = maxTokens,
            });
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Gemini] {label} 실패: {e.Message}");
            return null;
        }
    }
}

// Gemini가 돌려준 글에서 한글 이름만 건진다. 모델이 시킨 대로만 답하지 않아서
// ("**이름**", "THOUGHTS:", 번호 붙은 목록) 줄마다 첫 한글 덩어리만 뽑는다.
public static class HangulNames
{
    /// 여러 줄 응답에서 마지막 한글 줄의 첫 한글 덩어리 하나.
    public static string CleanSingle(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";

        string[] lines = raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string t = lines[i].Trim();
            if (t.Length == 0 || t.StartsWith("**") || t.StartsWith("##")) continue;
            if (t.StartsWith("THOUGHTS", StringComparison.OrdinalIgnoreCase)) continue;
            if (ContainsHangul(t)) return FirstRun(t);
        }
        return "";
    }

    /// 줄마다 첫 한글 덩어리를 wanted개까지 담는다.
    public static void ExtractMany(string raw, int wanted, List<string> outList)
    {
        if (string.IsNullOrEmpty(raw)) return;
        foreach (string line in raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (outList.Count >= wanted) break;
            string t = line.Trim();
            if (t.StartsWith("THOUGHTS", StringComparison.OrdinalIgnoreCase)) continue;

            string run = FirstRun(t);
            if (run.Length > 0) outList.Add(run);
        }
    }

    private static string FirstRun(string text)
    {
        int s = -1, e = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (IsHangul(text[i])) { if (s < 0) s = i; e = i; }
            else if (s >= 0) break;
        }
        return s < 0 ? "" : text.Substring(s, e - s + 1);
    }

    private static bool ContainsHangul(string s)
    {
        for (int i = 0; i < s.Length; i++) if (IsHangul(s[i])) return true;
        return false;
    }

    private static bool IsHangul(char c) => c >= 0xAC00 && c <= 0xD7A3;
}
