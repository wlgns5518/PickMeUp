using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 마을 건물 파츠를 Meshy로 굽는다.
//
//   부위 설명 + 공통 화풍(ArchStyle)
//        │ text-to-image (9크레딧)
//        ▼
//   컨셉 그림  Library/MeshyVillage/<id>/concept.png   ← 여기서 눈으로 보고, 마음에 안 들면 이 그림만 다시 뽑는다
//        │ image-to-3d (저폴리 + 텍스처, 15크레딧)
//        ▼
//   FBX + 베이스 컬러  Library/MeshyVillage/<id>/          (저장소 밖 — 크레딧이 든 원본)
//        │ 앉히기(크레딧 없음)
//        ▼
//   Assets/Environment/Village/Parts/<id>/
//        <id>_mesh.asset   발밑 가운데가 원점, 정면 +Z, 높이는 표의 미터 값
//        <id>_albedo.jpg   (<id>_emission.jpg)   <id>.mat
//        <id>.prefab       메시 + 머티리얼만 든 파츠 한 개. 조립기와 사람이 이것을 놓는다
//
// 건물 한 채를 통째로 뽑지 않는다. 본체·지붕·입구·창·부벽·조명처럼 자리 하나를 채우는 파츠로 뽑아
// VillagePrefabAssembler가 시설마다 조립한다. 여러 시설이 같은 파츠(공용 키트)를 돌려 쓰므로
// 크레딧은 파츠 수만큼만 든다.
//
// 두 단계로 나눈 이유: 3D가 그림보다 비싸고 오래 걸린다. 그림에서 이미 틀린 것(지붕이 붙은 본체,
// 벽이 붙은 지붕)은 3D에서 고쳐지지 않으므로 그림을 보고 넘긴다.
//
// FBX를 그대로 프로젝트에 두지 않는 이유: Meshy FBX는 2k 텍스처를 품고 와서 파츠 하나가 5~6MB다.
// 메시만 꺼내 에셋으로 두면 수백 KB다. 원본 FBX는 Library에 남으므로 "다시 앉히기"는 크레딧 없이 된다.
//
// 태스크 id는 파일로 남긴다. 3D는 대기열 때문에 오래 걸리기도 해서, 그 사이 에디터를 껐다 켜도
// "3D 모델 굽기"를 다시 누르면 새로 주문하지 않고 남은 태스크를 이어서 받는다.
public static class VillagePartBaker
{
    public const string PartsRoot = "Assets/Environment/Village/Parts";
    private const string ImportFolder = PartsRoot + "/_Import";
    private const string RawRoot = "Library/MeshyVillage";

    private const string ImageModel = "nano-banana-pro";
    // meshy-t2(smart-topology)도 15크레딧이지만 대기열이 한참 멈춰 있었다(2026-09-23, 앞선 태스크 140개).
    // lite는 곧바로 시작해 3분 안에 끝났고, 컨셉 그림을 충실히 따랐다.
    private const string MeshModel = "meshy-6-lite";

    public const int CreditsPerConcept = 9;
    public const int CreditsPerModel = 15;

    public readonly struct Part
    {
        public readonly string id;
        // 앉힐 때 메시를 이 높이(미터)로 맞춘다. 조립기는 이 크기를 기준으로 늘리고 줄인다.
        public readonly float height;
        public readonly int polycount;
        public readonly int textureSize;
        // 0이면 빛나지 않는다. 0보다 크면 베이스 컬러에서 밝고 채도 높은 곳(불꽃·등불·룬)만 골라 그 세기로 빛낸다.
        public readonly float glow;
        public readonly string subject;

        public Part(string id, float height, int polycount, int textureSize, float glow, string subject)
        {
            this.id = id; this.height = height; this.polycount = polycount;
            this.textureSize = textureSize; this.glow = glow; this.subject = subject;
        }
    }

