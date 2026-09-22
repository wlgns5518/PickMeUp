using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 재료 선택 → 결과 확인 → 실행. 캐릭터 합성·장비 합성·장비 제작이 모두 이 한 부품으로 짓는다.
//
//   ┌ 재료 선택 ─────────────────────── 2 / 3 ┐   ① 무엇을 넣었나 (조건 충족은 초록)
//   │   [슬롯]  +  [슬롯]  +  [슬롯]           │
//   └──────────────────────────────────────────┘
//                      ↓
//   ┏ 합성 결과 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┓   ② 무엇이 나오나 — 가장 크고 밝은 칸. 등급 색으로 빛난다.
//   ┃ [큰 슬롯]  이름 / 등급 / 예상 능력치      ┃
//   ┗━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┛
//   [ 선택지 탭 ]  (제작 방식처럼 필요한 화면만)
//   ┌ 필요한 것  보유/필요 ──────── [ 실행 ▸ 비용 ] ┐   ③ 무엇이 필요하고 지금 얼마 있나 + 실행 버튼
//
// 세 기능은 넣는 것(영웅·장비·재료)과 규칙이 다를 뿐 플레이어가 하는 일은 같다. 한 기능에서 익힌 자리
// (위에서 넣고, 가운데서 확인하고, 아래 오른쪽에서 누른다)가 다른 기능에서도 그대로 통해야 한다.
[DisallowMultipleComponent]
public class UiFlowPanel : MonoBehaviour
{
    public struct Layout
    {
        public int InputCount;
        public float InputSlotSize;
        public string InputTitle;   // "재료 선택"
        public string ResultTitle;  // "합성 결과"
        public string ActionLabel;  // "합성"
        public bool HasOptions;     // 결과와 실행 사이에 선택지 줄(탭)을 둘지
    }

    public struct Requirement
    {
        public Sprite Icon;
        public string Label;
        public string Have;
        public string Need;
        public bool Met;
    }

    private const float SectionHeaderHeight = 60f;
    private const float CaptionHeight = 34f;
    private const float ArrowHeight = 48f;
    private const float ResultHeight = 300f;
    private const float OptionsHeight = UiTheme.TabHeight + UiTheme.Space4;
    private const float ActionBarHeight = 136f;
    private const float ActionWidth = 380f;
    private const float RequirementRowHeight = 34f;
    private const int MaxRequirementRows = 3;

    private readonly List<UiSlot> inputs = new List<UiSlot>();
    private readonly List<TMP_Text> captions = new List<TMP_Text>();
    private TMP_Text conditionText;

    private UiKit.Surface resultCard;
    private TMP_Text resultName;
    private TMP_Text resultGrade;
    private TMP_Text resultStats;
    private TMP_Text resultNote;
    private TMP_Text resultHint;
    private float noteWidth;

    private readonly List<RectTransform> requirementRows = new List<RectTransform>();
    private readonly List<Image> requirementIcons = new List<Image>();
    private readonly List<TMP_Text> requirementLabels = new List<TMP_Text>();
    private readonly List<TMP_Text> requirementValues = new List<TMP_Text>();
    private TMP_Text requirementEmpty;

    public UiSlot ResultSlot { get; private set; }
    // 결과 칸 오른쪽 아래의 작은 버튼 자리("등급 확률").
    public RectTransform ResultActions { get; private set; }
    public RectTransform Options { get; private set; }
    public UiButton Action { get; private set; }
    public float Height { get; private set; }
    public RectTransform Rect => (RectTransform)transform;

    public UiSlot Input(int index) => inputs[index];
    public TMP_Text Caption(int index) => captions[index];

    /// width 폭의 세로 흐름을 짓는다. 자리는 돌려받은 Rect에 잡는다(높이는 Height).
    public static UiFlowPanel Create(RectTransform parent, string name, float width, Layout layout)
    {
        RectTransform rect = UiKit.Node(parent, name);
        var flow = rect.gameObject.AddComponent<UiFlowPanel>();
        flow.Build(width, layout);
        rect.sizeDelta = new Vector2(width, flow.Height);
        return flow;
    }

