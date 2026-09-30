using System;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// 새로 뽑은 캐릭터를 남기는 곳 — 초상화 PNG, CharacterSO 에셋, 보유 명단.
//
// 에디터에서는 프로젝트에 파일로 남기고(다음에 켜도 있다), 빌드에서는 스프라이트만 만들어 쥐여 준다.
// 저장 방식이 두 갈래라 부르는 쪽(MeshyCharacterGenerator)이 #if를 몰라도 되게 여기서 가른다.
public sealed class CharacterAssetWriter
{
    private readonly string imageDir;
    private readonly string assetDir;
    private readonly CharacterRosterSO roster;

    public CharacterAssetWriter(string imageDir, string assetDir, CharacterRosterSO roster)
    {
        this.imageDir = imageDir;
        this.assetDir = assetDir;
        this.roster = roster;
    }

    /// 초상화 스프라이트를 만든다. 에디터에서는 PNG로 저장하고 임포트한 에셋을 돌려주며 경로를 알려 준다.
    public Sprite StorePortrait(Texture2D tex, string characterName, out string assetPath)
    {
        assetPath = null;
#if UNITY_EDITOR
        try
        {
            string fullDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), imageDir);
            Directory.CreateDirectory(fullDir);

            string filename = $"{SanitizeFileName(characterName ?? "character")}_{DateTime.Now:yyyyMMdd_HHmmss}_{UnityEngine.Random.Range(1000, 9999)}.png";
            File.WriteAllBytes(Path.Combine(fullDir, filename), tex.EncodeToPNG());

            assetPath = $"{imageDir}/{filename}".Replace('\\', '/');
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Meshy] PNG 저장 실패: {e.Message}");
            return null;
        }
#else
        // FullRect로 만든다. 기본값(Tight)은 알파 외곽선을 따라 폴리곤을 뜨는데, 카드에 그대로
        // 붙일 사각 초상화라 얻는 것 없이 시간만 든다(1024짜리 한 장에 수 ms).
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect);
#endif
    }

    /// 에디터에서 만든 캐릭터를 .asset으로 저장하고 보유 명단에 얹는다. 빌드에서는 아무것도 하지 않는다.
    public void SaveCharacter(CharacterSO so)
    {
#if UNITY_EDITOR
        try
        {
            string fullDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetDir);
            Directory.CreateDirectory(fullDir);

            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{assetDir}/{SanitizeFileName(so.characterName)}.asset".Replace('\\', '/'));
            AssetDatabase.CreateAsset(so, path);
            RegisterInRoster(so);
            AssetDatabase.SaveAssets();
        }
        catch (Exception e) { Debug.LogError($"[Meshy] CharacterSO 저장 실패: {e.Message}"); }
#endif
    }

#if UNITY_EDITOR
    // 저장만 하고 명단에 얹지 않으면, 이번 판에는 손에 들어온 것처럼 보이지만
    // 다음에 켤 때 보유 명단은 로스터 에셋에서 다시 만들어지므로 그 캐릭터만 사라진다.
    // (실제로 .asset은 일곱인데 편성 창에는 다섯만 나오는 상태였다.)
    private void RegisterInRoster(CharacterSO so)
    {
        CharacterRosterSO target = roster != null ? roster : FindSingleRoster();
        if (target == null)
        {
            Debug.LogWarning($"[Meshy] 보유 명단(CharacterRosterSO)을 찾지 못해 {so.characterName}이(가) 명단에 오르지 않았습니다. " +
                             "생성기의 Roster 칸에 로스터 에셋을 지정하세요.");
            return;
        }

        target.EditorRegister(so);
    }

    // 로스터가 하나뿐일 때만 자동으로 고른다. 여럿이면 어느 쪽에 얹어야 할지 알 수 없으므로
    // 조용히 아무 데나 넣지 않고 인스펙터 지정을 요구한다.
    private static CharacterRosterSO FindSingleRoster()
    {
        string[] guids = AssetDatabase.FindAssets("t:CharacterRosterSO");
        if (guids.Length != 1) return null;

        return AssetDatabase.LoadAssetAtPath<CharacterRosterSO>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }
#endif

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "character";
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 || c == ' ' ? '_' : c);
        return sb.ToString();
    }
}
