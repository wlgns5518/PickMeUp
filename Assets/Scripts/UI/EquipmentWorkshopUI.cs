using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 마을 장비제작소에서 여는 창.
//
// 왼쪽 칸에서 만들 무기를 고른다. 무기는 종류(한손검·도끼·방패 …) 탭으로 나뉘어 있고, 목록은
// Forge.CollectCraftable이 WeaponCatalog에서 뽑아 준다 — 무기 에셋이 늘면 여기도 따라 는다.
//
// 오른쪽 칸 맨 위에서 재료 등급(E~S)을 고른다 — 얼마나 좋게 나오느냐는 이 재료가 정한다.
// 그 아래 두 탭. 자동 제작은 퍼즐 없이 눌러서 바로 만든다 — 등급은 고른 재료 그대로 나온다.
// 수동 제작은 난이도(쉬움~헬)를 고르면 재료 등급을 밑변으로 그 난이도만큼 위로 오를 수 있는
// 등급 확률표가 뜨고, "제작 시작"을 누르면 PuzzleGame이 그 난이도로 열린다. 퍼즐에 성공하면
// 그 표대로 등급을 굴려 장비가 나오고, 실패하면 아무것도 나오지 않는다. 실제 제작/확률 로직은
// Forge가 들고 있다 — 이 창은 고르고 보여주기만 한다.
//
// 퍼즐이 뜨는 동안은 이 창을 접어 둔다. 창이 떠 있으면 배경막이 퍼즐 판을 가린다.
// SummonUI와 같은 방식으로 캔버스부터 코드에서 만든다.
[DisallowMultipleComponent]
public class EquipmentWorkshopUI : FacilityWindow
{
    private enum Mode { Auto, Manual }

    [Header("Forge")]
    [Tooltip("비워두면 씬에서 찾는다.")]
    [SerializeField] private Forge forge;

    [Header("Open State")]
    [Tooltip("장비제작소를 누르지 않아도 처음부터 열려 있게 하려면 켠다.")]
    [SerializeField] private bool openOnStart;

    [Header("Warning Banner")]
    [Tooltip("경고를 띄울 장식 배너(Assets/Image/UI.png). 비워두면 금색 테두리에 검은 판으로 그린다.")]
    [SerializeField] private Sprite bannerSprite;
    [SerializeField] private float bannerWidth = 900f;

    // 오른쪽 칸(재료·제작 방식) 너비. 왼쪽 무기 칸은 그 앞에 붙는다.
    private const float ControlColumnWidth = 748f;
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

    // 왼쪽 무기 칸.
    private const float WeaponColumnWidth = 440f;
    private const float ColumnGap = 28f;
    private const int TypeTabColumns = 3;
    private const float TypeTabHeight = 52f;
    private const float WeaponRowHeight = 60f;
    private const float WeaponRowGap = 10f;
    private const float WeaponLabelInset = 18f;

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

    private AnnouncementBanner warningBanner;

    private RectTransform panelRect;
    private RectTransform autoSection;
    private RectTransform manualSection;

    private readonly List<Image> materialTabBackgrounds = new List<Image>();
    private readonly List<Image> modeTabBackgrounds = new List<Image>();
    private readonly List<Image> diffTabBackgrounds = new List<Image>();
    private readonly List<TMP_Text> rateGradeLabels = new List<TMP_Text>();
    private readonly List<TMP_Text> ratePercentLabels = new List<TMP_Text>();

    // 만들 수 있는 무기와, 그 무기들이 걸친 종류(열거형 순서). 종류 탭·목록 묶음은 weaponTypes와 같은 순서다.
    private readonly List<WeaponDefinition> craftableWeapons = new List<WeaponDefinition>();
    private readonly List<WeaponType> weaponTypes = new List<WeaponType>();
    private readonly List<Image> typeTabBackgrounds = new List<Image>();
    private readonly List<RectTransform> weaponLists = new List<RectTransform>();
    private readonly List<WeaponDefinition> weaponButtonTargets = new List<WeaponDefinition>();
    private readonly List<Image> weaponButtonBackgrounds = new List<Image>();

    private GameObject resultBar;
    private TMP_Text resultText;
    private Button startManualButton;
    private TMP_Text autoDescText;
    private TMP_Text autoCraftLabel;
    private TMP_Text startManualLabel;

