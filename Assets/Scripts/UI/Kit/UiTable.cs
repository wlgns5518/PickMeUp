using System.Collections.Generic;
using TMPro;
using UnityEngine;

// 머리줄 한 줄과 줄무늬 행으로 된 표. 확률표(소환소 등급 확률, 제작소 등급 확률)가 같이 쓴다.
//
//   ┌ 등급 ─────── 확률 ─────── 설명 ┐   머리줄(가라앉은 면)
//   │ ★1          60%          …     │   행(짝·홀 번갈아)
//   │ ★2          30%          …     │
//
// 예전에는 두 화면이 이 모양을 각자 짰다. 머리줄 높이, 행 사이 간격, 줄무늬 색이 조금씩 달라질 수 있었다.
// 칸 내용은 부르는 쪽이 채운다(Cell).
public sealed class UiTable
{
    public const float HeaderHeight = 44f;

    private readonly List<RectTransform> rows = new List<RectTransform>();
    private readonly List<TMP_Text[]> cells = new List<TMP_Text[]>();

    public int RowCount => rows.Count;

    /// top은 표가 시작하는 위치(부모 위쪽에서의 거리). columns는 칸마다 왼쪽 시작 위치,
    /// fontSizes는 칸마다 글자 크기(칸 수와 같거나 비워 두면 본문 크기).
    public static UiTable Create(RectTransform parent, float top, float[] columns, string[] titles,
        int rowCount, float rowHeight, float[] fontSizes = null)
    {
        var table = new UiTable();

        RectTransform header = UiKit.Node(parent, "Header");
        UiKit.TopStretch(header, top, HeaderHeight);
        UiKit.Rounded(header, "Fill", UiTheme.SurfaceSunken, UiTheme.RadiusS);
        for (int c = 0; c < titles.Length; c++)
        {
            TMP_Text t = UiKit.Text(header, "Col_" + c, titles[c], UiTheme.FontLabel, UiTheme.TextSecondary);
            UiKit.Fill(t.rectTransform, columns[c] + UiTheme.Space5, 0f, 0f, 0f);
        }

        float firstRow = top + HeaderHeight + UiTheme.Space2;
        for (int r = 0; r < rowCount; r++)
        {
            RectTransform row = UiKit.Node(parent, "Row_" + r);
            UiKit.TopStretch(row, firstRow + r * rowHeight, rowHeight);
            UiKit.Rounded(row, "Fill", r % 2 == 0 ? UiTheme.SurfaceRaised : UiTheme.Surface, UiTheme.RadiusS);

            var rowCells = new TMP_Text[columns.Length];
            for (int c = 0; c < columns.Length; c++)
            {
                float size = fontSizes != null && c < fontSizes.Length ? fontSizes[c] : UiTheme.FontBody;
                TMP_Text t = UiKit.Text(row, "Col_" + c, string.Empty, size, UiTheme.TextPrimary);
                UiKit.Fill(t.rectTransform, columns[c] + UiTheme.Space5, 0f, 0f, 0f);
                rowCells[c] = t;
            }

            table.rows.Add(row);
            table.cells.Add(rowCells);
        }

        return table;
    }

    public TMP_Text Cell(int row, int column) => cells[row][column];

    public void SetRowVisible(int row, bool visible) => rows[row].gameObject.SetActive(visible);
}
