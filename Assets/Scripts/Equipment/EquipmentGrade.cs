using UnityEngine;

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

    // 제작소와 무기창고가 같은 등급을 같은 색으로 보여야 한다.
    public static Color ColorOf(EquipmentGrade grade)
    {
        switch (grade)
        {
            case EquipmentGrade.S: return BattleHudPalette.Mvp;
            case EquipmentGrade.A: return new Color(0.80f, 0.55f, 1.00f);
            case EquipmentGrade.B: return new Color(0.55f, 0.75f, 1.00f);
            case EquipmentGrade.C: return new Color(0.55f, 0.85f, 0.60f);
            case EquipmentGrade.D: return new Color(0.75f, 0.75f, 0.75f);
            default:               return BattleHudPalette.PanelText;
        }
    }
}
