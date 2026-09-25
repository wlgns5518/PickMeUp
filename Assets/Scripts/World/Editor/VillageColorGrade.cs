using UnityEngine;

// 텍스처의 명암·결을 남긴 채 색만 팔레트(VillagePalette)로 옮기는 도우미. 모든 계산은 sRGB 값이다.
//   - 머티리얼 틴트: 텍스처 평균이 목표 색이 되도록 _BaseColor를 정한다(목표 ÷ 평균). 곱셈이라 명암·결이 그대로 남는다.
//   - 픽셀 옮기기: 견본(돌담·널·지붕) 평균에 견준 밝기 비율을 목표 색에 곱하고, 원래 색을 조금 남겨 돌마다 색 편차를 살린다.
internal static class VillageColorGrade
{
    public static float Luminance(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

    /// 텍스처 평균(sRGB). 읽기 불가 텍스처도 되도록 작은 렌더 텍스처에 줄여 그린 뒤 읽는다.
    public static Color MeanColor(Texture texture)
    {
        if (texture == null) return Color.gray;
        const int size = 64;
        RenderTexture rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Graphics.Blit(texture, rt);
        RenderTexture.active = rt;
        var read = new Texture2D(size, size, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        read.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        Color sum = Color.black;
        foreach (Color c in read.GetPixels()) sum += c;
        Object.DestroyImmediate(read);
        Color mean = sum / (size * size);
        mean.a = 1f;
        return mean;
    }

    /// 텍스처 평균이 target이 되는 _BaseColor. 너무 밝게 튀지 않게 채널마다 2배에서 자른다.
    public static Color TintFor(Texture texture, Color target)
    {
        Color mean = MeanColor(texture);
        return new Color(
            Mathf.Min(2f, target.r / Mathf.Max(0.02f, mean.r)),
            Mathf.Min(2f, target.g / Mathf.Max(0.02f, mean.g)),
            Mathf.Min(2f, target.b / Mathf.Max(0.02f, mean.b)),
            1f);
    }

    /// 픽셀 하나를 목표 색으로 옮긴다. mean은 그 픽셀이 속한 재질(견본)의 평균, keep은 원래 색을 남기는 정도.
    public static Color Grade(Color c, Color mean, Color target, float keep = 0.25f)
    {
        float meanLum = Mathf.Max(0.02f, Luminance(mean));
        float ratio = Luminance(c) / meanLum;
        Color flat = target * ratio;
        Color kept = c * (Luminance(target) / meanLum);
        Color result = Color.Lerp(flat, kept, keep);
        result.a = 1f;
        return result;
    }
}
