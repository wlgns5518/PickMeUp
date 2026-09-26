using System.Collections.Generic;
using TMPro;
using UnityEngine;

// 장비 제작소 — 한 건물에서 [장비 제작]과 [장비 합성]을 탭으로 오간다.
//
// 두 기능은 넣는 것(재료 / 장비)과 규칙이 다를 뿐 같은 흐름이다: 왼쪽 위에서 넣고, 가운데서 결과를 확인하고,
// 아래 오른쪽에서 실행한다(UiFlowPanel). 오른쪽은 넣을 것을 고르는 목록(UiPickerPanel)이다. 캐릭터 합성소와도
// 같은 자리 배치라, 한 곳에서 익힌 손놀림이 다른 곳에서 그대로 통한다.
//
// 장비 제작 — 재료 세 개 → 무기. 가장 많이 넣은 재료가 계열(금속·목재·방패)을, 세 재료의 평균 등급이 결과 등급의
//   밑변을 정한다(CraftRecipe). 자동 제작은 밑변 등급 그대로, 수동 제작은 퍼즐(난이도)을 풀면 더 높은 등급이 나올
//   수 있다(EquipmentCraftTable). 실제 제작은 Forge가 한다. 퍼즐이 뜨는 동안은 화면을 접는다.
// 장비 합성 — 장비 세 개 → 한 개. 결과 등급은 평균 등급 + 1(EquipmentSynthesis). 장착 중인 장비는 못 넣는다.
[DisallowMultipleComponent]
public class EquipmentWorkshopUI : UiScreen
{
    private enum Page { Craft, Synthesis }

    [Header("Forge")]
    [Tooltip("비워두면 씬에서 찾는다.")]
    [SerializeField] private Forge forge;

    [Header("Open State")]
    [Tooltip("장비제작소를 누르지 않아도 처음부터 열려 있게 하려면 켠다.")]
    [SerializeField] private bool openOnStart;

    private const float FlowWidth = 1040f;
    private const float PageTabWidth = 520f;

    private static readonly string[] PageTabs = { "장비 제작", "장비 합성" };
    private static readonly string[] ModeTabs = { "자동 제작", "수동 제작 (퍼즐)" };
    private static readonly string[] DifficultyTabs = { "쉬움", "보통", "어려움", "헬" };
    private static readonly PuzzleDifficulty[] Difficulties =
        { PuzzleDifficulty.Easy, PuzzleDifficulty.Normal, PuzzleDifficulty.Hard, PuzzleDifficulty.Hell };
    private static readonly string[] MaterialTabs = { "전체", "강철", "참나무", "가죽" };
    private static readonly string[] EquipmentTabs = { "전체", "무기", "방어구" };

    private static readonly EquipmentGrade[] Grades =
        { EquipmentGrade.E, EquipmentGrade.D, EquipmentGrade.C, EquipmentGrade.B, EquipmentGrade.A, EquipmentGrade.S };

    private UiTabs pageTabs;
    private RectTransform craftPage;
    private RectTransform synthesisPage;

    private UiFlowPanel craftFlow;
    private UiTabs modeTabs;
    private UiTabs difficultyTabs;
    private UiButton rateButton;
    private UiPickerPanel materialPicker;

    private UiFlowPanel synthFlow;
    private UiPickerPanel equipmentPicker;

    private UiPopup ratePopup;
    private readonly List<TMP_Text> rateCells = new List<TMP_Text>();
    private UiResultPopup resultPopup;

    // 넣은 재료. 앞에서부터 채워지고, 칸을 누르면 그 자리가 빠지며 뒤가 당겨진다.
    private readonly List<CraftMaterial> craftSlots = new List<CraftMaterial>();
    private readonly List<OwnedEquipment> synthSlots = new List<OwnedEquipment>();

    private readonly List<CraftMaterial> visibleMaterials = new List<CraftMaterial>();
    private readonly List<OwnedEquipment> visibleEquipment = new List<OwnedEquipment>();

    private Page page = Page.Craft;
    private bool manual;
    private PuzzleDifficulty difficulty = PuzzleDifficulty.Easy;

