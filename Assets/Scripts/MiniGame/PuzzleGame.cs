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
    [Tooltip("이 거리 이내로 다른 조각과 정합 위치에 놓이면 스냅 연결")]
    [SerializeField] private float pieceSnapDistance = 25f;
    [SerializeField] private bool autoStart = false;

    // 슬라이스 시 결정되는 실제 조각 픽셀 크기
    private float currentPieceSize;

    [Header("Empty Piece Skip")]
    [SerializeField] private bool skipEmptyPieces = true;
    [Range(0f, 1f)] [SerializeField] private float alphaThreshold = 0.05f;
    [Range(0f, 1f)] [SerializeField] private float minOpaqueRatio = 0.03f;

    [Header("UI")]
    [SerializeField] private TMP_Text timerText;

    [Header("Visual Polish")]
    [Tooltip("완성될 그림을 흐릿하게 미리 보여주는 가이드")]
    [Range(0f, 1f)] [SerializeField] private float referenceGhostAlpha = 0.14f;
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

    private readonly List<PuzzlePiece> pieces = new List<PuzzlePiece>();
    // 각 조각이 속한 그룹(리스트 참조 공유). 같은 그룹이면 같은 List 인스턴스
    private readonly Dictionary<PuzzlePiece, List<PuzzlePiece>> groupOf =
        new Dictionary<PuzzlePiece, List<PuzzlePiece>>();
    // 격자 좌표 → 조각. logicalNeighbors 구성 시에만 사용.
    private readonly Dictionary<Vector2Int, PuzzlePiece> pieceByGrid =
        new Dictionary<Vector2Int, PuzzlePiece>();
    // 조각별 스냅 후보(8방향, 투명 조각으로 인한 격자 빈칸은 건너뛰고 다음 실제 조각까지 탐색).
    // SlicePieces에서 한 번만 구성해 TrySnap이 O(1)에 가깝게 후보를 얻도록 함.
    private readonly Dictionary<PuzzlePiece, List<PuzzlePiece>> logicalNeighbors =
        new Dictionary<PuzzlePiece, List<PuzzlePiece>>();

    private float remainingTime;
    private int   lastShownSecond = -1;
    private bool  running;
    private int   gridN;

    // 완성 그림 미리보기 (조각들 뒤에 흐릿하게 표시)
    private Image referenceGhost;

    // 스냅 성공 시 조각들의 "팝" 스케일 애니메이션 (조각 → 남은 시간)
    private readonly Dictionary<PuzzlePiece, float> punchTimers = new Dictionary<PuzzlePiece, float>();
    private readonly List<PuzzlePiece> punchKeysBuffer = new List<PuzzlePiece>();

    // 진행률 텍스트 (최대 그룹 크기 / 전체 조각 수)
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

    public void StartPuzzle(Sprite sprite, PuzzleDifficulty diff)
    {
        sourceSprite = sprite;
        difficulty = diff;
        StartPuzzle();
    }

    public void StartPuzzle()
    {
        if (sourceSprite == null) { Debug.LogError("[Puzzle] sourceSprite 없음"); return; }
        if (playArea == null)     { Debug.LogError("[Puzzle] playArea 없음"); return; }

        if (puzzleRoot != null) puzzleRoot.SetActive(true);

        EnsureProgressText();
        EnsureBanner();
        HideBannerImmediate();

        ClearPieces();
        SlicePieces();
        UpdateReferenceGhost();
        ScatterPieces();

        remainingTime = timeLimit;
        lastShownSecond = -1;
        running = true;
        UpdateTimerUI();
        UpdateProgressUI();
    }

    public void StopPuzzle()
    {
        running = false;
        ClearPieces();
        if (puzzleRoot != null) puzzleRoot.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 조각 슬라이싱 / 흩뿌리기
    // ─────────────────────────────────────────────────────────────────────

    private Color32[] cachedPixels;
    private int cachedTexWidth;

    private void ClearPieces()
    {
        for (int i = 0; i < pieces.Count; i++)
            if (pieces[i] != null) Destroy(pieces[i].gameObject);
        pieces.Clear();
        groupOf.Clear();
        pieceByGrid.Clear();
        logicalNeighbors.Clear();
        punchTimers.Clear();
    }

    private void SlicePieces()
    {
        int n = (int)difficulty;
        gridN = n;
        Texture2D tex = sourceSprite.texture;
        Rect fullRect = sourceSprite.textureRect;

        // 픽셀 캐시 (콘텐츠 바운딩 + 투명 조각 체크 양쪽에서 사용)
#if UNITY_EDITOR
        EnsureReadable(tex);
#endif
        try { cachedPixels = tex.GetPixels32(); cachedTexWidth = tex.width; }
        catch (System.Exception e)
        {
            Debug.LogError($"[Puzzle] 픽셀 읽기 실패: {e.Message}\n" +
                           $"→ 텍스처 '{tex.name}'의 Read/Write Enabled를 켜주세요.");
            cachedPixels = null;
        }

        // 검 이미지가 실제로 차지하는 영역(타이트 바운딩)만 슬라이스 대상으로 사용
        Rect src = cachedPixels != null ? ComputeContentRect(fullRect) : fullRect;
        Debug.Log($"[Puzzle] 콘텐츠 영역 {src} (전체 {fullRect})");

        // Hell 픽셀 크기는 고정. 낮은 난이도는 어셈블 크기 동일하게 유지하도록 확대.
        // ex) Hell=40 → Hard=60, Normal≈86, Easy=150. 15×Hell = n×current 동일.
        const int hellN = (int)PuzzleDifficulty.Hell;
        currentPieceSize = (hellN / (float)n) * hellPieceSize;
        Debug.Log($"[Puzzle] 조각 표시 크기: {currentPieceSize:0.0}px (난이도 {difficulty}, n={n})");

        float pieceWPx = src.width / n;
        float pieceHPx = src.height / n;

        int skipped = 0;
        for (int row = 0; row < n; row++)
        {
            for (int col = 0; col < n; col++)
            {
                Rect pieceRect = new Rect(
                    src.x + col * pieceWPx,
                    src.y + (n - 1 - row) * pieceHPx,
                    pieceWPx, pieceHPx);

                if (skipEmptyPieces && cachedPixels != null && IsMostlyTransparent(pieceRect))
                {
                    skipped++;
                    continue;
                }

                Sprite pieceSprite = Sprite.Create(tex, pieceRect, new Vector2(0.5f, 0.5f), 100f);
                PuzzlePiece piece = CreatePiece(pieceSprite, currentPieceSize, currentPieceSize, col, row);

                pieces.Add(piece);
                pieceByGrid[new Vector2Int(col, row)] = piece;

                // 자기 자신으로 그룹 초기화
                var group = new List<PuzzlePiece> { piece };
                groupOf[piece] = group;
            }
        }

        if (skipped > 0) Debug.Log($"[Puzzle] 투명 조각 {skipped}개 생략 → 실제 {pieces.Count}개");
        cachedPixels = null;

        BuildLogicalNeighbors(n);
    }

    // 각 조각의 8방향 스냅 후보를 미리 계산. 투명 조각으로 격자에 빈칸이 있어도
    // 그 방향으로 계속 나아가 처음 만나는 실제 조각을 후보로 삼는다 (끊긴 연결 방지).
    private void BuildLogicalNeighbors(int n)
    {
        for (int idx = 0; idx < pieces.Count; idx++)
        {
            PuzzlePiece piece = pieces[idx];
            var candidates = new List<PuzzlePiece>(NeighborOffsets.Length);

            for (int d = 0; d < NeighborOffsets.Length; d++)
            {
                Vector2Int dir = NeighborOffsets[d];
                int col = piece.gridCol + dir.x;
                int row = piece.gridRow + dir.y;

                while (col >= 0 && col < n && row >= 0 && row < n)
                {
                    if (pieceByGrid.TryGetValue(new Vector2Int(col, row), out PuzzlePiece found))
                    {
                        candidates.Add(found);
                        break;
                    }
                    col += dir.x;
                    row += dir.y;
                }
            }

            logicalNeighbors[piece] = candidates;
        }
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

    // 텍스처 내에서 알파가 있는 픽셀들의 타이트 바운딩 박스
    private Rect ComputeContentRect(Rect fullRect)
    {
        int texH = cachedPixels.Length / cachedTexWidth;
        int x0 = Mathf.Clamp(Mathf.RoundToInt(fullRect.x), 0, cachedTexWidth - 1);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(fullRect.y), 0, texH - 1);
        int x1 = Mathf.Clamp(x0 + Mathf.RoundToInt(fullRect.width) - 1, x0, cachedTexWidth - 1);
        int y1 = Mathf.Clamp(y0 + Mathf.RoundToInt(fullRect.height) - 1, y0, texH - 1);

        byte threshold = (byte)Mathf.RoundToInt(alphaThreshold * 255f);
        int minX = x1, minY = y1, maxX = x0, maxY = y0;
        bool found = false;

        for (int y = y0; y <= y1; y++)
        {
            int rowStart = y * cachedTexWidth;
            for (int x = x0; x <= x1; x++)
            {
                if (cachedPixels[rowStart + x].a >= threshold)
                {
                    if (!found) { minX = maxX = x; minY = maxY = y; found = true; }
                    else
                    {
                        if (x < minX) minX = x;
                        else if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        else if (y > maxY) maxY = y;
                    }
                }
            }
        }

        if (!found) return fullRect;
        return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private bool IsMostlyTransparent(Rect pieceRect)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt(pieceRect.x), 0, cachedTexWidth - 1);
        int texH = cachedPixels.Length / cachedTexWidth;
        int y = Mathf.Clamp(Mathf.RoundToInt(pieceRect.y), 0, texH - 1);
        int w = Mathf.Clamp(Mathf.RoundToInt(pieceRect.width), 1, cachedTexWidth - x);
        int h = Mathf.Clamp(Mathf.RoundToInt(pieceRect.height), 1, texH - y);

        byte threshold = (byte)Mathf.RoundToInt(alphaThreshold * 255f);
        int total = w * h;
        int needed = Mathf.CeilToInt(total * minOpaqueRatio);
        int opaque = 0;
        int scanned = 0;

        for (int j = 0; j < h; j++)
        {
            int rowStart = (y + j) * cachedTexWidth + x;
            for (int i = 0; i < w; i++)
            {
                if (cachedPixels[rowStart + i].a >= threshold) opaque++;
                scanned++;

                // 조기 종료: 임계 도달 → 투명 아님
                if (opaque >= needed) return false;
                // 조기 종료: 남은 픽셀 다 채워도 임계 못 넘김 → 투명 확정
                if (opaque + (total - scanned) < needed) return true;
            }
        }
        return opaque < needed;
    }

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
        piece.Init(col, row, this, coreImg);
        return piece;
    }

    private void ScatterPieces()
    {
        Vector2 half = playArea.rect.size * 0.5f - new Vector2(currentPieceSize, currentPieceSize);
        for (int i = 0; i < pieces.Count; i++)
        {
            ((RectTransform)pieces[i].transform).anchoredPosition = new Vector2(
                Random.Range(-half.x, half.x),
                Random.Range(-half.y, half.y));
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 드래그 → 그룹 이동 / 스냅 / 그룹 병합
    // ─────────────────────────────────────────────────────────────────────

    public void OnPieceBeginDrag(PuzzlePiece p)
    {
        if (!running) return;
        if (!groupOf.TryGetValue(p, out var group)) return;
        for (int i = 0; i < group.Count; i++)
        {
            group[i].CG.blocksRaycasts = false;
            group[i].transform.SetAsLastSibling();
            group[i].RT.localScale = Vector3.one * dragLiftScale; // 집어 든 느낌
        }
    }

    public void OnPieceDrag(PuzzlePiece p, Vector2 delta)
    {
        if (!running) return;
        if (!groupOf.TryGetValue(p, out var group)) return;
        for (int i = 0; i < group.Count; i++)
            group[i].RT.anchoredPosition += delta;
    }

    public void OnPieceEndDrag(PuzzlePiece p)
    {
        if (!running) return;
        if (!groupOf.TryGetValue(p, out var group)) return;
        int sizeBefore = group.Count;
        for (int i = 0; i < group.Count; i++)
            group[i].CG.blocksRaycasts = true;

        TrySnap(p); // group(List) 내부 항목이 그대로 병합되어 갱신됨 (같은 리스트 인스턴스)

        for (int i = 0; i < group.Count; i++)
            group[i].RT.localScale = Vector3.one; // 들어올림 스케일 원복

        if (group.Count > sizeBefore)
            TriggerSnapPunch(group);

        UpdateProgressUI();

        // 모든 조각이 하나의 그룹이면 성공
        if (pieces.Count > 0 && groupOf[pieces[0]].Count == pieces.Count)
            Succeed();
    }

    // 인접 격자 오프셋 (상/하/좌/우 + 대각선 4방향, 총 8칸)
    private static readonly Vector2Int[] NeighborOffsets =
    {
        new Vector2Int(1, 0),  new Vector2Int(-1, 0),
        new Vector2Int(0, 1),  new Vector2Int(0, -1),
        new Vector2Int(1, 1),  new Vector2Int(1, -1),
        new Vector2Int(-1, 1), new Vector2Int(-1, -1),
    };

    // 드래그된 조각의 그룹과 격자상 인접한 조각만 검사 → 위치 정합 시 병합.
    // pieceByGrid로 O(1) 이웃 조회 (전체 조각 스캔 대신).
    // 한 번 병합되면 새 그룹으로 다시 검사 (체인 스냅 지원).
    private void TrySnap(PuzzlePiece dragged)
    {
        float snapSqr = pieceSnapDistance * pieceSnapDistance;
        bool snapped;
        do
        {
            snapped = false;
            var myGroup = groupOf[dragged];
            int myCount = myGroup.Count;

            for (int a = 0; a < myCount && !snapped; a++)
            {
                PuzzlePiece my = myGroup[a];
                Vector2 myPos = my.RT.anchoredPosition;
                int myCol = my.gridCol;
                int myRow = my.gridRow;

                var candidates = logicalNeighbors[my];
                for (int c = 0; c < candidates.Count; c++)
                {
                    PuzzlePiece other = candidates[c];
                    var otherGroup = groupOf[other];
                    if (otherGroup == myGroup) continue;

                    float dxPx = (other.gridCol - myCol) * currentPieceSize;
                    float dyPx = -(other.gridRow - myRow) * currentPieceSize;
                    Vector2 otherPos = other.RT.anchoredPosition;
                    float diffX = (otherPos.x - myPos.x) - dxPx;
                    float diffY = (otherPos.y - myPos.y) - dyPx;

                    if (diffX * diffX + diffY * diffY <= snapSqr)
                    {
                        // other 그룹 전체를 정합 위치로 평행이동
                        Vector2 snapDelta = new Vector2(myPos.x + dxPx - otherPos.x,
                                                        myPos.y + dyPx - otherPos.y);
                        int otherCount = otherGroup.Count;
                        for (int k = 0; k < otherCount; k++)
                            otherGroup[k].RT.anchoredPosition += snapDelta;

                        // 그룹 병합 — otherGroup → myGroup
                        for (int k = 0; k < otherCount; k++)
                        {
                            myGroup.Add(otherGroup[k]);
                            groupOf[otherGroup[k]] = myGroup;
                        }

                        snapped = true;
                        break;
                    }
                }
            }
        }
        while (snapped);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 완성 그림 미리보기 (조각들 뒤에 흐릿하게 표시)
    // ─────────────────────────────────────────────────────────────────────

    private void UpdateReferenceGhost()
    {
        if (referenceGhost == null)
        {
            var go = new GameObject("ReferenceGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(playArea, false);
            referenceGhost = go.GetComponent<Image>();
            referenceGhost.raycastTarget = false;
            referenceGhost.preserveAspect = true;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        referenceGhost.sprite = sourceSprite;
        referenceGhost.color = new Color(1f, 1f, 1f, referenceGhostAlpha);
        float side = gridN * currentPieceSize;
        var ghostRt = (RectTransform)referenceGhost.transform;
        ghostRt.sizeDelta = new Vector2(side, side);
        ghostRt.anchoredPosition = Vector2.zero;
        ghostRt.SetAsFirstSibling(); // 조각들보다 뒤에 표시
    }

    // ─────────────────────────────────────────────────────────────────────
    // 스냅 "팝" 애니메이션
    // ─────────────────────────────────────────────────────────────────────

    private void TriggerSnapPunch(List<PuzzlePiece> group)
    {
        for (int i = 0; i < group.Count; i++)
            punchTimers[group[i]] = snapPunchDuration;
    }

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

        int total = pieces.Count;
        int maxGroup = 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            int c = groupOf[pieces[i]].Count;
            if (c > maxGroup) maxGroup = c;
        }

        progressText.text = $"{maxGroup} / {total}";
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

        bool low = remainingTime <= lowTimeThreshold;
        timerText.color = low ? timerLowColor : timerNormalColor;

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
