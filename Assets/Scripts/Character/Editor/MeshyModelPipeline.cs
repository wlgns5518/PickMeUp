using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// 이미 있는 캐릭터에게 몸을 구워 주는 에디터 메뉴.
//
// 소환한 캐릭터는 소환하는 순간 뒤에서 몸이 구워진다(MeshyBodyService). 이 메뉴는 그 길을 타지 않은
// 캐릭터 — 소환 기능이 생기기 전부터 로스터에 있던 캐릭터 — 를 위한 것이다. 결과물은 소환과 똑같다:
// 리깅까지 끝난 GLB 한 장이 런타임 몸 저장소(CharacterModelStore)에 떨어지고, 전투에 들어갈 때
// CharacterBodyFactory가 그 파일로 몸을 세운다. 프로젝트(Assets) 안에는 아무것도 만들지 않는다.
//
//   초상화(상반신)  ──Gemini──▶  외형 설명(영어)
//                                    │
//                                    ▼  Meshy text-to-image (t-pose, 다시점)
//                              전신 시트 ── 하체가 여기서 생긴다
//                                    │
//                                    ▼  Meshy multi-image-to-3d
//                                  메시(텍스처 포함, 원점은 발밑)
//                                    │
//                                    ▼  Meshy rigging
//                              뼈가 들어간 GLB → CharacterModelStore/<id>.glb
//
// 예전에는 FBX를 받아 Assets/Character/CharacterModel에 프리팹으로 조립했다. 같은 캐릭터가
// 프리팹(에디터)과 GLB(빌드) 두 모습을 갖게 되고, 두 길이 조금씩 달라 한쪽만 이상하게 보이는
// 일이 생겨서 한 길로 합쳤다.
//
// 오래 걸린다(캐릭터 한 명에 3~10분). 그동안 스크립트가 다시 컴파일되면 도메인이 갈리면서
// 진행 중인 await가 통째로 사라지므로, 굽는 동안에는 어셈블리 리로드를 잠가 둔다.
public static class MeshyModelPipeline
{
    // 무엇을 주문하는지(프롬프트, 자세, 키, 폴리곤 수, 크레딧 어림값)는 전부 MeshyBodyRecipe에 있다.
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

        int existing = 0;
        foreach (CharacterSO so in targets) if (CharacterModelStore.Exists(so.Id)) existing++;
        if (existing > 0)
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "3D 모델 굽기",
                $"고른 {targets.Count}명 중 {existing}명은 이미 몸이 있다. 새로 구우면 덮어쓴다.\n" +
                $"대략 {targets.Count * MeshyBodyRecipe.CreditsPerCharacter} 크레딧.",
                "새로 굽는다", "그만둔다");
            if (!overwrite) return;
        }

        await BakeAll(targets);
    }

    [MenuItem("PickMeUp/Character/3D 모델 굽기 (선택한 캐릭터)", true)]
    private static bool BakeSelectedValidate() => Selected().Count > 0;

    [MenuItem("PickMeUp/Character/3D 모델 굽기 (몸 없는 캐릭터 전원)", priority = 21)]
    private static async void BakeRoster()
    {
        var roster = new List<CharacterSO>();
        foreach (string guid in AssetDatabase.FindAssets("t:CharacterSO"))
        {
            var so = AssetDatabase.LoadAssetAtPath<CharacterSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so != null && !CharacterModelStore.Exists(so.Id)) roster.Add(so);
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
        }

        Debug.Log($"[MeshyModelPipeline] {done}/{characters.Count}명의 몸을 구웠다. " +
                  $"받아 둔 곳: {CharacterModelStore.Root}");
    }

    /// 캐릭터 한 명분. 끝나면 CharacterModelStore에 그 캐릭터 id로 GLB가 놓여 있다.
    public static async Task Bake(CharacterSO character)
    {
        if (character == null) throw new ArgumentNullException(nameof(character));

        string label = string.IsNullOrEmpty(character.characterName) ? character.name : character.characterName;

        // 몸은 id로 찾는다. id가 메모리에만 있고 에셋에 안 적혀 있으면, 에디터를 다시 켰을 때
        // 새 id가 붙어 방금 구운 몸이 남의 것이 된다. 굽기 전에 에셋에 박아 둔다.
        character.EnsureId();
        AssetDatabase.SaveAssetIfDirty(character);
        string id = character.Id;

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
        await MeshyApi.Await<MeshyBodyRecipe.ModelTask>(
            MeshyBodyRecipe.MeshEndpoint, meshTask, (p, s) => Report(label, $"메시 {s}", 0.25f + p * 0.35f));

        Report(label, "뼈를 넣는 중", 0.60f);
        string rigTask = await MeshyApi.CreateRig(meshTask);
        MeshyBodyRecipe.RigTask rig = await MeshyApi.Await<MeshyBodyRecipe.RigTask>(
            MeshyBodyRecipe.RigEndpoint, rigTask, (p, s) => Report(label, $"리깅 {s}", 0.60f + p * 0.30f));

        if (rig.result == null || string.IsNullOrEmpty(rig.result.rigged_character_glb_url))
            throw new Exception("리깅이 끝났는데 GLB 주소가 없다.");

        Report(label, "받아 오는 중", 0.92f);

        // 소환 쪽과 같은 자리에 같은 방식으로 놓는다 — 옆에 다 받은 뒤에 갈아 끼운다.
        await MeshyApi.Download(rig.result.rigged_character_glb_url, CharacterModelStore.TempPathFor(id));
        if (!CharacterModelStore.Commit(id))
            throw new Exception("GLB를 받았는데 저장소에 앉히지 못했다(빈 파일).");

        EditorUtility.ClearProgressBar();
    }

    private static void Report(string label, string step, float progress)
    {
        EditorUtility.DisplayProgressBar($"3D 모델 굽기 — {label}", step, Mathf.Clamp01(progress));
    }
}
