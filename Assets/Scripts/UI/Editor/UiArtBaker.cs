using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// UI 그림을 Meshy text-to-image로 그려 앉히고 UiIconLibrary에 건다.
//
//   프롬프트 ──Meshy text-to-image──▶ 원본 PNG (Library/MeshyIcons — 크레딧이 든 것)
//                                          │ 아이콘: 투명 여백을 잘라 정사각으로, 넓이 평균으로 256x256
//                                          │ 배너:   가운데를 2:1로 잘라 1024x512
//                                          │ 무늬:   가운데를 2:1로 잘라 512x256, 가장자리를 섞어 이음새 없이
//                                          │ 소품:   투명 여백만 잘라 비율 그대로, 긴 변 512
//                                          ▼
//                            Assets/UI/Icons/<묶음>/<이름>.png  ──▶  Resources/UiIconLibrary.asset
//
// 크기를 2의 제곱으로 맞추는 이유: 밉맵이 단계마다 딱 절반으로 떨어지고, 압축 포맷(BC7·ASTC·ETC2)의
// 4x4 블록에 나누어 떨어진다. 모델이 주는 원본은 크기가 제멋대로다.
//
// 원본은 Library에 남긴다. 다듬는 방식(여백·크기)만 바꿀 때 크레딧을 다시 쓰지 않으려고 — 저장소에는
// 들어가지 않으므로 다른 머신에서 다시 다듬으려면 한 번은 새로 구워야 한다.
//
// 아이콘은 모두 같은 화풍 꼬리(IconStyle)를 붙인다. 한 장씩 화풍이 다르면 화면이 조각보가 된다.
public static class UiArtBaker
{
    private const string OutputRoot = "Assets/UI/Icons";
    private const string RawFolder = "Library/MeshyIcons";
    private const string LibraryPath = "Assets/UI/Resources/" + UiIconLibrary.ResourceName + ".asset";

    private const int IconSize = 256;
    private const int BannerWidth = 1024;
    private const int BannerHeight = 512;
    // 잘라 낸 아이콘 둘레에 남기는 여백(한 변에 대한 비율). 0이면 그림이 칸 끝에 닿아 옆 칸과 붙어 보인다.
    private const float Margin = 0.04f;

    private const int TextureWidth = 512;
    private const int TextureHeight = 256;
    // 무늬 가장자리에서 반 칸 밀린 그림과 섞는 띠의 폭(한 변에 대한 비율).
    private const float SeamBand = 0.16f;
    private const int PropMaxSize = 512;

    private const string AiModel = "nano-banana-pro";
    private const int CreditsPerImage = 9;

    // Icon: 정사각 아이콘. Banner: 2:1 한 장짜리 그림. Texture: 이어 붙이는 무늬. Prop: 비율을 지키는 배경 없는 소품.
    private enum Shape { Icon, Banner, Texture, Prop }

    private struct Art
    {
        public string group;   // 출력 폴더(Assets/UI/Icons/<group>)
        public string file;    // 파일 이름이자 원본 이름
        public string field;   // UiIconLibrary의 칸 이름
        public Shape shape;
        public string subject;

        public Art(string group, string file, string field, Shape shape, string subject)
        {
            this.group = group; this.file = file; this.field = field; this.shape = shape; this.subject = subject;
        }
    }

    private const string TopBar = "TopBar";
    private const string Equipment = "Equipment";
    private const string Summon = "Summon";
    private const string Tower = "Tower";

