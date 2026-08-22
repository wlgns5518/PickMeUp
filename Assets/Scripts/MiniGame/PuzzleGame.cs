using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum PuzzleDifficulty
{
    Easy   = 4,
    Normal = 7,
    Hard   = 10,
    Hell   = 15,
}

// 장비 그림 맞추기 미니게임.
//
// 그림을 격자로 자른 뒤 칸을 셋으로 나눈다. 그림이 한 픽셀도 없는 칸은 아예 만들지 않고,
// 그림이 있긴 하지만 너무 옅은 칸은 판의 제자리에 처음부터 맞춰 둔다. 나머지 칸만 판에
// 빈칸을 깔아 두고 조각을 바깥에 흩는다. 그 빈칸들이 모여 그리는 윤곽이 곧 맞춰야 할 형태다.
//
// 옅은 칸을 버리지 않는 이유는 다 맞췄을 때 그림에 구멍이 남지 않게 하기 위해서다.
// 그렇다고 플레이어에게 맡기면 거의 투명한 조각을 화면에서 찾아 헤매게 된다.
// 반대로 완전히 빈 칸은 만들 이유가 없다 — 지켜야 할 그림이 애초에 없으니 구멍도 나지 않는다.
// 대각선으로 누운 검처럼 자기 바운딩 박스를 얇게 가로지르는 그림에서는 이쪽이 다수다.
//
// 조각끼리 이어 붙이는 방식이 아니라 정해진 칸에 맞추는 방식이라 조각 하나하나가 독립적으로
// 판정된다 — 어디까지 맞췄는지가 언제나 분명하고, 한 조각을 잘못 놓아도 이미 맞춘 것들이
// 딸려 움직이지 않는다.
public class PuzzleGame : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("맞출 장비 이미지 (Read/Write Enabled 필요)")]
    [SerializeField] private Sprite sourceSprite;
    [Tooltip("조각이 배치되고 드래그되는 영역")]
    [SerializeField] private RectTransform playArea;
    [Tooltip("게임 전체 UI 루트 (검은 배경 포함). 시작 시 활성화, 종료 시 비활성화.")]
    [SerializeField] private GameObject puzzleRoot;

    [Header("Difficulty / Time")]
    [SerializeField] private PuzzleDifficulty difficulty = PuzzleDifficulty.Easy;
    [SerializeField] private float timeLimit = 180f;

    [Header("Layout")]
    [Tooltip("Hell(15x15)에서의 조각 한 변 픽셀 크기 (기준값). 낮은 난이도는 어셈블 크기 동일 유지를 위해 자동 확대.")]
    [SerializeField] private float hellPieceSize = 40f;
    [Tooltip("이 거리 이내로 제자리에 놓으면 판에 붙는다. 아래 비율과 비교해 더 큰 쪽이 쓰인다.")]
    [SerializeField] private float pieceSnapDistance = 25f;
    [Tooltip("조각 크기에 비례한 스냅 반경. 난이도가 낮아 조각이 커지면 판정도 같이 넉넉해진다.")]
    [Range(0.1f, 0.8f)] [SerializeField] private float snapRadiusRatio = 0.4f;
    [Tooltip("조각을 흩뿌릴 때 화면 가장자리에서 띄울 여백.")]
    [SerializeField] private float scatterMargin = 20f;
    [SerializeField] private bool autoStart = false;

    // 슬라이스 시 결정되는 실제 조각 픽셀 크기
    private float currentPieceSize;

    [Header("Background Pieces")]
    [Tooltip("그림이 옅게만 든 조각을 처음부터 맞춰진 상태로 둔다. 끄면 그 조각들도 직접 맞춰야 하는데, " +
             "거의 투명해서 화면에서 찾기가 매우 어렵다. " +
             "그림이 아예 없는 칸은 이 값과 무관하게 조각을 만들지 않는다.")]
    [SerializeField] private bool preplaceBackgroundPieces = true;
    [Tooltip("이 값 미만의 알파는 비어 있는 픽셀로 본다.")]
    [Range(0f, 1f)] [SerializeField] private float alphaThreshold = 0.05f;
    [Tooltip("칸 안에 불투명 픽셀이 이 비율에 못 미치면 배경 조각으로 본다. " +
             "0으로 두면 그림이 한 픽셀이라도 있는 칸은 전부 플레이어 몫이 된다.")]
    [Range(0f, 1f)] [SerializeField] private float minOpaqueRatio = 0.03f;

    [Header("UI")]
    [SerializeField] private TMP_Text timerText;

    [Header("Board")]
    [Tooltip("아직 비어 있는 칸의 색. 배경이 검정이라 옅은 흰색이 빈칸으로 잘 읽힌다.")]
    [SerializeField] private Color slotColor = new Color(1f, 1f, 1f, 0.13f);
    [Tooltip("조각이 들어간 칸의 색. 조각이 덮으므로 거의 안 보이지만, 조각 가장자리가 투명할 때 티가 난다.")]
    [SerializeField] private Color slotFilledColor = new Color(1f, 1f, 1f, 0.03f);
    [Tooltip("칸과 칸 사이를 띄우는 간격. 붙여 놓으면 격자가 한 덩어리로 보인다.")]
    [SerializeField] private float slotInset = 2f;

    [Header("Visual Polish")]
    [SerializeField] private Vector2 pieceShadowOffset = new Vector2(4f, -4f);
    [SerializeField] private Color pieceShadowColor = new Color(0f, 0f, 0f, 0.35f);
    [SerializeField] private float dragLiftScale = 1.08f;
    [SerializeField] private float snapPunchScale = 1.18f;
    [SerializeField] private float snapPunchDuration = 0.18f;
    [SerializeField] private float lowTimeThreshold = 10f;
    [SerializeField] private Color timerNormalColor = Color.white;
    [SerializeField] private Color timerLowColor = new Color(1f, 0.3f, 0.3f, 1f);
    [SerializeField] private float bannerDuration = 1.1f;
    [SerializeField] private Color successBannerColor = new Color(0.4f, 0.95f, 0.55f, 1f);
    [SerializeField] private Color failBannerColor = new Color(1f, 0.35f, 0.4f, 1f);

    [Header("Events")]
    public UnityEvent onSuccess;
    public UnityEvent onFail;

    // 플레이어가 맞춰야 하는 조각.
    private readonly List<PuzzlePiece> pieces = new List<PuzzlePiece>();
    // 배경만 있어 처음부터 제자리에 놓아 두는 조각. 진행률에도 성공 판정에도 들어가지 않는다.
    private readonly List<PuzzlePiece> backgroundPieces = new List<PuzzlePiece>();

    // 제자리에 놓인 조각 수. 전부 놓이면 성공이다.
    private int placedCount;

    private float remainingTime;
    private int   lastShownSecond = -1;
    private bool  running;
    private int   gridN;
    private bool  isTimerLow;

    // 가운데 판. 조각 하나마다 빈칸 하나가 깔린다.
    private RectTransform board;
    private readonly Dictionary<PuzzlePiece, Image> slotOf = new Dictionary<PuzzlePiece, Image>();

    // 스냅 성공 시 조각들의 "팝" 스케일 애니메이션 (조각 → 남은 시간)
    private readonly Dictionary<PuzzlePiece, float> punchTimers = new Dictionary<PuzzlePiece, float>();
    private readonly List<PuzzlePiece> punchKeysBuffer = new List<PuzzlePiece>();

    // 진행률 텍스트 (맞춘 조각 수 / 맞춰야 할 조각 수)
    private TMP_Text progressText;

    // 성공/실패 배너 (배너가 사라진 뒤에 실제로 숨김 처리)
    private TMP_Text bannerText;
    private RectTransform bannerRect;
    private CanvasGroup bannerGroup;
    private float bannerTimer;
    private bool pendingEndHide;

    private void Start()
    {
        if (autoStart) StartPuzzle();
    }

    private void Update()
    {
        UpdateSnapPunches();
        UpdateBanner();

        if (!running) return;

        remainingTime -= Time.deltaTime;
        UpdateTimerUI();

        if (remainingTime <= 0f) Fail();
    }

    // Forge의 난이도 버튼처럼 sprite를 넘기지 않는 호출도 있다. null을 그대로 대입하면
    // 인스펙터에 지정해둔 이미지까지 날아가 시작 자체가 실패하므로, 넘어온 값이 있을 때만 교체한다.
    public void StartPuzzle(Sprite sprite, PuzzleDifficulty diff)
    {
        if (sprite != null) sourceSprite = sprite;
        difficulty = diff;
        StartPuzzle();
    }

    public void StartPuzzle()
    {
        if (sourceSprite == null) { Debug.LogError("[Puzzle] sourceSprite 없음"); return; }
        if (playArea == null)     { Debug.LogError("[Puzzle] playArea 없음"); return; }

        if (puzzleRoot != null) puzzleRoot.SetActive(true);
        // 화면 루트가 켜져 있는 동안 뒤쪽 3D 장면을 멈춘다. 실제 처리는 MiniGameScreen이 한다 —
        // 미니게임마다 같은 코드를 복사하지 않도록 공통으로 빼 두었다.
        MiniGameScreen.Ensure(puzzleRoot);
        EnsurePuzzleCanvas();

        EnsureProgressText();
        EnsureBanner();
        HideBannerImmediate();

        ClearPieces();
        SlicePieces();
        BuildBoard();
        ScatterPieces();

        remainingTime = timeLimit;
        lastShownSecond = -1;
        running = true;

        isTimerLow = false;
        if (timerText != null) timerText.color = timerNormalColor;

        UpdateTimerUI();
        UpdateProgressUI();
    }

    public void StopPuzzle()
    {
        running = false;
        ClearPieces();
        if (puzzleRoot != null) puzzleRoot.SetActive(false);
    }

    // 퍼즐 UI를 자기 캔버스로 떼어 놓는다.
    //
    // 놀이 영역은 씬의 공용 캔버스에 얹혀 있다. 그대로 두면 조각 하나를 끌 때마다 그 캔버스에 딸린
    // 모든 UI(카드 컨테이너까지)의 배치가 다시 계산된다. 캔버스를 따로 두면 다시 계산되는 범위가
    // 퍼즐 안으로 갇힌다.
    //
    // 중첩 캔버스는 부모의 GraphicRaycaster가 봐주지 않는다. 조각을 집으려면 자기 것을 달아야 한다.
    private void EnsurePuzzleCanvas()
    {
        if (playArea.GetComponent<Canvas>() != null) return;

        playArea.gameObject.AddComponent<Canvas>();
        if (playArea.GetComponent<GraphicRaycaster>() == null)
            playArea.gameObject.AddComponent<GraphicRaycaster>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 조각 슬라이싱 / 흩뿌리기
    // ─────────────────────────────────────────────────────────────────────

    private void ClearPieces()
    {
        for (int i = 0; i < pieces.Count; i++)
            if (pieces[i] != null) Destroy(pieces[i].gameObject);
        pieces.Clear();

        for (int i = 0; i < backgroundPieces.Count; i++)
            if (backgroundPieces[i] != null) Destroy(backgroundPieces[i].gameObject);
        backgroundPieces.Clear();

        punchTimers.Clear();
        placedCount = 0;

        // 빈칸은 조각과 짝이라 함께 치운다. 남겨 두면 지워진 조각을 가리키는 칸이 판에 남는다.
        ClearSlots();
    }

    private void SlicePieces()
    {
        int n = (int)difficulty;
        gridN = n;
        Texture2D tex = sourceSprite.texture;
        Rect fullRect = sourceSprite.textureRect;

#if UNITY_EDITOR
        EnsureReadable(tex);
#endif
        // 콘텐츠 바운딩도 칸별 불투명 픽셀 수도 전부 여기서 나온다. 스프라이트마다 한 번만
        // 만들어 두고 재사용하므로, 같은 퍼즐을 다시 시작할 때는 픽셀을 아예 읽지 않는다.
        // 텍스처를 읽지 못하면 null이고, 그때는 알파를 모르는 채로 전체를 그냥 자른다.
        PuzzleAlphaMask mask = PuzzleAlphaMask.Get(sourceSprite, alphaThreshold);

        // 검 이미지가 실제로 차지하는 영역(타이트 바운딩)만 슬라이스 대상으로 사용
        Rect src = mask != null ? mask.ContentRect : fullRect;
        Debug.Log($"[Puzzle] 콘텐츠 영역 {src} (전체 {fullRect})");

        // Hell 픽셀 크기는 고정. 낮은 난이도는 어셈블 크기 동일하게 유지하도록 확대.
        // ex) Hell=40 → Hard=60, Normal≈86, Easy=150. 15×Hell = n×current 동일.
        const int hellN = (int)PuzzleDifficulty.Hell;
        currentPieceSize = (hellN / (float)n) * hellPieceSize;
        Debug.Log($"[Puzzle] 조각 표시 크기: {currentPieceSize:0.0}px (난이도 {difficulty}, n={n})");

        float pieceWPx = src.width / n;
        float pieceHPx = src.height / n;

        // 칸 안에 불투명 픽셀이 이만큼은 있어야 플레이어가 맞출 조각이 된다.
        // 최소 1인 이유는 비율을 0으로 두더라도 "그림이 하나도 없는 칸"은 걸러내야 하기 때문이다.
        int needed = Mathf.Max(1, Mathf.CeilToInt(pieceWPx * pieceHPx * minOpaqueRatio));

        int preplaced = 0;
        int skipped = 0;
        for (int row = 0; row < n; row++)
        {
            for (int col = 0; col < n; col++)
            {
                Rect pieceRect = new Rect(
                    src.x + col * pieceWPx,
                    src.y + (n - 1 - row) * pieceHPx,
                    pieceWPx, pieceHPx);

                // needed에 닿으면 거기서 세기를 멈추므로, 그림이 넉넉한 칸일수록 빨리 빠져나온다.
                // 못 미친 값은 끝까지 센 결과라 0을 0으로 믿어도 된다.
                int opaque = mask != null ? mask.CountOpaque(pieceRect, needed) : needed;

                // 그림이 한 픽셀도 없는 칸은 조각을 만들지 않는다. 만들어 봐야 아무것도 그리지
                // 않는 오브젝트가 되고, 없어도 완성 그림은 똑같다 — 지켜야 할 그림이 없으니까.
                if (opaque == 0)
                {
                    skipped++;
                    continue;
                }

                // FullRect가 꼭 필요하다. Sprite.Create는 기본이 Tight라 조각마다 알파 외곽선을
                // 따라가며 폴리곤 메시를 만드는데, 조각은 어차피 정사각형이라 아무 이득이 없으면서
                // 한 장당 4ms 가까이 든다(Hell 225장 = 840ms 멈춤).
                Sprite pieceSprite = Sprite.Create(tex, pieceRect, new Vector2(0.5f, 0.5f), 100f,
                    0, SpriteMeshType.FullRect);
                PuzzlePiece piece = CreatePiece(pieceSprite, currentPieceSize, currentPieceSize, col, row);

                // 그림이 옅게만 든 칸은 버리지 않고 처음부터 맞춰진 것으로 둔다. 버리면 그림에
                // 구멍이 남고, 그렇다고 플레이어에게 맡기면 보이지도 않는 조각을 찾아 헤매게 된다.
                if (preplaceBackgroundPieces && opaque < needed)
                {
                    backgroundPieces.Add(piece);
                    preplaced++;
                }
                else
                {
                    pieces.Add(piece);
                }
            }
        }

        if (skipped > 0)   Debug.Log($"[Puzzle] 빈 칸 {skipped}개는 조각을 만들지 않음");
        if (preplaced > 0) Debug.Log($"[Puzzle] 배경 조각 {preplaced}개는 맞춰진 채로 시작");
        Debug.Log($"[Puzzle] 맞출 조각 {pieces.Count}개 (격자 {n}x{n} = {n * n}칸)");
    }

#if UNITY_EDITOR
    // 에디터에서 텍스처 임포터를 자동으로 Read/Write 켜기
    private static void EnsureReadable(Texture2D tex)
    {
        if (tex == null) return;
        string path = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(path)) return;
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
            Debug.Log($"[Puzzle] '{tex.name}' Read/Write Enabled 자동 활성화");
        }
    }