    private void Build(float width, Layout layout)
    {
        float y = 0f;

        // ① 재료 선택
        float inputsHeight = SectionHeaderHeight + layout.InputSlotSize + CaptionHeight + UiTheme.Space5;
        UiKit.Surface inputsPanel = UiKit.Panel(Rect, "Inputs", UiTheme.Surface, UiTheme.RadiusL, UiTheme.Border);
        UiKit.TopLeft(inputsPanel.Rect, 0f, y, width, inputsHeight);

        TMP_Text title = UiKit.SectionTitle(inputsPanel.Rect, "Title", layout.InputTitle);
        UiKit.TopStretch(title.rectTransform, 0f, SectionHeaderHeight, UiTheme.Space5, 200f);
        conditionText = UiKit.Text(inputsPanel.Rect, "Condition", string.Empty, UiTheme.FontBody, UiTheme.TextSecondary,
            TextAlignmentOptions.Right);
        UiKit.TopStretch(conditionText.rectTransform, 0f, SectionHeaderHeight, 300f, UiTheme.Space5);

        BuildInputs(inputsPanel.Rect, layout);
        y += inputsHeight;

        // ↓
        Image arrow = UiKit.Glyph(Rect, "Arrow", UiSprites.Glyph.ChevronDown, UiTheme.TextMuted);
        UiKit.TopCenter(arrow.rectTransform, 0f, y + 4f, ArrowHeight - 8f, ArrowHeight - 8f);
        y += ArrowHeight;

        // ② 결과
        BuildResult(width, y, layout.ResultTitle);
        y += ResultHeight + UiTheme.Space4;

        if (layout.HasOptions)
        {
            Options = UiKit.Node(Rect, "Options");
            UiKit.TopLeft(Options, 0f, y, width, UiTheme.TabHeight);
            y += OptionsHeight;
        }

        // ③ 실행
        BuildActionBar(width, y, layout.ActionLabel);
        y += ActionBarHeight;

        Height = y;
    }

    private void BuildInputs(RectTransform panel, Layout layout)
    {
        int count = Mathf.Max(1, layout.InputCount);
        float slot = layout.InputSlotSize;
        float plus = Mathf.Clamp(slot * 0.36f, 36f, 64f);
        float rowWidth = count * slot + (count - 1) * (plus + UiTheme.Space5 * 2f);
        float x = -rowWidth * 0.5f + slot * 0.5f;

        for (int i = 0; i < count; i++)
        {
            UiSlot s = UiSlot.Create(panel, "Input_" + i, slot);
            UiKit.TopCenter(s.Rect, x, SectionHeaderHeight, slot, slot);
            inputs.Add(s);

            TMP_Text caption = UiKit.Ellipsis(UiKit.Text(panel, "Caption_" + i, string.Empty, UiTheme.FontLabel,
                UiTheme.TextSecondary, TextAlignmentOptions.Center));
            UiKit.TopCenter(caption.rectTransform, x, SectionHeaderHeight + slot + 2f, slot + 60f, CaptionHeight);
            captions.Add(caption);

            if (i < count - 1)
            {
                Image plusGlyph = UiKit.Glyph(panel, "Plus_" + i, UiSprites.Glyph.Plus, UiTheme.TextMuted);
                float plusX = x + slot * 0.5f + UiTheme.Space5 + plus * 0.5f;
                UiKit.TopCenter(plusGlyph.rectTransform, plusX, SectionHeaderHeight + (slot - plus) * 0.5f, plus, plus);
            }
            x += slot + plus + UiTheme.Space5 * 2f;
        }
    }

