using System.IO;
using UnityEditor;

public static class EditorAssetFolders
{
    /// "Assets/A/B/C"처럼 여러 단계를 한 번에 만든다. 이미 있으면 아무것도 하지 않는다.
    /// AssetDatabase.CreateFolder는 부모가 있어야 해서, 없는 부모부터 차례로 세운다.
    public static void Ensure(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        Ensure(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
