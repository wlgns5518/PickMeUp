using System.IO;
using UnityEditor;
using UnityEngine;

// 에디터 도구들이 굽는 머티리얼 에셋을 한 길로 만든다(마을 바닥, 섬 바위, 마법진, 마을 파츠, 조립 재질).
public static class EditorMaterials
{
    public const string LitShader = "Universal Render Pipeline/Lit";

    /// 경로에 머티리얼 에셋이 있으면 그것을, 없으면 URP Lit으로 새로 만들어 그 자리에 저장해 돌려준다.
    ///
    /// 이미 있는 에셋은 지우지 않고 그대로 돌려준다 — 부르는 쪽이 값만 덮어쓴다. 지우고 새로 만들면
    /// GUID가 바뀌어 그 머티리얼을 물고 있던 프리팹과 씬이 참조를 잃는다.
    public static Material LoadOrCreateLit(string path, string name = null)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;

        string folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        material = new Material(Shader.Find(LitShader)) { name = name ?? Path.GetFileNameWithoutExtension(path) };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
