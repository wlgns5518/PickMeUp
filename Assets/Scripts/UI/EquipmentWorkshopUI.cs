using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 마을 장비제작소에서 여는 창.
//
// 맨 위에서 재료 등급(E~S)을 먼저 고른다 — 무엇이 나오느냐는 이 재료가 정한다.
// 그 아래 두 탭. 자동 제작은 퍼즐 없이 눌러서 바로 만든다 — 등급은 고른 재료 그대로 나온다.
// 수동 제작은 난이도(쉬움~헬)를 고르면 재료 등급을 밑변으로 그 난이도만큼 위로 오를 수 있는
// 등급 확률표가 뜨고, "제작 시작"을 누르면 PuzzleGame이 그 난이도로 열린다. 퍼즐에 성공하면
// 그 표대로 등급을 굴려 장비가 나오고, 실패하면 아무것도 나오지 않는다. 실제 제작/확률 로직은
// Forge가 들고 있다 — 이 창은 고르고 보여주기만 한다.
//
// 퍼즐이 뜨는 동안은 이 창을 접어 둔다. 창이 떠 있으면 배경막이 퍼즐 판을 가린다.
// SummonUI와 같은 방식으로 캔버스부터 코드에서 만든다.
[DisallowMultipleComponent]
public class EquipmentWorkshopUI : MonoBehaviour, IFacilityWindow
{
    private enum Mode { Auto, Manual }

    [Header("Forge")]
    [Tooltip("비워두면 씬에서 찾는다.")]
    [SerializeField] private Forge forge;

    [Header("Font")]
    [Tooltip("한글이 포함되므로 한국어 SDF 폰트를 지정해야 한다 (Assets/Fonts/NotoSansKR-Black SDF).")]
    [SerializeField] private TMP_FontAsset koreanFont;

    [Header("Open State")]
    [Tooltip("장비제작소를 누르지 않아도 처음부터 열려 있게 하려면 켠다.")]
    [SerializeField] private bool openOnStart;

    [Header("Warning Banner")]
    [Tooltip("경고를 띄울 장식 배너(Assets/Image/UI.png). 비워두면 금색 테두리에 검은 판으로 그린다.")]
    [SerializeField] private Sprite bannerSprite;
    [SerializeField] private float bannerWidth = 900f;

    private const float PanelWidth = 820f;
    private static readonly Vector2 PanelPadding = new Vector2(36f, 30f);
    private const float TitleHeight = 62f;
    private const float TabHeight = 78f;
    private const float CloseSize = 56f;
    private const float Gap = 14f;

    private const float MaterialLabelHeight = 30f;
    private const float MaterialRowHeight = 60f;

    private const float DescHeight = 64f;
    private const float CraftButtonHeight = 100f;

    private const float DiffTabHeight = 66f;
    private const float RateHeaderHeight = 34f;
    private const float RateRowHeight = 44f;
    private const float StartButtonHeight = 90f;
    private const float HintHeight = 54f;

    private static readonly PuzzleDifficulty[] Difficulties =
    {
        PuzzleDifficulty.Easy, PuzzleDifficulty.Normal, PuzzleDifficulty.Hard, PuzzleDifficulty.Hell
    };

    // 재료 등급이자 화면에 뜨는 등급표의 행 순서. E가 가장 낮고 S가 가장 높다.
    private static readonly EquipmentGrade[] Grades =
    {
        EquipmentGrade.E, EquipmentGrade.D, EquipmentGrade.C, EquipmentGrade.B, EquipmentGrade.A, EquipmentGrade.S
    };

    // 결과 띠. 화면 위쪽에 붙인다.
    private const float BarWidth = 900f;
    private const float BarHeight = 96f;
    private const float BarPadding = 22f;
    private const float BarTopMargin = 32f;
    private const float BarButtonWidth = 160f;
    private const float BarButtonHeight = 60f;

