// Meshy에게 "몸을 만들어 달라"고 말하는 내용 한 벌.
//
// 몸을 굽는 입구가 둘이라서 여기에 모아 둔다 — 기존 캐릭터를 에디터 메뉴로 굽는 입구
// (MeshyModelPipeline)와, 소환 직후에 뒤에서 굽는 입구(MeshyBodyService). 두 입구 모두 MeshyBodyBaker로
// 같은 주문을 넣고 결과물(GLB 한 장)도 같다. 프롬프트가 갈리면 같은 직업인데도 어느 입구로 구웠느냐에
// 따라 다른 모습이 나온다.
//
// 여기 있는 것은 "무엇을 주문하는가"뿐이다. Meshy와 어떻게 말하는지(주소, 응답 모양, 상태 판정)는
// MeshyProtocol이, 실제로 보내고 기다리는 일은 MeshyClient가 맡는다.
public static class MeshyBodyRecipe
{
    public const string SheetEndpoint = MeshyProtocol.TextToImage;
    public const string MeshEndpoint = MeshyProtocol.MultiImageTo3D;
    public const string RigEndpoint = MeshyProtocol.Rigging;

    private const string SheetModel = "nano-banana-pro";
    private const string MeshModel = "meshy-7";

    // 리깅에 넘기는 키. Y Bot(믹사모 기본 체형)과 같은 눈높이라야 기존 애니메이션이 어색하지 않고,
    // 카메라와 내비메시 에이전트 높이도 그대로 쓸 수 있다.
    public const float CharacterHeightMeters = 1.7f;

    // 리깅은 30만 면을 넘는 메시를 받지 않는다. 게임에 쓸 것이므로 훨씬 아래로 잡는다.
    public const int TargetPolycount = 30000;

    // 시트 그림과 메시에 같은 자세를 건다. 둘이 다르면 메시 생성기가 그림을 자세에 맞춰
    // 억지로 비틀어 팔다리가 뭉개진다. 이유는 SheetPrompt 위 주석 참조.
    public const string PoseMode = "t-pose";

    // 굽기 한 번에 드는 크레딧. 실측값(시트 9 + 메시 30 + 리깅 5 = 44)에 여유를 붙였다.
    public const int CreditsPerCharacter = 60;

    // ── 초상화를 읽는 주문 ───────────────────────────────────────────────

    // 초상화는 허리 위만 있다. 하체는 존재하지 않으므로 읽어 올 수 없고 지어내는 수밖에 없는데,
    // 아무렇게나 지어내면 상의는 중세 가죽인데 하의는 청바지가 된다. "위와 같은 시대·재질로"를 붙인다.
    //
    // 무기와 무기를 담는 것(칼집·화살통·단검 띠)은 그림에 보여도 적지 않게 한다. 전신 시트 쪽에서
    // "No weapon"이라고 해도, 이 설명에 "허리에 칼집을 찼다"가 들어 있으면 그쪽이 이긴다 — 칼집이 메시에
    // 눌러붙어 나오고, 전투에서는 손에 든 무기와 따로 허리에 하나가 더 달린다(아엘리아·엘리온이 그랬다).
    // 무기는 손뼈 소켓에 따로 걸리고(WeaponEquipper), 무엇을 드는지는 그림이 아니라 장비가 정한다.
    public const string AppearanceInstruction =
        "You are writing the input prompt for a 3D character generator. " +
        "Look at this character illustration (it shows only the upper body). " +
        "Describe THE SAME character as a full-body character sheet, in one English paragraph under 130 words. " +
        "Cover: apparent gender, age and build; hair color, length and style; face, skin tone and eye color; " +
        "headwear; upper garment with exact colors and materials; armor pieces; " +
        "accessories such as belts, straps, pauldrons, gloves. " +
        "Then invent a lower body that matches the upper body's style and era: trousers or skirt, belt, boots, " +
        "with concrete colors and materials. " +
        "The character is unarmed: leave out every weapon and everything made to carry one, even if the illustration " +
        "clearly shows it — no sword, dagger, knife, axe, mace, spear, bow, arrows, staff, wand or shield, " +
        "and no scabbard, sheath, quiver, holster or weapon harness. Do not invent any either. " +
        "Describe belts and straps as plain, with nothing hanging from them, and the hands as empty. " +
        "Do NOT mention background, pose, lighting, camera, framing, or art style. " +
        "Output only the description paragraph.";

    // ── 3D 생성기에 넘길 그림의 주문서 ──────────────────────────────────

