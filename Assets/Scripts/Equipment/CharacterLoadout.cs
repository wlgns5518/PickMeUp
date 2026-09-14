// 캐릭터가 지금 실제로 손에 드는 것.
//
// 두 곳을 겹쳐 읽는다. 무기창고에서 들린 제작 장비(EquipmentInventory)가 있으면 그것이고,
// 없으면 캐릭터 에셋에 적힌 기본 장비다. 전투 수치(CharacterBattleSpawner)와 손에 보이는 모델
// (WeaponEquipper)이 같은 규칙을 읽어야 칼을 든 채로 맨손 배율을 맞는 일이 없으므로, 그 규칙을
// 여기 한 곳에만 둔다.
//
// 방패는 주무기가 허락할 때만 든다. 제작한 두손 무기를 들면 에셋에 적힌 기본 방패도 내려놓는다.
//
// 마법사는 제작 장비를 들지 않는다. 두 손으로 영창하는 직업이라 손에 쥘 자리가 없다.
// 장착 자체를 막는 것은 EquipmentInventory.Equip이고, 여기서도 한 번 더 무시한다 — 규칙이 생기기 전
// 세이브에 마법사 손에 걸린 장비가 남아 있어도 전투에는 새어 나가지 않는다.
public static class CharacterLoadout
{
    public static bool CanEquip(CharacterSO character) => character != null && character.job != JobType.Mage;

    public static WeaponDefinition MainHandOf(CharacterSO character)
    {
        if (character == null) return null;

        OwnedEquipment item = CraftedIn(character, EquipSlot.MainHand);
        return item != null ? item.Weapon : character.mainHandWeapon;
    }

    public static WeaponType MainHandTypeOf(CharacterSO character)
    {
        if (character == null) return WeaponType.None;

        OwnedEquipment item = CraftedIn(character, EquipSlot.MainHand);
        return item != null ? item.Weapon.type : character.MainHandType;
    }

    public static bool CanHoldShield(CharacterSO character) =>
        character != null && !CharacterRules.IsTwoHanded(MainHandTypeOf(character));

    public static bool HasShield(CharacterSO character)
    {
        if (!CanHoldShield(character)) return false;
        if (CraftedIn(character, EquipSlot.OffHand) != null) return true;
        return character.offHandWeapon != null || character.offHand == OffHandType.Shield;
    }

    // 방패를 들 수 없으면 null. 기본 방패가 종류(offHand)만 적혀 있고 에셋이 비어 있어도 null이다 —
    // 그때 무엇을 들지는 유닛 프리팹의 기본 장비가 정한다(WeaponEquipper).
    public static WeaponDefinition OffHandOf(CharacterSO character)
    {
        if (!CanHoldShield(character)) return null;

        OwnedEquipment item = CraftedIn(character, EquipSlot.OffHand);
        return item != null ? item.Weapon : character.offHandWeapon;
    }

    // 이 자리에 들린 제작 장비. 제작 장비를 들 수 없는 캐릭터면 무엇이 걸려 있든 없는 것으로 본다.
    private static OwnedEquipment CraftedIn(CharacterSO character, EquipSlot slot) =>
        CanEquip(character) ? EquipmentInventory.EquippedIn(character, slot) : null;
}