    private static readonly Color TabSelected = new Color(0.38f, 0.31f, 0.12f, 0.96f);
    private static readonly Color HintText = new Color(0.62f, 0.62f, 0.66f);

    private Canvas canvas;
    private RectTransform canvasRect;
    private GameObject popupRoot;
    private AnnouncementBanner warningBanner;
    private TMP_FontAsset resolvedFont;

    private RectTransform panelRect;
    private RectTransform autoSection;
    private RectTransform manualSection;

    private readonly List<Image> materialTabBackgrounds = new List<Image>();
    private readonly List<Image> modeTabBackgrounds = new List<Image>();
    private readonly List<Image> diffTabBackgrounds = new List<Image>();
    private readonly List<TMP_Text> rateGradeLabels = new List<TMP_Text>();
    private readonly List<TMP_Text> ratePercentLabels = new List<TMP_Text>();

    private GameObject resultBar;
    private TMP_Text resultText;
    private Button startManualButton;
    private TMP_Text autoDescText;

    private Mode mode = Mode.Auto;
    private EquipmentGrade selectedMaterial = EquipmentGrade.E;
    private PuzzleDifficulty selectedDifficulty = PuzzleDifficulty.Easy;

    public bool IsOpen => popupRoot != null && popupRoot.activeSelf;

    private void Awake()
    {
        if (forge == null) forge = FindAnyObjectByType<Forge>(FindObjectsInactive.Include);

        EnsureBuilt();
        SetOpen(openOnStart);

        if (forge != null)
        {
            forge.onCrafted.AddListener(HandleCrafted);
            forge.onFailed.AddListener(HandleFailed);
        }
    }

    private void Update()
    {
        warningBanner?.Tick(Time.deltaTime);
    }

    public void Show()
    {
        EnsureBuilt();
        SetOpen(true);
    }

    public void Hide()
    {
        SetOpen(false);
    }

    public void Toggle()
    {
        if (IsOpen) Hide();
        else Show();
    }

    private void SetOpen(bool open)
    {
        if (popupRoot != null) popupRoot.SetActive(open);
    }

    // ---- 제작 -------------------------------------------------------------

    private void CraftAuto()
    {
        if (forge == null)
        {
            warningBanner?.Show("장비제작소(Forge)를 찾지 못했습니다.");
            return;
        }

        forge.CraftAuto(selectedMaterial);
    }

    private void StartManual()
    {
        if (forge == null)
        {
            warningBanner?.Show("장비제작소(Forge)를 찾지 못했습니다.");
            return;
        }

        // 퍼즐이 뜨는 동안은 창을 접는다. 떠 있으면 배경막이 퍼즐 판을 가린다.
        Hide();
        forge.StartManual(selectedMaterial, selectedDifficulty);
    }

    private void HandleCrafted(CraftedEquipment result)
    {
        Show();
        ShowBar($"{result.name} 제작 완료 — {EquipmentGradeNames.NameOf(result.grade)} 등급", GradeColor(result.grade));
    }

    private void HandleFailed()
    {
        Show();
        ShowBar("제작 실패 — 재료를 잃었습니다.", BattleHudPalette.Dying);
    }

    private void Confirm()
    {
        HideBar();
    }

    // ---- 만들기 -------------------------------------------------------------

    private void EnsureBuilt()
    {
        if (canvas != null) return;

        resolvedFont = HudFactory.ResolveFont(koreanFont, this);

        Transform stale = transform.Find("EquipmentWorkshopCanvas");
        if (stale != null) DestroyImmediate(stale.gameObject);

        materialTabBackgrounds.Clear();
        modeTabBackgrounds.Clear();
        diffTabBackgrounds.Clear();
        rateGradeLabels.Clear();
        ratePercentLabels.Clear();

        BuildCanvas();
        BuildPopup();
        BuildResultBar();

        warningBanner = AnnouncementBanner.Create(canvasRect, resolvedFont, bannerSprite, null, bannerWidth);

        RefreshMode();
        RefreshMaterial();
        RefreshRates();
    }

