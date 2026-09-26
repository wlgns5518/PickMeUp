using System.Collections.Generic;
using TMPro;
using UnityEngine;

// 캐릭터 합성소 — 베이스 영웅이 다른 영웅을 재료로 태워 스킬 하나를 배운다.
//
//   ┌ 영웅 선택 ──────────── 1 / 2 ┐   ┌ 보유 영웅 ─────── 8명 ┐
//   │  [베이스]    +    [재료]      │   │ [ 등급순 | 레벨순 ]    │
//   └──────────────────────────────┘   │ [칸][칸][칸][칸][칸]   │
//                 ↓                     │                        │
//   ┏ 합성 결과 ━━━━━━━━━━━━━━━━━━━┓   │                        │
//   ┃ [베이스] 이름 / 스킬 2 → 3    ┃   │                        │
//   ┗━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┛   │                        │
//   ┌ 필요한 것 ────────── [ 합성 ] ┐   └────────────────────────┘
//
// 이 게임의 영웅은 저마다 고유해서 같은 영웅이 두 번 나오지 않는다. 그래서 "중복 카드 합치기"가 아니라
// 다른 영웅 한 명을 바치는 합성이고, 재료는 영영 사라진다. 그 무게가 화면에서 먼저 읽히도록:
//   - 베이스(남는 쪽)는 선택색, 재료(사라지는 쪽)는 위험색으로 칸·딱지·설명을 칠한다.
//   - 결과 칸에 "재료 영웅은 사라집니다"를 적고, 실행 전에 한 번 더 묻는다.
//
// 규칙(조건·골드·스킬 굴림)은 CharacterSynthesis에 있다. 이 화면은 고르고 보여 주기만 한다.
// 장비 합성·장비 제작과 같은 흐름 부품(UiFlowPanel)과 목록 부품(UiPickerPanel)으로 짓는다.
[DisallowMultipleComponent]
public class SynthesisUI : UiScreen
{
    [Header("Roster")]
    [Tooltip("시작 명단. 실제 보유 목록은 런타임(OwnedRoster)이 들고, 이건 씬에 부트스트랩이 없을 때의 대비책이다.")]
    [SerializeField] private CharacterRosterSO roster;

    [Header("Open State")]
    [Tooltip("합성소를 누르지 않아도 처음부터 열려 있게 하려면 켠다.")]
    [SerializeField] private bool openOnStart;

    private const float FlowWidth = 1040f;
    private const int BaseIndex = 0;
    private const int MaterialIndex = 1;
    private static readonly string[] SortTabs = { "등급순", "레벨순" };

    private UiFlowPanel flow;
    private UiPickerPanel picker;
    private UiResultPopup resultPopup;

    private readonly List<CharacterSO> sorted = new List<CharacterSO>();
    private CharacterSO main;
    private CharacterSO material;

    protected override string CanvasName => "SynthesisCanvas";
    // 소환 창(96)보다 위.
    protected override int SortingOrder => 97;
    protected override string Title => "캐릭터 합성소";

    private void Awake()
    {
        // 로스터 에셋은 시작 명단이다. Seed는 이미 있는 캐릭터를 건너뛰므로 두 번 불려도 결과가 같다.
        if (roster != null) OwnedRoster.Seed(roster.Members);

        EnsureBuilt();
        SetOpen(openOnStart);
    }

    private void OnEnable()
    {
        OwnedRoster.Changed += HandleDataChanged;
        PlayerAccount.Changed += HandleDataChanged;
    }

    private void OnDisable()
    {
        OwnedRoster.Changed -= HandleDataChanged;
        PlayerAccount.Changed -= HandleDataChanged;
    }

    public override void Show()
    {
        EnsureBuilt();
        Refresh();
        picker.Grid.ScrollToTop();
        SetOpen(true);
    }

    // 닫으면 올려 둔 영웅을 내려놓는다. 지난번 재료가 걸린 줄 모르고 다시 열어 합성을 누르면 엉뚱한 영웅이 사라진다.
    public override void Hide()
    {
        main = null;
        material = null;
        SetOpen(false);
    }

    private void HandleDataChanged()
    {
        // 명단에서 사라진 영웅을 자리에 물고 있으면 없는 영웅으로 합성하게 된다.
        if (main != null && !OwnedRoster.Contains(main)) main = null;
        if (material != null && !OwnedRoster.Contains(material)) material = null;
        if (IsOpen) Refresh();
    }

    // ---- 짓기 ---------------------------------------------------------------

