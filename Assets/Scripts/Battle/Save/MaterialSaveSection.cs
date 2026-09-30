using System.Collections.Generic;

// 제작 재료 칸. 종류와 등급이 같은 재료는 개수로 쌓인다.
internal sealed class MaterialSaveSection : ISaveSection
{
    private readonly MaterialStore store;

    public MaterialSaveSection(MaterialStore store)
    {
        this.store = store;
    }

    public void WriteTo(SaveData data)
    {
        data.materials = new List<MaterialRecord>();

        var stacks = new List<KeyValuePair<CraftMaterial, int>>();
        store.CollectNonEmpty(stacks);
        for (int i = 0; i < stacks.Count; i++)
        {
            data.materials.Add(new MaterialRecord
            {
                kind = stacks[i].Key.Kind,
                grade = stacks[i].Key.Grade,
                count = stacks[i].Value,
            });
        }
    }

    public void ReadFrom(SaveData data)
    {
        var restored = new List<KeyValuePair<CraftMaterial, int>>();

        if (data?.materials != null)
        {
            for (int i = 0; i < data.materials.Count; i++)
            {
                MaterialRecord record = data.materials[i];
                if (record == null || record.count <= 0) continue;
                restored.Add(new KeyValuePair<CraftMaterial, int>(new CraftMaterial(record.kind, record.grade), record.count));
            }
        }

        store.Restore(restored);
    }

    public void Forget() => store.Forget();
}