    // 모든 파츠가 같은 꼬리를 붙인다. 파츠가 제각기 그려지면 한 건물에 붙였을 때 재질이 따로 논다.
    private const string ArchStyle =
        " Dark fantasy medieval architecture for a game: dark grey rough stone with worn chipped edges and subtle cracks, " +
        "dark aged rough wood, dark forged iron with a little rust, a little moss and grime near the bottom. " +
        "Grounded realistic fantasy, restrained gothic details, not ornate, not colorful, not cartoon, not modern. " +
        "Bold simple silhouette with large clear forms and little small detail. " +
        "Single isolated object seen from the front at a three-quarter angle from slightly above, centered, " +
        "the whole object inside the frame, plain white background, soft even neutral lighting, " +
        "no cast shadow, no ground, no terrain, no people, no text.";

    // 붙여 쓰는 파츠(문·창·부벽)는 뒷면이 평평해야 벽에 대고 세울 수 있다.
    private const string FlatBack = " The back side is flat, meant to be set flush against a wall.";

    public static readonly Part[] Parts =
    {
        // ---- 공용 키트: 여러 시설이 돌려 쓴다 --------------------------------------------
        new Part("kit_plinth", 1.2f, 1500, 1024, 0f,
            "a low octagonal foundation platform of large dark grey stone slabs, much wider than tall, " +
            "with bevelled worn edges and one low step running around all its sides. The top surface is flat and empty."),
        new Part("kit_stairs", 1.4f, 1500, 1024, 0f,
            // "문으로 이어지는 계단"이라고 쓰면 계단 위에 문을 붙여 그린다. 계단만 시킨다.
            "a free-standing short wide straight flight of four dark stone steps with low plain stone side blocks. " +
            "Nothing at the top of the steps: no door, no wall, no arch behind them."),
        new Part("kit_door", 5.5f, 3000, 1024, 0f,
            "a building entrance: a pointed gothic arch doorway frame of dark carved stone blocks holding a heavy closed " +
            "double door of dark aged oak planks reinforced with black iron straps and a ring handle, " +
            "and a small dark slate canopy on two iron brackets above it." + FlatBack),
        new Part("kit_window", 3.2f, 1500, 1024, 0.6f,
            "a single tall narrow gothic window: pointed arch stone frame with a stone sill, dark iron grille bars " +
            "and leaded glass glowing with warm candlelight from inside." + FlatBack),
        new Part("kit_buttress", 7f, 1500, 1024, 0f,
            "a single stone wall buttress: a tall narrow sloped pier of dark grey stone blocks that steps back twice as it rises, " +
            "wrapped by two black riveted iron bands." + FlatBack),
        new Part("kit_lantern", 4f, 1500, 1024, 0.8f,
            "a street lantern post: a slim black forged iron post on a small square stone base, with a square iron lantern " +
            "with glass panes and a warm candle flame hanging from a curled bracket at the top."),
        new Part("kit_brazier", 1.8f, 1500, 1024, 1.5f,
            "a standing fire brazier: a wide black iron bowl on three curved iron legs, filled with glowing embers and " +
            "a bright orange fire, on a small round stone base."),
        new Part("kit_banner", 7f, 1500, 1024, 0f,
            "a tall banner pole: a black iron pole on a small stone base with a crossbar holding a long hanging cloth banner " +
            "of deep dark crimson with a faded simple silver sword emblem, the bottom edge tattered."),
        new Part("kit_crates", 2.2f, 1500, 1024, 0f,
            "a small cluster of supplies: two stacked wooden crates with iron corner bands, one iron-banded barrel " +
            "and a tied canvas sack, dark aged wood."),
        new Part("kit_pillar", 7f, 1500, 1024, 0f,
            "a single freestanding round stone column of dark grey stone drums on a square stone base with a simple square " +
            "capital, bound by two black iron rings."),
        new Part("kit_tower", 20f, 3000, 1024, 0f,
            "a slim round medieval watchtower: a tall cylindrical tower of dark grey stone blocks with a few narrow arrow-slit " +
            "windows, a dark wooden overhanging top floor with small windows and a steep conical dark slate roof with an iron spike. " +
            "Tall and narrow, about four times taller than wide."),
        new Part("kit_obelisk", 6f, 1500, 1024, 1.0f,
            "a tall carved obelisk of dark grey stone on a stepped base, with softly glowing pale cyan magical runes carved " +
            "down its face and a small iron cap on top."),

        // ---- 둥근 전당: 합성소·소환소 ------------------------------------------------------
        new Part("hall_body", 9f, 6000, 2048, 0f,
            "the ground floor of a round medieval stone hall, a short thick cylindrical wall of dark grey rough stone blocks, " +
            "about twice as wide as it is tall, with a plain arched empty doorway at the front, a dark iron band around it " +
            "and a simple stone ledge along the top edge. The top is flat and open: NO roof, no dome, no floor above."),
        new Part("hall_roof", 11f, 3000, 1024, 0f,
            "a separate conical roof for a round medieval tower, on its own: steep cone of overlapping dark slate shingles " +
            "with a wide round bottom edge that is perfectly flat and level, a thin dark iron band along the bottom rim, " +
            "a small iron finial spike on the tip. Only the roof itself, nothing under it, no walls."),

        // ---- 네모난 집: 무기창고·장비제작소 줄·숙소 --------------------------------------------
        new Part("house_body", 5f, 4000, 2048, 0f,
            "the ground floor of a rectangular medieval building: walls of dark grey rough stone blocks with dark timber " +
            "corner posts and a heavy timber beam along the top edge, about twice as wide as it is tall, one plain arched " +
            "empty doorway in the middle of the long front wall and two small shuttered windows. " +
            "The top is flat and open: NO roof, no floor above."),
        new Part("house_upper", 3.5f, 3000, 1024, 0f,
            "a timber-framed upper floor of a rectangular medieval building on its own: dark aged timber frame with dark " +
            "plaster panels, small shuttered windows and slightly overhanging jetty beams at the bottom. " +
            "Flat level bottom and flat open top: NO roof, no ground floor under it."),
        new Part("house_roof", 4.5f, 3000, 1024, 0f,
            "a separate gable roof for a rectangular building on its own: a steep pitched roof of overlapping dark slate " +
            "shingles, a dark timber ridge beam, plain triangular timber gable ends and small iron ridge ornaments. " +
            "The bottom rim is a flat level rectangle. Only the roof, no walls under it."),

        // ---- 시설마다 하나뿐인 것 -----------------------------------------------------------
        new Part("rift_arch", 24f, 6000, 2048, 0.4f,
            "a massive ancient gothic stone gateway standing alone: two thick dark stone pillars joined by a tall pointed arch, " +
            "carved with worn faintly glowing violet runes, bound by heavy black iron braces with broken chains hanging, " +
            "the arch cracked with a few stones missing. The opening inside the arch is empty. Taller than wide."),
        new Part("summon_orb", 9f, 3000, 1024, 1.2f,
            "a large summoning orb shrine: a big smooth dark crystal sphere glowing faint violet from inside, held up by " +
            "four curved black iron claws rising from a round carved stone pedestal with glowing rune marks."),
        new Part("forge", 8f, 5000, 2048, 1.0f,
            "an open-sided blacksmith forge: a heavy stone hearth with glowing coals under a dark slate lean-to roof " +
            "on thick timber posts, a tall stone chimney, a black iron anvil on a stump, a quench barrel and hanging tools."),
        new Part("weapon_rack", 2.6f, 2000, 1024, 0f,
            "a wooden weapon rack of dark timber with iron fittings holding upright spears, swords and a battle axe, " +
            "with a round wooden shield leaning at its side."),
        new Part("training_dummy", 2.2f, 1500, 1024, 0f,
            "a combat training dummy: a thick wooden post with a straw-stuffed burlap body wrapped in rope, a dented iron " +
            "helmet on top and two wooden arm stakes, set on a cross-shaped wooden foot."),

        // ---- 성벽: PolygonWall 변마다 되풀이해 세운다 --------------------------------------
        new Part("wall_segment", 16f, 2000, 1024, 0f,
            "a straight section of a tall medieval fortress curtain wall on its own: thick wall of dark grey rough stone blocks " +
            "with a crenellated battlement parapet along the top, two narrow arrow slits and a few dark iron braces, " +
            "weathered with moss at the bottom. Perfectly straight with flat cut left and right ends so identical sections " +
            "join end to end. Wider than tall."),
        new Part("wall_tower", 22f, 3000, 1024, 0f,
            "a square medieval fortress corner tower on its own: a tall thick square tower of dark grey rough stone blocks, " +
            "slightly wider at the base, crenellated battlements on top with corbels under them, a few narrow arrow slits " +
            "and dark iron bands. No door. About two and a half times taller than wide."),
    };

