using UnityEngine;

// 게임 데이터(영웅·장비·재료)를 슬롯 내용으로 바꾸는 곳.
//
// 같은 장비가 장비창 목록, 제작소 합성 재료 칸, 결과 칸에서 모두 같은 모습이어야 한다. 화면마다 슬롯을 따로
// 채우면 한 곳에서만 등급 딱지가 빠지거나 강화 단계 자리가 달라진다. 그래서 바꾸는 규칙은 여기 한 곳이다.
//
// 직업은 어디에도 적지 않는다(hidden-job-design). 영웅 칸에는 초상화·별·레벨만, 기본 장비는 이름 없이
// "기본 장비"로만 보인다. 이름이 보이는 것은 플레이어가 만든 제작 장비뿐이다.
public static class UiSlotContents
{
    public static UiSlotContent Hero(CharacterSO hero)
    {
        if (hero == null) return new UiSlotContent { Fallback = "?" };

        string name = HeroLabel.Name(hero);
        return new UiSlotContent
        {
            Icon = hero.portrait,
            FillsSlot = true,
            Fallback = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1),
            Tier = UiTheme.TierOfStars(hero.starCount),
            Badge = UiKit.Stars(hero.starCount),
            Caption = "Lv." + hero.Level,
        };
    }

    public static UiSlotContent Equipment(OwnedEquipment item)
    {
        if (item == null || item.Weapon == null) return new UiSlotContent { Fallback = "?" };

        return new UiSlotContent
        {
            Icon = UiIconLibrary.Weapon(item.Weapon.type),
            Fallback = CharacterRules.Korean(item.Weapon.type).Substring(0, 1),
            Tier = UiTheme.TierOf(item.Grade),
            Badge = EquipmentGradeNames.NameOf(item.Grade),
            Corner = item.Level > 0 ? "+" + item.Level : null,
        };
    }

    // 아직 만들어지지 않은 장비(제작·합성 결과 미리보기). 무기는 계열까지만 안다.
    public static UiSlotContent Preview(WeaponFamily family, EquipmentGrade grade)
    {
        return new UiSlotContent
        {
            Icon = UiIconLibrary.Family(family),
            Fallback = "?",
            Tier = UiTheme.TierOf(grade),
            Badge = EquipmentGradeNames.NameOf(grade),
        };
    }

    public static UiSlotContent Material(CraftMaterial material, int count = -1)
    {
        return new UiSlotContent
        {
            Icon = UiIconLibrary.Material(material.Kind),
            Fallback = MaterialNames.KindName(material.Kind).Substring(0, 1),
            Tier = UiTheme.TierOf(material.Grade),
            Badge = EquipmentGradeNames.NameOf(material.Grade),
            Tag = count >= 0 ? "x" + count : null,
            TagColor = UiTheme.SurfaceHover,
        };
    }

    // 제작 장비를 들지 않은 손. 무엇인지는 적지 않는다 — 기본 장비의 이름과 종류는 곧 직업이다.
    public static UiSlotContent BaseEquipment() => new UiSlotContent { Fallback = "기본" };

    // ---- 한 줄 설명 ---------------------------------------------------------

    // 장비의 주요 능력치 한 줄. 주무기는 공격, 방패는 방어에 곱해진다(EquipmentGradeRules·EquipmentEnhancement).
    public static string StatLine(OwnedEquipment item) =>
        item == null ? string.Empty : $"{StatName(item.Slot)} x{item.Power:0.00}";

    public static string StatName(EquipSlot slot) => slot == EquipSlot.OffHand ? "방어" : "공격";

    // 계열이 정해지면 무엇에 곱해지는지도 정해진다. 계열이 서지 않으면(어떤 무기든) 방패일 수도 있다.
    public static string StatNameOf(WeaponFamily family) =>
        family == WeaponFamily.Shield ? "방어" : family == WeaponFamily.Any ? "공격/방어" : "공격";

    public static string SlotName(EquipSlot slot) => slot == EquipSlot.OffHand ? "방어구" : "무기";

    // 장비 줄의 태그 — 누가 들었는지. 이 영웅이면 "장착", 다른 영웅이면 그 이름.
    public static void ApplyOwnerTag(ref UiSlotContent content, OwnedEquipment item, CharacterSO viewer)
    {
        if (item == null || !item.IsEquipped) return;

        if (item.IsHeldBy(viewer))
        {
            content.Tag = "장착";
            content.TagColor = UiTheme.Selection;
            return;
        }

        CharacterSO owner = EquipmentInventory.FindOwner(item, OwnedRoster.Members);
        content.Tag = owner != null ? HeroLabel.Name(owner) : "장착";
        content.TagColor = UiTheme.BorderStrong;
    }
}