    protected override string CanvasName => "EquipmentWorkshopCanvas";
    // 소환(96), 합성(97) 다음.
    protected override int SortingOrder => 98;
    protected override string Title => "장비 제작소";

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
        MaterialInventory.Changed += HandleDataChanged;
        EquipmentInventory.Changed += HandleDataChanged;
        PlayerAccount.Changed += HandleDataChanged;
    }

    private void OnDisable()
    {
        MaterialInventory.Changed -= HandleDataChanged;
        EquipmentInventory.Changed -= HandleDataChanged;
        PlayerAccount.Changed -= HandleDataChanged;
    }

    public override void Show()
    {
        EnsureBuilt();
        Refresh();
        SetOpen(true);
    }

    // 닫으면 넣어 둔 재료와 장비를 내려놓는다. 지난번에 무엇을 올려 뒀는지 화면이 기억하고 있으면,
    // 다시 열었을 때 그 사실을 모른 채 제작·합성을 눌러 엉뚱한 것을 태우게 된다(캐릭터 합성소도 같은 규칙).
    // 수동 제작으로 퍼즐이 뜨는 동안에도 닫히지만, 그때는 이미 재료가 창고에서 빠져 칸이 비어 있다.
    public override void Hide()
    {
        if (ratePopup != null) ratePopup.Hide();
        craftSlots.Clear();
        synthSlots.Clear();
        SetOpen(false);
    }

    private void HandleDataChanged()
    {
        TrimSlots();
        if (IsOpen) Refresh();
    }

    // ---- 짓기 ---------------------------------------------------------------

    protected override void BuildContent(RectTransform root, Vector2 size)
    {
        pageTabs = UiTabs.Create(root, "PageTabs", PageTabs, UiTheme.FontHeading);
        UiKit.TopLeft(pageTabs.Rect, 0f, 0f, PageTabWidth, UiTheme.TabHeight + 4f);
        pageTabs.Changed += index => { page = (Page)index; Refresh(); };
        // 장비 합성은 장비제작소 3레벨부터(FacilityUnlocks).
        pageTabs.LockedClicked += _ => toast.Show(
            FacilityUnlocks.Requirement(VillageBlockout.Kind.EquipmentWorkshop, FacilityUnlocks.EquipmentSynthesisLevel), UiToastKind.Warning);

        float pageTop = UiTheme.TabHeight + 4f + UiTheme.Space4;
        var pageSize = new Vector2(size.x, size.y - pageTop);

        craftPage = UiKit.Node(root, "CraftPage");
        UiKit.Fill(craftPage, 0f, pageTop, 0f, 0f);
        BuildCraftPage(craftPage, pageSize);

        synthesisPage = UiKit.Node(root, "SynthesisPage");
        UiKit.Fill(synthesisPage, 0f, pageTop, 0f, 0f);
        BuildSynthesisPage(synthesisPage, pageSize);
    }

    private void BuildCraftPage(RectTransform root, Vector2 size)
    {
        craftFlow = UiFlowPanel.Create(root, "Flow", FlowWidth, new UiFlowPanel.Layout
        {
            InputCount = CraftRecipe.SlotCount,
            InputSlotSize = UiTheme.SlotMedium,
            InputTitle = "재료 선택",
            ResultTitle = "제작 결과",
            ActionLabel = "제작",
            HasOptions = true,
        });
        UiKit.TopLeft(craftFlow.Rect, 0f, 0f, FlowWidth, craftFlow.Height);

        for (int i = 0; i < CraftRecipe.SlotCount; i++)
        {
            int index = i;
            craftFlow.Input(i).Clicked += () => RemoveCraftSlot(index);
        }
        craftFlow.Action.onClick.AddListener(Craft);

        modeTabs = UiTabs.Create(craftFlow.Options, "Mode", ModeTabs, UiTheme.FontLabel);
        UiKit.TopLeft(modeTabs.Rect, 0f, 0f, 440f, UiTheme.TabHeight);
        modeTabs.Changed += index => { manual = index == 1; Refresh(); };
        // 수동 제작(퍼즐)은 장비제작소 2레벨부터.
        modeTabs.LockedClicked += _ => toast.Show(
            FacilityUnlocks.Requirement(VillageBlockout.Kind.EquipmentWorkshop, FacilityUnlocks.ManualCraftLevel), UiToastKind.Warning);

        difficultyTabs = UiTabs.Create(craftFlow.Options, "Difficulty", DifficultyTabs, UiTheme.FontLabel);
        UiKit.TopRight(difficultyTabs.Rect, 0f, 0f, FlowWidth - 440f - UiTheme.Space4, UiTheme.TabHeight);
        difficultyTabs.Changed += index => { difficulty = Difficulties[index]; Refresh(); };

        rateButton = UiButton.Create(craftFlow.ResultActions, "Rates", "등급 확률", UiButtonStyle.Secondary, UiButtonSize.Small, OpenRates);
        UiKit.Fill(rateButton.Rect, 60f, 0f, 0f, 0f);

        float pickerX = FlowWidth + UiTheme.ColumnGap;
        materialPicker = UiPickerPanel.Create(root, "Materials", size.x - pickerX, "보유 재료", MaterialTabs, UiTheme.SlotSmall + 8f);
        UiKit.Fill(materialPicker.Rect, pickerX, 0f, 0f, 0f);
        materialPicker.Tabs.Changed += _ => Refresh();
        materialPicker.Grid.TileClicked += tile =>
        {
            if (tile.Payload is CraftMaterial material) AddCraftSlot(material);
        };
    }

    private void BuildSynthesisPage(RectTransform root, Vector2 size)
    {
        synthFlow = UiFlowPanel.Create(root, "Flow", FlowWidth, new UiFlowPanel.Layout
        {
            InputCount = EquipmentSynthesis.SlotCount,
            InputSlotSize = UiTheme.SlotMedium,
            InputTitle = "재료 장비 선택",
            ResultTitle = "합성 결과",
            ActionLabel = "합성",
        });
        UiKit.TopLeft(synthFlow.Rect, 0f, 0f, FlowWidth, synthFlow.Height);

        for (int i = 0; i < EquipmentSynthesis.SlotCount; i++)
        {
            int index = i;
            synthFlow.Input(i).Clicked += () => RemoveSynthSlot(index);
        }
        synthFlow.Action.onClick.AddListener(AskSynthesize);

        float pickerX = FlowWidth + UiTheme.ColumnGap;
        equipmentPicker = UiPickerPanel.Create(root, "Equipment", size.x - pickerX, "보유 장비", EquipmentTabs, UiTheme.SlotSmall + 8f);
        UiKit.Fill(equipmentPicker.Rect, pickerX, 0f, 0f, 0f);
        equipmentPicker.Tabs.Changed += _ => Refresh();
        equipmentPicker.Grid.TileClicked += tile => ToggleSynthSlot(tile.Payload as OwnedEquipment);
    }

    protected override void BuildOverlays()
    {
        BuildRatePopup();
        resultPopup = new UiResultPopup(overlay);
        Refresh();
    }

    // ---- 등급 확률 팝업 --------------------------------------------------------

    private void BuildRatePopup()
    {
        rateCells.Clear();
        ratePopup = UiPopup.Create(overlay, "RatePopup", "등급 확률", new Vector2(900f, 700f), false);

        float[] columns = { 0f, 260f, 480f };
        string[] titles = { "결과 등급", "확률", "능력치 배율" };

        RectTransform header = UiKit.Node(ratePopup.Body, "Header");
        UiKit.TopStretch(header, 0f, 44f);
        UiKit.Rounded(header, "Fill", UiTheme.SurfaceSunken, UiTheme.RadiusS);
        for (int c = 0; c < titles.Length; c++)
        {
            TMP_Text t = UiKit.Text(header, "Col_" + c, titles[c], UiTheme.FontLabel, UiTheme.TextSecondary);
            UiKit.Fill(t.rectTransform, columns[c] + UiTheme.Space5, 0f, 0f, 0f);
        }

        const float rowHeight = 60f;
        for (int g = 0; g < Grades.Length; g++)
        {
            RectTransform row = UiKit.Node(ratePopup.Body, "Row_" + g);
            UiKit.TopStretch(row, 52f + g * rowHeight, rowHeight);
            UiKit.Rounded(row, "Fill", g % 2 == 0 ? UiTheme.SurfaceRaised : UiTheme.Surface, UiTheme.RadiusS);

            Color color = UiTheme.GradeColor(Grades[g]);
            TMP_Text grade = UiKit.Text(row, "Grade", UiTheme.Paint(EquipmentGradeNames.NameOf(Grades[g]), color) +
                "  " + EquipmentGradeNames.PrefixOf(Grades[g]), UiTheme.FontBody, UiTheme.TextPrimary);
            UiKit.Fill(grade.rectTransform, columns[0] + UiTheme.Space5, 0f, 0f, 0f);

            TMP_Text percent = UiKit.Text(row, "Percent", string.Empty, UiTheme.FontHeading, color);
            UiKit.Fill(percent.rectTransform, columns[1] + UiTheme.Space5, 0f, 0f, 0f);
            rateCells.Add(percent);

            TMP_Text power = UiKit.Text(row, "Power", $"x{EquipmentGradeRules.PowerOf(Grades[g]):0.00}", UiTheme.FontBody, UiTheme.TextSecondary);
            UiKit.Fill(power.rectTransform, columns[2] + UiTheme.Space5, 0f, 0f, 0f);
        }

        TMP_Text note = UiKit.Text(ratePopup.Body, "Note", "실패하면 재료와 골드를 잃습니다.",
            UiTheme.FontLabel, UiTheme.TextSecondary);
        note.alignment = TextAlignmentOptions.BottomLeft;
        UiKit.BottomStretch(note.rectTransform, 0f, 44f);
    }

    private void OpenRates()
    {
        if (!CraftRecipe.IsComplete(craftSlots))
        {
            toast.Show("재료 3개를 넣으면 등급 확률을 볼 수 있습니다.", UiToastKind.Info);
            return;
        }

        EquipmentGrade baseGrade = CraftRecipe.BaseGradeOf(craftSlots);
        ratePopup.SetTitle($"등급 확률 — {DifficultyTabs[System.Array.IndexOf(Difficulties, difficulty)]} · 재료 {EquipmentGradeNames.NameOf(baseGrade)}등급");
        for (int g = 0; g < Grades.Length; g++)
            rateCells[g].text = EquipmentCraftTable.PercentText(baseGrade, difficulty, Grades[g]);
        ratePopup.Show();
    }

    // ---- 장비 제작 ------------------------------------------------------------

    private void AddCraftSlot(CraftMaterial material)
    {
        if (craftSlots.Count >= CraftRecipe.SlotCount)
        {
            toast.Show("재료 칸이 가득 찼습니다. 넣은 재료를 눌러 빼세요.", UiToastKind.Info);
            return;
        }
        if (AvailableCount(material) <= 0)
        {
            toast.Show($"남은 {material.DisplayName}{HeroLabel.SubjectParticle(material.DisplayName)} 없습니다.", UiToastKind.Warning);
            return;
        }

        craftSlots.Add(material);
        Refresh();
    }

    private void RemoveCraftSlot(int index)
    {
        if (index < 0 || index >= craftSlots.Count) return;
        craftSlots.RemoveAt(index);
        Refresh();
    }

    // 창고에 있는 개수에서 이미 칸에 넣은 만큼을 뺀 것.
    private int AvailableCount(CraftMaterial material)
    {
        int used = 0;
        for (int i = 0; i < craftSlots.Count; i++) if (craftSlots[i].Equals(material)) used++;
        return MaterialInventory.CountOf(material) - used;
    }

    private void Craft()
    {
        if (forge == null)
        {
            toast.Show("장비 제작소(Forge)를 찾지 못했습니다.", UiToastKind.Danger);
            return;
        }

        // 제작하면 창고에서 재료가 빠지며 Changed가 불린다. 칸을 먼저 비워 둬야 빠진 뒤의 개수로 칸을 다시 맞출 때
        // 방금 태운 재료가 칸에 남지 않는다. 실패하면 되돌린다.
        var used = new List<CraftMaterial>(craftSlots);
        craftSlots.Clear();

        string reason;
        bool started = manual ? forge.StartManual(used, difficulty, out reason) : forge.CraftAuto(used, out reason);
        if (!started)
        {
            craftSlots.AddRange(used);
            TrimSlots();
            toast.Show(reason, UiToastKind.Warning);
            Refresh();
            return;
        }

        // 퍼즐이 뜨는 동안은 화면을 접는다. 떠 있으면 퍼즐 판을 가린다.
        if (manual) Hide();
        else Refresh();
    }

    private void HandleCrafted(CraftedEquipment result)
    {
        Show();
        var content = new UiSlotContent
        {
            Icon = result.weapon != null ? UiIconLibrary.Weapon(result.weapon.type) : null,
            Fallback = "?",
            Tier = UiTheme.TierOf(result.grade),
            Badge = EquipmentGradeNames.NameOf(result.grade),
        };
        string stat = result.weapon != null ? UiSlotContents.StatName(result.weapon.slot) : "공격";
        resultPopup.Show("제작 완료", content, result.name,
            $"{stat} x{EquipmentGradeRules.PowerOf(result.grade):0.00}\n만든 장비는 장비창에 보관했습니다.");
    }

    private void HandleFailed()
    {
        Show();
        toast.Show("제작 실패 — 재료와 골드를 잃었습니다.", UiToastKind.Danger);
    }

    // ---- 장비 합성 ------------------------------------------------------------

    private void ToggleSynthSlot(OwnedEquipment item)
    {
        if (item == null) return;

        if (synthSlots.Remove(item))
        {
            Refresh();
            return;
        }

        if (!EquipmentSynthesis.CanUse(item, out string reason))
        {
            toast.Show(reason, UiToastKind.Warning);
            return;
        }
        if (synthSlots.Count >= EquipmentSynthesis.SlotCount)
        {
            toast.Show($"장비는 {EquipmentSynthesis.SlotCount}개까지 넣을 수 있습니다. 넣은 장비를 눌러 빼세요.", UiToastKind.Info);
            return;
        }

        synthSlots.Add(item);
        Refresh();
    }

    private void RemoveSynthSlot(int index)
    {
        if (index < 0 || index >= synthSlots.Count) return;
        synthSlots.RemoveAt(index);
        Refresh();
    }

    private void AskSynthesize()
    {
        if (!EquipmentSynthesis.CanSynthesize(synthSlots, out string reason))
        {
            toast.Show(reason, UiToastKind.Warning);
            return;
        }

        long cost = EquipmentSynthesis.Cost(synthSlots);
        if (PlayerAccount.Balance(Currency.Gold) < cost)
        {
            toast.Show($"골드가 부족합니다. ({UiKit.Amount(cost)} 필요)", UiToastKind.Warning);
            return;
        }

        bool enhanced = false;
        for (int i = 0; i < synthSlots.Count; i++) enhanced |= synthSlots[i].Level > 0;

        confirm.Ask("장비 합성",
            $"재료 장비 {EquipmentSynthesis.SlotCount}개가 사라집니다." +
            (enhanced ? "\n" + UiTheme.Paint("강화 단계는 이어지지 않습니다.", UiTheme.Warning) : string.Empty),
            "합성", true, Synthesize);
    }

    private void Synthesize()
    {
        var used = new List<OwnedEquipment>(synthSlots);
        synthSlots.Clear();

        if (!EquipmentSynthesis.TrySynthesize(used, out OwnedEquipment result, out string reason))
        {
            synthSlots.AddRange(used);
            TrimSlots();
            toast.Show(reason, UiToastKind.Warning);
            Refresh();
            return;
        }

        Refresh();
        resultPopup.Show("합성 완료", UiSlotContents.Equipment(result), result.DisplayName,
            $"{UiSlotContents.StatLine(result)}\n새 장비는 장비창에 보관했습니다.");
    }

    // 창고가 바뀌어(다른 곳에서 썼거나 세이브가 지워졌거나) 칸에 넣은 것이 더는 쓸 수 없게 되면 뺀다.
    private void TrimSlots()
    {
        for (int i = craftSlots.Count - 1; i >= 0; i--)
            if (AvailableCount(craftSlots[i]) < 0) craftSlots.RemoveAt(i);

        for (int i = synthSlots.Count - 1; i >= 0; i--)
            if (!EquipmentSynthesis.CanUse(synthSlots[i], out _)) synthSlots.RemoveAt(i);
    }

    // ---- 갱신 ---------------------------------------------------------------

    private void Refresh()
    {
        if (craftFlow == null || synthFlow == null || resultPopup == null) return;

        // 시설 레벨로 여는 탭. 잠긴 쪽에 머물러 있으면(레벨이 낮은 세이브를 불러왔을 때) 열린 쪽으로 돌린다.
        bool canSynthesize = FacilityUnlocks.CanSynthesizeEquipment;
        bool canManual = FacilityUnlocks.CanCraftManually;
        pageTabs.SetLocked((int)Page.Synthesis, !canSynthesize);
        modeTabs.SetLocked(1, !canManual);
        if (!canSynthesize) page = Page.Craft;
        if (!canManual) manual = false;

        pageTabs.Select((int)page, false);
        craftPage.gameObject.SetActive(page == Page.Craft);
        synthesisPage.gameObject.SetActive(page == Page.Synthesis);

        if (page == Page.Craft) RefreshCraft();
        else RefreshSynthesis();
    }

    private void RefreshCraft()
    {
        for (int i = 0; i < CraftRecipe.SlotCount; i++)
        {
            UiSlot slot = craftFlow.Input(i);
            if (i < craftSlots.Count)
            {
                slot.SetContent(UiSlotContents.Material(craftSlots[i]));
                craftFlow.Caption(i).text = craftSlots[i].DisplayName;
            }
            else
            {
                slot.SetEmpty("재료");
                craftFlow.Caption(i).text = UiTheme.Paint("빈 칸", UiTheme.TextMuted);
            }
        }

        bool complete = CraftRecipe.IsComplete(craftSlots);
        craftFlow.SetCondition($"{craftSlots.Count} / {CraftRecipe.SlotCount}", complete);

        modeTabs.Select(manual ? 1 : 0, false);
        difficultyTabs.gameObject.SetActive(manual);
        difficultyTabs.Select(System.Array.IndexOf(Difficulties, difficulty), false);
        rateButton.gameObject.SetActive(manual);

        if (!complete)
        {
            craftFlow.SetResultPlaceholder(
                "재료 3개를 넣으세요.");
        }
        else
        {
            WeaponFamily family = CraftRecipe.FamilyOf(craftSlots);
            EquipmentGrade baseGrade = CraftRecipe.BaseGradeOf(craftSlots);
            EquipmentGrade topGrade = manual ? TopGrade(baseGrade) : baseGrade;
            string stat = UiSlotContents.StatNameOf(family);

            UiSlotContent preview = UiSlotContents.Preview(family, baseGrade);
            if (manual && topGrade != baseGrade) preview.Badge = EquipmentGradeNames.NameOf(baseGrade) + "+";

            string grade = manual && topGrade != baseGrade
                ? $"{GradeText(baseGrade)} ~ {GradeText(topGrade)}등급"
                : $"{GradeText(baseGrade)}등급";
            string stats =
                $"{CraftRecipe.FamilyContents(family)} 중 하나\n" +
                $"예상 능력치  {stat} x{EquipmentGradeRules.PowerOf(baseGrade):0.00}" +
                (manual && topGrade != baseGrade ? $" ~ x{EquipmentGradeRules.PowerOf(topGrade):0.00}" : string.Empty);
            string note = manual ? "실패하면 재료와 골드를 잃습니다." : null;

            craftFlow.SetResult(preview, CraftRecipe.FamilyName(family), grade, stats, note);
        }

        RefreshCraftRequirements(complete);
        RefreshMaterialList();
    }

    // 수동 제작에서 나올 수 있는 가장 높은 등급(난이도표에서 확률이 0보다 큰 가장 윗 칸).
    private EquipmentGrade TopGrade(EquipmentGrade baseGrade)
    {
        for (int g = Grades.Length - 1; g >= 0; g--)
            if (EquipmentCraftTable.PercentText(baseGrade, difficulty, Grades[g]) != "0%") return Grades[g];
        return baseGrade;
    }

    private void RefreshCraftRequirements(bool complete)
    {
        var rows = new List<UiFlowPanel.Requirement>();

        // 넣은 재료를 종류·등급별로 묶어 "보유 / 필요". 모자라면 빨강.
        var distinct = new List<CraftMaterial>();
        for (int i = 0; i < craftSlots.Count; i++) if (!distinct.Contains(craftSlots[i])) distinct.Add(craftSlots[i]);
        for (int i = 0; i < distinct.Count; i++)
        {
            int need = 0;
            for (int j = 0; j < craftSlots.Count; j++) if (craftSlots[j].Equals(distinct[i])) need++;
            int have = MaterialInventory.CountOf(distinct[i]);
            rows.Add(new UiFlowPanel.Requirement
            {
                Icon = UiIconLibrary.Material(distinct[i].Kind),
                Label = distinct[i].DisplayName,
                Have = have.ToString(),
                Need = need.ToString(),
                Met = have >= need,
            });
        }

        long cost = complete ? Forge.CostOf(craftSlots) : 0;
        long gold = PlayerAccount.Balance(Currency.Gold);
        rows.Add(new UiFlowPanel.Requirement
        {
            Icon = UiIconLibrary.Currency(Currency.Gold),
            Label = "골드",
            Have = UiKit.Amount(gold),
            Need = complete ? UiKit.Amount(cost) : "-",
            Met = !complete || gold >= cost,
        });
        craftFlow.SetRequirements(rows);

        craftFlow.Action.SetLabel(manual ? "제작 시작" : "제작");
        if (complete) craftFlow.Action.SetCost(Currency.Gold, cost);
        else craftFlow.Action.ClearCost();
        craftFlow.Action.interactable = complete;
    }

    private void RefreshMaterialList()
    {
        visibleMaterials.Clear();
        int filter = materialPicker.Tabs.Selected;
        int total = 0;

        // 좋은 재료부터. 같은 등급이면 강철·참나무·가죽 순.
        for (int g = Grades.Length - 1; g >= 0; g--)
        {
            foreach (MaterialKind kind in MaterialNames.AllKinds)
            {
                var material = new CraftMaterial(kind, Grades[g]);
                int count = MaterialInventory.CountOf(material);
                total += count;
                if (count <= 0) continue;
                if (filter > 0 && (int)kind != filter - 1) continue;
                visibleMaterials.Add(material);
            }
        }

        materialPicker.Count.text = $"{total}개";
        materialPicker.Grid.Show(visibleMaterials.Count, BindMaterial, "재료가 없습니다.");
    }

    private void BindMaterial(UiTile tile, int index)
    {
        CraftMaterial material = visibleMaterials[index];
        tile.Payload = material;

        int available = AvailableCount(material);
        int used = MaterialInventory.CountOf(material) - available;
        tile.Slot.SetContent(UiSlotContents.Material(material, available));
        tile.Slot.SetSelected(used > 0);
        tile.Slot.SetDimmed(available <= 0, "모두 넣음");
        tile.SetText(material.DisplayName, used > 0 ? $"넣음 {used} · 남음 {available}" : $"보유 {available}");
    }

    private void RefreshSynthesis()
    {
        for (int i = 0; i < EquipmentSynthesis.SlotCount; i++)
        {
            UiSlot slot = synthFlow.Input(i);
            if (i < synthSlots.Count)
            {
                slot.SetContent(UiSlotContents.Equipment(synthSlots[i]));
                synthFlow.Caption(i).text = synthSlots[i].DisplayName;
            }
            else
            {
                slot.SetEmpty("장비");
                synthFlow.Caption(i).text = UiTheme.Paint("빈 칸", UiTheme.TextMuted);
            }
        }

        bool complete = EquipmentSynthesis.IsComplete(synthSlots);
        synthFlow.SetCondition($"장비 {synthSlots.Count} / {EquipmentSynthesis.SlotCount}", complete);

        if (synthSlots.Count == 0)
        {
            synthFlow.SetResultPlaceholder(
                $"장비 {EquipmentSynthesis.SlotCount}개를 넣으세요.");
        }
        else
        {
            WeaponFamily family = EquipmentSynthesis.ResultFamily(synthSlots);
            EquipmentGrade average = EquipmentSynthesis.AverageGrade(synthSlots);
            EquipmentGrade result = EquipmentSynthesis.ResultGrade(synthSlots);
            bool ok = EquipmentSynthesis.CanSynthesize(synthSlots, out string reason);

            UiSlotContent preview = UiSlotContents.Preview(family, result);
            string name = $"{EquipmentGradeNames.PrefixOf(result)} {CraftRecipe.FamilyName(family)}";
            string grade = $"평균 {GradeText(average)} → {GradeText(result)}등급";
            string stats =
                $"{CraftRecipe.FamilyContents(family)} 중 하나\n" +
                $"예상 능력치  {UiSlotContents.StatNameOf(family)} x{EquipmentGradeRules.PowerOf(result):0.00}";
            string note = !complete ? $"장비를 {EquipmentSynthesis.SlotCount - synthSlots.Count}개 더 넣으세요."
                : !ok ? reason
                : "강화 단계는 이어지지 않습니다.";

            synthFlow.SetResult(preview, name, grade, stats, note, complete && !ok ? UiTheme.Danger : UiTheme.Warning);
        }

        long cost = complete ? EquipmentSynthesis.Cost(synthSlots) : 0;
        long gold = PlayerAccount.Balance(Currency.Gold);
        synthFlow.SetRequirements(new List<UiFlowPanel.Requirement>
        {
            new UiFlowPanel.Requirement
            {
                Label = "재료 장비",
                Have = synthSlots.Count.ToString(),
                Need = EquipmentSynthesis.SlotCount.ToString(),
                Met = complete,
            },
            new UiFlowPanel.Requirement
            {
                Icon = UiIconLibrary.Currency(Currency.Gold),
                Label = "골드",
                Have = UiKit.Amount(gold),
                Need = complete ? UiKit.Amount(cost) : "-",
                Met = !complete || gold >= cost,
            },
        });

        if (complete) synthFlow.Action.SetCost(Currency.Gold, cost);
        else synthFlow.Action.ClearCost();
        synthFlow.Action.interactable = complete && EquipmentSynthesis.CanSynthesize(synthSlots, out _);

        RefreshEquipmentList();
    }

    private void RefreshEquipmentList()
    {
        visibleEquipment.Clear();
        int filter = equipmentPicker.Tabs.Selected;
        IReadOnlyList<OwnedEquipment> items = EquipmentInventory.Items;
        for (int i = 0; i < items.Count; i++)
        {
            OwnedEquipment item = items[i];
            if (filter == 1 && item.Slot != EquipSlot.MainHand) continue;
            if (filter == 2 && item.Slot != EquipSlot.OffHand) continue;
            visibleEquipment.Add(item);
        }
        // 넣을 수 있는 것(보관 중)을 앞에, 그 안에서는 낮은 등급부터 — 합성은 보통 낮은 장비를 올리는 데 쓴다.
        visibleEquipment.Sort((a, b) =>
        {
            int byEquipped = a.IsEquipped.CompareTo(b.IsEquipped);
            if (byEquipped != 0) return byEquipped;
            int byGrade = ((int)a.Grade).CompareTo((int)b.Grade);
            return byGrade != 0 ? byGrade : a.Level.CompareTo(b.Level);
        });

        equipmentPicker.Count.text = $"{items.Count}개";
        equipmentPicker.Grid.Show(visibleEquipment.Count, BindEquipment,
            items.Count == 0 ? "장비가 없습니다." : "해당하는 장비가 없습니다.");
    }

    private void BindEquipment(UiTile tile, int index)
    {
        OwnedEquipment item = visibleEquipment[index];
        tile.Payload = item;

        UiSlotContent content = UiSlotContents.Equipment(item);
        UiSlotContents.ApplyOwnerTag(ref content, item, null);
        tile.Slot.SetContent(content);
        tile.Slot.SetSelected(synthSlots.Contains(item));
        tile.Slot.SetDimmed(item.IsEquipped, "장착 중");
        tile.SetText(item.DisplayName, UiSlotContents.StatLine(item), UiTheme.GradeColor(item.Grade));
    }

    private static string GradeText(EquipmentGrade grade) =>
        UiTheme.Paint(EquipmentGradeNames.NameOf(grade), UiTheme.GradeColor(grade));
}