    private Mode mode = Mode.Auto;
    private WeaponDefinition selectedWeapon;
    private WeaponType selectedType;
    private EquipmentGrade selectedMaterial = EquipmentGrade.E;
    private PuzzleDifficulty selectedDifficulty = PuzzleDifficulty.Easy;

    protected override string CanvasName => "EquipmentWorkshopCanvas";
    // 소환(96), 합성(97) 다음.
    protected override int SortingOrder => 98;

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

    protected override void TickWindow(float deltaTime)
    {
        warningBanner?.Tick(deltaTime);
    }

    public override void Show()
    {
        EnsureBuilt();
        SetOpen(true);
    }

    public override void Hide()
    {
        SetOpen(false);
    }

    // ---- 제작 -------------------------------------------------------------

    private void CraftAuto()
    {
        if (forge == null)
        {
            warningBanner?.Show("장비제작소(Forge)를 찾지 못했습니다.");
            return;
        }

        if (selectedWeapon == null)
        {
            warningBanner?.Show("제작할 무기를 고르세요.");
            return;
        }

        forge.CraftAuto(selectedWeapon, selectedMaterial);
    }

    private void StartManual()
    {
        if (forge == null)
        {
            warningBanner?.Show("장비제작소(Forge)를 찾지 못했습니다.");
            return;
        }

        if (selectedWeapon == null)
        {
            warningBanner?.Show("제작할 무기를 고르세요.");
            return;
        }

        // 퍼즐이 뜨는 동안은 창을 접는다. 떠 있으면 배경막이 퍼즐 판을 가린다.
        Hide();
        forge.StartManual(selectedWeapon, selectedMaterial, selectedDifficulty);
    }

