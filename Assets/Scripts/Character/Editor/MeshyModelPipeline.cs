using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// 카드 한 장을 전투에 나갈 몸으로 바꾸는 길.
//
//   초상화(상반신)  ──Gemini──▶  외형 설명(영어)
//                                    │
//                                    ▼  Meshy text-to-image (a-pose, 다시점)
//                              전신 시트 ── 하체가 여기서 생긴다
//                                    │
//                                    ▼  Meshy multi-image-to-3d
//                                  메시(텍스처 포함, 원점은 발밑)
//                                    │
//                                    ▼  Meshy rigging
//                              뼈가 들어간 FBX
//                                    │
//                                    ▼  CharacterModelBuilder
//                        휴머노이드 아바타 + 전투 프리팹 → CharacterSO.battlePrefab
//
// 세 번의 Meshy 태스크는 서버 안에서 태스크 id로 이어진다. 중간 산출물을 받았다가 다시 올리지
// 않으므로, 우리가 실제로 내려받는 파일은 마지막 FBX와 텍스처, 그리고 남겨 둘 시트 그림뿐이다.
//
// 오래 걸린다(캐릭터 한 명에 3~10분). 그동안 스크립트가 다시 컴파일되면 도메인이 갈리면서
// 진행 중인 await가 통째로 사라지므로, 굽는 동안에는 어셈블리 리로드를 잠가 둔다.
public static class MeshyModelPipeline
{
    public const string ModelRoot = "Assets/Character/CharacterModel";

    // 무엇을 주문하는지(프롬프트, 키, 폴리곤 수, 크레딧 어림값)는 전부 MeshyBodyRecipe에 있다.
    // 빌드에서 도는 쪽(MeshyBodyService)과 같은 것을 봐야 두 길이 같은 사람을 만든다.

    // ── 메뉴 ─────────────────────────────────────────────────────────────

    [MenuItem("PickMeUp/Character/3D 모델 굽기 (선택한 캐릭터)", priority = 20)]
    private static async void BakeSelected()
    {
        List<CharacterSO> targets = Selected();
        if (targets.Count == 0)
        {
            EditorUtility.DisplayDialog("3D 모델 굽기", "CharacterSO 에셋을 골라라.", "확인");
            return;
        }
        await BakeAll(targets);
    }

    [MenuItem("PickMeUp/Character/3D 모델 굽기 (선택한 캐릭터)", true)]
    private static bool BakeSelectedValidate() => Selected().Count > 0;

    [MenuItem("PickMeUp/Character/3D 모델 굽기 (로스터 전원)", priority = 21)]
    private static async void BakeRoster()
    {
        var roster = new List<CharacterSO>();
        foreach (string guid in AssetDatabase.FindAssets("t:CharacterSO"))
        {
            var so = AssetDatabase.LoadAssetAtPath<CharacterSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so != null && so.battlePrefab == null) roster.Add(so);
        }

        if (roster.Count == 0)
        {
            EditorUtility.DisplayDialog("3D 모델 굽기", "아직 몸이 없는 캐릭터가 없다.", "확인");
            return;
        }

        bool go = EditorUtility.DisplayDialog(
            "3D 모델 굽기",
            $"몸이 없는 캐릭터 {roster.Count}명을 굽는다.\n" +
            $"대략 {roster.Count * MeshyBodyRecipe.CreditsPerCharacter} 크레딧, {roster.Count * 5}분쯤 걸린다.",
            "굽는다", "그만둔다");
        if (!go) return;

