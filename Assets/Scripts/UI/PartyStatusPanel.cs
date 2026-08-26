using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 화면 왼쪽에 세로로 쌓이는 아군 파티 패널 — 원작 게임 화면의 그 UI.
// 캐릭터 초상화 + 체력 게이지 + 마나 게이지 + 감정 라벨을 한 줄로 묶는다.
//
// 스테미너는 히든 스탯(HiddenStats.stamina)이라 겉으로 드러내지 않는다.
// 공포도 수치를 노출하지 않고 감정 라벨(공포/패닉/출혈/빈사/붕괴)로만 드러난다.
//
// 유닛 머리 위 바가 아니라 고정 패널인 이유는 원작 화면 구성을 따른 것이기도 하지만,
// 난전에서 바가 서로 겹쳐 아무것도 못 읽는 문제도 같이 사라진다.
public class PartyStatusPanel
{
    private const float RowHeight = 78f;
    private const float RowSpacing = 8f;
    private const float PortraitSize = 72f;
    private const float GaugeLeft = 80f;
    private const float GaugeWidth = 210f;
    private const float HpHeight = 16f;
    private const float ManaHeight = 8f;

    private static readonly StringBuilder LabelBuilder = new StringBuilder(24);

    // 평소에는 거의 보이지 않는 판(클릭 판정을 받으려면 완전 투명이면 안 된다),
    // 선택된 슬롯만 옅게 밝혀 지금 카메라가 누구를 보고 있는지 드러낸다.
    private static readonly Color SlotIdleColor = new Color(1f, 1f, 1f, 0.01f);
    private static readonly Color SlotSelectedColor = new Color(1f, 0.95f, 0.6f, 0.22f);

    // 한 명분 슬롯. 전투가 시작될 때 아군 수만큼 만들어지고 그 뒤로는 값만 갱신된다.
    private class Slot
    {
        public UnitController Unit;
        public RectTransform Root;
        public Image SelectionHighlight;
        public Image PortraitFrame;
        public Image Portrait;
        public Image HpFill;
        public Image ManaFill;
        public TMP_Text EmotionLabel;

        public float AppliedHpRatio = -1f;
        public float AppliedManaRatio = -1f;
        public EmotionState AppliedEmotion = (EmotionState)(-1);
        public bool AppliedDead;
        public bool AppliedSelected;
        public bool HasAppliedState;
    }

    private readonly RectTransform root;
    private readonly TMP_FontAsset font;
    private readonly List<Slot> slots = new List<Slot>();

    // 슬롯을 누르면 그 유닛을 넘긴다. 카메라 이동은 이 패널이 직접 하지 않는다 —
    // UI는 "누가 선택됐는지"만 알리고, 그걸로 무엇을 할지는 BattleHud가 정한다.
    public event Action<UnitController> UnitClicked;

    // 지금 선택된 유닛. 슬롯 강조 표시에만 쓴다.
    private UnitController selectedUnit;