    protected override void BuildContent(RectTransform root, Vector2 size)
    {
        flow = UiFlowPanel.Create(root, "Flow", FlowWidth, new UiFlowPanel.Layout
        {
            InputCount = 2,
            InputSlotSize = UiTheme.SlotLarge,
            InputTitle = "영웅 선택",
            ResultTitle = "합성 결과",
            ActionLabel = "합성",
        });
        UiKit.TopLeft(flow.Rect, 0f, 0f, FlowWidth, flow.Height);

        flow.Caption(BaseIndex).text = UiTheme.Paint("베이스 영웅", UiTheme.Selection) + " · 남음";
        flow.Caption(MaterialIndex).text = UiTheme.Paint("재료 영웅", UiTheme.Danger) + " · 사라짐";
        flow.Input(BaseIndex).Clicked += () => { main = null; Refresh(); };
        flow.Input(MaterialIndex).Clicked += () => { material = null; Refresh(); };
        flow.Action.onClick.AddListener(AskSynthesize);

        float pickerX = FlowWidth + UiTheme.ColumnGap;
        picker = UiPickerPanel.Create(root, "Heroes", size.x - pickerX, "보유 영웅", SortTabs, UiTheme.SlotMedium);
        UiKit.Fill(picker.Rect, pickerX, 0f, 0f, 0f);
        picker.Tabs.Changed += _ => Refresh();
        picker.Grid.TileClicked += tile => Pick(tile.Payload as CharacterSO);
    }

    protected override void BuildOverlays()
    {
        resultPopup = new UiResultPopup(overlay);
        Refresh();
    }

    // ---- 고르기 -------------------------------------------------------------

    // 목록에서 누르면 빈 자리에 올라가고, 올라간 영웅을 다시 누르면 내려온다. 두 자리가 다 찼을 때 새 영웅을
    // 누르면 재료 쪽이 바뀐다 — 베이스는 보통 그대로 두고 재료만 갈아 끼운다.
    private void Pick(CharacterSO hero)
    {
        if (hero == null) return;

        if (hero == main) main = null;
        else if (hero == material) material = null;
        else if (main == null) main = hero;
        else material = hero;

        Refresh();
    }

    // ---- 합성 ---------------------------------------------------------------

    private void AskSynthesize()
    {
        if (!CharacterSynthesis.CanSynthesize(main, material, out string reason))
        {
            toast.Show(reason, UiToastKind.Warning);
            return;
        }

        long cost = CharacterSynthesis.Cost(material);
        if (PlayerAccount.Balance(Currency.Gold) < cost)
        {
            toast.Show($"골드가 부족합니다. ({UiKit.Amount(cost)} 필요)", UiToastKind.Warning);
            return;
        }

        string materialName = UiTheme.Paint(HeroLabel.Name(material), UiTheme.Danger);
        confirm.Ask("캐릭터 합성",
            $"{materialName}{HeroLabel.SubjectParticle(HeroLabel.Name(material))} 사라집니다.\n" +
            "장착한 장비는 창고로 돌아갑니다.",
            "합성", true, Synthesize);
    }

    private void Synthesize()
    {
        CharacterSO baseHero = main;
        string consumedName = HeroLabel.Name(material);

        // 자리를 먼저 비우지 않는다 — 명단이 바뀌면 HandleDataChanged가 사라진 재료를 알아서 내려놓는다.
        if (!CharacterSynthesis.TrySynthesize(main, material, out string skillId, out string reason))
        {
            toast.Show(reason, UiToastKind.Warning);
            return;
        }

        material = null;
        Refresh();

        SkillDefinition? skill = SkillCatalog.Find(skillId);
        string description = skill.HasValue ? skill.Value.Description : string.Empty;
        resultPopup.Show("합성 완료", UiSlotContents.Hero(baseHero), HeroLabel.Name(baseHero),
            $"{consumedName}의 힘으로 새 스킬을 배웠습니다.\n" +
            $"{UiTheme.Paint("[" + SkillCatalog.NameOf(skillId) + "]", UiTheme.Primary)}\n{description}");
    }

    // ---- 갱신 ---------------------------------------------------------------

    private void Refresh()
    {
        if (flow == null || picker == null) return;

        RefreshInputs();
        RefreshResult();
        RefreshRequirements();
        RefreshList();
    }

    private void RefreshInputs()
    {
        ApplyInput(flow.Input(BaseIndex), main, "베이스 선택", UiTheme.Selection);
        ApplyInput(flow.Input(MaterialIndex), material, "재료 선택", UiTheme.Danger);

        int picked = (main != null ? 1 : 0) + (material != null ? 1 : 0);
        flow.SetCondition($"{picked} / 2", picked == 2);
    }