    public static bool TryFind(string id, out Part part)
    {
        foreach (Part p in Parts)
        {
            if (p.id != id) continue;
            part = p;
            return true;
        }
        part = default;
        return false;
    }

    // ---- 경로 -----------------------------------------------------------------

    private static string RawDir(string id) => Path.Combine(RawRoot, id);
    public static string ConceptPath(string id) => Path.Combine(RawDir(id), "concept.png");
    private static string ConceptTaskPath(string id) => Path.Combine(RawDir(id), "concept.task");
    private static string ModelTaskPath(string id) => Path.Combine(RawDir(id), "model.task");
    private static string RawModelPath(string id) => Path.Combine(RawDir(id), "model.fbx");
    private static string RawAlbedoPath(string id) => Path.Combine(RawDir(id), "base_color.png");

    private static string Folder(string id) => $"{PartsRoot}/{id}";
    public static string PrefabPath(string id) => $"{Folder(id)}/{id}.prefab";
    private static string MeshPath(string id) => $"{Folder(id)}/{id}_mesh.asset";
    private static string MaterialPath(string id) => $"{Folder(id)}/{id}.mat";
    private static string AlbedoPath(string id) => $"{Folder(id)}/{id}_albedo.jpg";
    private static string EmissionPath(string id) => $"{Folder(id)}/{id}_emission.jpg";

