// 장비제작소가 만들어 낸 결과 한 점. 인벤토리 시스템이 아직 없어 지금은 이 값만 들고 화면에
// 알리는 데 쓴다 — 인벤토리가 생기면 여기 담긴 값을 그대로 넘기면 된다.
public readonly struct CraftedEquipment
{
    public readonly string name;
    public readonly EquipmentGrade grade;

    public CraftedEquipment(string name, EquipmentGrade grade)
    {
        this.name = name;
        this.grade = grade;
    }
}