    private void HandleCrafted(CraftedEquipment result)
    {
        Show();
        ShowBar($"{result.name} ({EquipmentGradeNames.NameOf(result.grade)}) 제작 완료 — 무기창고에 보관했습니다", GradeColor(result.grade));
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

    protected override void BuildWindow()
    {
        materialTabBackgrounds.Clear();
        modeTabBackgrounds.Clear();
        diffTabBackgrounds.Clear();
        rateGradeLabels.Clear();
        ratePercentLabels.Clear();
        typeTabBackgrounds.Clear();
        weaponLists.Clear();
        weaponButtonTargets.Clear();
        weaponButtonBackgrounds.Clear();

        CollectWeapons();

        BuildCanvas();
        BuildPopup();
        BuildResultBar();

        warningBanner = AnnouncementBanner.Create(canvasRect, resolvedFont, bannerSprite, null, bannerWidth);

        RefreshMode();
        RefreshWeapon();
        RefreshMaterial();
        RefreshRates();
    }

    // 만들 수 있는 무기를 모으고 종류별로 나눈다. 고른 무기가 목록에 없으면 첫 종류의 첫 무기로 둔다.
    private void CollectWeapons()
    {
        Forge.CollectCraftable(craftableWeapons);

        weaponTypes.Clear();
        for (int i = 0; i < craftableWeapons.Count; i++)
        {
            if (!weaponTypes.Contains(craftableWeapons[i].type)) weaponTypes.Add(craftableWeapons[i].type);
        }
        weaponTypes.Sort();

        if (selectedWeapon == null || !craftableWeapons.Contains(selectedWeapon))
            selectedWeapon = weaponTypes.Count > 0 ? FirstWeaponOf(weaponTypes[0]) : null;
        if (selectedWeapon != null) selectedType = selectedWeapon.type;
    }

    private WeaponDefinition FirstWeaponOf(WeaponType type)
    {
        for (int i = 0; i < craftableWeapons.Count; i++)
        {
            if (craftableWeapons[i].type == type) return craftableWeapons[i];
        }
        return null;
    }

    private void BuildPopup()
    {
        BuildPanel(BuildPopupRoot());
    }

    private void BuildPanel(RectTransform popup)
    {
        float controlX = PanelPadding.x + WeaponColumnWidth + ColumnGap;
        float panelWidth = controlX + ControlColumnWidth + PanelPadding.x;
        float contentWidth = panelWidth - PanelPadding.x * 2f;

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
        HudFactory.SetTopLeft(title.rectTransform, new Vector2(contentWidth, TitleHeight), new Vector2(PanelPadding.x, -y));
        title.text = "장비제작소";

        BuildCloseButton(y, contentWidth);
        y += TitleHeight + Gap;

        float columnTop = y;

        BuildMaterialSelector(controlX, y, ControlColumnWidth);
        y += MaterialLabelHeight + MaterialRowHeight + Gap;

        BuildModeTabs(controlX, y, ControlColumnWidth);
        y += TabHeight + Gap;

        autoSection = HudFactory.CreateGroup(panelRect, "Auto");
        HudFactory.SetTopLeft(autoSection, new Vector2(ControlColumnWidth, DescHeight + Gap + CraftButtonHeight), new Vector2(controlX, -y));
        BuildAutoSection(autoSection, ControlColumnWidth);

        manualSection = HudFactory.CreateGroup(panelRect, "Manual");
        HudFactory.SetTopLeft(manualSection, new Vector2(ControlColumnWidth,
            DiffTabHeight + Gap + RateHeaderHeight + Grades.Length * RateRowHeight + Gap + StartButtonHeight + Gap + HintHeight),
            new Vector2(controlX, -y));
        BuildManualSection(manualSection, ControlColumnWidth);

        // 무기 칸은 오른쪽 칸 높이에 맞춘다 — 목록이 몇 줄까지 들어가는지가 여기서 정해진다.
        float columnBottom = y + Mathf.Max(autoSection.sizeDelta.y, manualSection.sizeDelta.y);
        BuildWeaponColumn(PanelPadding.x, columnTop, columnBottom);

        Image divider = HudFactory.CreateImage(panelRect, "Divider", BattleHudPalette.PortraitFrame);
        HudFactory.SetTopLeft(divider.rectTransform, new Vector2(2f, columnBottom - columnTop),
            new Vector2(PanelPadding.x + WeaponColumnWidth + ColumnGap * 0.5f - 1f, -columnTop));

        panelRect.sizeDelta = new Vector2(panelWidth, columnBottom + PanelPadding.y);
    }

    private void BuildWeaponColumn(float x, float top, float bottom)
    {
        float y = top;

        TMP_Text label = HudFactory.CreateText(panelRect, "WeaponLabel", resolvedFont, 24f, HintText);
        label.alignment = TextAlignmentOptions.Left;
        HudFactory.SetTopLeft(label.rectTransform, new Vector2(WeaponColumnWidth, MaterialLabelHeight), new Vector2(x, -y));
        y += MaterialLabelHeight;

        if (weaponTypes.Count == 0)
        {
            label.text = "만들 수 있는 무기가 없습니다";
            return;
        }
        label.text = "제작할 무기";

        float tabWidth = (WeaponColumnWidth - Gap * (TypeTabColumns - 1)) / TypeTabColumns;
        for (int i = 0; i < weaponTypes.Count; i++)
        {
            var type = weaponTypes[i];
            int column = i % TypeTabColumns;
            int row = i / TypeTabColumns;

            Image background = HudFactory.CreateImage(panelRect, "TypeTab_" + type, BattleHudPalette.PortraitFrame);
            background.raycastTarget = true;
            HudFactory.SetTopLeft(background.rectTransform, new Vector2(tabWidth, TypeTabHeight),
                new Vector2(x + column * (tabWidth + Gap), -(y + row * (TypeTabHeight + Gap))));

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => SelectWeaponType(type));

            TMP_Text tabLabel = HudFactory.CreateText(background.rectTransform, "Label", resolvedFont, 24f, BattleHudPalette.PanelText);
            HudFactory.Stretch(tabLabel.rectTransform);
            tabLabel.text = CharacterRules.Korean(type);

            typeTabBackgrounds.Add(background);
        }

        int tabRows = Mathf.CeilToInt(weaponTypes.Count / (float)TypeTabColumns);
        y += tabRows * TypeTabHeight + (tabRows - 1) * Gap + Gap;

        BuildWeaponLists(x, y, bottom - y);
    }