    public static bool HasConcept(string id) => File.Exists(ConceptPath(id));
    public static bool HasRawModel(string id) => File.Exists(RawModelPath(id)) && File.Exists(RawAlbedoPath(id));

    // ---- 메뉴 -----------------------------------------------------------------

    [MenuItem("PickMeUp/Village/1. 컨셉 그림 굽기 (없는 것만)", priority = 50)]
    private static async void BakeConceptsMenu()
    {
        List<string> ids = Matching(id => !HasConcept(id));
        if (ids.Count == 0) { EditorUtility.DisplayDialog("마을 파츠", "컨셉 그림이 없는 파츠가 없다.", "확인"); return; }
        if (!EditorUtility.DisplayDialog("마을 파츠",
                $"컨셉 그림 {ids.Count}장을 그린다. 대략 {ids.Count * CreditsPerConcept} 크레딧.", "굽는다", "그만둔다")) return;
        await BakeConcepts(ids, false);
    }

    [MenuItem("PickMeUp/Village/2. 3D 모델 굽기 (컨셉만 있는 것)", priority = 51)]
    private static async void BakeModelsMenu()
    {
        List<string> ids = Matching(id => HasConcept(id) && !HasRawModel(id));
        if (ids.Count == 0) { EditorUtility.DisplayDialog("마을 파츠", "3D로 구울 파츠가 없다(컨셉 그림부터).", "확인"); return; }
        if (!EditorUtility.DisplayDialog("마을 파츠",
                $"3D 모델 {ids.Count}개를 굽는다. 대략 {ids.Count * CreditsPerModel} 크레딧(이어 받는 것은 0).", "굽는다", "그만둔다")) return;
        await BakeModels(ids, false);
    }

    [MenuItem("PickMeUp/Village/3. 받아 둔 파츠 다시 앉히기 (크레딧 없이)", priority = 52)]
    private static void InstallMenu() => InstallAll();

    private static List<string> Matching(Func<string, bool> predicate)
    {
        var ids = new List<string>();
        foreach (Part part in Parts) if (predicate(part.id)) ids.Add(part.id);
        return ids;
    }

    // ---- 컨셉 그림 -------------------------------------------------------------

