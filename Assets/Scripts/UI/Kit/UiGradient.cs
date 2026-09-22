using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 그래픽에 색 변화를 입힌다 — 위→아래(화면 배경, 실행 버튼의 윤기, 등급 칸 아래쪽 물들임) 또는
// 왼쪽→오른쪽(배너 글자 뒤를 어둡게 누르는 막).
//
// 그라데이션 텍스처를 색 조합마다 만드는 대신 정점 색을 자리에 따라 섞는다 — 9-슬라이스 판에도 그대로 먹고
// 색을 바꿀 때 텍스처를 새로 만들 일이 없다. 결과는 원래 정점 색(Image.color)과 곱해진다.
// 단순(Simple) 이미지는 정점이 네 모서리뿐이라 색이 곧게 섞이고, 9-슬라이스는 조각 경계마다 정점이 있어도 같은 식이다.
[DisallowMultipleComponent]
public class UiGradient : BaseMeshEffect
{
    [SerializeField] private Color from = Color.white;
    [SerializeField] private Color to = Color.white;
    [SerializeField] private bool horizontal;

    private static readonly List<UIVertex> Vertices = new List<UIVertex>();

    /// 위(topColor)에서 아래(bottomColor)로.
    public void Set(Color topColor, Color bottomColor)
    {
        from = topColor;
        to = bottomColor;
        horizontal = false;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    /// 왼쪽(leftColor)에서 오른쪽(rightColor)으로.
    public void SetHorizontal(Color leftColor, Color rightColor)
    {
        from = leftColor;
        to = rightColor;
        horizontal = true;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0) return;

        Vertices.Clear();
        vh.GetUIVertexStream(Vertices);

        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < Vertices.Count; i++)
        {
            float p = horizontal ? Vertices[i].position.x : Vertices[i].position.y;
            if (p < min) min = p;
            if (p > max) max = p;
        }

        float span = Mathf.Max(0.0001f, max - min);
        for (int i = 0; i < Vertices.Count; i++)
        {
            UIVertex v = Vertices[i];
            float t = ((horizontal ? v.position.x : v.position.y) - min) / span;
            // 세로는 아래가 0이라 위 색(from)이 t=1 쪽이다.
            Color tint = horizontal ? Color.Lerp(from, to, t) : Color.Lerp(to, from, t);
            v.color = (Color32)((Color)v.color * tint);
            Vertices[i] = v;
        }

        vh.Clear();
        vh.AddUIVertexTriangleStream(Vertices);
    }
}
