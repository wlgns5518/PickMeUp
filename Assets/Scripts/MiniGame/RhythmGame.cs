using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class RhythmGame : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private RectTransform playArea;
    [SerializeField] private GameObject rhythmRoot;

    [Header("Difficulty / Time")]
    [SerializeField] private PuzzleDifficulty difficulty = PuzzleDifficulty.Easy;
    [SerializeField] private bool autoStart = false;

    [Header("Notes")]
    [SerializeField] private int laneCount = 4;
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);
    [SerializeField] private float noteHeight = 84f;
    [SerializeField] private float noteTravelTime = 1.6f;
    [SerializeField] private float beatInterval = 0.75f;
    [SerializeField] private float firstBeatDelay = 1f;
    [SerializeField] private float perfectWindow = 0.08f;
    [SerializeField] private float goodWindow = 0.16f;
    [SerializeField] private bool countBadClickAsMiss = false;

    [Header("Target Line")]
    [Range(0f, 1f)] [SerializeField] private float targetLineYRatio = 0.1f;
    [SerializeField] private float targetLineHeight = 14f;
    [SerializeField] private Color targetLineColor = new Color(0.1f, 0.9f, 1f, 0.85f);

    [Header("Input Feedback")]
    [SerializeField] private float clickLineTolerance = 20f;

    [Header("Clear Rule")]
    [Range(0f, 1f)] [SerializeField] private float requiredHitRatio = 0.75f;
    [SerializeField] private int maxMisses = 4;

    [Header("UI")]
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text comboText;

    [Header("Visual Polish")]
    [SerializeField] private Color laneBackgroundColorA = new Color(0.10f, 0.10f, 0.17f, 0.55f);
    [SerializeField] private Color laneBackgroundColorB = new Color(0.06f, 0.06f, 0.11f, 0.55f);
    [SerializeField] private Color laneDividerColor = new Color(1f, 1f, 1f, 0.07f);
    [SerializeField] private float receptorIdleAlpha = 0.35f;
    [SerializeField] private float receptorPunchScale = 1.55f;
    [SerializeField] private float receptorPunchDuration = 0.22f;
    [SerializeField] private float judgmentPopupDuration = 0.55f;
    [SerializeField] private float judgmentPopupRise = 60f;
    [SerializeField] private float comboPunchDuration = 0.16f;
    [SerializeField] private float comboPunchScale = 1.35f;
    [SerializeField] private float scoreRollSpeed = 8f;
    [SerializeField] private Color perfectColor = new Color(1f, 0.85f, 0.25f, 1f);
    [SerializeField] private Color goodColor = new Color(0.45f, 0.95f, 0.55f, 1f);
    [SerializeField] private Color missColor = new Color(1f, 0.35f, 0.4f, 1f);
    [SerializeField] [Range(0f, 1f)] private float noteAccentSaturation = 0.55f;

    [Header("Events")]
    public UnityEvent onSuccess;
    public UnityEvent onFail;

    private readonly List<float> noteHitTimes = new List<float>();
    private readonly List<RhythmNote> activeNotes = new List<RhythmNote>();
    private readonly List<RhythmNote> notePool = new List<RhythmNote>();

    private float elapsedTime;
    private float totalDuration;
    private int nextNoteIndex;
    private int resolvedNotes;
    private int hitCount;
    private int missCount;
    private int perfectCount;
    private int goodCount;
    private int score;
    private int combo;
    private int lastShownSecond = -1;
    private string lastJudgmentText = "";
    private bool running;
    private RectTransform targetLine;
    private Camera cachedUiCamera;
    private Vector2 cachedNoteSize;
    private float cachedTargetLineHeight;
    private float cachedClickLineTolerance;
    private float[] cachedLaneX;
    private Color[] laneAccentColors;

    // 레인 배경 / 구분선 (트랙 구조감)
    private Image[] laneBackgrounds;
    private Image[] laneDividers;

    // 타겟라인 리셉터(레인별 원형 판정 표시) — 히트 시 펀치 스케일 + 판정색 플래시
    private Image[] laneReceptors;
    private RectTransform[] laneReceptorRects;
    private float[] receptorPunchTimers;

    // 판정 팝업 텍스트 (PERFECT/GOOD/MISS)
    private TMP_Text judgmentPopupText;
    private RectTransform judgmentPopupRect;
    private CanvasGroup judgmentPopupGroup;
    private float judgmentPopupTimer;
    private Vector2 judgmentPopupBasePos;

    // 콤보 펀치 애니메이션
    private RectTransform comboRect;
    private float comboPunchTimer;

    // 점수 롤업(카운트업) 애니메이션
    private float displayedScore;
    private int lastShownScore = -1;


    private enum Judgment
    {
        Perfect,
        Good,
        Miss,
    }

    private sealed class RhythmNote
    {
        public RectTransform rectTransform;
        public Image coreImage;
        public Image glowImage;
        public float spawnTime;
        public float hitTime;
        public int lane;
        public bool pooled;
    }

    private void Start()
    {
        if (autoStart) StartRhythm();
    }

    private void Update()
    {
        UpdateReceptorPunches();
        UpdateJudgmentPopup();
        UpdateComboPunch();
        UpdateScoreRoll();

        if (!running) return;

        elapsedTime += Time.deltaTime;
        SpawnDueNotes();
        UpdateActiveNotes();
        if (!running) return;

        UpdateTimerUI();

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            TryHitNote();
        if (!running) return;

        if (missCount > maxMisses)
            Fail();
    }

    public void StartRhythm(PuzzleDifficulty diff)
    {
        difficulty = diff;
        StartRhythm();
    }

    public void StartRhythm(Sprite sprite, PuzzleDifficulty diff)
    {
        // Sprite 파라미터는 PuzzleGame과 비슷한 외부 호출 형태를 유지하기 위한 자리입니다.
        difficulty = diff;
        StartRhythm();
    }

    public void StartRhythm()
    {
        if (playArea == null) { Debug.LogError("[Rhythm] playArea is missing"); return; }

        if (rhythmRoot != null) rhythmRoot.SetActive(true);

        PrepareLayout();
        ClearNotes();
        ApplyDifficulty();
        BuildNotePattern();

        elapsedTime = 0f;
        nextNoteIndex = 0;
        resolvedNotes = 0;
        hitCount = 0;
        missCount = 0;
        perfectCount = 0;
        goodCount = 0;
        score = 0;
        combo = 0;
        displayedScore = 0f;
        lastShownSecond = -1;
        lastShownScore = -1;
        lastJudgmentText = "";
        running = true;

        HideJudgmentPopupImmediate();
        UpdateTimerUI();
        UpdateResultUI(true);
    }

    public void StopRhythm()
    {
        running = false;
        ClearNotes();
        if (rhythmRoot != null) rhythmRoot.SetActive(false);
    }

    private void ApplyDifficulty()
    {
        switch (difficulty)
        {
            case PuzzleDifficulty.Easy:
                beatInterval = 0.42f;
                noteTravelTime = 0.9f;
                maxMisses = 2;
                requiredHitRatio = 0.9f;
                break;
            case PuzzleDifficulty.Normal:
                beatInterval = 0.34f;
                noteTravelTime = 0.78f;
                maxMisses = 2;
                requiredHitRatio = 0.92f;
                break;
            case PuzzleDifficulty.Hard:
                beatInterval = 0.28f;
                noteTravelTime = 0.66f;
                maxMisses = 1;
                requiredHitRatio = 0.95f;
                break;
            case PuzzleDifficulty.Hell:
                beatInterval = 0.22f;
                noteTravelTime = 0.54f;
                maxMisses = 0;
                requiredHitRatio = 1f;
                break;
        }
    }

    private void BuildNotePattern()
    {
        noteHitTimes.Clear();

        int noteCount = GetNoteCount();
        for (int i = 0; i < noteCount; i++)
        {
            float syncopation = (i % 7 == 3 || i % 11 == 5) ? beatInterval * 0.5f : 0f;
            noteHitTimes.Add(firstBeatDelay + i * beatInterval + syncopation);
        }

        totalDuration = noteHitTimes.Count > 0
            ? noteHitTimes[noteHitTimes.Count - 1] + goodWindow
            : 0f;
    }

    private int GetNoteCount()
    {
        switch (difficulty)
        {
            case PuzzleDifficulty.Easy: return 36;
            case PuzzleDifficulty.Normal: return 48;
            case PuzzleDifficulty.Hard: return 60;
            case PuzzleDifficulty.Hell: return 76;
            default: return 10;
        }
    }

    private void SpawnDueNotes()
    {
        while (nextNoteIndex < noteHitTimes.Count &&
               noteHitTimes[nextNoteIndex] - noteTravelTime <= elapsedTime)
        {
            SpawnNote(nextNoteIndex, noteHitTimes[nextNoteIndex]);
            nextNoteIndex++;
        }
    }

    private void SpawnNote(int index, float hitTime)
    {
        RhythmNote note = GetPooledNote();
        note.spawnTime = hitTime - noteTravelTime;
        note.hitTime = hitTime;
        note.lane = GetLane(index);
        note.pooled = false;
        note.rectTransform.SetParent(playArea, false);
        note.rectTransform.sizeDelta = GetScaledNoteSize();
        note.rectTransform.localScale = Vector3.one * 0.55f;
        note.rectTransform.gameObject.SetActive(true);

        Color accent = GetLaneAccent(note.lane);
        note.coreImage.color = accent;
        note.glowImage.color = new Color(accent.r, accent.g, accent.b, 0.35f);

        SetNotePosition(note, 0f);
        activeNotes.Add(note);
    }

    private int GetLane(int index)
    {
        int safeLaneCount = Mathf.Max(1, laneCount);
        return Mathf.Abs((index * 3 + index / 2) % safeLaneCount);
    }

    private void UpdateActiveNotes()
    {
        // 프레임당 한 번만 계산 (노트마다 반복 계산 회피)
        Rect rect = playArea.rect;
        Vector2 scaledNoteSize = GetScaledNoteSize();
        float spawnY = rect.yMax - scaledNoteSize.y * 0.5f;
        float targetY = GetTargetY();

        for (int i = activeNotes.Count - 1; i >= 0; i--)
        {
            RhythmNote note = activeNotes[i];
            if (note == null || note.rectTransform == null)
            {
                activeNotes.RemoveAt(i);
                continue;
            }

            float timing = elapsedTime - note.hitTime;
            if (timing > goodWindow)
            {
                ResolveMiss(i);
                continue;
            }

            float progress = Mathf.InverseLerp(note.spawnTime, note.hitTime, elapsedTime);
            SetNotePosition(note, progress, spawnY, targetY);
        }

        if (resolvedNotes >= noteHitTimes.Count)
            FinishByResult();
    }

    private void SetNotePosition(RhythmNote note, float progress)
    {
        Rect rect = playArea.rect;
        Vector2 scaledNoteSize = GetScaledNoteSize();
        float spawnY = rect.yMax - scaledNoteSize.y * 0.5f;
        float targetY = GetTargetY();
        SetNotePosition(note, progress, spawnY, targetY);
    }

    private void SetNotePosition(RhythmNote note, float progress, float spawnY, float targetY)
    {
        float laneX = GetLaneX(note.lane);
        float clamped = Mathf.Clamp01(progress);
        float y = Mathf.Lerp(spawnY, targetY, clamped);
        note.rectTransform.anchoredPosition = new Vector2(laneX, y);

        // 노트가 판정선에 가까워질수록 커지는 접근 연출 (0.55배 → 1.0배)
        float scale = Mathf.Lerp(0.55f, 1f, clamped);
        note.rectTransform.localScale = new Vector3(scale, scale, 1f);
    }

    // 레인별 X좌표는 playArea/laneCount가 바뀌지 않는 한 고정이므로 캐시해서 재사용.
    // 레인 배경/구분선(UpdateLaneBackgroundLayout)과 동일한 공식을 사용해 노트가 레인 칸에 정확히 맞도록 정렬.
    private void RebuildLaneXCache()
    {
        int safeLaneCount = Mathf.Max(1, laneCount);
        Rect rect = playArea.rect;
        float laneWidth = rect.width / safeLaneCount;

        cachedLaneX = new float[safeLaneCount];
        for (int lane = 0; lane < safeLaneCount; lane++)
            cachedLaneX[lane] = rect.xMin + laneWidth * (lane + 0.5f);
    }

    private float GetLaneX(int lane)
    {
        int safeLaneCount = Mathf.Max(1, laneCount);
        if (cachedLaneX == null || cachedLaneX.Length != safeLaneCount)
            RebuildLaneXCache();

        return cachedLaneX[Mathf.Clamp(lane, 0, safeLaneCount - 1)];
    }

    private float GetTargetY()
    {
        Rect rect = playArea.rect;
        return rect.yMin + rect.height * targetLineYRatio;
    }

    private Color GetLaneAccent(int lane)
    {
        if (laneAccentColors == null || lane < 0 || lane >= laneAccentColors.Length)
            return Color.white;
        return laneAccentColors[lane];
    }

    private void TryHitNote()
    {
        if (!TryGetClickedLane(out int clickedLane))
            return;

        int bestIndex = -1;
        float bestTiming = float.MaxValue;

        for (int i = 0; i < activeNotes.Count; i++)
        {
            if (activeNotes[i].lane != clickedLane)
                continue;

            float timing = Mathf.Abs(elapsedTime - activeNotes[i].hitTime);
            if (timing < bestTiming)
            {
                bestTiming = timing;
                bestIndex = i;
            }
        }

        Judgment judgment = GetJudgment(bestTiming);
        if (bestIndex < 0 || judgment == Judgment.Miss)
        {
            if (countBadClickAsMiss)
            {
                missCount++;
                combo = 0;
                ShowJudgment(Judgment.Miss, clickedLane);
                UpdateResultUI(false);
            }
            return;
        }

        RhythmNote note = activeNotes[bestIndex];
        activeNotes.RemoveAt(bestIndex);
        ReleaseNote(note);

        hitCount++;
        resolvedNotes++;
        combo++;
        if (judgment == Judgment.Perfect)
        {
            perfectCount++;
            score += 100 + combo * 2;
        }
        else
        {
            goodCount++;
            score += 60 + combo;
        }

        ShowJudgment(judgment, clickedLane);
        UpdateResultUI(false);
        TriggerComboPunch();

        if (resolvedNotes >= noteHitTimes.Count)
            FinishByResult();
    }

    private bool TryGetClickedLane(out int lane)
    {
        lane = -1;
        if (Mouse.current == null) return false;

        Camera uiCamera = GetUiCamera();
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                playArea, Mouse.current.position.ReadValue(), uiCamera, out Vector2 localPoint))
        {
            return false;
        }

        float targetY = GetTargetY();
        if (Mathf.Abs(localPoint.y - targetY) > GetScaledClickLineTolerance())
            return false;

        Rect rect = playArea.rect;
        if (localPoint.x < rect.xMin || localPoint.x > rect.xMax)
            return false;

        int safeLaneCount = Mathf.Max(1, laneCount);
        float laneWidth = rect.width / safeLaneCount;
        lane = Mathf.FloorToInt((localPoint.x - rect.xMin) / laneWidth);
        lane = Mathf.Clamp(lane, 0, safeLaneCount - 1);
        return true;
    }

    private Camera GetUiCamera()
    {
        return cachedUiCamera;
    }

    private void ResolveMiss(int index)
    {
        RhythmNote note = activeNotes[index];
        int lane = note.lane;
        activeNotes.RemoveAt(index);
        ReleaseNote(note);

        missCount++;
        resolvedNotes++;
        combo = 0;
        ShowJudgment(Judgment.Miss, lane);
        UpdateResultUI(false);
    }

    private Judgment GetJudgment(float timing)
    {
        if (timing <= perfectWindow) return Judgment.Perfect;
        if (timing <= goodWindow) return Judgment.Good;
        return Judgment.Miss;
    }

    private Vector2 GetScaledNoteSize()
    {
        if (cachedNoteSize != Vector2.zero)
            return cachedNoteSize;

        Rect rect = playArea.rect;
        float scaleY = referenceResolution.y > 0f ? rect.height / referenceResolution.y : 1f;
        int safeLaneCount = Mathf.Max(1, laneCount);
        return new Vector2(
            Mathf.Max(1f, rect.width / safeLaneCount), // 노트 폭을 레인 폭에 정확히 맞춤
            Mathf.Max(1f, noteHeight * scaleY));
    }

    private float GetScaledTargetLineHeight()
    {
        if (cachedTargetLineHeight > 0f)
            return cachedTargetLineHeight;

        Rect rect = playArea.rect;
        float scaleY = referenceResolution.y > 0f ? rect.height / referenceResolution.y : 1f;
        return Mathf.Max(1f, targetLineHeight * scaleY);
    }

    private float GetScaledClickLineTolerance()
    {
        if (cachedClickLineTolerance > 0f)
            return cachedClickLineTolerance;

        Rect rect = playArea.rect;
        float scaleY = referenceResolution.y > 0f ? rect.height / referenceResolution.y : 1f;
        return Mathf.Max(1f, clickLineTolerance * scaleY);
    }

    private void PrepareLayout()
    {
        CacheLayoutValues();
        BuildLaneAccentColors();
        EnsureLaneBackgrounds();
        EnsureTargetLine();
        EnsureLaneReceptors();
        EnsureJudgmentPopup();
        UpdateLaneBackgroundLayout();
        UpdateTargetLineLayout();
        UpdateReceptorLayout();
    }

    private void CacheLayoutValues()
    {
        Rect rect = playArea.rect;
        float scaleY = referenceResolution.y > 0f ? rect.height / referenceResolution.y : 1f;
        int safeLaneCount = Mathf.Max(1, laneCount);
        cachedNoteSize = new Vector2(
            Mathf.Max(1f, rect.width / safeLaneCount), // 노트 폭을 레인 폭에 정확히 맞춤
            Mathf.Max(1f, noteHeight * scaleY));
        cachedTargetLineHeight = Mathf.Max(1f, targetLineHeight * scaleY);
        cachedClickLineTolerance = Mathf.Max(1f, clickLineTolerance * scaleY);

        Canvas canvas = playArea.GetComponentInParent<Canvas>();
        cachedUiCamera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : canvas.worldCamera;

        cachedLaneX = null; // playArea/노트 크기가 바뀔 수 있으므로 다음 조회 시 재계산
    }

    private void BuildLaneAccentColors()
    {
        int safeLaneCount = Mathf.Max(1, laneCount);
        laneAccentColors = new Color[safeLaneCount];
        for (int i = 0; i < safeLaneCount; i++)
        {
            float hue = safeLaneCount > 0 ? (float)i / safeLaneCount : 0f;
            laneAccentColors[i] = Color.HSVToRGB(hue, noteAccentSaturation, 1f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 트랙 배경 / 레인 구분선
    // ─────────────────────────────────────────────────────────────────────

    private void EnsureLaneBackgrounds()
    {
        int safeLaneCount = Mathf.Max(1, laneCount);
        if (laneBackgrounds != null && laneBackgrounds.Length == safeLaneCount) return;

        DestroyImages(laneBackgrounds);
        DestroyImages(laneDividers);

        laneBackgrounds = new Image[safeLaneCount];
        for (int i = 0; i < safeLaneCount; i++)
        {
            var go = new GameObject($"LaneBackground_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(playArea, false);
            go.transform.SetAsFirstSibling();

            var img = go.GetComponent<Image>();
            img.color = (i % 2 == 0) ? laneBackgroundColorA : laneBackgroundColorB;
            img.raycastTarget = false;
            laneBackgrounds[i] = img;
        }

        laneDividers = new Image[Mathf.Max(0, safeLaneCount - 1)];
        for (int i = 0; i < laneDividers.Length; i++)
        {
            var go = new GameObject($"LaneDivider_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(playArea, false);

            var img = go.GetComponent<Image>();
            img.color = laneDividerColor;
            img.raycastTarget = false;
            laneDividers[i] = img;
        }
    }

    private void UpdateLaneBackgroundLayout()
    {
        if (laneBackgrounds == null) return;

        Rect rect = playArea.rect;
        int safeLaneCount = Mathf.Max(1, laneBackgrounds.Length);
        float laneWidth = rect.width / safeLaneCount;

        for (int i = 0; i < laneBackgrounds.Length; i++)
        {
            if (laneBackgrounds[i] == null) continue;
            var rt = (RectTransform)laneBackgrounds[i].transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(laneWidth, rect.height);
            rt.anchoredPosition = new Vector2(rect.xMin + laneWidth * (i + 0.5f), 0f);
        }

        if (laneDividers == null) return;
        for (int i = 0; i < laneDividers.Length; i++)
        {
            if (laneDividers[i] == null) continue;
            var rt = (RectTransform)laneDividers[i].transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(2f, rect.height);
            rt.anchoredPosition = new Vector2(rect.xMin + laneWidth * (i + 1), 0f);
        }
    }

    private static void DestroyImages(Image[] images)
    {
        if (images == null) return;
        for (int i = 0; i < images.Length; i++)
            if (images[i] != null) Destroy(images[i].gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 판정선 / 레인 리셉터 (히트 시 펀치 스케일 + 판정색 플래시)
    // ─────────────────────────────────────────────────────────────────────

    private void EnsureTargetLine()
    {
        if (targetLine != null) return;

        var lineObject = new GameObject("TargetLine", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        lineObject.transform.SetParent(playArea, false);

        targetLine = (RectTransform)lineObject.transform;
        var image = lineObject.GetComponent<Image>();
        image.color = targetLineColor;
        image.raycastTarget = false;
    }

    private void UpdateTargetLineLayout()
    {
        if (targetLine == null) return;

        targetLine.SetParent(playArea, false);
        targetLine.anchorMin = targetLine.anchorMax = new Vector2(0.5f, 0.5f);
        targetLine.pivot = new Vector2(0.5f, 0.5f);
        targetLine.anchoredPosition = new Vector2(0f, GetTargetY());
        targetLine.sizeDelta = new Vector2(playArea.rect.width, GetScaledTargetLineHeight());

        var image = targetLine.GetComponent<Image>();
        if (image == null)
            image = targetLine.gameObject.AddComponent<Image>();

        image.color = targetLineColor;
        image.raycastTarget = false;
    }

    private void EnsureLaneReceptors()
    {
        int safeLaneCount = Mathf.Max(1, laneCount);
        if (laneReceptors != null && laneReceptors.Length == safeLaneCount) return;

        DestroyImages(laneReceptors);

        laneReceptors = new Image[safeLaneCount];
        laneReceptorRects = new RectTransform[safeLaneCount];
        receptorPunchTimers = new float[safeLaneCount];

        for (int i = 0; i < safeLaneCount; i++)
        {
            var go = new GameObject($"LaneReceptor_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(playArea, false);

            var img = go.GetComponent<Image>();
            img.raycastTarget = false;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            laneReceptors[i] = img;
            laneReceptorRects[i] = rt;
        }
    }

    private void UpdateReceptorLayout()
    {
        if (laneReceptors == null) return;

        // 노트와 동일한 직사각형 크기로 리셉터 배치
        Vector2 receptorSize = GetScaledNoteSize();
        float targetY = GetTargetY();

        for (int lane = 0; lane < laneReceptors.Length; lane++)
        {
            if (laneReceptors[lane] == null) continue;
            laneReceptorRects[lane].sizeDelta = receptorSize;
            laneReceptorRects[lane].anchoredPosition = new Vector2(GetLaneX(lane), targetY);

            Color idle = GetLaneAccent(lane);
            idle.a = receptorIdleAlpha;
            laneReceptors[lane].color = idle;
        }
    }

    private void PunchReceptor(int lane, Color flashColor)
    {
        if (laneReceptors == null || lane < 0 || lane >= laneReceptors.Length) return;
        if (laneReceptors[lane] == null) return;

        receptorPunchTimers[lane] = receptorPunchDuration;
        laneReceptors[lane].color = flashColor;
    }

    private void UpdateReceptorPunches()
    {
        if (laneReceptors == null || receptorPunchTimers == null) return;

        for (int lane = 0; lane < laneReceptors.Length; lane++)
        {
            if (laneReceptors[lane] == null) continue;

            if (receptorPunchTimers[lane] > 0f)
            {
                receptorPunchTimers[lane] = Mathf.Max(0f, receptorPunchTimers[lane] - Time.unscaledDeltaTime);
                float t = receptorPunchDuration > 0f ? receptorPunchTimers[lane] / receptorPunchDuration : 0f;

                float scale = Mathf.Lerp(1f, receptorPunchScale, t);
                laneReceptorRects[lane].localScale = new Vector3(scale, scale, 1f);

                Color idle = GetLaneAccent(lane);
                idle.a = receptorIdleAlpha;
                laneReceptors[lane].color = Color.Lerp(idle, laneReceptors[lane].color, t);
            }
            else if (laneReceptorRects[lane].localScale.x != 1f)
            {
                laneReceptorRects[lane].localScale = Vector3.one;
                Color idle = GetLaneAccent(lane);
                idle.a = receptorIdleAlpha;
                laneReceptors[lane].color = idle;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 판정 팝업 (PERFECT / GOOD / MISS)
    // ─────────────────────────────────────────────────────────────────────

    private void EnsureJudgmentPopup()
    {
        if (judgmentPopupText != null) return;

        var go = new GameObject("JudgmentPopup", typeof(RectTransform));
        go.transform.SetParent(playArea, false);

        judgmentPopupText = go.AddComponent<TextMeshProUGUI>();
        judgmentPopupText.alignment = TextAlignmentOptions.Center;
        judgmentPopupText.fontSize = 42f;
        judgmentPopupText.fontStyle = FontStyles.Bold;
        judgmentPopupText.raycastTarget = false;
        judgmentPopupText.text = "";

        judgmentPopupRect = (RectTransform)go.transform;
        judgmentPopupRect.anchorMin = judgmentPopupRect.anchorMax = new Vector2(0.5f, 0.5f);
        judgmentPopupRect.pivot = new Vector2(0.5f, 0.5f);
        judgmentPopupRect.sizeDelta = new Vector2(400f, 80f);

        judgmentPopupGroup = go.AddComponent<CanvasGroup>();
        judgmentPopupGroup.alpha = 0f;
    }

    private void ShowJudgmentPopup(Judgment judgment, int lane)
    {
        if (judgmentPopupText == null) return;

        Color color;
        string label;
        switch (judgment)
        {
            case Judgment.Perfect: color = perfectColor; label = "PERFECT"; break;
            case Judgment.Good: color = goodColor; label = "GOOD"; break;
            default: color = missColor; label = "MISS"; break;
        }

        judgmentPopupText.text = label;
        judgmentPopupText.color = color;

        judgmentPopupBasePos = new Vector2(GetLaneX(Mathf.Max(0, lane)), GetTargetY() + GetScaledNoteSize().y);
        judgmentPopupRect.anchoredPosition = judgmentPopupBasePos;
        judgmentPopupRect.localScale = Vector3.one * 1.25f;
        judgmentPopupGroup.alpha = 1f;
        judgmentPopupTimer = judgmentPopupDuration;
    }

    private void UpdateJudgmentPopup()
    {
        if (judgmentPopupGroup == null || judgmentPopupTimer <= 0f) return;

        judgmentPopupTimer = Mathf.Max(0f, judgmentPopupTimer - Time.unscaledDeltaTime);
        float t = 1f - (judgmentPopupTimer / judgmentPopupDuration);

        float scale = Mathf.Lerp(1.25f, 1f, Mathf.Clamp01(t * 4f));
        judgmentPopupRect.localScale = Vector3.one * scale;
        judgmentPopupRect.anchoredPosition = judgmentPopupBasePos + new Vector2(0f, judgmentPopupRise * t);
        judgmentPopupGroup.alpha = 1f - t;
    }

    private void HideJudgmentPopupImmediate()
    {
        judgmentPopupTimer = 0f;
        if (judgmentPopupGroup != null) judgmentPopupGroup.alpha = 0f;
    }

    // ─────────────────────────────────────────────────────────────────────
    // 콤보 펀치 / 점수 롤업
    // ─────────────────────────────────────────────────────────────────────

    private void TriggerComboPunch()
    {
        if (comboText == null) return;
        if (comboRect == null) comboRect = comboText.rectTransform;

        comboPunchTimer = comboPunchDuration;
    }

    private void UpdateComboPunch()
    {
        if (comboRect == null || comboPunchTimer <= 0f) return;

        comboPunchTimer = Mathf.Max(0f, comboPunchTimer - Time.unscaledDeltaTime);
        float t = comboPunchDuration > 0f ? comboPunchTimer / comboPunchDuration : 0f;
        float scale = Mathf.Lerp(1f, comboPunchScale, t);
        comboRect.localScale = Vector3.one * scale;
    }

    private void UpdateScoreRoll()
    {
        if (scoreText == null) return;
        if (Mathf.Approximately(displayedScore, score)) return;

        displayedScore = Mathf.MoveTowards(displayedScore, score, Mathf.Max(1f, Mathf.Abs(score - displayedScore)) * scoreRollSpeed * Time.unscaledDeltaTime);
        int shown = Mathf.RoundToInt(displayedScore);
        if (shown == lastShownScore) return;

        lastShownScore = shown;
        scoreText.text = shown.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 노트 풀
    // ─────────────────────────────────────────────────────────────────────

    private void ClearNotes()
    {
        for (int i = 0; i < activeNotes.Count; i++)
        {
            ReleaseNote(activeNotes[i]);
        }

        activeNotes.Clear();
        noteHitTimes.Clear();
    }

    private RhythmNote GetPooledNote()
    {
        int lastIndex = notePool.Count - 1;
        if (lastIndex >= 0)
        {
            RhythmNote note = notePool[lastIndex];
            notePool.RemoveAt(lastIndex);
            note.pooled = false;
            return note;
        }

        var root = new GameObject("RhythmNote", typeof(RectTransform));
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);

        // 바깥쪽 글로우 (은은하게 크게, 반투명) — 직사각형 노트이므로 스프라이트 없이 단색 사각형
        var glowObject = new GameObject("Glow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        glowObject.transform.SetParent(root.transform, false);
        var glowImage = glowObject.GetComponent<Image>();
        glowImage.raycastTarget = false;
        var glowRect = (RectTransform)glowObject.transform;
        glowRect.anchorMin = Vector2.zero;
        glowRect.anchorMax = Vector2.one;
        glowRect.pivot = new Vector2(0.5f, 0.5f);
        glowRect.offsetMin = glowRect.offsetMax = Vector2.zero;
        glowRect.localScale = Vector3.one * 1.25f;

        // 안쪽 코어 (선명한 노트 본체) — 직사각형
        var coreObject = new GameObject("Core", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        coreObject.transform.SetParent(root.transform, false);
        var coreImage = coreObject.GetComponent<Image>();
        coreImage.raycastTarget = false;
        var coreRect = (RectTransform)coreObject.transform;
        coreRect.anchorMin = Vector2.zero;
        coreRect.anchorMax = Vector2.one;
        coreRect.pivot = new Vector2(0.5f, 0.5f);
        coreRect.offsetMin = coreRect.offsetMax = Vector2.zero;
        coreRect.localScale = Vector3.one * 0.82f;

        root.transform.SetParent(playArea, false);

        return new RhythmNote
        {
            rectTransform = rootRect,
            coreImage = coreImage,
            glowImage = glowImage,
        };
    }

    private void ReleaseNote(RhythmNote note)
    {
        if (note == null || note.rectTransform == null) return;
        if (note.pooled) return;

        note.pooled = true;
        note.rectTransform.gameObject.SetActive(false);
        notePool.Add(note);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 상태 UI
    // ─────────────────────────────────────────────────────────────────────

    private void UpdateTimerUI()
    {
        if (timerText == null) return;

        float remainingTime = Mathf.Max(0f, totalDuration - elapsedTime);
        int totalSec = Mathf.CeilToInt(remainingTime);
        if (totalSec == lastShownSecond) return;

        lastShownSecond = totalSec;
        int m = totalSec / 60;
        int s = totalSec % 60;
        timerText.text = $"{m:00}:{s:00}";
    }

    private void UpdateResultUI(bool immediateScore)
    {
        if (immediateScore)
        {
            displayedScore = score;
            lastShownScore = score;
            if (scoreText != null) scoreText.text = score.ToString();
        }

        if (comboText != null)
            comboText.text = string.IsNullOrEmpty(lastJudgmentText)
                ? $"{combo} Combo"
                : $"{lastJudgmentText}\n{combo} Combo";
    }

    private void ShowJudgment(Judgment judgment, int lane)
    {
        lastJudgmentText = judgment.ToString();

        Color flashColor;
        switch (judgment)
        {
            case Judgment.Perfect: flashColor = perfectColor; break;
            case Judgment.Good: flashColor = goodColor; break;
            default: flashColor = missColor; break;
        }

        PunchReceptor(lane, flashColor);
        ShowJudgmentPopup(judgment, lane);

        Debug.Log($"[Rhythm] {judgment}");
    }

    private void FinishByResult()
    {
        if (!running) return;

        float hitRatio = noteHitTimes.Count > 0 ? hitCount / (float)noteHitTimes.Count : 0f;
        if (hitRatio >= requiredHitRatio && missCount <= maxMisses)
            Succeed();
        else
            Fail();
    }

    private void Succeed()
    {
        if (!running) return;
        running = false;
        Debug.Log($"[Rhythm] Success! ({difficulty}, score {score}, perfect {perfectCount}, good {goodCount}, miss {missCount})");
        onSuccess?.Invoke();
        EndAndHide();
    }

    private void Fail()
    {
        if (!running) return;
        running = false;
        Debug.Log($"[Rhythm] Fail ({difficulty}, score {score}, perfect {perfectCount}, good {goodCount}, miss {missCount})");
        onFail?.Invoke();
        EndAndHide();
    }

    private void EndAndHide()
    {
        ClearNotes();
        if (rhythmRoot != null) rhythmRoot.SetActive(false);
    }
}