    private static readonly Art[] Arts =
    {
        // ---- 상단바 -----------------------------------------------------------
        new Art(TopBar, "icon_gold", "gold", Shape.Icon,
            "A pile of shiny gold coins seen from a front three-quarter view: three short stacks of thick round " +
            "gold coins of different heights standing side by side, with a few loose coins leaning against the front. " +
            "Each coin has a raised rim and an embossed crown emblem. Warm polished gold with bright specular highlights."),
        new Art(TopBar, "icon_gem", "gem", Shape.Icon,
            "A single large faceted purple gemstone seen from a front three-quarter view: a brilliant-cut amethyst " +
            "with a flat top table and sharp angled facets, deep violet body with bright lavender and magenta facet " +
            "highlights and one small white sparkle."),
        new Art(TopBar, "icon_mail", "mail", Shape.Icon,
            "A closed paper envelope seen from the front, slightly tilted: cream-white parchment paper with a folded " +
            "triangular flap on top, sealed in the middle by a round red wax seal, thin golden trim along the edges."),
        new Art(TopBar, "icon_settings", "settings", Shape.Icon,
            "A single metal cogwheel gear facing the viewer straight on: eight evenly spaced squared teeth around the rim " +
            "and a round hole in the center. Polished silver steel with dark grey shading and a subtle cool blue rim light."),

        // ---- 장비·재료 ----------------------------------------------------------
        new Art(Equipment, "weapon_sword1h", "swordOneHand", Shape.Icon,
            "A single one-handed longsword lying diagonally from bottom-left to top-right: straight polished steel blade, " +
            "simple steel cross guard, brown leather-wrapped grip and a round pommel."),
        new Art(Equipment, "weapon_sword2h", "swordTwoHand", Shape.Icon,
            "A single large two-handed greatsword lying diagonally from bottom-left to top-right: long broad steel blade, " +
            "wide straight cross guard and a long leather-wrapped grip made for two hands."),
        new Art(Equipment, "weapon_bow", "bow", Shape.Icon,
            "A single wooden recurve bow standing diagonally: curved dark wood limbs, leather-wrapped grip in the middle " +
            "and a taut string. No arrow."),
        new Art(Equipment, "weapon_spear", "spear", Shape.Icon,
            "A single spear lying diagonally from bottom-left to top-right: long straight wooden shaft with a leaf-shaped " +
            "steel spearhead and a small leather binding below the head."),
        new Art(Equipment, "weapon_dagger", "dagger", Shape.Icon,
            "A single short dagger lying diagonally from bottom-left to top-right: short double-edged steel blade, " +
            "small curved cross guard and a dark leather grip."),
        new Art(Equipment, "weapon_axe", "axe", Shape.Icon,
            "A single one-handed battle axe lying diagonally from bottom-left to top-right: sturdy wooden handle " +
            "with one crescent-shaped steel axe head."),
        new Art(Equipment, "weapon_blunt", "blunt", Shape.Icon,
            "A single flanged mace lying diagonally from bottom-left to top-right: wooden handle with a leather grip " +
            "and a heavy steel head with vertical flanges."),
        new Art(Equipment, "weapon_polearm", "polearm", Shape.Icon,
            "A single halberd lying diagonally from bottom-left to top-right: long wooden pole topped with a steel axe blade, " +
            "a spear point and a back hook."),
        new Art(Equipment, "weapon_shield", "shield", Shape.Icon,
            "A single round knight shield seen from the front: wooden planks with a steel rim and a round steel boss in the center."),
        new Art(Equipment, "slot_accessory", "accessory", Shape.Icon,
            "A single golden amulet necklace seen from the front: a round gold pendant set with a small blue gemstone, " +
            "hanging from a short gold chain."),
        new Art(Equipment, "material_metal", "metal", Shape.Icon,
            "A small stack of three polished steel ingots seen from a front three-quarter view, cool silver-grey metal " +
            "with bright highlights."),
        new Art(Equipment, "material_wood", "wood", Shape.Icon,
            "A small bundle of three sturdy oak logs tied together with a rope, seen from a front three-quarter view, " +
            "warm brown wood with visible grain and bark."),
        new Art(Equipment, "material_leather", "leather", Shape.Icon,
            "A folded piece of tanned brown leather hide tied with a thin cord, seen from a front three-quarter view, " +
            "soft warm leather texture."),

        // ---- 소환 배너 ----------------------------------------------------------
        new Art(Summon, "banner_normal", "bannerNormal", Shape.Banner,
            "Wide fantasy illustration for a mobile RPG summon banner: an ancient stone summoning circle glowing with soft " +
            "blue runes inside a quiet moonlit temple hall, drifting light particles, faint silhouettes of adventurers " +
            "forming inside the light. Painted semi-realistic style, cinematic lighting, calm cool blue and teal colors, " +
            "the right half holds the circle and the left half is darker open space."),
        new Art(Summon, "banner_premium", "bannerPremium", Shape.Banner,
            "Wide fantasy illustration for a mobile RPG premium summon banner: a radiant golden portal bursting with light " +
            "at the top of a grand marble staircase among clouds, gold and violet energy swirling, a heroic silhouette " +
            "stepping out of the light. Painted semi-realistic style, dramatic cinematic lighting, rich gold and purple colors, " +
            "the right half holds the portal and the left half is darker open space."),

        // ---- 층 선택의 탑 ---------------------------------------------------------
        new Art(Tower, "tower_wall", "towerWall", Shape.Texture,
            "Seamless tileable texture of a medieval fantasy tower wall: weathered warm grey stone blocks laid in regular " +
            "horizontal courses with thin dark mortar lines, each block slightly different in tone, subtle chips and a little moss. " +
            "Flat front orthographic view with no perspective, even soft light from the top-left, the blocks fill the whole " +
            "frame edge to edge. No windows, no doors, no objects."),
        new Art(Tower, "tower_window", "towerWindow", Shape.Prop,
            "A single tall arched window of a stone tower seen straight from the front: a carved light grey stone frame " +
            "with a rounded arch top and a small stone sill, leaded glass panes glowing with warm golden candlelight from inside."),
        new Art(Tower, "tower_roof", "towerRoof", Shape.Prop,
            "The pointed cone-shaped roof of a tall round fantasy stone tower seen straight from the front: overlapping dark " +
            "blue slate tiles, a ring of stone battlements along its wide base, a thin golden spire on the tip with a small " +
            "red pennant flag. Only the roof itself, its bottom edge is a straight horizontal line."),
        new Art(Tower, "tower_door", "towerDoor", Shape.Prop,
            "The grand entrance of a stone tower seen straight from the front: a tall arched double door of dark oak planks " +
            "with black iron bands and ring handles, set in a carved stone archway, three wide stone steps in front of it."),
        new Art(Tower, "sky_cloud", "cloud", Shape.Prop,
            "A single soft fluffy white cumulus cloud, wider than tall, gently lit from above with light grey-blue shading " +
            "underneath, on a plain solid dark navy background."),
    };