    // 초상화 프롬프트와 노리는 것이 다르다. 카드 그림은 멋있어야 하지만 이 그림은 읽히기만 하면
    // 된다 — 그림자와 연출은 그대로 메시에 눌러붙기 때문에 오히려 방해가 된다.
    // 그래서 조명은 평평하게, 배경은 없이, 팔다리는 몸통에서 떼어 놓게 시킨다.
    //
    // 자세는 T포즈다. 전투 동작은 전부 휴머노이드라 "T포즈에서 근육을 얼마나 틀었는가"로 저장돼
    // 있어서, 몸이 T포즈로 태어나야 아바타의 기준 자세와 모델이 처음부터 일치한다.
    // 예전에는 A포즈로 받고 아바타를 세울 때 팔을 펴서 맞췄는데(CharacterModelRig.EnforceTPose),
    // 펴는 각도가 어긋나면 모든 팔 동작이 그만큼 틀어진다 — 한 번은 50도 접힌 채로 싸웠다.
    // 겨드랑이와 옆구리가 떨어져 있어 메시가 붙어 나오지 않고 리깅도 팔을 가려내기 쉽다.
    //
    // 손에는 아무것도 들리지 않게 하고, 허리와 등에도 아무것도 달리지 않게 한다. 무기는 손뼈 소켓에
    // 따로 걸리므로(WeaponEquipper), 메시에 칼이나 칼집이 붙어 나오면 칼을 두 자루 든 캐릭터가 된다.
    // 여기서는 물건 이름(칼집 등)을 늘어놓지 않는다 — 그림 생성기는 "없다"고 적은 낱말을 오히려 그려 넣기도 한다.
    // 무엇을 빼는지 낱낱이 적는 일은 외형 설명을 쓰는 쪽(AppearanceInstruction)이 맡는다.
    //
    // 손은 쥔 주먹으로 시킨다. 리깅에 손가락 뼈가 들어오지 않아서(Meshy의 자동 리깅이 그렇다)
    // 메시에 구워진 손 모양이 그 캐릭터의 영구적인 손 모양이 된다 — 편 손으로 구우면 칼을
    // 손바닥에 얹고 다니는 것처럼 보인다. 다만 잘 듣지는 않는다. pose_mode가 정해 둔 자세 쪽이
    // 세서 반쯤 펴진 손으로 나오는 경우가 많고, 멀리서 보는 전투 화면에서는 티가 나지 않는다.
    public static string SheetPrompt(string appearance)
    {
        return "Full body character sheet of a single human character, head to toe. " +
               (appearance ?? "").TrimEnd() + " " +
               "Both hands are clenched into tight fists, fingers fully curled into the palm, " +
               "thumb folded over the fingers, as if gripping an invisible handle. " +
               "T-pose: standing upright and symmetrical, both arms stretched straight out to the sides " +
               "at shoulder height, perfectly horizontal, elbows straight, palms facing down, " +
               "both legs straight and slightly apart, both feet flat on the ground and fully visible. " +
               "No weapon, no props, nothing held, nothing hanging from the belt, nothing strapped to the back. " +
               "Clean even neutral lighting, no cast shadows, no background scenery. " +
               "Game-ready semi-realistic fantasy character, adult proportions, not chibi, not cute. " +
               "Single character only, no text, no logo, no watermark.";
    }

    // 초상화도 키도 없을 때의 최소한. 같은 직업이면 다 같이 생기게 되지만,
    // 적어도 하체와 직업 복장은 갖춘 사람이 나온다.
    public static string AppearanceFromJob(JobType job)
    {
        return $"An adult human {EnglishJob(job)}, unarmed, in practical fantasy clothing, " +
               "with a full head of hair, a plain tunic or jacket, a plain leather belt, " +
               "sturdy trousers and worn leather boots.";
    }

    public static string EnglishJob(JobType job)
    {
        switch (job)
        {
            case JobType.Melee:      return "swordsman";
            case JobType.Mage:       return "mage";
            case JobType.Archer:     return "archer";
            case JobType.Assassin:   return "assassin";
            case JobType.Tank:       return "armored knight";
            case JobType.Support:    return "priest healer";
            case JobType.Lancer:     return "spearman";
            case JobType.Carpenter:  return "carpenter";
            case JobType.Cook:       return "cook";
            case JobType.Blacksmith: return "blacksmith";
            case JobType.Tanner:     return "leatherworker";
            default:                 return job.ToString().ToLowerInvariant();
        }
    }

    // ── 요청 본문 ────────────────────────────────────────────────────────

    public static string SheetBody(string prompt) =>
        MeshyRequests.TextToImage(SheetModel, prompt, poseMode: PoseMode, generateMultiView: true, removeBackground: true);

    // 여러 장을 받는 쪽으로 보낸다. 한 장짜리 엔드포인트는 다시점 그림 태스크를 입력으로 받지 않고,
    // 정면 한 장만 넘기면 뒷면을 지어내게 되어 등이 뭉개진다.
    //
    // origin_at=bottom이 중요하다 — 원점이 발밑에 서 있어야 Unity에서 바닥에 세울 때
    // 캐릭터가 땅에 박히거나 떠 있지 않는다.
    public static string MeshBody(string sheetTaskId) =>
        new JsonBody()
            .Add("input_task_id", sheetTaskId)
            .Add("ai_model", MeshModel)
            .Add("pose_mode", PoseMode)
            .Add("should_texture", true)
            .Add("texture_resolution", "2k")
            .Add("enable_pbr", false)
            .Add("should_remesh", true)
            .Add("topology", "triangle")
            .Add("target_polycount", TargetPolycount)
            .AddStrings("target_formats", "glb", "fbx")
            .Add("auto_size", true)
            .Add("origin_at", "bottom")
            .ToString();

    public static string RigBody(string meshTaskId) =>
        new JsonBody()
            .Add("input_task_id", meshTaskId)
            .Add("height_meters", CharacterHeightMeters)
            .ToString();
}