    private PartyStatusPanel(RectTransform parent, TMP_FontAsset font, Vector2 margin)
    {
        this.font = font;

        root = HudFactory.CreateGroup(parent, "PartyStatusPanel");
        // 화면 왼쪽 위 기준. 해상도가 바뀌어도 같은 자리에 붙어 있도록 앵커를 모서리에 고정한다.
        root.anchorMin = new Vector2(0f, 1f);
        root.anchorMax = new Vector2(0f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = new Vector2(margin.x, -margin.y);
        root.sizeDelta = new Vector2(GaugeLeft + GaugeWidth, 0f);
    }

    public static PartyStatusPanel Create(RectTransform parent, TMP_FontAsset font, Vector2 margin)
    {
        return new PartyStatusPanel(parent, font, margin);
    }

    // 전투가 시작될 때 한 번 호출. 이후 아군이 죽어 레지스트리에서 빠져도 슬롯은 그대로 남아
    // "누가 쓰러졌는지"를 계속 보여준다(원작에서 잃은 동료가 목록에서 사라지지 않는 것과 같다).
    public void Bind(IReadOnlyList<UnitController> allies)
    {
        int count = allies != null ? allies.Count : 0;

        for (int i = 0; i < count; i++)
        {
            Slot slot = GetSlot(i);
            slot.Unit = allies[i];
            slot.HasAppliedState = false;
            slot.Root.gameObject.SetActive(true);
            ApplyPortrait(slot);
        }

        for (int i = count; i < slots.Count; i++)
        {
            slots[i].Unit = null;
            slots[i].Root.gameObject.SetActive(false);
        }
    }

    public void Refresh()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            Slot slot = slots[i];
            if (slot.Unit == null || !slot.Root.gameObject.activeSelf) continue;

            RefreshSlot(slot);
        }
    }

    // 카메라가 실제로 잡은 대상을 UI에 되비춘다. 클릭이 아니라 카메라 쪽에서 시작된
    // 선택(전투 시작 시 첫 아군)도 같은 경로로 강조되도록 이 메서드를 단일 출처로 둔다.
    public void SetSelected(UnitController unit)
    {
        selectedUnit = unit;
    }

    private void HandleSlotClicked(int index)
    {
        if (index < 0 || index >= slots.Count) return;

        UnitController unit = slots[index].Unit;
        if (unit == null) return;

        UnitClicked?.Invoke(unit);
    }

    private void RefreshSlot(Slot slot)
    {
        UnitController unit = slot.Unit;
        UnitStats stats = unit.Stats;

        float hpRatio = Mathf.Clamp01(stats.currentHp / Mathf.Max(1f, stats.maxHp));
        bool isDead = unit.IsDead;

        if (!slot.HasAppliedState || !Mathf.Approximately(hpRatio, slot.AppliedHpRatio))
        {
            slot.AppliedHpRatio = hpRatio;
            slot.HpFill.rectTransform.sizeDelta = new Vector2(GaugeWidth * hpRatio, HpHeight);
        }

        UnitEmotion emotion = unit.Emotion;

        // 쓰러진 뒤에는 마나도 의미가 없으므로 게이지를 비우고 슬롯 전체를 어둡게 만든다.
        float manaRatio = isDead ? 0f : Mathf.Clamp01(stats.currentMana / Mathf.Max(1f, stats.maxMana));
        if (!slot.HasAppliedState || !Mathf.Approximately(manaRatio, slot.AppliedManaRatio))
        {
            slot.AppliedManaRatio = manaRatio;
            slot.ManaFill.rectTransform.sizeDelta = new Vector2(GaugeWidth * manaRatio, ManaHeight);
        }

        EmotionState state = !isDead && emotion != null ? emotion.State : EmotionState.None;
        if (!slot.HasAppliedState || state != slot.AppliedEmotion || isDead != slot.AppliedDead)
        {
            slot.AppliedEmotion = state;
            ApplyEmotionLabel(slot, state, isDead);
        }

        if (!slot.HasAppliedState || isDead != slot.AppliedDead)
        {
            slot.AppliedDead = isDead;
            Color tint = isDead ? BattleHudPalette.DeadTint : BattleHudPalette.AliveTint;
            slot.Portrait.color = tint;
            slot.PortraitFrame.color = isDead
                ? BattleHudPalette.DeadTint * BattleHudPalette.PortraitFrame
                : BattleHudPalette.PortraitFrame;
        }

        bool selected = unit == selectedUnit;
        if (!slot.HasAppliedState || selected != slot.AppliedSelected)
        {
            slot.AppliedSelected = selected;
            slot.SelectionHighlight.color = selected ? SlotSelectedColor : SlotIdleColor;
        }

        slot.HasAppliedState = true;
    }

    // 여러 감정이 겹칠 수 있어 가장 심각한 것 하나를 대표로 보여주고, 성격이 다른 출혈만 덧붙인다.
    private void ApplyEmotionLabel(Slot slot, EmotionState state, bool isDead)
    {
        if (isDead)
        {
            slot.EmotionLabel.text = "전투 불능";
            slot.EmotionLabel.color = BattleHudPalette.Dying;
            return;
        }

        EmotionState primary = GetMostSevere(state);
        bool bleeding = (state & EmotionState.Bleeding) != 0;

        if (primary == EmotionState.None && !bleeding)
        {
            slot.EmotionLabel.text = "";
            return;
        }

        LabelBuilder.Clear();
        if (primary != EmotionState.None) LabelBuilder.Append(UnitEmotion.Korean(primary));
        if (bleeding)
        {
            if (LabelBuilder.Length > 0) LabelBuilder.Append(' ');
            LabelBuilder.Append(UnitEmotion.Korean(EmotionState.Bleeding));
        }

        slot.EmotionLabel.SetText(LabelBuilder);
        slot.EmotionLabel.color = BattleHudPalette.ForEmotion(
            primary != EmotionState.None ? primary : EmotionState.Bleeding);
    }

    private static EmotionState GetMostSevere(EmotionState state)
    {
        if ((state & EmotionState.Broken) != 0) return EmotionState.Broken;
        if ((state & EmotionState.Dying) != 0) return EmotionState.Dying;
        if ((state & EmotionState.Panic) != 0) return EmotionState.Panic;
        if ((state & EmotionState.Fear) != 0) return EmotionState.Fear;
        return EmotionState.None;
    }

    private void ApplyPortrait(Slot slot)
    {
        CharacterSO source = slot.Unit != null ? slot.Unit.SourceCharacter : null;
        Sprite portrait = source != null ? source.portrait : null;

        slot.Portrait.sprite = portrait;
        // 초상화가 없는 캐릭터는 빈 칸 대신 프레임만 보이게 둔다(흰 사각형이 튀지 않도록).
        slot.Portrait.enabled = portrait != null;
        slot.Portrait.preserveAspect = true;
    }

    private Slot GetSlot(int index)
    {
        while (slots.Count <= index) slots.Add(CreateSlot(slots.Count));
        return slots[index];
    }

    private Slot CreateSlot(int index)
    {
        var slot = new Slot();

        slot.Root = HudFactory.CreateGroup(root, "PartySlot_" + index);
        slot.Root.anchorMin = new Vector2(0f, 1f);
        slot.Root.anchorMax = new Vector2(0f, 1f);
        slot.Root.pivot = new Vector2(0f, 1f);
        slot.Root.sizeDelta = new Vector2(GaugeLeft + GaugeWidth, RowHeight);
        slot.Root.anchoredPosition = new Vector2(0f, -index * (RowHeight + RowSpacing));

        // 선택 강조. 클릭 판정도 이 이미지가 받는다 — 행 전체를 덮으므로 초상화든 게이지든
        // 어디를 눌러도 같은 슬롯이 선택된다. HudFactory 기본값이 raycastTarget=false이므로
        // 여기서만 명시적으로 켜서, 나머지 HUD는 여전히 클릭을 가로채지 않게 둔다.
        slot.SelectionHighlight = HudFactory.CreateImage(slot.Root, "SelectionHighlight", SlotIdleColor);
        HudFactory.SetTopLeft(slot.SelectionHighlight.rectTransform, new Vector2(GaugeLeft + GaugeWidth, RowHeight), Vector2.zero);
        slot.SelectionHighlight.raycastTarget = true;

        // 클릭을 슬롯 인덱스로 되돌려 받는다. 슬롯은 재사용되므로 유닛이 아니라 인덱스를 넘긴다.
        var trigger = slot.SelectionHighlight.gameObject.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        int captured = index;
        entry.callback.AddListener(_ => HandleSlotClicked(captured));
        trigger.triggers.Add(entry);

        slot.PortraitFrame = HudFactory.CreateImage(slot.Root, "PortraitFrame", BattleHudPalette.PortraitFrame);
        HudFactory.SetTopLeft(slot.PortraitFrame.rectTransform, new Vector2(PortraitSize, PortraitSize), Vector2.zero);

        slot.Portrait = HudFactory.CreateImage(slot.Root, "Portrait", BattleHudPalette.AliveTint);
        HudFactory.SetTopLeft(slot.Portrait.rectTransform, new Vector2(PortraitSize - 6f, PortraitSize - 6f), new Vector2(3f, -3f));

        Image hpBackground = HudFactory.CreateImage(slot.Root, "HpBackground", BattleHudPalette.GaugeBackground);
        HudFactory.SetTopLeft(hpBackground.rectTransform, new Vector2(GaugeWidth, HpHeight), new Vector2(GaugeLeft, -8f));

        slot.HpFill = HudFactory.CreateImage(slot.Root, "HpFill", BattleHudPalette.PartyHp);
        HudFactory.SetTopLeft(slot.HpFill.rectTransform, new Vector2(GaugeWidth, HpHeight), new Vector2(GaugeLeft, -8f));

        Image manaBackground = HudFactory.CreateImage(slot.Root, "ManaBackground", BattleHudPalette.GaugeBackground);
        HudFactory.SetTopLeft(manaBackground.rectTransform, new Vector2(GaugeWidth, ManaHeight), new Vector2(GaugeLeft, -28f));

        slot.ManaFill = HudFactory.CreateImage(slot.Root, "ManaFill", BattleHudPalette.Mana);
        HudFactory.SetTopLeft(slot.ManaFill.rectTransform, new Vector2(GaugeWidth, ManaHeight), new Vector2(GaugeLeft, -28f));

        slot.EmotionLabel = HudFactory.CreateText(slot.Root, "EmotionLabel", font, 20f, BattleHudPalette.Fear);
        slot.EmotionLabel.alignment = TextAlignmentOptions.TopLeft;
        HudFactory.SetTopLeft(slot.EmotionLabel.rectTransform, new Vector2(GaugeWidth, 26f), new Vector2(GaugeLeft, -40f));
        slot.EmotionLabel.text = "";

        return slot;
    }

    // 좌상단을 기준으로 배치. 게이지가 왼쪽에서 오른쪽으로 줄어들도록 피벗도 왼쪽에 둔다.
}
