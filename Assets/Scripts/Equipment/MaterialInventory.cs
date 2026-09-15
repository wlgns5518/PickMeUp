using System;
using System.Collections.Generic;
using UnityEngine;

// 모아 둔 제작 재료. 종류 x 등급마다 개수 하나씩이다.
//
// 재료는 층을 클리어할 때 받고(MaterialDrops), 장비제작소에서 세 개씩 넣어 소모한다.
// EquipmentInventory와 같은 규칙을 따른다 — 필요할 때 세이브 파일에서 스스로 읽어 오고,
// 바뀔 때마다 곧바로 저장한다. 전투 씬이 창고를 한 번도 열지 않은 채 저장해도 빈 창고로
// 덮어쓰지 않으려면 누가 먼저 읽든 파일에 있던 것이 먼저 올라와 있어야 한다.
public static class MaterialInventory
{
    private static readonly int KindCount = Enum.GetValues(typeof(MaterialKind)).Length;
    private static readonly int GradeCount = Enum.GetValues(typeof(EquipmentGrade)).Length;

    private static readonly int[] counts = new int[KindCount * GradeCount];
    private static bool loaded;

    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 이전 플레이의 재료가 남지 않도록 비운다. 실제 값은 세이브에서 다시 읽는다.
        Array.Clear(counts, 0, counts.Length);
        loaded = false;
        Changed = null;
    }

    private static int IndexOf(MaterialKind kind, EquipmentGrade grade) => (int)kind * GradeCount + (int)grade;

    private static bool IsValid(MaterialKind kind, EquipmentGrade grade) =>
        (int)kind >= 0 && (int)kind < KindCount && (int)grade >= 0 && (int)grade < GradeCount;

    private static void EnsureLoaded()
    {
        if (loaded) return;

        // 읽는 도중 Restore가 다시 이리로 들어오지 않도록 먼저 세운다.
        loaded = true;
        SaveSystem.LoadMaterials();
    }

    public static int CountOf(MaterialKind kind, EquipmentGrade grade)
    {
        if (!IsValid(kind, grade)) return 0;
        EnsureLoaded();
        return counts[IndexOf(kind, grade)];
    }

    public static int CountOf(CraftMaterial material) => CountOf(material.Kind, material.Grade);

    public static int TotalCount
    {
        get
        {
            EnsureLoaded();
            int total = 0;
            for (int i = 0; i < counts.Length; i++) total += counts[i];
            return total;
        }
    }

    public static void Add(CraftMaterial material, int amount = 1)
    {
        if (amount <= 0 || !IsValid(material.Kind, material.Grade)) return;
        EnsureLoaded();

        counts[IndexOf(material.Kind, material.Grade)] += amount;
        Commit();
    }

    // 여러 개를 한 번에 넣는다. 전투 보상처럼 몇 개씩 들어올 때 저장을 한 번만 하려고 둔다.
    public static void AddRange(IReadOnlyList<CraftMaterial> materials)
    {
        if (materials == null || materials.Count == 0) return;
        EnsureLoaded();

        bool changed = false;
        for (int i = 0; i < materials.Count; i++)
        {
            if (!IsValid(materials[i].Kind, materials[i].Grade)) continue;
            counts[IndexOf(materials[i].Kind, materials[i].Grade)]++;
            changed = true;
        }
        if (changed) Commit();
    }

    /// 넣은 재료를 전부 가지고 있을 때만 한꺼번에 뺀다. 하나라도 모자라면 아무것도 빼지 않는다 —
    /// 세 개 중 두 개만 빠진 채 제작이 멈추면 재료만 사라진다.
    public static bool TryConsume(IReadOnlyList<CraftMaterial> materials)
    {
        if (materials == null || materials.Count == 0) return false;
        EnsureLoaded();

        var needed = new Dictionary<int, int>();
        for (int i = 0; i < materials.Count; i++)
        {
            if (!IsValid(materials[i].Kind, materials[i].Grade)) return false;
            int index = IndexOf(materials[i].Kind, materials[i].Grade);
            needed.TryGetValue(index, out int n);
            needed[index] = n + 1;
        }

        foreach (KeyValuePair<int, int> pair in needed)
        {
            if (counts[pair.Key] < pair.Value) return false;
        }

        foreach (KeyValuePair<int, int> pair in needed) counts[pair.Key] -= pair.Value;
        Commit();
        return true;
    }

    private static void Commit()
    {
        SaveSystem.SaveMaterials();
        Changed?.Invoke();
    }

    // ---- 세이브 연동 ------------------------------------------------------

    // 저장할 때 SaveSystem이 훑는다. 개수가 0인 칸은 건너뛴다.
    internal static void CollectNonEmpty(List<KeyValuePair<CraftMaterial, int>> results)
    {
        results.Clear();
        EnsureLoaded();

        foreach (MaterialKind kind in MaterialNames.AllKinds)
        {
            for (int g = 0; g < GradeCount; g++)
            {
                int count = counts[IndexOf(kind, (EquipmentGrade)g)];
                if (count > 0) results.Add(new KeyValuePair<CraftMaterial, int>(new CraftMaterial(kind, (EquipmentGrade)g), count));
            }
        }
    }

    // 세이브에서 읽어 온 개수를 그대로 얹는다. 저장을 다시 부르지 않는다.
    internal static void Restore(IEnumerable<KeyValuePair<CraftMaterial, int>> restored)
    {
        loaded = true;
        Array.Clear(counts, 0, counts.Length);
        if (restored != null)
        {
            foreach (KeyValuePair<CraftMaterial, int> pair in restored)
            {
                if (pair.Value <= 0 || !IsValid(pair.Key.Kind, pair.Key.Grade)) continue;
                counts[IndexOf(pair.Key.Kind, pair.Key.Grade)] += pair.Value;
            }
        }
        Changed?.Invoke();
    }

    // 세이브 파일이 지워졌다. 들고 있던 값을 버리고, 다음에 쓰일 때 파일에서 다시 읽는다.
    internal static void Forget()
    {
        Array.Clear(counts, 0, counts.Length);
        loaded = false;
    }
}
