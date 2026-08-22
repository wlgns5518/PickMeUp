using UnityEngine;

// 마우스를 올린 동안 잠깐 밝아지는 강조.
//
// 눌러서 여는 것들(FacilityGate 등)이 같은 규칙을 쓴다.
// MonoBehaviour가 아니라 주인이 만들어 들고 있는 평범한 클래스다 — AnnouncementBanner와 같은 방식.
//
// 머티리얼을 복제하면 오브젝트마다 인스턴스가 생긴다. 색만 덮어쓰는 프로퍼티 블록을 쓴다.
public class HoverHighlight
{
    // URP Lit은 _BaseColor, 빌트인/구형 셰이더는 _Color를 쓴다. 있는 쪽에만 넣는다.
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private readonly Renderer[] targets;
    private readonly Color[] baseColors;
    private readonly float brightness;
    private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

    public HoverHighlight(Renderer[] targets, float brightness)
    {
        this.targets = targets ?? new Renderer[0];
        this.brightness = brightness;

        baseColors = new Color[this.targets.Length];
        for (int i = 0; i < this.targets.Length; i++)
            baseColors[i] = ReadBaseColor(this.targets[i]);
    }

    public void Set(bool on)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            Renderer target = targets[i];
            if (target == null) continue;

            Color color = baseColors[i];
            if (on) color = new Color(color.r * brightness, color.g * brightness, color.b * brightness, color.a);

            target.GetPropertyBlock(block);
            Material material = target.sharedMaterial;
            if (material != null && material.HasProperty(BaseColorId)) block.SetColor(BaseColorId, color);
            if (material != null && material.HasProperty(ColorId)) block.SetColor(ColorId, color);
            target.SetPropertyBlock(block);
        }
    }

    private static Color ReadBaseColor(Renderer target)
    {
        Material material = target != null ? target.sharedMaterial : null;
        if (material == null) return Color.white;
        if (material.HasProperty(BaseColorId)) return material.GetColor(BaseColorId);
        if (material.HasProperty(ColorId)) return material.GetColor(ColorId);
        return Color.white;
    }
}
