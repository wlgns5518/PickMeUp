using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 칸(UiTile)을 격자로 늘어놓고 세로로 굴리는 목록. 보유 영웅·보유 장비·보유 재료를 고르는 곳이 모두 이것이다.
//
// 칸은 지우지 않고 다시 쓴다. 목록은 무엇을 하나 고를 때마다 다시 칠해지는데(고른 표시, 흐림), 그때마다
// 칸을 새로 만들면 영웅 서른에 장비 쉰이면 클릭 한 번에 수백 개가 생겼다 사라진다(ArmoryUI 주석과 같은 이유).
// 남는 칸은 꺼 둔다.
[DisallowMultipleComponent]
public class UiTileGrid : MonoBehaviour
{
    // 슬롯의 선택 고리가 칸 밖으로 나가는 만큼. 목록 가장자리에서 이만큼 띄워야 가림막에 잘리지 않는다.
    private const float RingMargin = 8f;

    private readonly List<UiTile> tiles = new List<UiTile>();
    private RectTransform content;
    private ScrollRect scroll;
    private TMP_Text emptyText;
    private float slotSize;
    private float gap;
    private float width;
    private int columns;

    public event Action<UiTile> TileClicked;

    public RectTransform Rect => (RectTransform)transform;
    // 칸을 끌어 옮기는 화면(파티 편성)이 세로 끌기를 목록에 넘겨줄 때 쓴다(CardDragSource.SetScrollPassThrough).
    public ScrollRect Scroll => scroll;

    /// width는 목록이 차지할 폭. 캔버스가 막 만들어졌을 때는 rect가 0으로 읽혀 여기서 받는다.
    public static UiTileGrid Create(RectTransform parent, string name, float width, float slotSize, float gap = UiTheme.Space4)
    {
        RectTransform rect = UiKit.Node(parent, name);
        UiKit.Fill(rect);

        var grid = rect.gameObject.AddComponent<UiTileGrid>();
        grid.slotSize = slotSize;
        grid.gap = gap;
        grid.content = UiKit.ScrollArea(rect, "Scroll", out grid.scroll);

        grid.emptyText = UiKit.Wrap(UiKit.Text(rect, "Empty", string.Empty, UiTheme.FontBody, UiTheme.TextMuted,
            TextAlignmentOptions.Center));
        UiKit.Fill(grid.emptyText.rectTransform, 24f);
        grid.emptyText.gameObject.SetActive(false);

        // 한 줄에 몇 칸. 칸 사이 간격을 뺀 폭에 들어가는 만큼이다. 양옆은 선택 고리(칸 밖 5px)가 잘리지 않게 비운다.
        grid.width = width - RingMargin * 2f;
        grid.columns = Mathf.Max(1, Mathf.FloorToInt((grid.width + gap) / (slotSize + gap)));
        return grid;
    }

    /// count칸을 깔고 칸마다 bind를 부른다. count가 0이면 emptyMessage를 띄운다.
    public void Show(int count, Action<UiTile, int> bind, string emptyMessage = null)
    {
        // 목록 폭은 화면 비율에 따라 달라진다. 이미 배치가 끝났으면 실제 폭으로 칸 수를 다시 잡는다
        // (막 지은 직후에는 0으로 읽혀 만들 때 받은 어림값을 그대로 쓴다).
        float measured = Rect.rect.width;
        if (measured > 1f)
        {
            width = measured - RingMargin * 2f;
            columns = Mathf.Max(1, Mathf.FloorToInt((width + gap) / (slotSize + gap)));
        }

        float tileHeight = UiTile.HeightOf(slotSize);
        // 남는 폭은 칸 사이에 고르게 나눈다. 왼쪽에 몰면 오른쪽이 비어 보인다.
        float step = columns > 1 ? (width - slotSize) / (columns - 1) : 0f;
        step = Mathf.Max(slotSize + gap, Mathf.Min(step, slotSize + gap * 3f));
        float gridWidth = slotSize + step * (columns - 1);
        float startX = RingMargin + Mathf.Max(0f, (width - gridWidth) * 0.5f);

        for (int i = 0; i < count; i++)
        {
            UiTile tile = Take(i);
            tile.gameObject.SetActive(true);

            int column = i % columns;
            int row = i / columns;
            UiKit.TopLeft(tile.Rect, startX + column * step, UiTheme.Space2 + row * (tileHeight + gap), slotSize, tileHeight);
            bind(tile, i);
        }

        for (int i = count; i < tiles.Count; i++)
        {
            tiles[i].Payload = null;
            tiles[i].gameObject.SetActive(false);
        }

        int rows = Mathf.CeilToInt(count / (float)columns);
        UiKit.SetContentHeight(content, UiTheme.Space2 * 2f + rows * tileHeight + Mathf.Max(0, rows - 1) * gap);

        bool empty = count == 0;
        emptyText.gameObject.SetActive(empty && !string.IsNullOrEmpty(emptyMessage));
        emptyText.text = emptyMessage ?? string.Empty;
    }

    public void ScrollToTop()
    {
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
    }

    private UiTile Take(int index)
    {
        if (index < tiles.Count) return tiles[index];

        UiTile tile = UiTile.Create(content, "Tile_" + index, slotSize);
        tile.Slot.Clicked += () => TileClicked?.Invoke(tile);
        tiles.Add(tile);
        return tile;
    }
}
