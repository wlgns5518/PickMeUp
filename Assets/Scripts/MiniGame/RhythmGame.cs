using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
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
    [SerializeField] private float noteWidth = 480f;
    [SerializeField] private float noteHeight = 24f;
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
    [SerializeField] private float laneFlashDuration = 0.14f;
    [SerializeField] private Color laneFlashColor = new Color(1f, 1f, 1f, 0.35f);

    [Header("Clear Rule")]
    [Range(0f, 1f)] [SerializeField] private float requiredHitRatio = 0.75f;
    [SerializeField] private int maxMisses = 4;

    [Header("UI")]
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text comboText;

    [Header("Events")]
    public UnityEvent onSuccess;
    public UnityEvent onFail;

    private readonly List<float> noteHitTimes = new List<float>();
    private readonly List<RhythmNote> activeNotes = new List<RhythmNote>();

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
    private Image[] laneFlashImages;
    private float[] laneFlashTimers;

    private enum Judgment
    {
        Perfect,
        Good,
        Miss,
    }

    private sealed class RhythmNote
    {
        public RectTransform rectTransform;
        public float spawnTime;
        public float hitTime;
        public int lane;
    }

    private void Start()
    {
        if (autoStart) StartRhythm();
    }

    private void Update()
    {
        if (!running) return;

        elapsedTime += Time.deltaTime;
        SpawnDueNotes();
        UpdateActiveNotes();
        UpdateLaneFlashes();
        if (!running) return;

        UpdateTimerUI();

        if (Input.GetMouseButtonDown(0))
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
        lastShownSecond = -1;
        lastJudgmentText = "";
        running = true;

        UpdateTimerUI();
        UpdateResultUI();
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
        var noteObject = new GameObject("RhythmNote", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        noteObject.transform.SetParent(playArea, false);

        var noteRect = (RectTransform)noteObject.transform;
        var image = noteObject.GetComponent<Image>();
        image.color = new Color(1f, 0.86f, 0.25f, 1f);
        image.raycastTarget = false;

        noteRect.anchorMin = noteRect.anchorMax = new Vector2(0.5f, 0.5f);
        noteRect.pivot = new Vector2(0.5f, 0.5f);
        noteRect.sizeDelta = GetScaledNoteSize();

        var note = new RhythmNote
        {
            rectTransform = noteRect,
            spawnTime = hitTime - noteTravelTime,
            hitTime = hitTime,
            lane = GetLane(index),
        };

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
            SetNotePosition(note, progress);
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
        float laneX = GetLaneX(note.lane);
        float y = Mathf.Lerp(spawnY, targetY, Mathf.Clamp01(progress));
        note.rectTransform.anchoredPosition = new Vector2(laneX, y);
    }

    private float GetLaneX(int lane)
    {
        Rect rect = playArea.rect;
        int safeLaneCount = Mathf.Max(1, laneCount);
        float width = Mathf.Max(1f, rect.width - GetScaledNoteSize().x);
        float left = -width * 0.5f;
        float step = safeLaneCount > 1 ? width / (safeLaneCount - 1) : 0f;
        return left + step * Mathf.Clamp(lane, 0, safeLaneCount - 1);
    }

    private float GetTargetY()
    {
        Rect rect = playArea.rect;
        return rect.yMin + rect.height * targetLineYRatio;
    }

    private void TryHitNote()
    {
        if (!TryGetClickedLane(out int clickedLane))
            return;

        FlashLane(clickedLane);

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
                ShowJudgment(Judgment.Miss);
                UpdateResultUI();
            }
            return;
        }

        RhythmNote note = activeNotes[bestIndex];
        activeNotes.RemoveAt(bestIndex);
        if (note.rectTransform != null)
            Destroy(note.rectTransform.gameObject);

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

        ShowJudgment(judgment);
        UpdateResultUI();

        if (resolvedNotes >= noteHitTimes.Count)
            FinishByResult();
    }

    private bool TryGetClickedLane(out int lane)
    {
        lane = -1;
        Camera uiCamera = GetUiCamera();
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                playArea, Input.mousePosition, uiCamera, out Vector2 localPoint))
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
        Canvas canvas = playArea != null ? playArea.GetComponentInParent<Canvas>() : null;
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        return canvas.worldCamera;
    }

    private void ResolveMiss(int index)
    {
        RhythmNote note = activeNotes[index];
        activeNotes.RemoveAt(index);
        if (note != null && note.rectTransform != null)
            Destroy(note.rectTransform.gameObject);

        missCount++;
        resolvedNotes++;
        combo = 0;
        ShowJudgment(Judgment.Miss);
        UpdateResultUI();
    }

    private Judgment GetJudgment(float timing)
    {
        if (timing <= perfectWindow) return Judgment.Perfect;
        if (timing <= goodWindow) return Judgment.Good;
        return Judgment.Miss;
    }

    private Vector2 GetScaledNoteSize()
    {
        Rect rect = playArea.rect;
        float scaleX = referenceResolution.x > 0f ? rect.width / referenceResolution.x : 1f;
        float scaleY = referenceResolution.y > 0f ? rect.height / referenceResolution.y : 1f;
        return new Vector2(
            Mathf.Max(1f, noteWidth * scaleX),
            Mathf.Max(1f, noteHeight * scaleY));
    }

    private float GetScaledTargetLineHeight()
    {
        Rect rect = playArea.rect;
        float scaleY = referenceResolution.y > 0f ? rect.height / referenceResolution.y : 1f;
        return Mathf.Max(1f, targetLineHeight * scaleY);
    }

    private float GetScaledClickLineTolerance()
    {
        Rect rect = playArea.rect;
        float scaleY = referenceResolution.y > 0f ? rect.height / referenceResolution.y : 1f;
        return Mathf.Max(1f, clickLineTolerance * scaleY);
    }

    private void PrepareLayout()
    {
        EnsureTargetLine();
        EnsureLaneFlashImages();
        UpdateTargetLineLayout();
        UpdateLaneFlashLayout();
    }

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

    private void EnsureLaneFlashImages()
    {
        int safeLaneCount = Mathf.Max(1, laneCount);
        if (laneFlashImages != null && laneFlashImages.Length == safeLaneCount)
            return;

        if (laneFlashImages != null)
        {
            for (int i = 0; i < laneFlashImages.Length; i++)
            {
                if (laneFlashImages[i] != null)
                    Destroy(laneFlashImages[i].gameObject);
            }
        }

        laneFlashImages = new Image[safeLaneCount];
        laneFlashTimers = new float[safeLaneCount];

        for (int i = 0; i < safeLaneCount; i++)
        {
            var flashObject = new GameObject($"LaneFlash_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            flashObject.transform.SetParent(playArea, false);
            flashObject.transform.SetAsFirstSibling();

            var image = flashObject.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = false;
            laneFlashImages[i] = image;
        }
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

    private void UpdateLaneFlashLayout()
    {
        if (laneFlashImages == null) return;

        Rect rect = playArea.rect;
        int safeLaneCount = Mathf.Max(1, laneFlashImages.Length);
        float laneWidth = rect.width / safeLaneCount;

        for (int i = 0; i < laneFlashImages.Length; i++)
        {
            if (laneFlashImages[i] == null) continue;

            var flashRect = (RectTransform)laneFlashImages[i].transform;
            flashRect.anchorMin = flashRect.anchorMax = new Vector2(0.5f, 0.5f);
            flashRect.pivot = new Vector2(0.5f, 0.5f);
            flashRect.sizeDelta = new Vector2(laneWidth, rect.height);
            flashRect.anchoredPosition = new Vector2(rect.xMin + laneWidth * (i + 0.5f), 0f);
        }
    }

    private void FlashLane(int lane)
    {
        if (laneFlashTimers == null || lane < 0 || lane >= laneFlashTimers.Length)
            return;

        laneFlashTimers[lane] = laneFlashDuration;
        if (laneFlashImages != null && lane < laneFlashImages.Length && laneFlashImages[lane] != null)
            laneFlashImages[lane].color = laneFlashColor;
    }

    private void UpdateLaneFlashes()
    {
        if (laneFlashImages == null || laneFlashTimers == null) return;

        for (int i = 0; i < laneFlashImages.Length; i++)
        {
            if (laneFlashImages[i] == null) continue;

            if (laneFlashTimers[i] > 0f)
            {
                laneFlashTimers[i] = Mathf.Max(0f, laneFlashTimers[i] - Time.deltaTime);
                float alpha = laneFlashDuration > 0f ? laneFlashTimers[i] / laneFlashDuration : 0f;
                Color color = laneFlashColor;
                color.a *= alpha;
                laneFlashImages[i].color = color;
            }
            else
            {
                laneFlashImages[i].color = Color.clear;
            }
        }
    }

    private void ClearNotes()
    {
        for (int i = 0; i < activeNotes.Count; i++)
        {
            if (activeNotes[i] != null && activeNotes[i].rectTransform != null)
                Destroy(activeNotes[i].rectTransform.gameObject);
        }

        activeNotes.Clear();
        noteHitTimes.Clear();
    }

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

    private void UpdateResultUI()
    {
        if (scoreText != null)
            scoreText.text = score.ToString();

        if (comboText != null)
            comboText.text = string.IsNullOrEmpty(lastJudgmentText)
                ? $"{combo} Combo"
                : $"{lastJudgmentText}\n{combo} Combo";
    }

    private void ShowJudgment(Judgment judgment)
    {
        lastJudgmentText = judgment.ToString();

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
