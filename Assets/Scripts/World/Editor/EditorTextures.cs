using System.IO;
using UnityEditor;
using UnityEngine;

// 에디터 도구가 픽셀로 칠한 텍스처를 JPG 에셋으로 내려놓는 한 길(시설·집 재질 투영, 성벽 팔레트, 깃발 물들이기).
public static class EditorTextures
{
    /// pixels를 texture에 담아 path에 JPG(품질 90)로 쓰고, 임포트한 에셋을 돌려준다. 넘긴 텍스처는 여기서 치운다.
    /// clamp: 한 장을 통째로 펼쳐 쓰는 그림이라 가장자리에서 반대편 색이 번지면 안 될 때.
    public static Texture2D SaveJpg(Texture2D texture, Color32[] pixels, string path, int maxSize, bool clamp)
    {
        texture.SetPixels32(pixels);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToJPG(90));
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        if (clamp) importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = maxSize;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
