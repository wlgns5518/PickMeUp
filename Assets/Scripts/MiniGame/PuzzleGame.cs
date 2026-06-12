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

    [Header("Events")]
    public UnityEvent onSuccess;
    public UnityEvent onFail;

    private readonly List<PuzzlePiece> pieces = new List<PuzzlePiece>();
    // 각 조각이 속한 그룹(리스트 참조 공유). 같은 그룹이면 같은 List 인스턴스
    private readonly Dictionary<PuzzlePiece, List<PuzzlePiece>> groupOf =
        new Dictionary<PuzzlePiece, List<PuzzlePiece>>();

    private float remainingTime;
    private int   lastShownSecond = -1;
    private bool  running;

    private void Start()
    {
        if (autoStart) StartPuzzle();
    }

    private void Update()
    {
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

        ClearPieces();
        SlicePieces();
        ScatterPieces();

        remainingTime = timeLimit;
        lastShownSecond = -1;
        running = true;
        UpdateTimerUI();
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
    }

    private void SlicePieces()
    {
        int n = (int)difficulty;
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
                PuzzlePiece piece = CreatePiece(pieceSprite, currentPieceSize, currentPieceSize);
                piece.Init(col, row, this);

                pieces.Add(piece);

                // 자기 자신으로 그룹 초기화
                var group = new List<PuzzlePiece> { piece };
                groupOf[piece] = group;
            }
        }

        if (skipped > 0) Debug.Log($"[Puzzle] 투명 조각 {skipped}개 생략 → 실제 {pieces.Count}개");
        cachedPixels = null;
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

    private PuzzlePiece CreatePiece(Sprite sprite, float w, float h)
    {
        var go = new GameObject("Piece", typeof(RectTransform), typeof(CanvasRenderer),
                                typeof(Image), typeof(CanvasGroup), typeof(PuzzlePiece));
        go.transform.SetParent(playArea, false);

        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = true;
        img.preserveAspect = true;

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);

        return go.GetComponent<PuzzlePiece>();
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
        for (int i = 0; i < group.Count; i++)
            group[i].CG.blocksRaycasts = true;

        TrySnap(p);

        // 모든 조각이 하나의 그룹이면 성공
        if (pieces.Count > 0 && groupOf[pieces[0]].Count == pieces.Count)
            Succeed();
    }

    // 드래그된 조각의 그룹과 다른 모든 그룹 간 페어 검사 → 위치 정합 시 병합.
    // 한 번 병합되면 새 그룹으로 다시 검사 (체인 스냅 지원).
    private void TrySnap(PuzzlePiece dragged)
    {
        float snapSqr = pieceSnapDistance * pieceSnapDistance;
        bool snapped;
        do
        {
            snapped = false;
            var myGroup = groupOf[dragged];
            int piecesCount = pieces.Count;
            int myCount = myGroup.Count;

            for (int a = 0; a < myCount && !snapped; a++)
            {
                PuzzlePiece my = myGroup[a];
                Vector2 myPos = my.RT.anchoredPosition;
                int myCol = my.gridCol;
                int myRow = my.gridRow;

                for (int b = 0; b < piecesCount; b++)
                {
                    PuzzlePiece other = pieces[b];
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
    // 타이머 / 종료
    // ─────────────────────────────────────────────────────────────────────

    private void UpdateTimerUI()
    {
        if (timerText == null) return;
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
        EndAndHide();
    }

    private void Fail()
    {
        if (!running) return;
        running = false;
        Debug.Log("[Puzzle] 실패 (시간 초과)");
        onFail?.Invoke();
        EndAndHide();
    }

    private void EndAndHide()
    {
        ClearPieces();
        if (puzzleRoot != null) puzzleRoot.SetActive(false);
    }
}
