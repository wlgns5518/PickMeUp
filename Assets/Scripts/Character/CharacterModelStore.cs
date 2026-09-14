using System;
using System.IO;
using UnityEngine;

// 굽은 몸이 디스크에 눕는 자리.
//
// 빌드에는 AssetDatabase가 없다. 소환한 캐릭터의 몸을 프리팹으로 저장할 수 없으므로,
// 리깅까지 끝난 GLB 파일 자체를 저장해 두고 다음에 켤 때 다시 읽어 세운다(CharacterBodyFactory).
// 이게 없으면 게임을 껐다 켤 때마다 같은 캐릭터를 44크레딧씩 다시 굽게 된다.
//
// GLB인 이유: 텍스처까지 파일 하나에 들어 있고, 런타임에 읽을 수 있는 형식이 이것뿐이다.
// FBX는 임포터가 있는 에디터에서만 읽힌다 — 그쪽은 CharacterModelBuilder가 맡는다.
public static class CharacterModelStore
{
    private const string FolderName = "CharacterModels";
    private const string Extension = ".glb";

    public static string Root => Path.Combine(Application.persistentDataPath, FolderName);

    public static string PathFor(string characterId) =>
        Path.Combine(Root, Sanitize(characterId) + Extension);

    /// 내려받는 동안 쓰는 자리. 다 받고 나서 Commit으로 갈아 끼운다.
    public static string TempPathFor(string characterId) => PathFor(characterId) + ".part";

    public static bool Exists(string characterId) =>
        !string.IsNullOrEmpty(characterId) && File.Exists(PathFor(characterId));

    /// 다 받은 임시 파일을 진짜 자리에 앉힌다. 받다 만 파일이 진짜 자리에 남으면
    /// 다음에 켤 때 그걸 읽다가 죽으므로, 완성된 뒤에만 이름을 바꾼다.
    public static bool Commit(string characterId)
    {
        string temporary = TempPathFor(characterId);
        if (!File.Exists(temporary)) return false;

        var info = new FileInfo(temporary);
        if (info.Length == 0)
        {
            File.Delete(temporary);
            return false;
        }

        string path = PathFor(characterId);
        if (File.Exists(path)) File.Delete(path);
        File.Move(temporary, path);

        Debug.Log($"[CharacterModelStore] 몸을 저장했다: {path} ({info.Length / 1024} KB)");
        return true;
    }

    public static void Save(string characterId, byte[] glb)
    {
        if (string.IsNullOrEmpty(characterId) || glb == null || glb.Length == 0) return;

        Directory.CreateDirectory(Root);
        File.WriteAllBytes(TempPathFor(characterId), glb);
        Commit(characterId);
    }

    public static void Delete(string characterId)
    {
        if (!Exists(characterId)) return;
        File.Delete(PathFor(characterId));
    }

    // 캐릭터 id는 GUID라 원래 안전하지만, 옛 세이브가 이름으로 떨어지는 경로가 있어서 한 번 거른다.
    private static string Sanitize(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
            sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
