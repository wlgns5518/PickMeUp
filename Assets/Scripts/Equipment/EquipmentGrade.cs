// 장비 등급. 캐릭터의 별 등급(CharacterSO.starCount)과는 다른 축이다 — 이건 장비제작소에서
// 만든 장비 한 점이 얼마나 좋은지를 가른다. E가 가장 낮고 S가 가장 높다.
public enum EquipmentGrade
{
    E,
    D,
    C,
    B,
    A,
    S,
}

public static class EquipmentGradeNames
{
    // 등급 이름 자체가 알파벳 한 글자라 열거형 이름을 그대로 쓴다.
    public static string NameOf(EquipmentGrade grade) => grade.ToString();
}