    /// 컨셉 그림을 한꺼번에 주문한다. 한 장이 실패해도 나머지는 받는다.
    public static async Task BakeConcepts(IEnumerable<string> ids, bool overwrite)
    {
        EditorApplication.LockReloadAssemblies();
        try
        {
            var jobs = new List<Task>();
            foreach (string id in ids)
            {
                if (!TryFind(id, out Part part)) { Debug.LogError($"[VillagePartBaker] 표에 없는 파츠: {id}"); continue; }
                if (!overwrite && HasConcept(id)) continue;
                jobs.Add(BakeConcept(part, overwrite));
            }
            await Task.WhenAll(jobs);
        }
        catch (Exception)
        {
            // 한 장씩의 실패는 BakeConcept가 이미 남겼다.
        }
        finally
        {
            EditorApplication.UnlockReloadAssemblies();
        }
    }

    // 한꺼번에 스무 개를 넣으면 Meshy가 429로 막는다. 동시에 도는 주문을 몇 개로 묶는다.
    private static readonly System.Threading.SemaphoreSlim Slots = new System.Threading.SemaphoreSlim(4);

    private static async Task BakeConcept(Part part, bool overwrite)
    {
        await Slots.WaitAsync();
        try
        {
            // 주문은 들어갔는데 받기 전에 끊긴 것(429, 에디터 종료)은 새로 주문하지 않고 그 태스크를 이어 받는다.
            string taskId = null;
            if (!overwrite && File.Exists(ConceptTaskPath(part.id))) taskId = File.ReadAllText(ConceptTaskPath(part.id)).Trim();

            if (string.IsNullOrEmpty(taskId))
            {
                string body =
                    "{\"ai_model\":" + MeshyBodyRecipe.EscapeJson(ImageModel) +
                    ",\"prompt\":" + MeshyBodyRecipe.EscapeJson("Game asset: " + part.subject + ArchStyle) +
                    ",\"aspect_ratio\":\"1:1\"" +
                    // 배경이 남으면 3D 생성기가 배경까지 형태로 읽는다.
                    ",\"remove_background\":true}";

                taskId = await MeshyApi.CreateImage(body);
                Directory.CreateDirectory(RawDir(part.id));
                File.WriteAllText(ConceptTaskPath(part.id), taskId);
            }

            MeshyBodyRecipe.ImageTask task = await MeshyApi.Await<MeshyBodyRecipe.ImageTask>(
                MeshyBodyRecipe.SheetEndpoint, taskId, null);
            if (task.image_urls == null || task.image_urls.Length == 0)
                throw new Exception("그림이 비어서 돌아왔다.");

            await MeshyApi.Download(task.image_urls[0], ConceptPath(part.id));
            // 새 그림이 생기면 옛 그림으로 주문한 3D는 더 이상 이 그림과 맞지 않는다.
            File.Delete(ModelTaskPath(part.id));
            Debug.Log($"[VillagePartBaker] {part.id} 컨셉 완료 (태스크 {taskId}).");
        }
        catch (Exception e)
        {
            Debug.LogError($"[VillagePartBaker] {part.id} 컨셉 실패: {e.Message}");
            throw;
        }
        finally
        {
            Slots.Release();
        }
    }

    // ---- 3D ------------------------------------------------------------------

    /// 컨셉 그림에서 3D를 뽑아 받고 프로젝트에 앉힌다. 남은 태스크가 있으면 새로 주문하지 않고 이어 받는다.
    public static async Task BakeModels(IEnumerable<string> ids, bool overwrite)
    {
        var done = new List<string>();
        EditorApplication.LockReloadAssemblies();
        try
        {
            var jobs = new List<Task>();
            foreach (string id in ids)
            {
                if (!TryFind(id, out Part part)) { Debug.LogError($"[VillagePartBaker] 표에 없는 파츠: {id}"); continue; }
                if (!HasConcept(id)) { Debug.LogWarning($"[VillagePartBaker] {id}는 컨셉 그림이 없다."); continue; }
                if (!overwrite && HasRawModel(id)) continue;
                jobs.Add(BakeModel(part, overwrite, done));
            }
            await Task.WhenAll(jobs);
        }
        catch (Exception)
        {
            // 하나씩의 실패는 BakeModel이 이미 남겼다. 받은 것은 아래에서 앉힌다.
        }
        finally
        {
            EditorApplication.UnlockReloadAssemblies();
        }

        foreach (string id in done) Install(id);
        AssetDatabase.SaveAssets();
    }