    private const string IconStyle =
        " Game UI item icon for a fantasy mobile RPG, painted semi-realistic style with crisp clean edges, " +
        "rich saturated colors, strong highlights and a bold silhouette that stays readable at small sizes. " +
        "A single object centered with generous empty space around it, the whole object fully inside the frame. " +
        "Plain background. No text, no numbers, no letters, no border, no frame, no badge, no cast shadow, no glow halo.";

    private const string BannerStyle = " No text, no letters, no logo, no UI, no frame, no watermark.";

    private const string TextureStyle =
        " Hand-painted texture for a fantasy mobile RPG, semi-realistic, rich but muted colors. " +
        "No text, no letters, no border, no frame, no vignette, no watermark.";

    private const string PropStyle =
        " 2D game art for a fantasy mobile RPG, painted semi-realistic style with crisp clean edges and rich colors, " +
        "front orthographic view with no perspective. A single object centered, the whole object fully inside the frame. " +
        "Plain background. No text, no numbers, no letters, no border, no frame, no cast shadow.";

    private static string StyleOf(Shape shape) =>
        shape == Shape.Banner ? BannerStyle : shape == Shape.Texture ? TextureStyle : shape == Shape.Prop ? PropStyle : IconStyle;

    // ---- 메뉴 ---------------------------------------------------------------

