using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 메인 씬에서 들어갈 층을 고르는 화면.
//
// 층은 자동으로 넘어가지 않는다. 여기서 직접 고른 뒤 전투 씬으로 들어가고,
// 전투가 끝나면 다시 이 화면으로 돌아온다.
//
// BattleHud와 같은 방식으로 캔버스부터 코드에서 만든다.
// 이쪽은 클릭을 받아야 하므로 BattleHud와 달리 GraphicRaycaster를 붙인다.
[DisallowMultipleComponent]
public class FloorSelectUI : MonoBehaviour
{
    [Header("Scene")]
    [Tooltip("모든 층이 함께 쓰는 전투 씬. 난이도는 고른 층 번호로 조정된다. Build Settings에 등록돼 있어야 한다.")]
    [SerializeField] private string battleSceneName = "Floor1";

    [Header("Font")]
    [Tooltip("한글이 포함되므로 한국어 SDF 폰트를 지정해야 한다 (Assets/Fonts/NotoSansKR-Black SDF).")]
    [SerializeField] private TMP_FontAsset koreanFont;

    [Header("Layout")]
    [Tooltip("화면에 늘어놓을 층 버튼 개수. 층 씬(Floor1~Floor9)이 있는 만큼만 의미가 있다.")]
    [SerializeField] private int visibleFloorCount = 9;
    [SerializeField] private Vector2 panelMargin = new Vector2(40f, 40f);

    private const float ButtonWidth = 220f;
    private const float ButtonHeight = 64f;
    private const float ButtonSpacing = 10f;

    private Canvas canvas;
    private RectTransform canvasRect;
    private TMP_FontAsset resolvedFont;
    private readonly List<Button> floorButtons = new List<Button>();
    private readonly List<TMP_Text> floorLabels = new List<TMP_Text>();

    private void Awake()
    {
        EnsureBuilt();
    }

    private void OnEnable()
    {
        EnsureBuilt();
        RefreshButtons();
    }

    // 전투에서 돌아왔을 때 해금 상태가 바뀌었을 수 있으므로 다시 그린다.
    private void Start()
    {
        RefreshButtons();
    }

    private void EnsureBuilt()
    {
        if (canvas != null) return;

        resolvedFont = HudFactory.ResolveFont(koreanFont, this);

        // 참조만 잃고 남아 있는 이전 캔버스를 먼저 치운다(도메인 리로드 대비).
        Transform stale = transform.Find("FloorSelectCanvas");
        if (stale != null) DestroyImmediate(stale.gameObject);

        floorButtons.Clear();
        floorLabels.Clear();
        BuildCanvas();
        BuildButtons();
    }

    private void BuildCanvas()
    {
        var canvasGo = new GameObject("FloorSelectCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.layer = LayerMask.NameToLayer("UI");

        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasRect = (RectTransform)canvasGo.transform;
    }

    private void BuildButtons()
    {
        RectTransform root = HudFactory.CreateGroup(canvasRect, "FloorList");
        root.anchorMin = new Vector2(0f, 1f);
        root.anchorMax = new Vector2(0f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = new Vector2(panelMargin.x, -panelMargin.y);

        TMP_Text title = HudFactory.CreateText(root, "Title", resolvedFont, 34f, BattleHudPalette.PanelText);
        title.alignment = TextAlignmentOptions.TopLeft;
        SetTopLeft(title.rectTransform, new Vector2(ButtonWidth, 44f), Vector2.zero);
        title.text = "층 선택";

        for (int i = 0; i < Mathf.Max(1, visibleFloorCount); i++)
        {
            int floor = FloorProgress.FirstFloor + i;
            float y = -(52f + i * (ButtonHeight + ButtonSpacing));

            Image background = HudFactory.CreateImage(root, "Floor_" + floor, BattleHudPalette.PanelBody);
            background.raycastTarget = true;
            SetTopLeft(background.rectTransform, new Vector2(ButtonWidth, ButtonHeight), new Vector2(0f, y));

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            TMP_Text label = HudFactory.CreateText(background.rectTransform, "Label", resolvedFont, 26f, BattleHudPalette.PanelText);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            // 클로저가 반복 변수를 붙잡지 않도록 지역 변수에 복사해 넘긴다.
            int captured = floor;
            button.onClick.AddListener(() => EnterFloor(captured));

            floorButtons.Add(button);
            floorLabels.Add(label);
        }
    }

    private void RefreshButtons()
    {
        for (int i = 0; i < floorButtons.Count; i++)
        {
            int floor = FloorProgress.FirstFloor + i;
            bool unlocked = FloorProgress.IsUnlocked(floor);
            bool cleared = floor <= FloorProgress.HighestCleared;

            floorButtons[i].interactable = unlocked;

            TMP_Text label = floorLabels[i];
            if (!unlocked) label.text = floor + "층  (잠김)";
            else if (cleared) label.text = floor + "층  ✓";
            else label.text = floor + "층";

            label.color = unlocked ? BattleHudPalette.PanelText : BattleHudPalette.Dying;
        }
    }

    private void EnterFloor(int floor)
    {
        if (!FloorProgress.TrySelect(floor))
        {
            Debug.LogWarning($"[FloorSelectUI] 아직 열리지 않은 층입니다: {floor}층");
            return;
        }

        if (string.IsNullOrEmpty(battleSceneName))
        {
            Debug.LogError("[FloorSelectUI] 전투 씬 이름이 비어 있습니다.");
            return;
        }

        // 모든 층이 같은 씬을 쓴다. 고른 층은 FloorProgress.SelectedFloor로 전달되고,
        // 스포너가 그 값으로 적 수와 능력치를 키운다.
        string sceneName = battleSceneName;
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[FloorSelectUI] 씬 '{sceneName}'을 불러올 수 없습니다. Build Settings에 등록됐는지 확인하세요.");
            return;
        }

        SceneManager.LoadScene(sceneName);
    }

    private static void SetTopLeft(RectTransform rect, Vector2 size, Vector2 offset)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = offset;
    }
}