    private void BuildResult(float width, float y, string titleText)
    {
        // 결과 칸은 재료 칸보다 한 단계 밝은 판에 그림자를 깔아 떠 보이게 하고, 테두리는 결과 등급 색으로 칠한다.
        resultCard = UiKit.Panel(Rect, "Result", UiTheme.SurfaceRaised, UiTheme.RadiusL, UiTheme.BorderStrong, 24);
        UiKit.TopLeft(resultCard.Rect, 0f, y, width, ResultHeight);
        resultCard.Border.sprite = UiSprites.Outline(UiTheme.RadiusL, 3);

        TMP_Text title = UiKit.SectionTitle(resultCard.Rect, "Title", titleText);
        UiKit.TopStretch(title.rectTransform, 0f, SectionHeaderHeight, UiTheme.Space5, UiTheme.Space5);

        float slotSize = ResultHeight - SectionHeaderHeight - UiTheme.Space5 - 12f;
        ResultSlot = UiSlot.Create(resultCard.Rect, "Slot", slotSize);
        UiKit.TopLeft(ResultSlot.Rect, UiTheme.Space5 + 8f, SectionHeaderHeight, slotSize, slotSize);
        ResultSlot.SetInteractable(false);

        float textX = UiTheme.Space5 + 8f + slotSize + UiTheme.Space5 + 8f;
        float textWidth = width - textX - UiTheme.Space5;

        resultName = UiKit.Ellipsis(UiKit.Text(resultCard.Rect, "Name", string.Empty, UiTheme.FontTitle, UiTheme.TextPrimary));
        UiKit.TopLeft(resultName.rectTransform, textX, SectionHeaderHeight - 4f, textWidth, 52f);

        resultGrade = UiKit.Text(resultCard.Rect, "Grade", string.Empty, UiTheme.FontBody, UiTheme.TextSecondary);
        UiKit.TopLeft(resultGrade.rectTransform, textX, SectionHeaderHeight + 50f, textWidth, 36f);

        resultStats = UiKit.Wrap(UiKit.Text(resultCard.Rect, "Stats", string.Empty, UiTheme.FontBody, UiTheme.TextPrimary));
        resultStats.alignment = TextAlignmentOptions.TopLeft;
        resultStats.lineSpacing = 6f;
        UiKit.TopLeft(resultStats.rectTransform, textX, SectionHeaderHeight + 92f, textWidth, 90f);

        resultNote = UiKit.Wrap(UiKit.Text(resultCard.Rect, "Note", string.Empty, UiTheme.FontLabel, UiTheme.Warning));
        resultNote.alignment = TextAlignmentOptions.BottomLeft;
        UiKit.TopLeft(resultNote.rectTransform, textX, ResultHeight - 70f - UiTheme.Space4, textWidth, 70f);
        noteWidth = textWidth;

        resultHint = UiKit.Wrap(UiKit.Text(resultCard.Rect, "Hint", string.Empty, UiTheme.FontBody, UiTheme.TextMuted));
        UiKit.TopLeft(resultHint.rectTransform, textX, SectionHeaderHeight, textWidth, slotSize);

        ResultActions = UiKit.Node(resultCard.Rect, "Actions");
        UiKit.BottomRight(ResultActions, UiTheme.Space5, UiTheme.Space5, 260f, UiTheme.ButtonSmall);
    }

    private void BuildActionBar(float width, float y, string actionLabel)
    {
        UiKit.Surface bar = UiKit.Panel(Rect, "ActionBar", UiTheme.Surface, UiTheme.RadiusL, UiTheme.Border);
        UiKit.TopLeft(bar.Rect, 0f, y, width, ActionBarHeight);

        float listWidth = width - ActionWidth - UiTheme.Space5 * 3f;
        float listTop = (ActionBarHeight - MaxRequirementRows * RequirementRowHeight) * 0.5f;

        for (int i = 0; i < MaxRequirementRows; i++)
        {
            RectTransform row = UiKit.Node(bar.Rect, "Requirement_" + i);
            UiKit.TopLeft(row, UiTheme.Space5, listTop + i * RequirementRowHeight, listWidth, RequirementRowHeight);

            Image icon = UiKit.Image(row, "Icon", null, Color.white);
            UiKit.LeftMiddle(icon.rectTransform, 0f, RequirementRowHeight, RequirementRowHeight);

            TMP_Text label = UiKit.Ellipsis(UiKit.Text(row, "Label", string.Empty, UiTheme.FontLabel, UiTheme.TextSecondary));
            UiKit.Fill(label.rectTransform, RequirementRowHeight + UiTheme.Space2, 0f, 150f, 0f);

            TMP_Text value = UiKit.Text(row, "Value", string.Empty, UiTheme.FontLabel, UiTheme.TextPrimary, TextAlignmentOptions.Right);
            UiKit.RightMiddle(value.rectTransform, 0f, 150f, RequirementRowHeight);

            requirementRows.Add(row);
            requirementIcons.Add(icon);
            requirementLabels.Add(label);
            requirementValues.Add(value);
        }

        requirementEmpty = UiKit.Text(bar.Rect, "Empty", string.Empty, UiTheme.FontLabel, UiTheme.TextMuted);
        UiKit.LeftMiddle(requirementEmpty.rectTransform, UiTheme.Space5, listWidth, ActionBarHeight);

        Action = UiButton.Create(bar.Rect, "Action", actionLabel, UiButtonStyle.Primary, UiButtonSize.Large, null);
        UiKit.RightMiddle(Action.Rect, UiTheme.Space5, ActionWidth, UiTheme.ButtonLarge);
    }