    [MenuItem("PickMeUp/UI/아이콘 굽기 (Meshy) — 상단바", priority = 40)]
    private static void BakeTopBarMenu() => BakeWithDialog(TopBar);

    [MenuItem("PickMeUp/UI/아이콘 굽기 (Meshy) — 장비·재료", priority = 41)]
    private static void BakeEquipmentMenu() => BakeWithDialog(Equipment);

    [MenuItem("PickMeUp/UI/아이콘 굽기 (Meshy) — 소환 배너", priority = 42)]
    private static void BakeSummonMenu() => BakeWithDialog(Summon);

    [MenuItem("PickMeUp/UI/아이콘 굽기 (Meshy) — 층 선택 탑", priority = 43)]
    private static void BakeTowerMenu() => BakeWithDialog(Tower);

    [MenuItem("PickMeUp/UI/아이콘 다시 다듬기 (크레딧 없이)", priority = 60)]
    private static void RefineMenu() => RefineAll();

    [MenuItem("PickMeUp/UI/아이콘 다시 다듬기 (크레딧 없이)", true)]
    private static bool RefineMenuValidate() => Directory.Exists(RawFolder);

    [MenuItem("PickMeUp/UI/아이콘 라이브러리 다시 걸기", priority = 61)]
    private static void FillLibraryMenu() => FillLibrary();

    private static async void BakeWithDialog(string group)
    {
        int count = Count(group);
        bool go = EditorUtility.DisplayDialog(
            "UI 아이콘 굽기",
            $"'{group}' 묶음 {count}장을 새로 그린다.\n대략 {count * CreditsPerImage} 크레딧. 있던 그림은 덮어쓴다(참조는 그대로 이어진다).",
            "굽는다", "그만둔다");
        if (!go) return;

        await Bake(group);
    }

    private static int Count(string group)
    {
        int n = 0;
        foreach (Art art in Arts) if (group == null || art.group == group) n++;
        return n;
    }

    // ---- 굽기 ---------------------------------------------------------------

    /// 한 묶음(group이 null이면 전부)을 한꺼번에 주문한다. 한 장이 실패해도 나머지는 앉힌다.
    /// 확인 창이 없으니 코드에서 부를 때 쓴다.
    public static async Task Bake(string group)
    {
        int balance = await MeshyApi.Balance();
        int needed = Count(group) * CreditsPerImage;
        if (balance >= 0 && balance < needed)
        {
            Debug.LogError($"[UiArtBaker] 크레딧이 모자란다 — 남은 {balance}, 필요 {needed}.");
            return;
        }

        // 그리는 동안의 재컴파일이 await를 삼키지 않도록 잠근다(MeshyModelPipeline과 같은 이유).
        EditorApplication.LockReloadAssemblies();
        try
        {
            var jobs = new List<Task>();
            foreach (Art art in Arts)
                if (group == null || art.group == group) jobs.Add(BakeOne(art));
            await Task.WhenAll(jobs);
        }
        catch (Exception)
        {
            // 한 장씩의 실패는 BakeOne이 이미 남겼다. 여기서는 나머지를 앉히는 것까지만.
        }
        finally
        {
            EditorApplication.UnlockReloadAssemblies();
            AssetDatabase.Refresh();
            FillLibrary();
        }
    }

