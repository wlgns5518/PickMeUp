using System;
using System.Threading.Tasks;
using UnityEngine;

// 카드에 걸린 초상화를 읽어, 3D 생성기에 넘길 외형 설명을 영어 한 문단으로 받아 온다.
//
// 왜 그림을 그대로 넘기지 않는가: Meshy의 text-to-image는 참조 이미지를 받지 않는다.
// 초상화 자체를 3D에 밀어 넣을 방법이 없으니, 그림과 3D를 잇는 다리는 결국 말뿐이다.
// 그래서 Gemini에게 초상화를 보여 주고 "이 사람"을 글로 받아, 그 글로 전신 시트를 새로 굽는다.
//
// 무엇을 물어보는지는 MeshyBodyRecipe.AppearanceInstruction에 있다. 에디터 메뉴와 소환 직후의 굽기가
// 모두 여기를 거친다 — 예전에는 둘이 같은 질문을 따로 보내고 있었다.
public static class CharacterAppearance
{
    private const string Model = "gemini-2.5-flash";
    private const int MaxOutputTokens = 500;

    /// 초상화(PNG)에서 외형을 읽는다. 초상화도 키도 없거나 Gemini가 실패하면
    /// 직업만 가지고 만든 설명으로 떨어진다 — 굽기를 막을 일은 아니다.
    public static async Task<string> DescribeAsync(CharacterSO character, byte[] portraitPng, GeminiClient gemini = null)
    {
        gemini = gemini ?? GeminiClient.Shared;
        string label = character.characterName;

        if (portraitPng == null || !gemini.HasKey)
        {
            Debug.LogWarning($"[CharacterAppearance] {label}: " +
                             (portraitPng == null ? "초상화를 읽지 못해" : "Gemini 키가 없어") +
                             " 직업만 보고 외형을 짓는다. 3D가 카드 그림과 다른 사람이 된다.");
            return FromJobOnly(character);
        }

        var prompt = new GeminiPrompt
        {
            Text = MeshyBodyRecipe.AppearanceInstruction,
            Png = portraitPng,
            Temperature = 0.4f,
            MaxOutputTokens = MaxOutputTokens,
        };

        string described;
        try
        {
            described = await gemini.GenerateAsync(Model, prompt);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CharacterAppearance] {label}: {e.Message}");
            return FromJobOnly(character);
        }

        if (string.IsNullOrWhiteSpace(described))
        {
            Debug.LogWarning($"[CharacterAppearance] {label}: Gemini가 빈 답을 줬다.");
            return FromJobOnly(character);
        }

        return described.Trim();
    }

    // 초상화를 못 읽었을 때의 최소한. 같은 직업이면 다 같이 생기게 되지만,
    // 적어도 하체와 직업 복장은 갖춘 사람이 나온다.
    private static string FromJobOnly(CharacterSO character) =>
        MeshyBodyRecipe.AppearanceFromJob(character.job);
}