    // 종류마다 목록 한 벌씩 미리 깔아 두고 탭에 따라 켜고 끈다.
    // 한 줄로 다 안 들어가는 종류는 칸을 나눠 여러 줄로 늘어놓는다.
    private void BuildWeaponLists(float x, float y, float height)
    {
        int maxRows = Mathf.Max(1, Mathf.FloorToInt((height + WeaponRowGap) / (WeaponRowHeight + WeaponRowGap)));
        var weapons = new List<WeaponDefinition>();

        for (int t = 0; t < weaponTypes.Count; t++)
        {
            WeaponType type = weaponTypes[t];

            weapons.Clear();
            for (int i = 0; i < craftableWeapons.Count; i++)
            {
                if (craftableWeapons[i].type == type) weapons.Add(craftableWeapons[i]);
            }

            RectTransform list = HudFactory.CreateGroup(panelRect, "Weapons_" + type);
            HudFactory.SetTopLeft(list, new Vector2(WeaponColumnWidth, height), new Vector2(x, -y));

            int columns = Mathf.CeilToInt(weapons.Count / (float)maxRows);
            float itemWidth = (WeaponColumnWidth - Gap * (columns - 1)) / columns;

            for (int i = 0; i < weapons.Count; i++)
            {
                var weapon = weapons[i];
                int column = i % columns;
                int row = i / columns;

                Image background = HudFactory.CreateImage(list, "Weapon_" + weapon.name, BattleHudPalette.PortraitFrame);
                background.raycastTarget = true;
                HudFactory.SetTopLeft(background.rectTransform, new Vector2(itemWidth, WeaponRowHeight),
                    new Vector2(column * (itemWidth + Gap), -row * (WeaponRowHeight + WeaponRowGap)));

                var button = background.gameObject.AddComponent<Button>();
                button.targetGraphic = background;
                button.onClick.AddListener(() => SelectWeapon(weapon));

                TMP_Text weaponLabel = HudFactory.CreateText(background.rectTransform, "Label", resolvedFont, 27f, BattleHudPalette.PanelText);
                weaponLabel.alignment = TextAlignmentOptions.Left;
                HudFactory.Stretch(weaponLabel.rectTransform);
                weaponLabel.rectTransform.offsetMin = new Vector2(WeaponLabelInset, 0f);
                weaponLabel.rectTransform.offsetMax = new Vector2(-WeaponLabelInset, 0f);
                weaponLabel.text = weapon.DisplayName;

                weaponButtonTargets.Add(weapon);
                weaponButtonBackgrounds.Add(background);
            }

            weaponLists.Add(list);
        }
    }

    private void BuildCloseButton(float y, float contentWidth)
    {
        Image background = HudFactory.CreateImage(panelRect, "Close", BattleHudPalette.PanelBackdrop);
        background.raycastTarget = true;
        HudFactory.SetTopLeft(background.rectTransform, new Vector2(CloseSize, CloseSize),
            new Vector2(PanelPadding.x + contentWidth - CloseSize, -y));

        var button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(Hide);

        TMP_Text label = HudFactory.CreateText(background.rectTransform, "Label", resolvedFont, 28f, BattleHudPalette.PanelText);
        HudFactory.Stretch(label.rectTransform);
        label.text = "X";
    }

