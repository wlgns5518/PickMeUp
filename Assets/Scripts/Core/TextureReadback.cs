using UnityEngine;

// 화면에 걸린 그림을 PNG 바이트로 떠 내는 곳.
public static class TextureReadback
{
    /// 스프라이트의 텍스처는 읽기 허용이 아닌 경우가 많다(에디터에서 임포트한 것이 그렇다).
    /// 렌더 텍스처에 한 번 그려서 읽을 수 있는 복사본을 뜬다 — 임포트 설정과 무관하게 통한다.
    public static byte[] EncodePng(Sprite sprite)
    {
        if (sprite == null || sprite.texture == null) return null;

        Texture2D source = sprite.texture;
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
            if (readable != null) Object.Destroy(readable);
        }
    }
}