    private static async Task BakeOne(Art art)
    {
        try
        {
            bool banner = art.shape == Shape.Banner;
            // 배너와 무늬는 칸을 꽉 채우는 그림이라 배경을 지우지 않는다.
            bool keepBackground = banner || art.shape == Shape.Texture;
            string body =
                "{\"ai_model\":" + MeshyBodyRecipe.EscapeJson(AiModel) +
                ",\"prompt\":" + MeshyBodyRecipe.EscapeJson(art.subject + StyleOf(art.shape)) +
                ",\"aspect_ratio\":\"" + (banner ? "16:9" : "1:1") + "\"" +
                ",\"remove_background\":" + (keepBackground ? "false" : "true") + "}";

            string taskId = await MeshyApi.CreateImage(body);
            MeshyBodyRecipe.ImageTask task = await MeshyApi.Await<MeshyBodyRecipe.ImageTask>(
                MeshyBodyRecipe.SheetEndpoint, taskId, null);

            if (task.image_urls == null || task.image_urls.Length == 0)
                throw new Exception("그림이 비어서 돌아왔다.");

            string raw = RawPathFor(art.file);
            await MeshyApi.Download(task.image_urls[0], raw);
            Refine(art, raw);
            Debug.Log($"[UiArtBaker] {art.file} 완료 (태스크 {taskId}).");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UiArtBaker] {art.file} 실패: {e.Message}");
            throw;
        }
    }

    /// Library에 남은 원본으로 다듬기만 다시 한다.
    public static void RefineAll()
    {
        foreach (Art art in Arts)
        {
            string raw = RawPathFor(art.file);
            if (!File.Exists(raw)) continue;
            Refine(art, raw);
        }
        AssetDatabase.Refresh();
        FillLibrary();
    }

    private static string RawPathFor(string file) => Path.Combine(RawFolder, file + ".png");
    private static string OutputPathFor(Art art) => $"{OutputRoot}/{art.group}/{art.file}.png";

    // ---- 라이브러리 ---------------------------------------------------------

    /// 구워 둔 그림을 UiIconLibrary의 칸에 건다. 에셋이 없으면 만든다.
    public static void FillLibrary()
    {
        var library = AssetDatabase.LoadAssetAtPath<UiIconLibrary>(LibraryPath);
        if (library == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
            library = ScriptableObject.CreateInstance<UiIconLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }

        var so = new SerializedObject(library);
        foreach (Art art in Arts)
        {
            SerializedProperty property = so.FindProperty(art.field);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(OutputPathFor(art));
            if (property != null && sprite != null) property.objectReferenceValue = sprite;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssetIfDirty(library);
    }

    // ---- 다듬기 -------------------------------------------------------------

    private static void Refine(Art art, string rawPath)
    {
        byte[] raw = File.ReadAllBytes(rawPath);
        byte[] png;
        switch (art.shape)
        {
            case Shape.Banner: png = FitBanner(raw, art.file); break;
            case Shape.Texture: png = FitTexture(raw, art.file); break;
            case Shape.Prop: png = TrimProp(raw, art.file); break;
            default: png = TrimIcon(raw, art.file); break;
        }

        string path = OutputPathFor(art);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, png);

        bool opaque = art.shape == Shape.Banner || art.shape == Shape.Texture;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        // 투명한 칸의 색을 이웃한 색으로 번지게 한다. 안 하면 가장자리에 검은 테가 생긴다.
        importer.alphaIsTransparency = !opaque;
        // 256을 80~130px로 줄여 그리므로 밉맵이 없으면 가장자리가 반짝이며 깨진다.
        importer.mipmapEnabled = true;
        // 무늬는 Image(Tiled)가 한 장을 되풀이해 깐다. Repeat이어야 사각형 하나로 그린다(아니면 칸마다 사각형을 만든다).
        importer.wrapMode = art.shape == Shape.Texture ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = art.shape == Shape.Banner ? BannerWidth
            : art.shape == Shape.Texture ? TextureWidth
            : art.shape == Shape.Prop ? PropMaxSize
            : IconSize;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    // 무늬: 가운데를 2:1로 잘라 512x256으로 줄이고, 이어 붙였을 때 이음새가 없게 가장자리를 섞는다.
    // 2:1인 이유 — 탑의 한 층(줄 높이)에 무늬 한 장이 딱 들어가야 층과 층 사이에서 무늬가 끊기지 않는다.
    private static byte[] FitTexture(byte[] raw, string label)
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Texture2D result = null;
        try
        {
            if (!source.LoadImage(raw)) throw new Exception($"{label} 원본을 그림으로 읽지 못했다.");

            int w = source.width, h = source.height;
            float targetAspect = (float)TextureWidth / TextureHeight;
            float cropW = w, cropH = w / targetAspect;
            if (cropH > h) { cropH = h; cropW = h * targetAspect; }

            Color32[] pixels = AreaResample(source.GetPixels32(), w, h, (w - cropW) * 0.5f, (h - cropH) * 0.5f,
                cropW, cropH, TextureWidth, TextureHeight);
            pixels = MakeSeamless(pixels, TextureWidth, TextureHeight);

            result = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, false);
            result.SetPixels32(pixels);
            result.Apply();
            return result.EncodeToPNG();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            if (result != null) UnityEngine.Object.DestroyImmediate(result);
        }
    }

    // 가로로 한 번, 세로로 한 번 — 가장자리 띠에서 반 칸 밀린 자기 자신과 섞는다.
    // 밀린 그림은 원래 그림의 한가운데가 가장자리에 오므로 끝과 끝이 이어진다. 대신 밀린 그림의 이음새가
    // 한가운데 생기는데, 한가운데는 원래 그림을 쓰므로 가려진다. 한 번에 두 축을 밀면 가장자리 띠 한가운데에
    // 밀린 그림의 이음새가 드러나서 축마다 따로 한다.
    private static Color32[] MakeSeamless(Color32[] px, int w, int h)
    {
        var across = new Color32[px.Length];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float t = SeamWeight(x, w);
                across[y * w + x] = Color32.Lerp(px[y * w + (x + w / 2) % w], px[y * w + x], t);
            }
        }

        var result = new Color32[px.Length];
        for (int y = 0; y < h; y++)
        {
            float t = SeamWeight(y, h);
            int shifted = (y + h / 2) % h;
            for (int x = 0; x < w; x++)
                result[y * w + x] = Color32.Lerp(across[shifted * w + x], across[y * w + x], t);
        }
        return result;
    }

    // 가장자리에서 0, SeamBand 안쪽부터 1. 부드럽게(smoothstep) 넘어간다.
    private static float SeamWeight(int i, int size)
    {
        float edge = Mathf.Min(i + 0.5f, size - i - 0.5f) / (size * SeamBand);
        float t = Mathf.Clamp01(edge);
        return t * t * (3f - 2f * t);
    }

    // 소품: 투명 여백만 잘라 내용물 그대로의 비율로, 긴 변이 PropMaxSize를 넘지 않게 줄인다.
    // 아이콘처럼 정사각 여백을 두지 않는 이유 — 지붕의 아래 변, 문의 계단 끝을 탑 벽에 딱 맞춰 놓아야 한다.
    private static byte[] TrimProp(byte[] raw, string label)
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Texture2D result = null;
        try
        {
            if (!source.LoadImage(raw)) throw new Exception($"{label} 원본을 그림으로 읽지 못했다.");

            int w = source.width, h = source.height;
            Color32[] pixels = source.GetPixels32();
            if (!HasTransparentBorder(pixels, w, h))
            {
                Debug.LogWarning($"[UiArtBaker] {label} 배경이 지워지지 않고 왔다 — 가장자리 색을 직접 뺀다.");
                KeyOutBorderColor(pixels, w, h);
            }

            RectInt bounds = OpaqueBounds(pixels, w, h);
            if (bounds.width <= 0) throw new Exception($"{label} 그림이 통째로 투명하다.");

            // 압축 블록(4x4)에 나누어 떨어지게 4의 배수로.
            float scale = Mathf.Min(1f, (float)PropMaxSize / Mathf.Max(bounds.width, bounds.height));
            int outW = Mathf.Max(4, Mathf.RoundToInt(bounds.width * scale / 4f) * 4);
            int outH = Mathf.Max(4, Mathf.RoundToInt(bounds.height * scale / 4f) * 4);

            result = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
            result.SetPixels32(AreaResample(pixels, w, h, bounds.x, bounds.y, bounds.width, bounds.height, outW, outH));
            result.Apply();
            return result.EncodeToPNG();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            if (result != null) UnityEngine.Object.DestroyImmediate(result);
        }
    }

    // 배너: 가운데를 2:1로 잘라 1024x512로 줄인다. 배경을 지우지 않은 한 장짜리 그림이다.
    private static byte[] FitBanner(byte[] raw, string label)
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Texture2D result = null;
        try
        {
            if (!source.LoadImage(raw)) throw new Exception($"{label} 원본을 그림으로 읽지 못했다.");

            int w = source.width, h = source.height;
            float targetAspect = (float)BannerWidth / BannerHeight;
            float cropW = w, cropH = w / targetAspect;
            if (cropH > h) { cropH = h; cropW = h * targetAspect; }

            float originX = (w - cropW) * 0.5f;
            float originY = (h - cropH) * 0.5f;

            result = new Texture2D(BannerWidth, BannerHeight, TextureFormat.RGBA32, false);
            result.SetPixels32(AreaResample(source.GetPixels32(), w, h, originX, originY, cropW, cropH, BannerWidth, BannerHeight));
            result.Apply();
            return result.EncodeToPNG();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            if (result != null) UnityEngine.Object.DestroyImmediate(result);
        }
    }

    // 아이콘: 투명 여백을 잘라 내용물을 가운데 둔 정사각으로 만들고 IconSize로 줄인다.
    private static byte[] TrimIcon(byte[] raw, string label)
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Texture2D result = null;
        try
        {
            if (!source.LoadImage(raw)) throw new Exception($"{label} 원본을 그림으로 읽지 못했다.");

            int w = source.width, h = source.height;
            Color32[] pixels = source.GetPixels32();

            // 배경 제거를 요청했지만 불투명하게 돌아오는 경우가 있다. 그때는 가장자리 색을 직접 뺀다.
            if (!HasTransparentBorder(pixels, w, h))
            {
                Debug.LogWarning($"[UiArtBaker] {label} 배경이 지워지지 않고 왔다 — 가장자리 색을 직접 뺀다.");
                KeyOutBorderColor(pixels, w, h);
            }

            RectInt bounds = OpaqueBounds(pixels, w, h);
            if (bounds.width <= 0) throw new Exception($"{label} 그림이 통째로 투명하다.");

            float side = Mathf.Max(bounds.width, bounds.height) / (1f - 2f * Margin);
            float originX = bounds.x + bounds.width * 0.5f - side * 0.5f;
            float originY = bounds.y + bounds.height * 0.5f - side * 0.5f;

            result = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            result.SetPixels32(AreaResample(pixels, w, h, originX, originY, side, side, IconSize, IconSize));
            result.Apply();
            return result.EncodeToPNG();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            if (result != null) UnityEngine.Object.DestroyImmediate(result);
        }
    }

    private const byte OpaqueAlpha = 10;

    private static bool HasTransparentBorder(Color32[] px, int w, int h)
    {
        int clear = 0, total = 0;
        for (int x = 0; x < w; x++)
        {
            total += 2;
            if (px[x].a < 16) clear++;
            if (px[(h - 1) * w + x].a < 16) clear++;
        }
        for (int y = 0; y < h; y++)
        {
            total += 2;
            if (px[y * w].a < 16) clear++;
            if (px[y * w + w - 1].a < 16) clear++;
        }
        return clear * 2 > total;
    }

    // 네 모서리의 평균색과 가까운 색을 가장자리부터 번져 가며 지운다. 가장자리에 닿지 않은 같은 색
    // (봉투 안쪽의 흰 종이 등)은 남는다.
    private static void KeyOutBorderColor(Color32[] px, int w, int h)
    {
        Color32 a = px[0], b = px[w - 1], c = px[(h - 1) * w], d = px[h * w - 1];
        int kr = (a.r + b.r + c.r + d.r) / 4, kg = (a.g + b.g + c.g + d.g) / 4, kb = (a.b + b.b + c.b + d.b) / 4;
        const int Tolerance = 48;

        var visited = new bool[w * h];
        var queue = new Queue<int>();
        for (int x = 0; x < w; x++) { queue.Enqueue(x); queue.Enqueue((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { queue.Enqueue(y * w); queue.Enqueue(y * w + w - 1); }

        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            if (visited[i]) continue;
            visited[i] = true;

            Color32 p = px[i];
            int dist = Mathf.Abs(p.r - kr) + Mathf.Abs(p.g - kg) + Mathf.Abs(p.b - kb);
            if (dist > Tolerance) continue;

            px[i].a = 0;
            int x = i % w, y = i / w;
            if (x > 0) queue.Enqueue(i - 1);
            if (x < w - 1) queue.Enqueue(i + 1);
            if (y > 0) queue.Enqueue(i - w);
            if (y < h - 1) queue.Enqueue(i + w);
        }
    }

    private static RectInt OpaqueBounds(Color32[] px, int w, int h)
    {
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (px[y * w + x].a <= OpaqueAlpha) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        return maxX < 0 ? new RectInt(0, 0, 0, 0) : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    // 한 칸이 덮는 원본 넓이만큼 평균한다(4배 가까이 줄일 때 쌍선형으로 찍으면 테두리가 계단이 된다).
    // 색은 알파로 가중해 섞는다 — 그러지 않으면 투명한 칸의 색(대개 검정)이 가장자리로 스며 테가 생긴다.
    // 원본 바깥은 투명한 칸으로 친다.
    private static Color32[] AreaResample(Color32[] src, int w, int h, float originX, float originY,
        float cropW, float cropH, int outW, int outH)
    {
        var dst = new Color32[outW * outH];
        float scaleX = cropW / outW;
        float scaleY = cropH / outH;

        for (int dy = 0; dy < outH; dy++)
        {
            float y0 = originY + dy * scaleY, y1 = y0 + scaleY;
            int sy0 = Mathf.FloorToInt(y0), sy1 = Mathf.CeilToInt(y1);

            for (int dx = 0; dx < outW; dx++)
            {
                float x0 = originX + dx * scaleX, x1 = x0 + scaleX;
                int sx0 = Mathf.FloorToInt(x0), sx1 = Mathf.CeilToInt(x1);

                double r = 0, g = 0, b = 0, alpha = 0, area = 0;
                for (int sy = sy0; sy < sy1; sy++)
                {
                    float wy = Mathf.Min(y1, sy + 1) - Mathf.Max(y0, sy);
                    if (wy <= 0f) continue;

                    for (int sx = sx0; sx < sx1; sx++)
                    {
                        float wx = Mathf.Min(x1, sx + 1) - Mathf.Max(x0, sx);
                        if (wx <= 0f) continue;

                        float weight = wx * wy;
                        area += weight;
                        if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;

                        Color32 c = src[sy * w + sx];
                        double wa = c.a / 255.0 * weight;
                        r += c.r * wa;
                        g += c.g * wa;
                        b += c.b * wa;
                        alpha += wa;
                    }
                }

                dst[dy * outW + dx] = alpha <= 0.0
                    ? new Color32(0, 0, 0, 0)
                    : new Color32(
                        (byte)Math.Round(r / alpha),
                        (byte)Math.Round(g / alpha),
                        (byte)Math.Round(b / alpha),
                        (byte)Math.Round(alpha / area * 255.0));
            }
        }
        return dst;
    }
}
