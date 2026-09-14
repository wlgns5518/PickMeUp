// 장비제작소가 방금 만들어 낸 결과 한 점. 창고에 들어간 실물은 OwnedEquipment이고,
// 이건 "무엇이 나왔는지"를 화면에 알리는 데만 쓴다.
public readonly struct CraftedEquipment
{
    public readonly WeaponDefinition weapon;
    public readonly string name;
    public readonly EquipmentGrade grade;

    public CraftedEquipment(WeaponDefinition weapon, EquipmentGrade grade)
    {
        this.weapon = weapon;
        this.name = weapon != null ? weapon.DisplayName : string.Empty;
        this.grade = grade;
    }
}
