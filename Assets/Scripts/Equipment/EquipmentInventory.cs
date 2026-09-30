using System;
using System.Collections.Generic;

// 무기창고 — 제작소에서 만든 장비 전부와, 그중 누가 무엇을 들고 있는지.
//
// 입구일 뿐이다. 창고의 규칙은 EquipmentStore가, 세이브와의 연결은 GameServices가 든다.
// 생성자로 의존성을 받을 수 있는 코드는 IEquipmentStore(GameServices.Armory)를 받는다.
public static class EquipmentInventory
{
    private static EquipmentStore State => GameServices.ArmoryState;

    // 창고 내용이나 누가 무엇을 들었는지가 바뀌었다. 무기창고 창이 다시 그린다.
    public static event Action Changed
    {
        add => State.Changed += value;
        remove => State.Changed -= value;
    }

    public static IReadOnlyList<OwnedEquipment> Items => State.Items;

    public static OwnedEquipment Add(WeaponDefinition weapon, EquipmentGrade grade) => State.Add(weapon, grade);

    public static bool Contains(OwnedEquipment item) => State.Contains(item);

    public static void Remove(OwnedEquipment item) => State.Remove(item);

    internal static OwnedEquipment ReplaceWith(IReadOnlyList<OwnedEquipment> consumed, WeaponDefinition weapon,
        EquipmentGrade grade) => State.ReplaceWith(consumed, weapon, grade);

    internal static void SetLevel(OwnedEquipment item, int level) => State.SetLevel(item, level);

    public static OwnedEquipment EquippedIn(CharacterSO character, EquipSlot slot) => State.EquippedIn(character, slot);

    // 장비를 든 영웅을 후보 중에서 찾는다. 주인은 장비 쪽에 Id로만 적혀 있다.
    public static CharacterSO FindOwner(OwnedEquipment item, IReadOnlyList<CharacterSO> candidates)
    {
        if (item == null || !item.IsEquipped || candidates == null) return null;

        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i] != null && candidates[i].Id == item.OwnerId) return candidates[i];
        }
        return null;
    }

    public static bool Equip(CharacterSO character, OwnedEquipment item, out string reason) =>
        State.Equip(character, item, out reason);

    public static void Unequip(OwnedEquipment item) => State.Unequip(item);

    public static void UnequipAll(CharacterSO character) => State.UnequipAll(character);

    internal static void Restore(IEnumerable<OwnedEquipment> restored) => State.Restore(restored);

    internal static void Forget() => State.Forget();
}
