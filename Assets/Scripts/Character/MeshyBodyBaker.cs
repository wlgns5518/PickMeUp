using System;
using System.Threading.Tasks;

// 외형 설명 한 문단을 받아, 뼈가 들어간 GLB 한 장을 몸 저장소(CharacterModelStore)에 앉힌다.
//
//   외형 설명  ──text-to-image(T포즈, 다시점)──▶  전신 시트
//              ──multi-image-to-3d──────────▶  메시(텍스처 포함, 원점은 발밑)
//              ──rigging───────────────────▶  뼈가 들어간 GLB → CharacterModelStore/<id>.glb
//
// 세 번의 Meshy 태스크는 서버 안에서 태스크 id로 이어진다. 에디터 메뉴(MeshyModelPipeline)와 소환 직후의
// 뒤편 굽기(MeshyBodyService)가 이 사슬을 같이 쓴다. 예전에는 둘이 같은 사슬을 따로 짜 들고 있어서,
// 한쪽에서만 검사(시트가 비어 왔는가 등)가 빠지는 일이 있었다.
public sealed class MeshyBodyBaker
{
    public enum Stage { Sheet, Mesh, Rig, Download }

    /// 단계, 전체 진행률(0~1), Meshy가 알려 준 상태 문자열.
    public delegate void ProgressHandler(Stage stage, float progress, string status);

    private readonly MeshyClient meshy;

    public MeshyBodyBaker(MeshyClient meshy = null)
    {
        this.meshy = meshy ?? MeshyClient.Shared;
    }

    /// 굽고 받아서 저장소에 앉힌다. 어느 단계에서든 실패하면 예외를 던진다.
    public async Task BakeAsync(string characterId, string appearance, ProgressHandler onProgress = null)
    {
        if (string.IsNullOrEmpty(characterId)) throw new ArgumentException("캐릭터 id가 비어 있다.", nameof(characterId));

        string sheetTask = await meshy.CreateTaskAsync(MeshyBodyRecipe.SheetEndpoint,
            MeshyBodyRecipe.SheetBody(MeshyBodyRecipe.SheetPrompt(appearance)));
        MeshyProtocol.ImageTask sheet = await meshy.AwaitTaskAsync<MeshyProtocol.ImageTask>(
            MeshyBodyRecipe.SheetEndpoint, sheetTask, Report(onProgress, Stage.Sheet, 0.05f, 0.15f));
        if (sheet.FirstImageUrl == null) throw new Exception("전신 시트가 비어서 돌아왔다.");

        string meshTask = await meshy.CreateTaskAsync(MeshyBodyRecipe.MeshEndpoint, MeshyBodyRecipe.MeshBody(sheetTask));
        await meshy.AwaitTaskAsync<MeshyProtocol.ModelTask>(
            MeshyBodyRecipe.MeshEndpoint, meshTask, Report(onProgress, Stage.Mesh, 0.20f, 0.40f));

        string rigTask = await meshy.CreateTaskAsync(MeshyBodyRecipe.RigEndpoint, MeshyBodyRecipe.RigBody(meshTask));
        MeshyProtocol.RigTask rig = await meshy.AwaitTaskAsync<MeshyProtocol.RigTask>(
            MeshyBodyRecipe.RigEndpoint, rigTask, Report(onProgress, Stage.Rig, 0.60f, 0.30f));

        string glbUrl = rig.result?.rigged_character_glb_url;
        if (string.IsNullOrEmpty(glbUrl)) throw new Exception("리깅이 끝났는데 GLB 주소가 없다.");

        onProgress?.Invoke(Stage.Download, 0.92f, null);

        // 6MB가 넘는다. 메모리에 통째로 올리지 않고 옆 자리에 파일로 받은 뒤 갈아 끼운다 —
        // 받다 만 파일이 진짜 자리에 남으면 다음에 켤 때 그걸 읽다가 죽는다.
        await meshy.DownloadAsync(glbUrl, CharacterModelStore.TempPathFor(characterId));
        if (!CharacterModelStore.Commit(characterId))
            throw new Exception("GLB를 받았는데 저장소에 앉히지 못했다(빈 파일).");
    }

    private static Action<float, string> Report(ProgressHandler onProgress, Stage stage, float start, float span) =>
        onProgress == null ? (Action<float, string>)null : (p, status) => onProgress(stage, start + p * span, status);
}
