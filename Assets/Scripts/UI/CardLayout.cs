using UnityEngine;

// 영웅 카드를 격자로 늘어놓을 때 쓰는 계산.
//
// 편성 창(DeckBuildUI)과 합성 창(SynthesisUI)이 같은 카드를 같은 규칙으로 깐다.
// 한쪽에서만 배율 규칙을 고치면 두 창의 카드 크기가 서로 어긋나므로 계산은 여기 한 곳에 둔다.
public static class CardLayout
{
    // CharacterCard 프리팹의 기준 크기. 카드는 이 크기로 만들어 놓고 배율로 줄여 쓴다.
    public static readonly Vector2 CardSize = new Vector2(300f, 450f);

    /// count장을 area 안에 다 넣을 수 있는 가장 큰 배율과, 그때의 열 수.
    /// 열 수를 1부터 훑는 이유는 세로로 길쭉한 칸에서는 한 줄로 세우는 편이 더 클 때가 있기 때문이다.
    public static float Fit(int count, Vector2 area, float spacing, float framePadding, float maxScale, out int columns)
    {
        columns = Mathf.Max(1, count);
        float best = 0f;

        for (int cols = 1; cols <= count; cols++)
        {
            int rows = Mathf.CeilToInt(count / (float)cols);
            float cellWidth = (area.x - (cols - 1) * spacing) / cols - framePadding * 2f;
            float cellHeight = (area.y - (rows - 1) * spacing) / rows - framePadding * 2f;
            if (cellWidth <= 0f || cellHeight <= 0f) continue;

            float scale = Mathf.Min(cellWidth / CardSize.x, cellHeight / CardSize.y);
            if (scale <= best) continue;

            best = scale;
            columns = cols;
        }

        return Mathf.Clamp(Mathf.Min(best, maxScale), 0.1f, maxScale);
    }

    // 카드 둘레에 테두리를 남긴 칸 크기.
    public static Vector2 SlotSize(float scale, float framePadding)
    {
        return CardSize * scale + new Vector2(framePadding * 2f, framePadding * 2f);
    }

    // 카드는 원래 크기 그대로 만들고 배율만 줄인다. 안쪽 글자와 별이 함께 줄어든다.
    public static void CenterInSlot(RectTransform card, float scale)
    {
        card.anchorMin = new Vector2(0.5f, 0.5f);
        card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.anchoredPosition = Vector2.zero;
        card.localScale = Vector3.one * scale;
    }
}