    private void BuildCanvas()
    {
        var canvasGo = new GameObject("EquipmentWorkshopCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.layer = LayerMask.NameToLayer("UI");

        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 소환(96), 합성(97) 다음.
        canvas.sortingOrder = 98;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasRect = (RectTransform)canvasGo.transform;
    }

    private void BuildPopup()
    {
        RectTransform popup = HudFactory.CreateGroup(canvasRect, "Popup");
        Stretch(popup);
        popupRoot = popup.gameObject;

        BuildBackdrop(popup);
        BuildPanel(popup);
    }

    private void BuildBackdrop(RectTransform popup)
    {
        Image backdrop = HudFactory.CreateImage(popup, "Backdrop", BattleHudPalette.PanelBackdrop);
        backdrop.raycastTarget = true;
        Stretch(backdrop.rectTransform);

        var button = backdrop.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(Hide);
    }

    private void BuildPanel(RectTransform popup)
    {
        float contentWidth = PanelWidth - PanelPadding.x * 2f;

        Image panel = HudFactory.CreateImage(popup, "Panel", BattleHudPalette.PanelBody);
        panel.raycastTarget = true;
        panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;

        float y = PanelPadding.y;

        TMP_Text title = HudFactory.CreateText(panelRect, "Title", resolvedFont, 42f, BattleHudPalette.PanelText);
        title.alignment = TextAlignmentOptions.Left;
        SetTopLeft(title.rectTransform, new Vector2(contentWidth, TitleHeight), new Vector2(PanelPadding.x, -y));
        title.text = "장비제작소";

        BuildCloseButton(y, contentWidth);
        y += TitleHeight + Gap;

        BuildMaterialSelector(y, contentWidth);
        y += MaterialLabelHeight + MaterialRowHeight + Gap;

        BuildModeTabs(y, contentWidth);
        y += TabHeight + Gap;

        autoSection = HudFactory.CreateGroup(panelRect, "Auto");
        SetTopLeft(autoSection, new Vector2(contentWidth, DescHeight + Gap + CraftButtonHeight), new Vector2(PanelPadding.x, -y));
        BuildAutoSection(autoSection, contentWidth);

        manualSection = HudFactory.CreateGroup(panelRect, "Manual");
        SetTopLeft(manualSection, new Vector2(contentWidth,
            DiffTabHeight + Gap + RateHeaderHeight + Grades.Length * RateRowHeight + Gap + StartButtonHeight + Gap + HintHeight),
            new Vector2(PanelPadding.x, -y));
        BuildManualSection(manualSection, contentWidth);

        float panelHeight = y + Mathf.Max(autoSection.sizeDelta.y, manualSection.sizeDelta.y) + PanelPadding.y;
        panelRect.sizeDelta = new Vector2(PanelWidth, panelHeight);
    }

    private void BuildCloseButton(float y, float contentWidth)
    {
        Image background = HudFactory.CreateImage(panelRect, "Close", BattleHudPalette.PanelBackdrop);
        background.raycastTarget = true;
        SetTopLeft(background.rectTransform, new Vector2(CloseSize, CloseSize),
            new Vector2(PanelPadding.x + contentWidth - CloseSize, -y));

        var button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(Hide);

        TMP_Text label = HudFactory.CreateText(background.rectTransform, "Label", resolvedFont, 28f, BattleHudPalette.PanelText);
        Stretch(label.rectTransform);
        label.text = "X";
    }

    private void BuildMaterialSelector(float y, float contentWidth)
    {
        TMP_Text label = HudFactory.CreateText(panelRect, "MaterialLabel", resolvedFont, 24f, HintText);
        label.alignment = TextAlignmentOptions.Left;
        SetTopLeft(label.rectTransform, new Vector2(contentWidth, MaterialLabelHeight), new Vector2(PanelPadding.x, -y));
        label.text = "재료 등급 — 무엇을 넣느냐가 결과를 정합니다";

        float rowY = y + MaterialLabelHeight;
        float tabWidth = (contentWidth - Gap * (Grades.Length - 1)) / Grades.Length;

        for (int i = 0; i < Grades.Length; i++)
        {
            var grade = Grades[i];

            Image background = HudFactory.CreateImage(panelRect, "MaterialTab_" + grade, BattleHudPalette.PortraitFrame);
            background.raycastTarget = true;
            SetTopLeft(background.rectTransform, new Vector2(tabWidth, MaterialRowHeight),
                new Vector2(PanelPadding.x + i * (tabWidth + Gap), -rowY));

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => SelectMaterial(grade));

            TMP_Text gradeLabel = HudFactory.CreateText(background.rectTransform, "Label", resolvedFont, 32f, GradeColor(grade));
            Stretch(gradeLabel.rectTransform);
            gradeLabel.text = EquipmentGradeNames.NameOf(grade);

            materialTabBackgrounds.Add(background);
        }
    }