    // ---- 갱신 ---------------------------------------------------------------

    /// 재료 선택 칸 오른쪽 위의 조건("2 / 3"). 채웠으면 초록.
    public void SetCondition(string text, bool met)
    {
        conditionText.text = text ?? string.Empty;
        conditionText.color = met ? UiTheme.Success : UiTheme.TextSecondary;
    }

    /// 아직 결과를 알 수 없을 때. 결과 칸은 "?"로, 오른쪽에는 무엇을 더 해야 하는지.
    public void SetResultPlaceholder(string hint)
    {
        ResultSlot.SetContent(new UiSlotContent { Fallback = "?" });
        resultName.text = string.Empty;
        resultGrade.text = string.Empty;
        resultStats.text = string.Empty;
        resultNote.text = string.Empty;
        resultHint.text = hint ?? string.Empty;
        resultCard.Border.color = UiTheme.BorderStrong;
        if (resultCard.Shadow != null) resultCard.Shadow.color = UiTheme.Shadow;
    }

    /// 결과 미리보기. stats는 여러 줄(리치 텍스트), note는 경고·주의(없으면 null).
    public void SetResult(UiSlotContent slot, string name, string grade, string stats, string note, Color? noteColor = null)
    {
        ResultSlot.SetContent(slot);
        resultHint.text = string.Empty;
        resultName.text = name ?? string.Empty;
        resultName.color = slot.Tier > 0 ? UiTheme.Tier(slot.Tier) : UiTheme.TextPrimary;
        resultGrade.text = grade ?? string.Empty;
        resultStats.text = stats ?? string.Empty;
        resultNote.text = note ?? string.Empty;
        resultNote.color = noteColor ?? UiTheme.Warning;
        // 오른쪽 아래에 작은 버튼("등급 확률")이 떠 있을 때만 그 폭을 비운다. 없으면 안내가 한 줄에 다 들어가게.
        float reserved = HasVisibleActions() ? ResultActions.sizeDelta.x + UiTheme.Space4 : 0f;
        resultNote.rectTransform.sizeDelta = new Vector2(noteWidth - reserved, resultNote.rectTransform.sizeDelta.y);

        // 결과는 등급 색으로 빛난다. 무엇이 나오는지가 이 화면에서 가장 먼저 읽혀야 한다.
        Color glow = slot.Tier > 0 ? UiTheme.Tier(slot.Tier) : UiTheme.Selection;
        resultCard.Border.color = glow;
        if (resultCard.Shadow != null) resultCard.Shadow.color = UiTheme.WithAlpha(glow, 0.35f);
    }

    private bool HasVisibleActions()
    {
        for (int i = 0; i < ResultActions.childCount; i++)
            if (ResultActions.GetChild(i).gameObject.activeSelf) return true;
        return false;
    }

    /// 실행 막대 왼쪽의 "필요한 것 — 보유 / 필요". 최대 세 줄. 비었으면 emptyText.
    public void SetRequirements(IReadOnlyList<Requirement> requirements, string emptyText = null)
    {
        int count = requirements != null ? Mathf.Min(requirements.Count, MaxRequirementRows) : 0;
        for (int i = 0; i < requirementRows.Count; i++)
        {
            bool show = i < count;
            requirementRows[i].gameObject.SetActive(show);
            if (!show) continue;

            Requirement r = requirements[i];
            requirementIcons[i].sprite = r.Icon;
            requirementIcons[i].enabled = r.Icon != null;
            requirementLabels[i].rectTransform.offsetMin = new Vector2(r.Icon != null ? RequirementRowHeight + UiTheme.Space2 : 0f, 0f);
            requirementLabels[i].text = r.Label;

            // 모자란 쪽(보유)이 빨갛다. "12 / 20"에서 무엇이 모자란지 한눈에.
            string have = UiTheme.Paint(r.Have, r.Met ? UiTheme.Success : UiTheme.Danger);
            requirementValues[i].text = string.IsNullOrEmpty(r.Need) ? have : $"{have} <color=#{UiTheme.Html(UiTheme.TextMuted)}>/</color> {r.Need}";
        }

        requirementEmpty.gameObject.SetActive(count == 0);
        requirementEmpty.text = emptyText ?? string.Empty;
    }
}