#endif

    private PuzzlePiece CreatePiece(Sprite sprite, float w, float h, int col, int row)
    {
        var go = new GameObject("Piece", typeof(RectTransform), typeof(CanvasGroup), typeof(PuzzlePiece));
        go.transform.SetParent(playArea, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);

        // 그림자 (같은 스프라이트를 검게 물들여 살짝 오프셋 — 조각 실루엣과 정확히 일치)
        var shadowGo = new GameObject("Shadow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        shadowGo.transform.SetParent(go.transform, false);
        var shadowImg = shadowGo.GetComponent<Image>();
        shadowImg.sprite = sprite;
        shadowImg.raycastTarget = false;
        shadowImg.preserveAspect = true;
        shadowImg.color = pieceShadowColor;
        var shadowRt = (RectTransform)shadowGo.transform;
        shadowRt.anchorMin = Vector2.zero;
        shadowRt.anchorMax = Vector2.one;
        shadowRt.offsetMin = shadowRt.offsetMax = Vector2.zero;
        shadowRt.anchoredPosition = pieceShadowOffset;

        // 본체 (그림자보다 나중에 그려져 위에 표시됨)
        var coreGo = new GameObject("Core", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        coreGo.transform.SetParent(go.transform, false);
        var coreImg = coreGo.GetComponent<Image>();
        coreImg.sprite = sprite;
        coreImg.raycastTarget = true;
        coreImg.preserveAspect = true;
        var coreRt = (RectTransform)coreGo.transform;
        coreRt.anchorMin = Vector2.zero;
        coreRt.anchorMax = Vector2.one;
        coreRt.offsetMin = coreRt.offsetMax = Vector2.zero;

        var piece = go.GetComponent<PuzzlePiece>();
        piece.Init(col, row, this, coreImg, shadowImg);
        return piece;
    }

    // 조각은 판 바깥에 흩는다. 판 위에 겹쳐 놓으면 맞춰야 할 그림이 가려져 게임이 되지 않는다.
    // 화면이 좁아 바깥에 자리가 없으면 어쩔 수 없이 판 위에도 떨어진다.
    private void ScatterPieces()
    {
        float boardHalf = gridN * currentPieceSize * 0.5f;
        float keepOut = boardHalf + currentPieceSize * 0.5f;
        Vector2 half = playArea.rect.size * 0.5f
                       - Vector2.one * (currentPieceSize * 0.5f + scatterMargin);
        half = Vector2.Max(half, Vector2.one * currentPieceSize * 0.5f);

        for (int i = 0; i < pieces.Count; i++)
        {
            Vector2 pos = Vector2.zero;
            for (int attempt = 0; attempt < ScatterAttempts; attempt++)
            {
                pos = new Vector2(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y));
                if (Mathf.Abs(pos.x) > keepOut || Mathf.Abs(pos.y) > keepOut) break;
            }

            pieces[i].RT.anchoredPosition = pos;
        }
    }

    private const int ScatterAttempts = 24;

    // ─────────────────────────────────────────────────────────────────────
    // 드래그 → 그룹 이동 / 스냅 / 그룹 병합
    // ─────────────────────────────────────────────────────────────────────

    public void OnPieceBeginDrag(PuzzlePiece p)
    {
        if (!running || p.Placed) return;

        p.CG.blocksRaycasts = false;
        p.transform.SetAsLastSibling();   // 끌고 다니는 동안은 무엇보다 위에
        p.RT.localScale = Vector3.one * dragLiftScale; // 집어 든 느낌
    }

    public void OnPieceDrag(PuzzlePiece p, Vector2 delta)
    {
        if (!running || p.Placed) return;

        p.RT.anchoredPosition += delta;
    }

    public void OnPieceEndDrag(PuzzlePiece p)
    {
        if (!running || p.Placed) return;

        p.CG.blocksRaycasts = true;
        p.RT.localScale = Vector3.one;    // 들어올림 스케일 원복

        Vector2 slot = SlotPosition(p);
        float radius = SnapRadius();
        if ((p.RT.anchoredPosition - slot).sqrMagnitude <= radius * radius) PlacePiece(p, slot, true);

        UpdateProgressUI();

        if (pieces.Count > 0 && placedCount >= pieces.Count) Succeed();
    }

    // 판 위에서 이 조각이 있어야 할 자리. 판은 놀이 영역 한가운데(0,0)에 놓인다.
    private Vector2 SlotPosition(PuzzlePiece piece)
    {
        float half = gridN * currentPieceSize * 0.5f;
        return new Vector2(
            -half + (piece.gridCol + 0.5f) * currentPieceSize,
             half - (piece.gridRow + 0.5f) * currentPieceSize);
    }

    // 조각이 클수록 판정도 넉넉해야 한다. Easy(조각 150px)에서 25px만 허용하면
    // 눈으로는 제자리인데 안 붙는 일이 잦다.
    private float SnapRadius()
    {
        return Mathf.Max(pieceSnapDistance, currentPieceSize * snapRadiusRatio);
    }

    // countsTowardGoal이 false면 처음부터 맞춰져 있던 배경 조각이다. 자리에 굳히기만 하고
    // 진행률이나 성공 판정은 건드리지 않는다. "팝" 연출도 넣지 않는다 — 플레이어가 한 일이 아니다.
    private void PlacePiece(PuzzlePiece piece, Vector2 slot, bool countsTowardGoal)
    {
        // 판으로 옮겨 붙인다. 판은 자기 캔버스라 여기 들어온 조각은 남은 조각을 끌 때
        // 다시 배치되지 않는다. 판이 놀이 영역 한가운데라 자리 좌표는 그대로 통한다.
        // 빈칸보다 뒤에 붙으므로 자연히 그 위에 그려진다.
        if (board != null) piece.transform.SetParent(board, false);
        piece.RT.anchoredPosition = slot;
        piece.MarkPlaced();

        // 채운 칸은 눌러 둔다. 남은 빈칸만 밝게 보여야 어디가 비었는지 한눈에 들어온다.
        if (slotOf.TryGetValue(piece, out Image marker) && marker != null) marker.color = slotFilledColor;

        if (!countsTowardGoal) return;

        placedCount++;
        punchTimers[piece] = snapPunchDuration;
    }

    // ─────────────────────────────────────────────────────────────────────
    // 가운데 판 (조각이 들어갈 빈칸)
    // ─────────────────────────────────────────────────────────────────────

    // 실제로 만들어진 조각 하나마다 빈칸을 하나씩 깐다.
    //
    // 배경만 있는 칸은 처음부터 맞춰져 있으므로 빈칸을 만들지 않는다. 그래서 빈칸들이 모여
    // 그리는 윤곽이 곧 맞춰야 할 그림의 형태가 된다 — 완성 그림을 통째로 깔아 두는 것보다
    // "어디를 채워야 하는지"가 분명하고, 어느 조각이 어디로 가는지는 직접 찾아야 한다.
    private void BuildBoard()
    {
        if (board == null)
        {
            // 판은 자기 캔버스를 가진다. 빈칸과 이미 맞춘 조각은 더 이상 움직이지 않으므로,
            // 손에 든 조각을 끌 때 함께 다시 배치될 이유가 없다. Hell 난이도에서 이쪽이 200장을 넘는다.
            // 안에 든 것들은 클릭을 받지 않으니 GraphicRaycaster는 달지 않는다.
            var root = new GameObject("Board", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(playArea, false);
            board = (RectTransform)root.transform;
            board.anchorMin = board.anchorMax = new Vector2(0.5f, 0.5f);
            board.pivot = new Vector2(0.5f, 0.5f);
        }

        ClearSlots();

        float side = gridN * currentPieceSize;
        board.sizeDelta = new Vector2(side, side);
        board.anchoredPosition = Vector2.zero;

        // 판은 조각들보다 뒤, 검은 배경보다는 앞에 있어야 한다.
        // 놀이 영역에는 화면을 통째로 덮는 불투명한 BlackBG가 자식으로 놓여 있어서
        // 맨 앞 형제로 보내면 판이 그 뒤로 숨는다. 맨 뒤로 보낸 다음 조각들을 그 위로 올린다.
        board.SetAsLastSibling();
        for (int i = 0; i < pieces.Count; i++)
            pieces[i].transform.SetAsLastSibling();

        // 칸끼리 조금씩 띄워야 격자가 눈에 들어온다. 딱 붙이면 한 덩어리로 보인다.
        float markerSize = Mathf.Max(4f, currentPieceSize - slotInset * 2f);
        for (int i = 0; i < pieces.Count; i++)
        {
            PuzzlePiece piece = pieces[i];
            Image marker = CreateSlotMarker(piece);

            RectTransform rt = marker.rectTransform;
            rt.sizeDelta = new Vector2(markerSize, markerSize);
            rt.anchoredPosition = SlotPosition(piece);

            slotOf[piece] = marker;
        }

        // 배경 조각은 빈칸을 만들지 않고 곧장 제자리에 굳힌다. 이미 맞춰진 자리이므로
        // 진행률에도 성공 판정에도 넣지 않는다.
        for (int i = 0; i < backgroundPieces.Count; i++)
            PlacePiece(backgroundPieces[i], SlotPosition(backgroundPieces[i]), false);
    }

    private Image CreateSlotMarker(PuzzlePiece piece)
    {
        var go = new GameObject($"Slot_{piece.gridCol}_{piece.gridRow}",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(board, false);

        var image = go.GetComponent<Image>();
        // 빈칸은 보여주기만 한다. 레이캐스트를 받으면 그 위에 놓인 조각을 집을 수 없다.
        image.raycastTarget = false;
        image.color = slotColor;

        var rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        return image;
    }

    private void ClearSlots()
    {
        if (board != null)
            for (int i = board.childCount - 1; i >= 0; i--) Destroy(board.GetChild(i).gameObject);

        slotOf.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 스냅 "팝" 애니메이션
    // ─────────────────────────────────────────────────────────────────────

    private void UpdateSnapPunches()
    {
        if (punchTimers.Count == 0) return;

        punchKeysBuffer.Clear();
        punchKeysBuffer.AddRange(punchTimers.Keys);

        for (int i = 0; i < punchKeysBuffer.Count; i++)
        {
            PuzzlePiece piece = punchKeysBuffer[i];
            if (piece == null || piece.RT == null)
            {
                punchTimers.Remove(piece);
                continue;
            }

            float timer = punchTimers[piece] - Time.deltaTime;
            if (timer <= 0f)
            {
                piece.RT.localScale = Vector3.one;
                punchTimers.Remove(piece);
            }
            else
            {
                punchTimers[piece] = timer;
                float t = timer / snapPunchDuration;
                float scale = Mathf.Lerp(1f, snapPunchScale, t);
                piece.RT.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 진행률 UI
    // ─────────────────────────────────────────────────────────────────────

    private void EnsureProgressText()
    {
        if (progressText != null) return;

        var go = new GameObject("ProgressText", typeof(RectTransform));
        go.transform.SetParent(playArea, false);

        progressText = go.AddComponent<TextMeshProUGUI>();
        progressText.alignment = TextAlignmentOptions.TopLeft;
        progressText.fontSize = 32f;
        progressText.fontStyle = FontStyles.Bold;
        progressText.color = Color.white;
        progressText.raycastTarget = false;

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(20f, -20f);
        rt.sizeDelta = new Vector2(300f, 50f);
    }

    private void UpdateProgressUI()
    {
        if (progressText == null) return;

        progressText.text = $"{placedCount} / {pieces.Count}";
        progressText.transform.SetAsLastSibling();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 성공/실패 배너
    // ─────────────────────────────────────────────────────────────────────

    private void EnsureBanner()
    {
        if (bannerText != null) return;

        var go = new GameObject("Banner", typeof(RectTransform));
        go.transform.SetParent(playArea, false);

        bannerText = go.AddComponent<TextMeshProUGUI>();
        bannerText.alignment = TextAlignmentOptions.Center;
        bannerText.fontSize = 56f;
        bannerText.fontStyle = FontStyles.Bold;
        bannerText.raycastTarget = false;
        bannerText.text = "";

        bannerRect = (RectTransform)go.transform;
        bannerRect.anchorMin = bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
        bannerRect.pivot = new Vector2(0.5f, 0.5f);
        bannerRect.sizeDelta = new Vector2(700f, 120f);

        bannerGroup = go.AddComponent<CanvasGroup>();
        bannerGroup.alpha = 0f;
    }

    private void HideBannerImmediate()
    {
        bannerTimer = 0f;
        pendingEndHide = false;
        if (bannerGroup != null) bannerGroup.alpha = 0f;
    }

    private void ShowBanner(string text, Color color)
    {
        EnsureBanner();
        bannerText.text = text;
        bannerText.color = color;
        bannerRect.localScale = Vector3.one * 1.3f;
        bannerRect.transform.SetAsLastSibling();
        bannerGroup.alpha = 1f;
        bannerTimer = bannerDuration;
        pendingEndHide = true;
    }

    private void UpdateBanner()
    {
        if (bannerTimer <= 0f) return;

        bannerTimer = Mathf.Max(0f, bannerTimer - Time.deltaTime);
        float t = 1f - (bannerTimer / bannerDuration);

        float scale = Mathf.Lerp(1.3f, 1f, Mathf.Clamp01(t * 4f));
        bannerRect.localScale = Vector3.one * scale;
        bannerGroup.alpha = t < 0.7f ? 1f : 1f - Mathf.InverseLerp(0.7f, 1f, t);

        if (bannerTimer <= 0f && pendingEndHide)
        {
            pendingEndHide = false;
            EndAndHide();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 타이머 / 종료
    // ─────────────────────────────────────────────────────────────────────

    private void UpdateTimerUI()
    {
        if (timerText == null) return;

        // 색상 대입은 TMP 정점을 다시 만들게 하므로 실제로 바뀔 때만 건드린다.
        bool low = remainingTime <= lowTimeThreshold;
        if (low != isTimerLow)
        {
            isTimerLow = low;
            timerText.color = low ? timerLowColor : timerNormalColor;
        }

        int totalSec = Mathf.CeilToInt(Mathf.Max(0f, remainingTime));
        if (totalSec == lastShownSecond) return; // 같은 초면 string alloc 회피
        lastShownSecond = totalSec;
        int m = totalSec / 60;
        int s = totalSec % 60;
        timerText.text = $"{m:00}:{s:00}";
    }

    private void Succeed()
    {
        if (!running) return;
        running = false;
        Debug.Log($"[Puzzle] 성공! ({difficulty}, 남은 시간 {remainingTime:0.0}s)");
        onSuccess?.Invoke();
        ShowBanner("COMPLETE!", successBannerColor);
    }

    private void Fail()
    {
        if (!running) return;
        running = false;
        Debug.Log("[Puzzle] 실패 (시간 초과)");
        onFail?.Invoke();
        ShowBanner("TIME OVER", failBannerColor);
    }

    private void EndAndHide()
    {
        ClearPieces();
        if (puzzleRoot != null) puzzleRoot.SetActive(false);
    }
}
