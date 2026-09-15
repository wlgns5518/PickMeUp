using System;

// 제작 재료의 종류. 넣은 재료 중 가장 많은 종류가 나올 무기의 계열을 정한다(CraftRecipe).
// 값은 세이브에 정수로 적힌다. 순서를 바꾸거나 중간에 끼우면 모아 둔 재료가 통째로 다른 종류가 되므로
// 새 종류는 반드시 끝에만 덧붙인다.
public enum MaterialKind
{
    Metal,
    Wood,
    Leather,
}

// 재료 한 개. 종류와 등급이 같으면 같은 재료다 — 창고에는 개수로 쌓인다(MaterialInventory).
public readonly struct CraftMaterial : IEquatable<CraftMaterial>
{
    public readonly MaterialKind Kind;
    public readonly EquipmentGrade Grade;

    public CraftMaterial(MaterialKind kind, EquipmentGrade grade)
    {
        Kind = kind;
        Grade = grade;
    }

    // "B급 강철"
    public string DisplayName => $"{EquipmentGradeNames.NameOf(Grade)}급 {MaterialNames.KindName(Kind)}";

    public bool Equals(CraftMaterial other) => Kind == other.Kind && Grade == other.Grade;
    public override bool Equals(object obj) => obj is CraftMaterial other && Equals(other);
    public override int GetHashCode() => (int)Kind * 16 + (int)Grade;
    public override string ToString() => DisplayName;
}

public static class MaterialNames
{
    public static readonly MaterialKind[] AllKinds = { MaterialKind.Metal, MaterialKind.Wood, MaterialKind.Leather };

    public static string KindName(MaterialKind kind)
    {
        switch (kind)
        {
            case MaterialKind.Wood:    return "참나무";
            case MaterialKind.Leather: return "가죽";
            default:                   return "강철";
        }
    }
}
