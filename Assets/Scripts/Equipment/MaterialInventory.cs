using System;
using System.Collections.Generic;

// 모아 둔 제작 재료. 종류 x 등급마다 개수 하나씩이다.
//
// 입구일 뿐이다. 개수와 규칙은 MaterialStore가, 세이브와의 연결은 GameServices가 든다.
// 생성자로 의존성을 받을 수 있는 코드는 IMaterialStore(GameServices.Materials)를 받는다.
public static class MaterialInventory
{
    private static MaterialStore State => GameServices.MaterialState;

    public static event Action Changed
    {
        add => State.Changed += value;
        remove => State.Changed -= value;
    }

    public static int CountOf(MaterialKind kind, EquipmentGrade grade) => State.CountOf(kind, grade);

    public static int CountOf(CraftMaterial material) => State.CountOf(material);

    public static int TotalCount => State.TotalCount;

    public static void Add(CraftMaterial material, int amount = 1) => State.Add(material, amount);

    public static void AddRange(IReadOnlyList<CraftMaterial> materials) => State.AddRange(materials);

    public static bool TryConsume(IReadOnlyList<CraftMaterial> materials) => State.TryConsume(materials);

    internal static void CollectNonEmpty(List<KeyValuePair<CraftMaterial, int>> results) => State.CollectNonEmpty(results);

    internal static void Restore(IEnumerable<KeyValuePair<CraftMaterial, int>> restored) => State.Restore(restored);

    internal static void Forget() => State.Forget();
}