    private static void ApplyInput(UiSlot slot, CharacterSO hero, string hint, Color roleColor)
    {
        if (hero == null)
        {
            slot.SetEmpty(hint);
            slot.SetSelected(false);
            return;
        }

        slot.SetContent(UiSlotContents.Hero(hero));
        // 넣은 칸의 고리는 역할 색이다. 체크는 "고른 것"이 아니라 "빼려면 누르세요"라는 표시로 남긴다.
        slot.SetSelected(true, roleColor);
    }

    private void RefreshResult()
    {
        if (main == null)
        {
            flow.SetResultPlaceholder("베이스 영웅을 고르세요.");
            flow.Action.interactable = false;
            return;
        }

        int skills = main.SkillCount;
        int max = FacilityUnlocks.SynthesisSkillCap;
        string grade = $"{UiTheme.Paint(UiKit.Stars(main.starCount), UiTheme.StarColor(main.starCount))} · Lv.{main.Level}";

        if (material == null)
        {
            flow.SetResult(UiSlotContents.Hero(main), HeroLabel.Name(main), grade,
                $"스킬 {skills} / {max}", "재료 영웅을 고르세요.", UiTheme.TextMuted);
            flow.Action.interactable = false;
            return;
        }

        bool ok = CharacterSynthesis.CanSynthesize(main, material, out string reason);
        string stats = ok
            ? $"스킬 {skills} → {UiTheme.Paint((skills + 1).ToString(), UiTheme.Success)} / {max}\n" +
              "아직 없는 스킬 중 무작위"
            : $"스킬 {skills} / {max}";

        string note = ok ? $"{HeroLabel.Name(material)} 소멸" : reason;

        flow.SetResult(UiSlotContents.Hero(main), HeroLabel.Name(main), grade, stats, note, ok ? UiTheme.Danger : UiTheme.Warning);
        flow.Action.interactable = ok;
    }

    private void RefreshRequirements()
    {
        long cost = CharacterSynthesis.Cost(material);
        long gold = PlayerAccount.Balance(Currency.Gold);

        var rows = new List<UiFlowPanel.Requirement>
        {
            new UiFlowPanel.Requirement { Label = "베이스 영웅", Have = main != null ? "1" : "0", Need = "1", Met = main != null },
            new UiFlowPanel.Requirement { Label = "재료 영웅", Have = material != null ? "1" : "0", Need = "1", Met = material != null },
            new UiFlowPanel.Requirement
            {
                Icon = UiIconLibrary.Currency(Currency.Gold), Label = "골드",
                Have = UiKit.Amount(gold), Need = material != null ? UiKit.Amount(cost) : "-", Met = material == null || gold >= cost,
            },
        };
        flow.SetRequirements(rows);

        if (material != null) flow.Action.SetCost(Currency.Gold, cost);
        else flow.Action.ClearCost();
    }

    private void RefreshList()
    {
        sorted.Clear();
        IReadOnlyList<CharacterSO> members = OwnedRoster.Members;
        for (int i = 0; i < members.Count; i++) if (members[i] != null) sorted.Add(members[i]);

        bool byLevel = picker.Tabs.Selected == 1;
        sorted.Sort((a, b) =>
        {
            int primary = byLevel ? b.Level.CompareTo(a.Level) : b.starCount.CompareTo(a.starCount);
            if (primary != 0) return primary;
            int secondary = byLevel ? b.starCount.CompareTo(a.starCount) : b.Level.CompareTo(a.Level);
            return secondary != 0 ? secondary : string.CompareOrdinal(HeroLabel.Name(a), HeroLabel.Name(b));
        });

        picker.Count.text = $"{sorted.Count}명";
        picker.Grid.Show(sorted.Count, BindHero, "보유한 영웅이 없습니다.");
    }

    private void BindHero(UiTile tile, int index)
    {
        CharacterSO hero = sorted[index];
        tile.Payload = hero;

        UiSlotContent content = UiSlotContents.Hero(hero);
        bool isMain = hero == main;
        bool isMaterial = hero == material;
        if (isMain) { content.Tag = "베이스"; content.TagColor = UiTheme.Selection; }
        else if (isMaterial) { content.Tag = "재료"; content.TagColor = UiTheme.Danger; }

        tile.Slot.SetContent(content);
        tile.Slot.SetSelected(isMain || isMaterial, isMaterial ? UiTheme.Danger : UiTheme.Selection);
        tile.Slot.SetDimmed(false);
        tile.SetText(HeroLabel.Name(hero), $"스킬 {hero.SkillCount}/{FacilityUnlocks.SynthesisSkillCap}");
    }
}