    private void BuildMaterialSelector(float x, float y, float contentWidth)
    {
        TMP_Text label = HudFactory.CreateText(panelRect, "MaterialLabel", resolvedFont, 24f, HintText);
        label.alignment = TextAlignmentOptions.Left;
        HudFactory.SetTopLeft(label.rectTransform, new Vector2(contentWidth, MaterialLabelHeight), new Vector2(x, -y));
        label.text = "재료 등급 — 무엇을 넣느냐가 등급을 정합니다";

        float rowY = y + MaterialLabelHeight;
        float tabWidth = (contentWidth - Gap * (Grades.Length - 1)) / Grades.Length;

        for (int i = 0; i < Grades.Length; i++)
        {
            var grade = Grades[i];

            Image background = HudFactory.CreateImage(panelRect, "MaterialTab_" + grade, BattleHudPalette.PortraitFrame);
            background.raycastTarget = true;
            HudFactory.SetTopLeft(background.rectTransform, new Vector2(tabWidth, MaterialRowHeight),
                new Vector2(x + i * (tabWidth + Gap), -rowY));

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => SelectMaterial(grade));

            TMP_Text gradeLabel = HudFactory.CreateText(background.rectTransform, "Label", resolvedFont, 32f, GradeColor(grade));
            HudFactory.Stretch(gradeLabel.rectTransform);
            gradeLabel.text = EquipmentGradeNames.NameOf(grade);

            materialTabBackgrounds.Add(background);
        }
    }

    private void BuildModeTabs(float x, float y, float contentWidth)
    {
        string[] labels = { "자동 제작", "수동 제작" };
        float tabWidth = (contentWidth - Gap) * 0.5f;

        for (int i = 0; i < labels.Length; i++)
        {
            var thisMode = (Mode)i;

            Image background = HudFactory.CreateImage(panelRect, "ModeTab_" + thisMode, BattleHudPalette.PortraitFrame);
            background.raycastTarget = true;
            HudFactory.SetTopLeft(background.rectTransform, new Vector2(tabWidth, TabHeight),
                new Vector2(x + i * (tabWidth + Gap), -y));

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => SelectMode(thisMode));

            TMP_Text label = HudFactory.CreateText(background.rectTransform, "Label", resolvedFont, 30f, BattleHudPalette.PanelText);
            HudFactory.Stretch(label.rectTransform);
            label.text = labels[i];

            modeTabBackgrounds.Add(background);
        }
    }

    private void BuildAutoSection(RectTransform section, float contentWidth)
    {
        autoDescText = HudFactory.CreateText(section, "Desc", resolvedFont, 26f, HintText);
        autoDescText.alignment = TextAlignmentOptions.TopLeft;
        HudFactory.SetTopLeft(autoDescText.rectTransform, new Vector2(contentWidth, DescHeight), Vector2.zero);

        Image background = HudFactory.CreateImage(section, "CraftButton", BattleHudPalette.PortraitFrame);
        background.raycastTarget = true;
        HudFactory.SetTopLeft(background.rectTransform, new Vector2(contentWidth, CraftButtonHeight), new Vector2(0f, -(DescHeight + Gap)));

        var button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(CraftAuto);

        autoCraftLabel = HudFactory.CreateText(background.rectTransform, "Label", resolvedFont, 34f, BattleHudPalette.Mvp);
        HudFactory.Stretch(autoCraftLabel.rectTransform);
        autoCraftLabel.text = "제작하기";
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
            HudFactory.SetTopLeft(background.rectTransform, new Vector2(diffTabWidth, DiffTabHeight),
                new Vector2(i * (diffTabWidth + Gap), 0f));

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => SelectDifficulty(difficulty));

            TMP_Text label = HudFactory.CreateText(background.rectTransform, "Label", resolvedFont, 26f, BattleHudPalette.PanelText);
            HudFactory.Stretch(label.rectTransform);
            label.text = diffLabels[i];

            diffTabBackgrounds.Add(background);
        }

        float y = DiffTabHeight + Gap;

        TMP_Text header = HudFactory.CreateText(section, "RateHeader", resolvedFont, 24f, HintText);
        header.alignment = TextAlignmentOptions.Left;
        HudFactory.SetTopLeft(header.rectTransform, new Vector2(contentWidth, RateHeaderHeight), new Vector2(0f, -y));
        header.text = "고른 난이도의 등급별 확률";
        y += RateHeaderHeight;

        for (int i = 0; i < Grades.Length; i++)
        {
            RectTransform row = HudFactory.CreateGroup(section, "Rate_" + Grades[i]);
            HudFactory.SetTopLeft(row, new Vector2(contentWidth, RateRowHeight), new Vector2(0f, -y));

            TMP_Text grade = HudFactory.CreateText(row, "Grade", resolvedFont, 27f, BattleHudPalette.PanelText);
            grade.alignment = TextAlignmentOptions.Left;
            HudFactory.Stretch(grade.rectTransform);
            grade.text = EquipmentGradeNames.NameOf(Grades[i]);
            grade.color = GradeColor(Grades[i]);

            TMP_Text percent = HudFactory.CreateText(row, "Percent", resolvedFont, 27f, BattleHudPalette.PanelText);
            percent.alignment = TextAlignmentOptions.Right;
            HudFactory.Stretch(percent.rectTransform);
            percent.color = GradeColor(Grades[i]);

            rateGradeLabels.Add(grade);
            ratePercentLabels.Add(percent);

            y += RateRowHeight;
        }

        y += Gap;

        Image startBackground = HudFactory.CreateImage(section, "StartButton", BattleHudPalette.PortraitFrame);
        startBackground.raycastTarget = true;
        HudFactory.SetTopLeft(startBackground.rectTransform, new Vector2(contentWidth, StartButtonHeight), new Vector2(0f, -y));

        startManualButton = startBackground.gameObject.AddComponent<Button>();
        startManualButton.targetGraphic = startBackground;
        startManualButton.onClick.AddListener(StartManual);

        startManualLabel = HudFactory.CreateText(startBackground.rectTransform, "Label", resolvedFont, 34f, BattleHudPalette.Mvp);
        HudFactory.Stretch(startManualLabel.rectTransform);
        startManualLabel.text = "제작 시작 (퍼즐)";
        y += StartButtonHeight + Gap;

        TMP_Text hint = HudFactory.CreateText(section, "Hint", resolvedFont, 22f, HintText);
        hint.alignment = TextAlignmentOptions.TopLeft;
        HudFactory.SetTopLeft(hint.rectTransform, new Vector2(contentWidth, HintHeight), new Vector2(0f, -y));
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
        // 이름이 긴 무기("원형 강철 방패")면 한 줄에 다 안 들어간다. 잘리느니 글자를 줄인다.
        resultText.enableAutoSizing = true;
        resultText.fontSizeMin = 20f;
        resultText.fontSizeMax = 28f;
        SetLeftMiddle(resultText.rectTransform, new Vector2(textWidth, BarHeight), BarPadding);

        float confirmX = BarWidth - BarPadding - BarButtonWidth;
        Image confirmBackground = HudFactory.CreateImage(barRect, "Confirm", TabSelected);
        confirmBackground.raycastTarget = true;
        SetLeftMiddle(confirmBackground.rectTransform, new Vector2(BarButtonWidth, BarButtonHeight), confirmX);

        var confirmButton = confirmBackground.gameObject.AddComponent<Button>();
        confirmButton.targetGraphic = confirmBackground;
        confirmButton.onClick.AddListener(Confirm);

        TMP_Text confirmLabel = HudFactory.CreateText(confirmBackground.rectTransform, "Label", resolvedFont, 26f, BattleHudPalette.PanelText);
        HudFactory.Stretch(confirmLabel.rectTransform);
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

    // 탭을 바꾸면 그 종류의 첫 무기를 고른다. 고른 무기가 가려진 채 버튼에 딴 종류 이름이 남아 있으면 헷갈린다.
    private void SelectWeaponType(WeaponType type)
    {
        if (selectedWeapon == null || selectedWeapon.type != type)
            selectedWeapon = FirstWeaponOf(type);
        selectedType = type;
        RefreshWeapon();
    }

    private void SelectWeapon(WeaponDefinition weapon)
    {
        selectedWeapon = weapon;
        selectedType = weapon.type;
        RefreshWeapon();
    }

    private void RefreshWeapon()
    {
        for (int i = 0; i < typeTabBackgrounds.Count; i++)
            typeTabBackgrounds[i].color = weaponTypes[i] == selectedType ? TabSelected : BattleHudPalette.PortraitFrame;

        for (int i = 0; i < weaponLists.Count; i++)
            weaponLists[i].gameObject.SetActive(weaponTypes[i] == selectedType);

        for (int i = 0; i < weaponButtonBackgrounds.Count; i++)
            weaponButtonBackgrounds[i].color = weaponButtonTargets[i] == selectedWeapon ? TabSelected : BattleHudPalette.PortraitFrame;

        string weaponName = selectedWeapon != null ? selectedWeapon.DisplayName : "무기";
        if (autoCraftLabel != null) autoCraftLabel.text = $"{weaponName} 제작하기";
        if (startManualLabel != null) startManualLabel.text = $"{weaponName} 제작 시작 (퍼즐)";
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

    private static Color GradeColor(EquipmentGrade grade) => EquipmentGradeNames.ColorOf(grade);

    // ---- 자리 잡기 ------------------------------------------------------------

    private static void SetLeftMiddle(RectTransform rect, Vector2 size, float x)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(x, 0f);
    }

}
