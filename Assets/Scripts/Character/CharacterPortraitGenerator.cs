using System;
using System.Threading.Tasks;
using UnityEngine;

// 소환 카드에 걸 초상화를 Meshy에게 그려 받는다. 실패하면 null — 초상화 없이도 카드는 나온다.
//
// 3D 몸(MeshyBodyRecipe)과 노리는 것이 다르다. 카드 그림은 멋있어야 하므로 조명과 연출을 살리고,
// 배경만 흰색으로 비워 받아서 여기서 투명하게 지운다.
public sealed class CharacterPortraitGenerator
{
    private readonly MeshyClient meshy;
    private readonly string aiModel;

    public CharacterPortraitGenerator(MeshyClient meshy, string aiModel)
    {
        this.meshy = meshy ?? MeshyClient.Shared;
        this.aiModel = aiModel;
    }

    public static string Prompt(CharacterRoller.JobEntry job, CharacterRoller.Trait trait)
    {
        return
            $"A single {trait.english} human {job.portraitWord} character, " +
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

    /// 그림을 주문하고 받아 텍스처로 돌려준다. 받은 텍스처를 지우는 것은 부른 쪽의 몫이다.
    public async Task<Texture2D> GenerateAsync(string prompt)
    {
        string url;
        try
        {
            string taskId = await meshy.CreateImageAsync(MeshyRequests.TextToImage(aiModel, prompt));
            MeshyProtocol.ImageTask task = await meshy.AwaitTaskAsync<MeshyProtocol.ImageTask>(MeshyProtocol.TextToImage, taskId);
            url = task.FirstImageUrl;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Meshy] 초상화 실패: {e.Message}");
            return null;
        }

        if (string.IsNullOrEmpty(url))
        {
            Debug.LogError("[Meshy] 초상화 태스크는 끝났는데 그림 주소가 없다.");
            return null;
        }

        Texture2D texture = await UnityWebTransport.DownloadTextureAsync(url);
        if (texture == null) Debug.LogWarning("[Meshy] 이미지 다운로드 null");
        return texture;
    }
}

// 흰 배경을 투명하게 지운다.
public static class WhiteBackgroundRemover
{
    /// 모든 채널이 threshold 이상인 픽셀을 배경으로 본다. softEdge면 threshold~255 사이를 서서히 지운다.
    /// 원본이 RGBA32면 그 자리에서 고쳐 돌려주고, 아니면 새 텍스처를 만들고 원본은 지운다.
    public static Texture2D Apply(Texture2D src, int threshold, bool softEdge)
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
            // 내려받은 원본은 여기서 역할이 끝났다. 런타임 텍스처라 지우지 않으면 소환할 때마다 한 장씩 남는다.
            UnityEngine.Object.Destroy(src);
            return result;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Meshy] 배경 투명 처리 실패, 원본 사용: {e.Message}");
            return src;
        }
    }
}