    private static async Task BakeModel(Part part, bool overwrite, List<string> done)
    {
        try
        {
            string taskId = null;
            if (!overwrite && File.Exists(ModelTaskPath(part.id))) taskId = File.ReadAllText(ModelTaskPath(part.id)).Trim();

            if (string.IsNullOrEmpty(taskId))
            {
                // 그림 주소는 며칠 지나면 만료된다. 받아 둔 그림을 그대로 실어 보낸다.
                string image = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(ConceptPath(part.id)));
                string body =
                    "{\"image_url\":" + MeshyBodyRecipe.EscapeJson(image) +
                    ",\"ai_model\":\"" + MeshModel + "\"" +
                    ",\"should_remesh\":true" +
                    ",\"topology\":\"triangle\"" +
                    ",\"target_polycount\":" + part.polycount +
                    ",\"should_texture\":true" +
                    // 모바일에서도 돌릴 것이라 베이스 컬러 한 장만 쓴다.
                    ",\"enable_pbr\":false" +
                    ",\"target_formats\":[\"fbx\"]}";

                // 3D는 몇 분씩 걸리므로 슬롯은 주문하는 동안만 잡는다. 기다리는 동안의 폴링은 429를 알아서 기다린다.
                await Slots.WaitAsync();
                try
                {
                    taskId = await MeshyApi.CreateModelFromImage(body);
                }
                finally
                {
                    Slots.Release();
                }
                Directory.CreateDirectory(RawDir(part.id));
                File.WriteAllText(ModelTaskPath(part.id), taskId);
            }

            MeshyBodyRecipe.ModelTask task = await MeshyApi.Await<MeshyBodyRecipe.ModelTask>(
                MeshyApi.ImageTo3DEndpoint, taskId, null);

            if (task.model_urls == null || string.IsNullOrEmpty(task.model_urls.fbx))
                throw new Exception("FBX 주소가 없다.");
            if (task.texture_urls == null || task.texture_urls.Length == 0 || string.IsNullOrEmpty(task.texture_urls[0].base_color))
                throw new Exception("베이스 컬러 주소가 없다.");

            await MeshyApi.Download(task.model_urls.fbx, RawModelPath(part.id));
            await MeshyApi.Download(task.texture_urls[0].base_color, RawAlbedoPath(part.id));
            lock (done) done.Add(part.id);
            Debug.Log($"[VillagePartBaker] {part.id} 3D 받음 (태스크 {taskId}).");
        }
        catch (Exception e)
        {
            Debug.LogError($"[VillagePartBaker] {part.id} 3D 실패: {e.Message}");
            throw;
        }
    }

    // ---- 앉히기 ---------------------------------------------------------------

    public static void InstallAll()
    {
        foreach (Part part in Parts)
            if (HasRawModel(part.id)) Install(part.id);
        AssetDatabase.SaveAssets();
    }

    /// Library에 받아 둔 FBX와 베이스 컬러를 프로젝트 파츠로 만든다. 크레딧은 들지 않는다.
    /// 같은 경로에 덮어쓰므로 GUID가 그대로라, 이미 조립된 시설 프리팹의 참조는 끊기지 않는다.
    public static void Install(string id)
    {
        if (!TryFind(id, out Part part) || !HasRawModel(id)) return;

        Directory.CreateDirectory(Folder(id));

        WriteTextures(part);
        AssetDatabase.ImportAsset(AlbedoPath(id), ImportAssetOptions.ForceUpdate);
        ConfigureTexture(AlbedoPath(id), part.textureSize);
        if (part.glow > 0f)
        {
            AssetDatabase.ImportAsset(EmissionPath(id), ImportAssetOptions.ForceUpdate);
            ConfigureTexture(EmissionPath(id), part.textureSize / 2);
        }

        Mesh mesh = WriteMesh(part);
        Material material = WriteMaterial(part);
        WritePrefab(part, mesh, material);
        Debug.Log($"[VillagePartBaker] {id} 앉힘 — 삼각형 {mesh.triangles.Length / 3}, 크기 {mesh.bounds.size}.");
    }

    // FBX를 잠깐 프로젝트에 들여 메시만 꺼낸다. 발밑 가운데를 원점으로, 표의 높이로 맞춰 에셋으로 남긴다.
    // Meshy FBX는 정면이 +Z로 들어온다(유니티 규약과 같다 — 출입구 쪽으로 광선을 쏴서 확인). 돌리지 않는다.
    private static Mesh WriteMesh(Part part)
    {
        Directory.CreateDirectory(ImportFolder);
        string temp = $"{ImportFolder}/{part.id}.fbx";
        File.Copy(RawModelPath(part.id), temp, true);
        AssetDatabase.ImportAsset(temp, ImportAssetOptions.ForceSynchronousImport);

        var importer = (ModelImporter)AssetImporter.GetAtPath(temp);
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importBlendShapes = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.SaveAndReimport();

        try
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(temp);
            var combine = new List<CombineInstance>();
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh source = filter.sharedMesh;
                if (source == null) continue;
                for (int sub = 0; sub < source.subMeshCount; sub++)
                    combine.Add(new CombineInstance { mesh = source, subMeshIndex = sub, transform = filter.transform.localToWorldMatrix });
            }
            if (combine.Count == 0) throw new Exception($"{part.id} FBX에 메시가 없다.");

            var merged = new Mesh { indexFormat = IndexFormat.UInt32 };
            // 머티리얼이 한 장이라 서브메시를 하나로 합친다. 드로우콜이 파츠당 하나가 된다.
            merged.CombineMeshes(combine.ToArray(), true, true);

            Mesh mesh = Normalized(part, merged.vertices, merged.normals, merged.uv, merged.triangles);
            UnityEngine.Object.DestroyImmediate(merged);
            MeshUtility.Optimize(mesh);
            // 폴리곤은 Meshy 주문(target_polycount)에서 정한다. 여기서 더 줄이지 않는다 —
            // 유니티 메시 LOD로 절반을 굳혀 봤더니 UV 이음새가 무너져 돌 무늬가 뭉개졌고,
            // 런타임 메시 LOD는 플레이 시작 때의 정적 배칭(StaticBatchingUtility)이 결합하면서 잃는다.
            MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.Medium);

            // 같은 에셋에 내용만 덮어쓴다. 지우고 새로 만들면 GUID가 바뀌어 조립된 프리팹이 메시를 잃는다.
            // 읽기 가능으로 둔다: 마을은 플레이 시작 때 StaticBatchingUtility로 묶는데, 읽을 수 없는 메시는 묶지 못한다.
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(part.id));
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            AssetDatabase.CreateAsset(mesh, MeshPath(part.id));
            return mesh;
        }
        finally
        {
            AssetDatabase.DeleteAsset(temp);
            if (Directory.GetFiles(ImportFolder, "*.fbx").Length == 0) AssetDatabase.DeleteAsset(ImportFolder);
        }
    }

    // 발밑 가운데를 원점으로, 표의 높이(미터)로 맞춘 메시.
    private static Mesh Normalized(Part part, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] triangles)
    {
        var bounds = new Bounds(vertices[0], Vector3.zero);
        foreach (Vector3 v in vertices) bounds.Encapsulate(v);
        float scale = part.height / Mathf.Max(0.0001f, bounds.size.y);
        var pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

        var moved = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++) moved[i] = (vertices[i] - pivot) * scale;

        var mesh = new Mesh
        {
            name = part.id + "_mesh",   // 에셋 파일 이름과 같아야 유니티가 경고하지 않는다
            indexFormat = moved.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.SetVertices(moved);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // 베이스 컬러는 2k로 온다. 멀리서 보는 마을이라 파츠마다 정한 크기로 줄여 JPG로 둔다 —
    // 저장소에 들어가는 파일이라 PNG(장당 수 MB)로 두지 않는다.
    private static void WriteTextures(Part part)
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Texture2D albedo = null, emission = null;
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(RawAlbedoPath(part.id))))
                throw new Exception($"{part.id} 베이스 컬러를 그림으로 읽지 못했다.");

            albedo = Downsample(source, part.textureSize);
            File.WriteAllBytes(AlbedoPath(part.id), albedo.EncodeToJPG(90));

            if (part.glow > 0f)
            {
                emission = GlowMask(albedo, part.textureSize / 2);
                File.WriteAllBytes(EmissionPath(part.id), emission.EncodeToJPG(90));
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            if (albedo != null) UnityEngine.Object.DestroyImmediate(albedo);
            if (emission != null) UnityEngine.Object.DestroyImmediate(emission);
        }
    }

    // 정수배로 떨어지면 칸 평균, 아니면 쌍선형. 텍스처는 2의 제곱으로 오므로 거의 항상 앞쪽이다.
    private static Texture2D Downsample(Texture2D source, int maxSize)
    {
        int w = source.width, h = source.height;
        int size = Mathf.Min(maxSize, Mathf.Max(w, h));
        int tw = w >= h ? size : Mathf.Max(1, size * w / h);
        int th = h >= w ? size : Mathf.Max(1, size * h / w);

        Color32[] src = source.GetPixels32();
        var dst = new Color32[tw * th];

        if (w % tw == 0 && h % th == 0)
        {
            int fx = w / tw, fy = h / th, n = fx * fy;
            for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
            {
                int r = 0, g = 0, b = 0;
                for (int j = 0; j < fy; j++)
                for (int i = 0; i < fx; i++)
                {
                    Color32 c = src[(y * fy + j) * w + x * fx + i];
                    r += c.r; g += c.g; b += c.b;
                }
                dst[y * tw + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
            }
        }
        else
        {
            for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
                dst[y * tw + x] = source.GetPixelBilinear((x + 0.5f) / tw, (y + 0.5f) / th);
        }

        var result = new Texture2D(tw, th, TextureFormat.RGBA32, false);
        result.SetPixels32(dst);
        result.Apply();
        return result;
    }

    // 베이스 컬러에서 불꽃·등불·룬만 남긴다. 밝으면서 채도가 있는 곳이다 —
    // 돌의 밝은 모서리는 회색(채도 없음)이고 이끼·녹은 어두워서 걸러진다.
    private static Texture2D GlowMask(Texture2D albedo, int size)
    {
        Texture2D small = Downsample(albedo, size);
        Color32[] px = small.GetPixels32();
        for (int i = 0; i < px.Length; i++)
        {
            Color c = px[i];
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            float saturation = max > 0.001f ? (max - min) / max : 0f;
            float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.85f, max)) *
                           Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.45f, saturation));
            px[i] = new Color(c.r * weight, c.g * weight, c.b * weight, 1f);
        }
        small.SetPixels32(px);
        small.Apply();
        return small;
    }

    private static void ConfigureTexture(string path, int maxSize)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) return;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = maxSize;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SaveAndReimport();
    }

    // 파츠마다 머티리얼 한 장. Meshy가 FBX에 넣어 주는 재질은 쓰지 않는다 — 게임용 값이 아니다
    // (베이스 컬러를 발광에 또 걸고 metallic이 1로 온다. 캐릭터 몸에서 겪은 것과 같다).
    private static Material WriteMaterial(Part part)
    {
        string path = MaterialPath(part.id);
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = part.id };
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath(part.id)));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Metallic", 0f);
        // 돌·나무·녹슨 철이라 거의 반사하지 않는다. 0이면 빛을 받는 면이 밋밋하게 죽는다.
        material.SetFloat("_Smoothness", 0.12f);
        // 같은 파츠를 여러 번 세우는 곳(집 여덟 채, 공방 아홉 채)이 많다.
        material.enableInstancing = true;

        if (part.glow > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(EmissionPath(part.id)));
            material.SetColor("_EmissionColor", Color.white * part.glow);
        }
        else
        {
            material.DisableKeyword("_EMISSION");
            material.SetTexture("_EmissionMap", null);
            material.SetColor("_EmissionColor", Color.black);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static void WritePrefab(Part part, Mesh mesh, Material material)
    {
        var go = new GameObject(part.id);
        try
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath(part.id));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