        await BakeAll(roster);
    }

    private static List<CharacterSO> Selected()
    {
        var list = new List<CharacterSO>();
        foreach (UnityEngine.Object o in Selection.objects)
            if (o is CharacterSO so) list.Add(so);
        return list;
    }

    // ── 굽기 ─────────────────────────────────────────────────────────────

    private static async Task BakeAll(List<CharacterSO> characters)
    {
        int balance;
        try
        {
            balance = await MeshyApi.Balance();
        }
        catch (Exception e)
        {
            Debug.LogError($"[MeshyModelPipeline] Meshy에 닿지 못했다: {e.Message}");
            return;
        }

        int needed = characters.Count * MeshyBodyRecipe.CreditsPerCharacter;
        if (balance >= 0 && balance < needed)
        {
            Debug.LogWarning($"[MeshyModelPipeline] 크레딧이 모자랄 수 있다 — 남은 {balance}, 필요 대략 {needed}. " +
                             "그래도 시작한다. 도중에 끊기면 남은 캐릭터만 다시 구우면 된다.");
        }

        // 굽는 도중의 재컴파일이 await를 삼키지 않도록 잠근다.
        EditorApplication.LockReloadAssemblies();
        int done = 0;
        try
        {
            foreach (CharacterSO character in characters)
            {
                try
                {
                    await Bake(character);
                    done++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[MeshyModelPipeline] {character.characterName} 굽기 실패: {e}", character);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            EditorApplication.UnlockReloadAssemblies();
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"[MeshyModelPipeline] {done}/{characters.Count}명의 몸을 구웠다.");
    }

    /// 캐릭터 한 명분. 끝나면 CharacterSO.battlePrefab에 전투 프리팹이 꽂혀 있다.
    public static async Task<GameObject> Bake(CharacterSO character)
    {
        if (character == null) throw new ArgumentNullException(nameof(character));

        string label = string.IsNullOrEmpty(character.characterName) ? character.name : character.characterName;
        string directory = Path.Combine(ModelRoot, Sanitize(label)).Replace('\\', '/');
        Directory.CreateDirectory(directory);

        Report(label, "초상화를 읽는 중", 0.05f);
        string appearance = await CharacterAppearance.Describe(character);

        Report(label, "전신 시트를 그리는 중", 0.10f);
        string sheetTask = await MeshyApi.CreateModelSheet(appearance);
        MeshyBodyRecipe.ImageTask sheet = await MeshyApi.Await<MeshyBodyRecipe.ImageTask>(
            MeshyBodyRecipe.SheetEndpoint, sheetTask, (p, s) => Report(label, $"전신 시트 {s}", 0.10f + p * 0.15f));

        if (sheet.image_urls == null || sheet.image_urls.Length == 0)
            throw new Exception("전신 시트가 비어서 돌아왔다.");

        Report(label, "메시를 뽑는 중", 0.25f);
        string meshTask = await MeshyApi.CreateMesh(sheetTask);
        MeshyBodyRecipe.ModelTask mesh = await MeshyApi.Await<MeshyBodyRecipe.ModelTask>(
            MeshyBodyRecipe.MeshEndpoint, meshTask, (p, s) => Report(label, $"메시 {s}", 0.25f + p * 0.35f));

        Report(label, "뼈를 넣는 중", 0.60f);
        string rigTask = await MeshyApi.CreateRig(meshTask);
        MeshyBodyRecipe.RigTask rig = await MeshyApi.Await<MeshyBodyRecipe.RigTask>(
            MeshyBodyRecipe.RigEndpoint, rigTask, (p, s) => Report(label, $"리깅 {s}", 0.60f + p * 0.25f));

        if (rig.result == null || string.IsNullOrEmpty(rig.result.rigged_character_fbx_url))
            throw new Exception("리깅이 끝났는데 FBX 주소가 없다.");

        Report(label, "받아 오는 중", 0.85f);

        // 시트는 다시 구울 때의 출발점으로 남긴다. 첫 장이 정면이다.
        for (int i = 0; i < sheet.image_urls.Length; i++)
            await MeshyApi.Download(sheet.image_urls[i], Path.Combine(directory, $"{Sanitize(label)}_Sheet{i}.png"));

        string fbxPath = Path.Combine(directory, Sanitize(label) + ".fbx").Replace('\\', '/');
        await MeshyApi.Download(rig.result.rigged_character_fbx_url, fbxPath);

        // FBX는 텍스처를 품고 오지 않는다. 메시 태스크가 구운 베이스 컬러를 따로 받아 재질을 만든다.
        string texturePath = null;
        if (mesh.texture_urls != null && mesh.texture_urls.Length > 0 &&
            !string.IsNullOrEmpty(mesh.texture_urls[0].base_color))
        {
            texturePath = Path.Combine(directory, Sanitize(label) + "_BaseColor.png").Replace('\\', '/');
            await MeshyApi.Download(mesh.texture_urls[0].base_color, texturePath);
        }

        Report(label, "몸을 조립하는 중", 0.92f);

        // 조립하는 쪽은 곧바로 임포터를 집어 든다. 비동기 임포트를 기다렸다가는 없는 것을 잡는다.
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceSynchronousImport);
        if (!string.IsNullOrEmpty(texturePath))
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);

        string sheetPath = Path.Combine(directory, Sanitize(label) + "_Sheet0.png").Replace('\\', '/');
        GameObject prefab = CharacterModelBuilder.Build(character, fbxPath, texturePath, sheetPath);

        EditorUtility.ClearProgressBar();
        return prefab;
    }


    private static void Report(string label, string step, float progress)
    {
        EditorUtility.DisplayProgressBar($"3D 모델 굽기 — {label}", step, Mathf.Clamp01(progress));
    }

    // 파일 이름으로 쓸 수 없는 글자만 걷어낸다. 한글은 그대로 둔다 —
    // 초상화 폴더도 한글 이름을 쓰고 있어서, 여기만 로마자로 바꾸면 짝을 찾기 어려워진다.
    public static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Character";

        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name.Trim())
            sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
