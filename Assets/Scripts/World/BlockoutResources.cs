using System.Collections.Generic;
using UnityEngine;

// 마을 블록아웃이 코드로 찍어내는 머티리얼과 메시를 들고 있는 곳.
//
// 에셋이 아니라서(HideFlags.DontSave) 직접 지우지 않으면 다시 세울 때마다 그대로 쌓인다. 예전에는
// 이 캐시와 정리가 VillageBlockout 안에 섞여 있어서, 무엇을 만들고 무엇을 지우는지 한눈에 안 보였다.
public sealed class BlockoutResources
{
    private readonly Dictionary<Color, Material> solid = new Dictionary<Color, Material>();
    private readonly Dictionary<Color, Material> glow = new Dictionary<Color, Material>();
    private readonly List<Mesh> meshes = new List<Mesh>();

    /// 색 하나에 머티리얼 하나. 같은 색 도형 수백 개가 머티리얼을 나눠 쓴다.
    public Material Solid(Color color)
    {
        if (solid.TryGetValue(color, out Material cached) && cached != null) return cached;

        Material material = NewMaterial(color);
        solid[color] = material;
        return material;
    }

    /// 스스로 빛나는 판(결계 불빛, 틈 빛). 블룸에 걸리게 발광을 조금 세게 준다.
    public Material Glow(Color color)
    {
        if (glow.TryGetValue(color, out Material cached) && cached != null) return cached;

        Material material = NewMaterial(color);
        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 1.6f);
        glow[color] = material;
        return material;
    }

    /// 코드로 짠 메시를 정리 목록에 올린다.
    public Mesh Track(Mesh mesh)
    {
        meshes.Add(mesh);
        return mesh;
    }

    /// 지금 들고 있는 것 전부(머티리얼 → 발광 머티리얼 → 메시 순). 프리팹으로 구울 때 에셋으로 옮긴다.
    public IEnumerable<Object> All
    {
        get
        {
            foreach (Material material in solid.Values) yield return material;
            foreach (Material material in glow.Values) yield return material;
            foreach (Mesh mesh in meshes) yield return mesh;
        }
    }

    public void DestroyAll()
    {
        foreach (Object generated in All) UnityObjects.Destroy(generated);
        Forget();
    }

    /// 지우지 않고 목록만 비운다. 구워서 에셋이 된 것들을 놓아줄 때 쓴다 —
    /// 에셋은 DestroyImmediate로 지울 수 없어서, 캐시에 남겨 두면 다음 Rebuild가 예외를 낸다.
    public void Forget()
    {
        solid.Clear();
        glow.Clear();
        meshes.Clear();
    }

    private static Material NewMaterial(Color color)
    {
        // URP가 없는 프로젝트에서도 색은 나오도록 빌트인 셰이더로 떨어진다.
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        var material = new Material(shader)
        {
            name = "Blockout " + ColorUtility.ToHtmlStringRGB(color),
            hideFlags = HideFlags.DontSave   // 에셋으로 남기지 않는다
        };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.12f);
        return material;
    }
}
