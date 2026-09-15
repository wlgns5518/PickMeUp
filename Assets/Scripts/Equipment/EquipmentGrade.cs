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

    // 무기 이름 앞에 붙는 말. 같은 롱소드라도 "조잡한 롱소드"와 "전설의 롱소드"는 다른 물건이다.
    // 무기마다 이름을 따로 짓지 않고 수식어 하나로 가른다 — 무기 에셋이 늘어도 이름표를 새로 쓸 일이 없다.
    public static string PrefixOf(EquipmentGrade grade)
    {
        switch (grade)
        {
            case EquipmentGrade.S: return "전설의";
            case EquipmentGrade.A: return "명장의";
            case EquipmentGrade.B: return "정교한";
            case EquipmentGrade.C: return "단단한";
            case EquipmentGrade.D: return "평범한";
            default:               return "조잡한";
        }
    }

    // "전설의 롱소드". 제작소 결과와 무기창고가 같은 이름을 쓴다.
    public static string ItemName(WeaponDefinition weapon, EquipmentGrade grade) =>
        weapon == null ? string.Empty : PrefixOf(grade) + " " + weapon.DisplayName;

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

// 등급이 장비의 힘에 곱하는 배율.
//
// 주무기는 공격력 배율(WeaponCombatProfile.AttackMultiplier)에, 방패는 피해 감소와 막기 보너스
// (JobProfile.ShieldDamageReduction / ShieldBlockBonus)에 곱해진다. 무기 종류의 개성 — 사거리와
// 공격 속도 — 은 등급과 무관하다. 전설의 단검도 단검처럼 짧고 빠르다.
//
// 한 단계에 0.20씩, E의 0.80에서 S의 1.80까지 가파르게 벌렸다. S 장비 하나가 판세를 바꿀 만큼 커야
// 헬 난이도 수동 제작이 뚜렷한 목표가 된다. 캐릭터 에셋에 적힌 기본 장비는 1(D등급과 같다)이라,
// 제작 장비를 들지 않은 영웅의 전투는 이 표가 생기기 전과 똑같다 — 조잡한 장비는 기본 장비보다 못하다.
public static class EquipmentGradeRules
{
    public const float BasePower = 1f;

    public static float PowerOf(EquipmentGrade grade)
    {
        switch (grade)
        {
            case EquipmentGrade.S: return 1.80f;
            case EquipmentGrade.A: return 1.60f;
            case EquipmentGrade.B: return 1.40f;
            case EquipmentGrade.C: return 1.20f;
            case EquipmentGrade.D: return 1.00f;
            default:               return 0.80f;
        }
    }
}
