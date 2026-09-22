// 무기창고에 든 장비 한 점 — 실물.
//
// 무기 에셋(WeaponDefinition)은 "롱소드라는 종류"일 뿐이라 여러 자루를 구별하지 못한다.
// 제작소에서 나온 롱소드 두 자루는 등급이 다를 수 있고, 한 자루는 아드리엘이, 다른 한 자루는
// 창고에 들어 있을 수 있다. 그 차이를 담는 것이 이 객체다.
//
// 누가 들고 있는지는 장비 쪽이 기억한다(OwnerId). 캐릭터 쪽에 "무엇을 들었는지"를 따로 적어 두면
// 두 기록이 어긋나는 순간 같은 칼이 두 사람 손에 들리거나, 아무도 안 든 칼이 창고에서 사라진다.
public class OwnedEquipment
{
    public readonly WeaponDefinition Weapon;
    public readonly EquipmentGrade Grade;

    // 들고 있는 캐릭터의 CharacterSO.Id. 비어 있으면 창고에 보관 중이다.
    // 바꾸는 길은 EquipmentInventory의 장착/해제뿐이다 — 한 손에 두 점이 걸리지 않게 거기서 막는다.
    public string OwnerId { get; internal set; }

    // 강화 단계(0~EquipmentEnhancement.MaxLevel). 바꾸는 길은 EquipmentEnhancement 하나뿐이다.
    public int Level { get; internal set; }

    public OwnedEquipment(WeaponDefinition weapon, EquipmentGrade grade, string ownerId = null, int level = 0)
    {
        Weapon = weapon;
        Grade = grade;
        OwnerId = string.IsNullOrEmpty(ownerId) ? null : ownerId;
        Level = EquipmentEnhancement.ClampLevel(level);
    }

    public bool IsEquipped => !string.IsNullOrEmpty(OwnerId);

    public EquipSlot Slot => Weapon != null ? Weapon.slot : EquipSlot.MainHand;

    // "+3 전설의 롱소드". 등급 수식어와 강화 단계가 붙은 이름이다 — 무기 에셋의 이름은 Weapon.DisplayName.
    public string DisplayName
    {
        get
        {
            string name = EquipmentGradeNames.ItemName(Weapon, Grade);
            return Level > 0 ? $"+{Level} {name}" : name;
        }
    }

    // 이 장비가 전투에 곱하는 배율 — 등급 배율(EquipmentGradeRules)에 강화 배율을 곱한다.
    public float Power => EquipmentGradeRules.PowerOf(Grade) * EquipmentEnhancement.Multiplier(Level);

    public bool IsHeldBy(CharacterSO character) => character != null && IsEquipped && OwnerId == character.Id;
}