    private void BuildModeTabs(float y, float contentWidth)
    {
        string[] labels = { "자동 제작", "수동 제작" };
        float tabWidth = (contentWidth - Gap) * 0.5f;

        for (int i = 0; i < labels.Length; i++)
        {
            var thisMode = (Mode)i;

            Image background = HudFactory.CreateImage(panelRect, "ModeTab_" + thisMode, BattleHudPalette.PortraitFrame);
            background.raycastTarget = true;
            SetTopLeft(background.rectTransform, new Vector2(tabWidth, TabHeight),
                new Vector2(PanelPadding.x + i * (tabWidth + Gap), -y));

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => SelectMode(thisMode));

            TMP_Text label = HudFactory.CreateText(background.rectTransform, "Label", resolvedFont, 30f, BattleHudPalette.PanelText);
            Stretch(label.rectTransform);
            label.text = labels[i];

            modeTabBackgrounds.Add(background);
        }
    }

    private void BuildAutoSection(RectTransform section, float contentWidth)
    {
        autoDescText = HudFactory.CreateText(section, "Desc", resolvedFont, 26f, HintText);
        autoDescText.alignment = TextAlignmentOptions.TopLeft;
        SetTopLeft(autoDescText.rectTransform, new Vector2(contentWidth, DescHeight), Vector2.zero);

        Image background = HudFactory.CreateImage(section, "CraftButton", BattleHudPalette.PortraitFrame);
        background.raycastTarget = true;
        SetTopLeft(background.rectTransform, new Vector2(contentWidth, CraftButtonHeight), new Vector2(0f, -(DescHeight + Gap)));

        var button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(CraftAuto);

        TMP_Text label = HudFactory.CreateText(background.rectTransform, "Label", resolvedFont, 34f, BattleHudPalette.Mvp);
        Stretch(label.rectTransform);
        label.text = "제작하기";
    }

    private void BuildManualSection(RectTransform section, float contentWidth)
    {
        string[] diffLabels = { "쉬움", "보통", "어려움", "헬" };
        float diffTabWidth = (contentWidth - Gap * (Difficulties.Length - 1)) / Difficulties.Length;

        for (int i = 0; i < Difficulties.Length; i++)
        {
            var difficulty = Difficulties[i];

            Image background = HudFactory.CreateImage(section, "DiffTab_" + difficulty, BattleHudPalette.PortraitFrame);
            background.raycastTarget = true;
            SetTopLeft(background.rectTransform, new Vector2(diffTabWidth, DiffTabHeight),
                new Vector2(i * (diffTabWidth + Gap), 0f));

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => SelectDifficulty(difficulty));

            TMP_Text label = HudFactory.CreateText(background.rectTransform, "Label", resolvedFont, 26f, BattleHudPalette.PanelText);
            Stretch(label.rectTransform);
            label.text = diffLabels[i];

            diffTabBackgrounds.Add(background);
        }

        float y = DiffTabHeight + Gap;

        TMP_Text header = HudFactory.CreateText(section, "RateHeader", resolvedFont, 24f, HintText);
        header.alignment = TextAlignmentOptions.Left;
        SetTopLeft(header.rectTransform, new Vector2(contentWidth, RateHeaderHeight), new Vector2(0f, -y));
        header.text = "고른 난이도의 등급별 확률";
        y += RateHeaderHeight;

        for (int i = 0; i < Grades.Length; i++)
        {
            RectTransform row = HudFactory.CreateGroup(section, "Rate_" + Grades[i]);
            SetTopLeft(row, new Vector2(contentWidth, RateRowHeight), new Vector2(0f, -y));

            TMP_Text grade = HudFactory.CreateText(row, "Grade", resolvedFont, 27f, BattleHudPalette.PanelText);
            grade.alignment = TextAlignmentOptions.Left;
            Stretch(grade.rectTransform);
            grade.text = EquipmentGradeNames.NameOf(Grades[i]);
            grade.color = GradeColor(Grades[i]);

            TMP_Text percent = HudFactory.CreateText(row, "Percent", resolvedFont, 27f, BattleHudPalette.PanelText);
            percent.alignment = TextAlignmentOptions.Right;
            Stretch(percent.rectTransform);
            percent.color = GradeColor(Grades[i]);

            rateGradeLabels.Add(grade);
            ratePercentLabels.Add(percent);

            y += RateRowHeight;
        }

        y += Gap;

        Image startBackground = HudFactory.CreateImage(section, "StartButton", BattleHudPalette.PortraitFrame);
        startBackground.raycastTarget = true;
        SetTopLeft(startBackground.rectTransform, new Vector2(contentWidth, StartButtonHeight), new Vector2(0f, -y));

        startManualButton = startBackground.gameObject.AddComponent<Button>();
        startManualButton.targetGraphic = startBackground;
        startManualButton.onClick.AddListener(StartManual);

        TMP_Text startLabel = HudFactory.CreateText(startBackground.rectTransform, "Label", resolvedFont, 34f, BattleHudPalette.Mvp);
        Stretch(startLabel.rectTransform);
        startLabel.text = "제작 시작 (퍼즐)";
        y += StartButtonHeight + Gap;

        TMP_Text hint = HudFactory.CreateText(section, "Hint", resolvedFont, 22f, HintText);
        hint.alignment = TextAlignmentOptions.TopLeft;
        SetTopLeft(hint.rectTransform, new Vector2(contentWidth, HintHeight), new Vector2(0f, -y));
        hint.text = "퍼즐을 맞추면 장비가 나옵니다. 시간 안에 못 맞추면 재료만 잃습니다.";
    }

    // ---- 결과 띠 ------------------------------------------------------------

    private void BuildResultBar()
    {
        Image bar = HudFactory.CreateImage(canvasRect, "ResultBar", BattleHudPalette.PanelBody);
        bar.raycastTarget = true;
        RectTransform barRect = bar.rectTransform;
        barRect.anchorMin = new Vector2(0.5f, 1f);
        barRect.anchorMax = new Vector2(0.5f, 1f);
        barRect.pivot = new Vector2(0.5f, 1f);
        barRect.sizeDelta = new Vector2(BarWidth, BarHeight);
        barRect.anchoredPosition = new Vector2(0f, -BarTopMargin);
        resultBar = bar.gameObject;

        float textWidth = BarWidth - BarPadding * 2f - BarButtonWidth - Gap;

        resultText = HudFactory.CreateText(barRect, "Result", resolvedFont, 28f, BattleHudPalette.PanelText);
        resultText.alignment = TextAlignmentOptions.Left;
        SetLeftMiddle(resultText.rectTransform, new Vector2(textWidth, BarHeight), BarPadding);

        float confirmX = BarWidth - BarPadding - BarButtonWidth;
        Image confirmBackground = HudFactory.CreateImage(barRect, "Confirm", TabSelected);
        confirmBackground.raycastTarget = true;
        SetLeftMiddle(confirmBackground.rectTransform, new Vector2(BarButtonWidth, BarButtonHeight), confirmX);

        var confirmButton = confirmBackground.gameObject.AddComponent<Button>();
        confirmButton.targetGraphic = confirmBackground;
        confirmButton.onClick.AddListener(Confirm);

        TMP_Text confirmLabel = HudFactory.CreateText(confirmBackground.rectTransform, "Label", resolvedFont, 26f, BattleHudPalette.PanelText);
        Stretch(confirmLabel.rectTransform);
        confirmLabel.text = "확인";

        resultBar.SetActive(false);
    }

    private void ShowBar(string message, Color color)
    {
        if (resultBar == null) return;

        resultBar.SetActive(true);
        resultText.text = message;
        resultText.color = color;
    }

    private void HideBar()
    {
        if (resultBar != null) resultBar.SetActive(false);
    }

    // ---- 갱신 ---------------------------------------------------------------

    private void SelectMode(Mode newMode)
    {
        mode = newMode;
        RefreshMode();
    }

    private void RefreshMode()
    {
        if (autoSection != null) autoSection.gameObject.SetActive(mode == Mode.Auto);
        if (manualSection != null) manualSection.gameObject.SetActive(mode == Mode.Manual);

        for (int i = 0; i < modeTabBackgrounds.Count; i++)
            modeTabBackgrounds[i].color = (Mode)i == mode ? TabSelected : BattleHudPalette.PortraitFrame;
    }

    private void SelectMaterial(EquipmentGrade material)
    {
        selectedMaterial = material;
        RefreshMaterial();
        RefreshRates();
    }

    private void RefreshMaterial()
    {
        for (int i = 0; i < materialTabBackgrounds.Count; i++)
            materialTabBackgrounds[i].color = Grades[i] == selectedMaterial ? TabSelected : BattleHudPalette.PortraitFrame;

        if (autoDescText != null)
            autoDescText.text = $"퍼즐 없이 바로 만듭니다. 등급은 넣은 재료({EquipmentGradeNames.NameOf(selectedMaterial)}) 그대로 나옵니다.";
    }

    private void SelectDifficulty(PuzzleDifficulty difficulty)
    {
        selectedDifficulty = difficulty;
        RefreshRates();
    }

    private void RefreshRates()
    {
        for (int i = 0; i < diffTabBackgrounds.Count; i++)
            diffTabBackgrounds[i].color = Difficulties[i] == selectedDifficulty ? TabSelected : BattleHudPalette.PortraitFrame;

        for (int i = 0; i < Grades.Length; i++)
            ratePercentLabels[i].text = EquipmentCraftTable.PercentText(selectedMaterial, selectedDifficulty, Grades[i]);
    }

    private static Color GradeColor(EquipmentGrade grade)
    {
        switch (grade)
        {
            case EquipmentGrade.S: return BattleHudPalette.Mvp;
            case EquipmentGrade.A: return new Color(0.80f, 0.55f, 1.00f);
            case EquipmentGrade.B: return new Color(0.55f, 0.75f, 1.00f);
            case EquipmentGrade.C: return new Color(0.55f, 0.85f, 0.60f);
            case EquipmentGrade.D: return new Color(0.75f, 0.75f, 0.75f);
            default:                       return BattleHudPalette.PanelText;
        }
    }

    // ---- 자리 잡기 ------------------------------------------------------------

    private static void SetTopLeft(RectTransform rect, Vector2 size, Vector2 offset)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = offset;
    }

    private static void SetLeftMiddle(RectTransform rect, Vector2 size, float x)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(x, 0f);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
