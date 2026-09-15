using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 9-slice 스프라이트의 모서리가 칸보다 커지지 않게 배율을 맞춘다.
//
// 킷 스프라이트는 400px 폭 버튼 기준(1x)으로 그려져 모서리 보더가 36~110px이다. 그대로 36px짜리 탭에 깔면
// 위아래 보더(72px)가 칸 높이를 넘어 잘린 모서리가 뭉개진다. 칸이 원본보다 낮으면 그 비율만큼
// pixelsPerUnitMultiplier를 올려 모서리를 같은 모양 그대로 줄인다.
// 원본보다 큰 칸에서는 키우지 않는다 — 모서리 장식과 글로우가 흐려진다.
//
// 칸 크기는 대개 이미지를 만든 뒤에 정해지고(SetTopLeft), 늘어나는 창은 화면 크기에 따라 달라진다.
// 그래서 만들 때 한 번 계산하지 않고 크기가 바뀔 때마다 다시 맞춘다.
[RequireComponent(typeof(Image))]
[DisallowMultipleComponent]
public class NeonSliceFit : UIBehaviour
{
    private Image image;

    protected override void OnEnable()
    {
        base.OnEnable();
        Apply();
    }

    protected override void OnRectTransformDimensionsChange()
    {
        Apply();
    }

    // 스프라이트를 갈아 끼운 뒤에도 불러야 한다. 스프라이트마다 원본 높이와 보더가 다르다.
    public void Apply()
    {
        if (image == null) image = GetComponent<Image>();

        Sprite sprite = image.sprite;
        float multiplier = 1f;

        if (sprite != null && image.type == Image.Type.Sliced)
        {
            Rect rect = image.rectTransform.rect;
            Vector4 border = sprite.border; // L, B, R, T

            if (rect.height > 0f) multiplier = Mathf.Max(multiplier, sprite.rect.height / rect.height);
            // 가로로 좁은 칸(정사각 닫기 버튼 등)에서는 좌우 보더가 먼저 넘친다.
            if (rect.width > 0f) multiplier = Mathf.Max(multiplier, (border.x + border.z) / rect.width);
        }

        if (!Mathf.Approximately(image.pixelsPerUnitMultiplier, multiplier))
            image.pixelsPerUnitMultiplier = multiplier;
    }
}
