using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 마을 장비제작소에서 여는 창.
//
// 무엇을 만들지는 고르지 않는다. 재료 세 개를 넣으면 무기가 랜덤으로 나온다(CraftRecipe).
//
// 왼쪽 칸은 모아 둔 재료(MaterialInventory)다. 등급 x 종류(강철·참나무·가죽) 칸마다 남은 개수가 뜨고,
// 누르면 오른쪽 빈 칸에 들어간다. 이미 칸에 넣은 만큼은 남은 개수에서 빠져 보인다.
//
// 오른쪽 맨 위 세 칸이 넣은 재료다. 누르면 뺀다. 세 칸이 차면 그 아래에 무엇이 나올지 —
// 계열(가장 많이 넣은 종류)과 결과 등급의 밑변(평균 등급) — 이 뜬다.
// 그 아래 두 탭. 자동 제작은 퍼즐 없이 눌러서 바로 만든다 — 등급은 밑변 그대로 나온다.
// 수동 제작은 난이도(쉬움~헬)를 고르면 밑변에서 그 난이도만큼 위로 오를 수 있는 등급 확률표가 뜨고,
// "제작 시작"을 누르면 PuzzleGame이 그 난이도로 열린다. 퍼즐에 성공하면 그 표대로 등급을 굴려
// 장비가 나오고, 실패하면 아무것도 나오지 않는다. 재료는 어느 쪽이든 시작하는 순간 빠진다.
// 실제 제작/확률 로직은 Forge가 들고 있다 — 이 창은 고르고 보여주기만 한다.
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
    [Tooltip("경고 배너가 넘지 않을 가로 길이. 모양은 킷의 장식 메시지 박스다.")]
    [SerializeField] private float bannerWidth = 900f;

    // 오른쪽 칸(넣은 재료·제작 방식) 너비. 왼쪽 재료 칸은 그 앞에 붙는다.
    private const float ControlColumnWidth = 748f;
    private static readonly Vector2 PanelPadding = new Vector2(36f, 30f);
    private const float TitleHeight = 60f;
    private const float TitleGap = 22f;
    private const float TabHeight = 72f;
    private const float Gap = 14f;
    private const float LabelHeight = 30f;

    // 오른쪽 위 — 넣은 재료 세 칸과 예상 결과.
    private const float SlotRowHeight = 72f;
    private const float PreviewHeight = 40f;

    private const float DescHeight = 64f;
    private const float CraftButtonHeight = 100f;

    private const float DiffTabHeight = 66f;
    private const float RateHeaderHeight = 34f;
    private const float RateRowHeight = 44f;
    private const float StartButtonHeight = 90f;
    private const float HintHeight = 54f;

    // 왼쪽 재료 칸.
    private const float MaterialColumnWidth = 440f;
    private const float ColumnGap = 28f;
    private const float GridHeaderHeight = 40f;
    private const float GradeColumnWidth = 64f;
    private const float CellHeight = 58f;
    private const float CellGap = 10f;

    private static readonly PuzzleDifficulty[] Difficulties =
    {
        PuzzleDifficulty.Easy, PuzzleDifficulty.Normal, PuzzleDifficulty.Hard, PuzzleDifficulty.Hell
    };

    // 재료 칸의 행 순서이자 등급표의 행 순서. E가 가장 낮고 S가 가장 높다.
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

    private class MaterialCell
    {
        public CraftMaterial Material;
        public NeonButton Button;
    }

    private AnnouncementBanner warningBanner;

    private RectTransform panelRect;
    private RectTransform autoSection;
    private RectTransform manualSection;

    private readonly List<NeonButton> modeTabs = new List<NeonButton>();
    private readonly List<NeonButton> diffTabs = new List<NeonButton>();
    private readonly List<TMP_Text> ratePercentLabels = new List<TMP_Text>();
    private readonly List<MaterialCell> materialCells = new List<MaterialCell>();
    private readonly List<NeonButton> slotButtons = new List<NeonButton>();

    private GameObject resultBar;
    private TMP_Text resultText;
    private TMP_Text autoDescText;
    private TMP_Text previewText;
    private TMP_Text rateHeaderText;
    private TMP_Text materialLabel;

    // 넣은 재료. 앞에서부터 채워지고, 칸을 누르면 그 자리가 빠지며 뒤가 당겨진다.
    private readonly List<CraftMaterial> slots = new List<CraftMaterial>();

    private Mode mode = Mode.Auto;
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

    private void OnEnable()
    {
        MaterialInventory.Changed += HandleMaterialsChanged;
    }

    private void OnDisable()
    {
        MaterialInventory.Changed -= HandleMaterialsChanged;
    }

    public override void Show()
    {
        EnsureBuilt();
        RefreshAll();
        SetOpen(true);
    }

    public override void Hide()
    {
        SetOpen(false);
    }

    // ---- 재료 넣고 빼기 ------------------------------------------------------

    private void ClickMaterial(CraftMaterial material)
    {
        if (slots.Count >= CraftRecipe.SlotCount)
        {
            warningBanner?.Show("칸이 가득 찼습니다. 넣은 재료를 눌러 빼세요.");
            return;
        }

        if (AvailableCount(material) <= 0)
        {
            warningBanner?.Show($"남은 {material.DisplayName}{HeroLabel.SubjectParticle(material.DisplayName)} 없습니다.");
            return;
        }

        slots.Add(material);
        RefreshAll();
    }

    private void ClickSlot(int index)
    {
        if (index < 0 || index >= slots.Count) return;

        slots.RemoveAt(index);
        RefreshAll();
    }

    // 창고에 있는 개수에서 이미 칸에 넣은 만큼을 뺀 것.
    private int AvailableCount(CraftMaterial material)
    {
        int used = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].Equals(material)) used++;
        }
        return MaterialInventory.CountOf(material) - used;
    }

    // 창고의 재료가 줄었는데(다른 곳에서 썼거나 세이브가 지워졌거나) 칸에 그대로 남아 있으면
    // 없는 재료로 제작을 누르게 된다. 모자란 만큼 뒤에서부터 뺀다.
    private void TrimSlotsToInventory()
    {
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            if (AvailableCount(slots[i]) < 0) slots.RemoveAt(i);
        }
    }

    private void HandleMaterialsChanged()
    {
        TrimSlotsToInventory();
        if (IsOpen) RefreshAll();
    }

    // ---- 제작 -------------------------------------------------------------

    private void CraftAuto()
    {
        if (!CanCraft()) return;

        // 제작하면 창고에서 재료가 빠지며 Changed가 불린다. 칸을 먼저 비워 둬야 빠진 뒤의 개수로
        // 칸을 다시 맞출 때 방금 태운 재료가 칸에 남지 않는다. 실패하면 되돌린다.
        var used = new List<CraftMaterial>(slots);
        slots.Clear();

        string reason;
        if (!forge.CraftAuto(used, out reason))
        {
            slots.AddRange(used);
            TrimSlotsToInventory();
            warningBanner?.Show(reason);
        }
        RefreshAll();
    }

    private void StartManual()
    {
        if (!CanCraft()) return;

        var used = new List<CraftMaterial>(slots);
        slots.Clear();

        string reason;
        if (!forge.StartManual(used, selectedDifficulty, out reason))
        {
            slots.AddRange(used);
            TrimSlotsToInventory();
            warningBanner?.Show(reason);
            RefreshAll();
            return;
        }

        // 퍼즐이 뜨는 동안은 창을 접는다. 떠 있으면 배경막이 퍼즐 판을 가린다.
        Hide();
    }

    private bool CanCraft()
    {
        if (forge == null)
        {
            warningBanner?.Show("장비제작소(Forge)를 찾지 못했습니다.");
            return false;
        }

        if (!CraftRecipe.IsComplete(slots))
        {
            warningBanner?.Show($"재료 {CraftRecipe.SlotCount}개를 모두 넣으세요.");
            return false;
        }
        return true;
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
        modeTabs.Clear();
        diffTabs.Clear();
        ratePercentLabels.Clear();
        materialCells.Clear();
        slotButtons.Clear();

        BuildCanvas();
        BuildPopup();
        BuildResultBar();

        warningBanner = AnnouncementBanner.Create(canvasRect, resolvedFont, null, bannerWidth);

        RefreshAll();
    }

    private void BuildPopup()
    {
        BuildPanel(BuildPopupRoot());
    }

    private void BuildPanel(RectTransform popup)
    {
        float controlX = PanelPadding.x + MaterialColumnWidth + ColumnGap;
        float panelWidth = controlX + ControlColumnWidth + PanelPadding.x;
        float contentWidth = panelWidth - PanelPadding.x * 2f;

        panelRect = HudFactory.CreatePanel(popup, "Panel").rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;

        float y = PanelPadding.y;

        BuildTitleBar(panelRect, "장비제작소", PanelPadding.x, y, contentWidth, TitleHeight);
        y += TitleHeight + TitleGap;

        float columnTop = y;

        BuildSlots(controlX, y, ControlColumnWidth);
        y += LabelHeight + SlotRowHeight + 8f + PreviewHeight + Gap;

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

        float columnBottom = y + Mathf.Max(autoSection.sizeDelta.y, manualSection.sizeDelta.y);
        BuildMaterialColumn(PanelPadding.x, columnTop, columnBottom);

        // 두 칸을 가르는 세로선. 킷 구분선은 가로로 옅어지는 그림이라 세로로는 쓸 수 없어 옅은 강조색 선을 긋는다.
        Image divider = HudFactory.CreateImage(panelRect, "Divider",
            new Color(BattleHudPalette.Accent.r, BattleHudPalette.Accent.g, BattleHudPalette.Accent.b, 0.18f));
        HudFactory.SetTopLeft(divider.rectTransform, new Vector2(2f, columnBottom - columnTop),
            new Vector2(PanelPadding.x + MaterialColumnWidth + ColumnGap * 0.5f - 1f, -columnTop));

        panelRect.sizeDelta = new Vector2(panelWidth, columnBottom + PanelPadding.y);
    }

    // 왼쪽 — 등급(행) x 종류(열) 재료 칸과, 무엇을 넣으면 무엇이 나오는지 안내.
    private void BuildMaterialColumn(float x, float top, float bottom)
    {
        float y = top;

        materialLabel = HudFactory.CreateText(panelRect, "MaterialLabel", resolvedFont, 24f, BattleHudPalette.TextMuted);
        materialLabel.alignment = TextAlignmentOptions.Left;
        HudFactory.SetTopLeft(materialLabel.rectTransform, new Vector2(MaterialColumnWidth, LabelHeight), new Vector2(x, -y));
        y += LabelHeight;

        MaterialKind[] kinds = MaterialNames.AllKinds;
        float cellWidth = (MaterialColumnWidth - GradeColumnWidth - CellGap * kinds.Length) / kinds.Length;

        for (int k = 0; k < kinds.Length; k++)
        {
            TMP_Text header = HudFactory.CreateText(panelRect, "Kind_" + kinds[k], resolvedFont, 24f, BattleHudPalette.TextPrimary);
            HudFactory.SetTopLeft(header.rectTransform, new Vector2(cellWidth, GridHeaderHeight),
                new Vector2(x + GradeColumnWidth + CellGap + k * (cellWidth + CellGap), -y));
            header.text = MaterialNames.KindName(kinds[k]);
        }
        y += GridHeaderHeight;

        for (int g = 0; g < Grades.Length; g++)
        {
            EquipmentGrade grade = Grades[g];
            float rowY = y + g * (CellHeight + CellGap);

            TMP_Text gradeLabel = HudFactory.CreateText(panelRect, "Grade_" + grade, resolvedFont, 30f, GradeColor(grade));
            HudFactory.SetTopLeft(gradeLabel.rectTransform, new Vector2(GradeColumnWidth, CellHeight), new Vector2(x, -rowY));
            gradeLabel.text = EquipmentGradeNames.NameOf(grade);

            for (int k = 0; k < kinds.Length; k++)
            {
                var material = new CraftMaterial(kinds[k], grade);

                NeonButton cell = HudFactory.CreateButton(panelRect, "Cell_" + kinds[k] + "_" + grade, NeonButtonStyle.Ghost,
                    resolvedFont, string.Empty, 26f, () => ClickMaterial(material));
                HudFactory.SetTopLeft(cell.Rect, new Vector2(cellWidth, CellHeight),
                    new Vector2(x + GradeColumnWidth + CellGap + k * (cellWidth + CellGap), -rowY));

                materialCells.Add(new MaterialCell { Material = material, Button = cell });
            }
        }
        y += Grades.Length * CellHeight + (Grades.Length - 1) * CellGap + Gap * 2f;

        TMP_Text guide = HudFactory.CreateText(panelRect, "Guide", resolvedFont, 21f, BattleHudPalette.TextMuted);
        guide.alignment = TextAlignmentOptions.TopLeft;
        guide.textWrappingMode = TextWrappingModes.Normal;
        HudFactory.SetTopLeft(guide.rectTransform, new Vector2(MaterialColumnWidth, Mathf.Max(0f, bottom - y)), new Vector2(x, -y));
        guide.text =
            "재료를 누르면 빈 칸에 들어갑니다.\n" +
            "가장 많이 넣은 재료가 계열을 정합니다.\n" +
            $"  강철 — {CraftRecipe.FamilyContents(WeaponFamily.Metal)}\n" +
            $"  참나무 — {CraftRecipe.FamilyContents(WeaponFamily.Wood)}\n" +
            $"  가죽 — {CraftRecipe.FamilyContents(WeaponFamily.Shield)}\n" +
            $"  셋 다 다르면 — {CraftRecipe.FamilyContents(WeaponFamily.Any)}\n" +
            "등급은 세 재료의 평균(내림)에서 시작합니다.\n" +
            "재료는 층을 클리어하면 얻습니다.";
    }

    // 오른쪽 위 — 넣은 재료 세 칸과, 세 칸이 차면 무엇이 나올지.
    private void BuildSlots(float x, float y, float contentWidth)
    {
        TMP_Text label = HudFactory.CreateText(panelRect, "SlotLabel", resolvedFont, 24f, BattleHudPalette.TextMuted);
        label.alignment = TextAlignmentOptions.Left;
        HudFactory.SetTopLeft(label.rectTransform, new Vector2(contentWidth, LabelHeight), new Vector2(x, -y));
        label.text = "넣은 재료 — 누르면 뺍니다";

        float rowY = y + LabelHeight;
        float slotWidth = (contentWidth - Gap * (CraftRecipe.SlotCount - 1)) / CraftRecipe.SlotCount;

        for (int i = 0; i < CraftRecipe.SlotCount; i++)
        {
            int index = i;

            NeonButton slot = HudFactory.CreateButton(panelRect, "Slot_" + i, NeonButtonStyle.Ghost,
                resolvedFont, string.Empty, 28f, () => ClickSlot(index));
            HudFactory.SetTopLeft(slot.Rect, new Vector2(slotWidth, SlotRowHeight), new Vector2(x + i * (slotWidth + Gap), -rowY));

            slotButtons.Add(slot);
        }

        previewText = HudFactory.CreateText(panelRect, "Preview", resolvedFont, 26f, BattleHudPalette.TextMuted);
        previewText.alignment = TextAlignmentOptions.Left;
        HudFactory.SetTopLeft(previewText.rectTransform, new Vector2(contentWidth, PreviewHeight),
            new Vector2(x, -(rowY + SlotRowHeight + 8f)));
    }

    private void BuildModeTabs(float x, float y, float contentWidth)
    {
        string[] labels = { "자동 제작", "수동 제작" };
        float tabWidth = (contentWidth - Gap) * 0.5f;

        for (int i = 0; i < labels.Length; i++)
        {
            var thisMode = (Mode)i;

            NeonButton tab = HudFactory.CreateButton(panelRect, "ModeTab_" + thisMode, NeonButtonStyle.Ghost,
                resolvedFont, labels[i], 30f, () => SelectMode(thisMode));
            HudFactory.SetTopLeft(tab.Rect, new Vector2(tabWidth, TabHeight), new Vector2(x + i * (tabWidth + Gap), -y));

            modeTabs.Add(tab);
        }
    }

    private void BuildAutoSection(RectTransform section, float contentWidth)
    {
        autoDescText = HudFactory.CreateText(section, "Desc", resolvedFont, 26f, BattleHudPalette.TextMuted);
        autoDescText.alignment = TextAlignmentOptions.TopLeft;
        HudFactory.SetTopLeft(autoDescText.rectTransform, new Vector2(contentWidth, DescHeight), Vector2.zero);

        NeonButton craft = HudFactory.CreateButton(section, "CraftButton", NeonButtonStyle.Primary,
            resolvedFont, "제작하기", 34f, CraftAuto);
        HudFactory.SetTopLeft(craft.Rect, new Vector2(contentWidth, CraftButtonHeight), new Vector2(0f, -(DescHeight + Gap)));
    }

    private void BuildManualSection(RectTransform section, float contentWidth)
    {
        string[] diffLabels = { "쉬움", "보통", "어려움", "헬" };
        float diffTabWidth = (contentWidth - Gap * (Difficulties.Length - 1)) / Difficulties.Length;

        for (int i = 0; i < Difficulties.Length; i++)
        {
            var difficulty = Difficulties[i];

            NeonButton tab = HudFactory.CreateButton(section, "DiffTab_" + difficulty, NeonButtonStyle.Ghost,
                resolvedFont, diffLabels[i], 26f, () => SelectDifficulty(difficulty));
            HudFactory.SetTopLeft(tab.Rect, new Vector2(diffTabWidth, DiffTabHeight), new Vector2(i * (diffTabWidth + Gap), 0f));

            diffTabs.Add(tab);
        }

        float y = DiffTabHeight + Gap;

        rateHeaderText = HudFactory.CreateText(section, "RateHeader", resolvedFont, 24f, BattleHudPalette.TextMuted);
        rateHeaderText.alignment = TextAlignmentOptions.Left;
        HudFactory.SetTopLeft(rateHeaderText.rectTransform, new Vector2(contentWidth, RateHeaderHeight), new Vector2(0f, -y));
        y += RateHeaderHeight;

        for (int i = 0; i < Grades.Length; i++)
        {
            RectTransform row = HudFactory.CreateGroup(section, "Rate_" + Grades[i]);
            HudFactory.SetTopLeft(row, new Vector2(contentWidth, RateRowHeight), new Vector2(0f, -y));

            TMP_Text grade = HudFactory.CreateText(row, "Grade", resolvedFont, 27f, BattleHudPalette.TextPrimary);
            grade.alignment = TextAlignmentOptions.Left;
            HudFactory.Stretch(grade.rectTransform);
            // 등급 글자만으로는 무엇이 달라지는지 알 수 없다. 이름에 붙는 말과 배율을 같이 적는다.
            grade.text = $"{EquipmentGradeNames.NameOf(Grades[i])}  {EquipmentGradeNames.PrefixOf(Grades[i])} · x{EquipmentGradeRules.PowerOf(Grades[i]):0.00}";
            grade.color = GradeColor(Grades[i]);

            TMP_Text percent = HudFactory.CreateText(row, "Percent", resolvedFont, 27f, BattleHudPalette.TextPrimary);
            percent.alignment = TextAlignmentOptions.Right;
            HudFactory.Stretch(percent.rectTransform);
            percent.color = GradeColor(Grades[i]);

            ratePercentLabels.Add(percent);

            y += RateRowHeight;
        }

        y += Gap;

        NeonButton start = HudFactory.CreateButton(section, "StartButton", NeonButtonStyle.Primary,
            resolvedFont, "제작 시작 (퍼즐)", 34f, StartManual);
        HudFactory.SetTopLeft(start.Rect, new Vector2(contentWidth, StartButtonHeight), new Vector2(0f, -y));
        y += StartButtonHeight + Gap;

        TMP_Text hint = HudFactory.CreateText(section, "Hint", resolvedFont, 22f, BattleHudPalette.TextMuted);
        hint.alignment = TextAlignmentOptions.TopLeft;
        HudFactory.SetTopLeft(hint.rectTransform, new Vector2(contentWidth, HintHeight), new Vector2(0f, -y));
        hint.text = "퍼즐을 맞추면 장비가 나옵니다. 시간 안에 못 맞추면 재료만 잃습니다.";
    }

    // ---- 결과 띠 ------------------------------------------------------------

    private void BuildResultBar()
    {
        RectTransform barRect = HudFactory.CreateHudStrip(canvasRect, "ResultBar").rectTransform;
        barRect.anchorMin = new Vector2(0.5f, 1f);
        barRect.anchorMax = new Vector2(0.5f, 1f);
        barRect.pivot = new Vector2(0.5f, 1f);
        barRect.sizeDelta = new Vector2(BarWidth, BarHeight);
        barRect.anchoredPosition = new Vector2(0f, -BarTopMargin);
        resultBar = barRect.gameObject;

        float textWidth = BarWidth - BarPadding * 2f - BarButtonWidth - Gap;

        resultText = HudFactory.CreateText(barRect, "Result", resolvedFont, 28f, BattleHudPalette.TextPrimary);
        resultText.alignment = TextAlignmentOptions.Left;
        // 이름이 긴 무기("전설의 원형 강철 방패")면 한 줄에 다 안 들어간다. 잘리느니 글자를 줄인다.
        resultText.enableAutoSizing = true;
        resultText.fontSizeMin = 20f;
        resultText.fontSizeMax = 28f;
        SetLeftMiddle(resultText.rectTransform, new Vector2(textWidth, BarHeight), BarPadding);

        float confirmX = BarWidth - BarPadding - BarButtonWidth;
        NeonButton confirm = HudFactory.CreateButton(barRect, "Confirm", NeonButtonStyle.Primary, resolvedFont, "확인", 26f, Confirm);
        SetLeftMiddle(confirm.Rect, new Vector2(BarButtonWidth, BarButtonHeight), confirmX);

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

    private void RefreshAll()
    {
        RefreshMode();
        RefreshMaterialCells();
        RefreshSlots();
        RefreshRates();
    }

    private void SelectMode(Mode newMode)
    {
        mode = newMode;
        RefreshMode();
    }

    private void RefreshMode()
    {
        if (autoSection != null) autoSection.gameObject.SetActive(mode == Mode.Auto);
        if (manualSection != null) manualSection.gameObject.SetActive(mode == Mode.Manual);

        for (int i = 0; i < modeTabs.Count; i++)
            modeTabs[i].SetStyle((Mode)i == mode ? NeonButtonStyle.Primary : NeonButtonStyle.Ghost);
    }

    private void RefreshMaterialCells()
    {
        if (materialLabel != null) materialLabel.text = $"보유 재료 {MaterialInventory.TotalCount}개";

        for (int i = 0; i < materialCells.Count; i++)
        {
            MaterialCell cell = materialCells[i];
            int available = AvailableCount(cell.Material);

            // 남은 재료가 있는 칸만 네온 테두리로 띄운다. 빈 칸도 누를 수는 있다 — 누르면 왜 안 들어가는지 알려 준다.
            cell.Button.Label.text = available > 0 ? available.ToString() : "-";
            cell.Button.SetStyle(available > 0 ? NeonButtonStyle.Secondary : NeonButtonStyle.Muted);
            cell.Button.SetLabelColor(available > 0 ? GradeColor(cell.Material.Grade) : BattleHudPalette.TextMuted);
        }
    }

    private void RefreshSlots()
    {
        for (int i = 0; i < slotButtons.Count; i++)
        {
            bool filled = i < slots.Count;
            NeonButton slot = slotButtons[i];
            // 넣은 재료는 등급색 글자가 읽혀야 해서 밝은 판(Primary) 대신 네온 테두리로 표시한다.
            slot.SetStyle(filled ? NeonButtonStyle.Secondary : NeonButtonStyle.Ghost);
            slot.Label.text = filled ? slots[i].DisplayName : "빈 칸";
            slot.SetLabelColor(filled ? GradeColor(slots[i].Grade) : BattleHudPalette.TextMuted);
        }

        bool complete = CraftRecipe.IsComplete(slots);
        if (!complete)
        {
            previewText.text = $"재료를 {CraftRecipe.SlotCount - slots.Count}개 더 넣으세요.";
            previewText.color = BattleHudPalette.TextMuted;
            autoDescText.text = "재료 세 개를 넣으면 퍼즐 없이 바로 만듭니다.";
            return;
        }

        WeaponFamily family = CraftRecipe.FamilyOf(slots);
        EquipmentGrade baseGrade = CraftRecipe.BaseGradeOf(slots);
        string familyName = CraftRecipe.FamilyName(family);

        previewText.text = $"{familyName} ({CraftRecipe.FamilyContents(family)}) · 밑변 {EquipmentGradeNames.NameOf(baseGrade)}등급";
        previewText.color = BattleHudPalette.TextPrimary;
        autoDescText.text = $"퍼즐 없이 바로 만듭니다. {EquipmentGradeNames.NameOf(baseGrade)}등급 {familyName}{HeroLabel.SubjectParticle(familyName)} 나옵니다.";
    }

    private void SelectDifficulty(PuzzleDifficulty difficulty)
    {
        selectedDifficulty = difficulty;
        RefreshRates();
    }

    private void RefreshRates()
    {
        for (int i = 0; i < diffTabs.Count; i++)
            diffTabs[i].SetStyle(Difficulties[i] == selectedDifficulty ? NeonButtonStyle.Primary : NeonButtonStyle.Ghost);

        bool complete = CraftRecipe.IsComplete(slots);
        EquipmentGrade baseGrade = CraftRecipe.BaseGradeOf(slots);

        if (rateHeaderText != null)
        {
            rateHeaderText.text = complete
                ? $"밑변 {EquipmentGradeNames.NameOf(baseGrade)}등급에서 고른 난이도로 나올 등급별 확률"
                : "재료를 넣으면 등급별 확률이 보입니다";
        }

        for (int i = 0; i < Grades.Length; i++)
            ratePercentLabels[i].text = complete ? EquipmentCraftTable.PercentText(baseGrade, selectedDifficulty, Grades[i]) : "-";
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
