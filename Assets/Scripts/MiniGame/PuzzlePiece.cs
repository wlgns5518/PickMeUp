using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 퍼즐 조각 하나.
//
// 중앙 판의 제자리에 놓이면 Placed가 되고 거기서 굳는다. 다시 집어 들 수 없게 하는 이유는
// 맞힌 조각을 실수로 끌어 흐트러뜨리면 어디까지 맞췄는지 다시 세어야 하기 때문이다.
[RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
public class PuzzlePiece : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public int gridCol;
    public int gridRow;

    public RectTransform RT { get; private set; }
    public CanvasGroup   CG { get; private set; }
    // 실제 그림 스프라이트를 그리는 이미지. 그림자를 뒤에 깔기 위해 별도 자식 오브젝트에 위치.
    public Image         Image { get; private set; }

    // 제자리에 놓였는지. 놓인 조각은 더 이상 드래그되지 않는다.
    public bool Placed { get; private set; }

    private Image shadow;
    private PuzzleGame manager;
    private Canvas canvas;

    // 조각을 만들 때 한 번. 조각은 퍼즐이 끝나도 지우지 않고 다음 퍼즐에 다시 쓰이므로(PuzzleGame 조각 풀),
    // 판마다 바뀌는 것(자리·그림·놓였는지)은 여기가 아니라 Bind가 채운다.
    public void Setup(PuzzleGame mgr, Image coreImage, Image shadowImage)
    {
        manager = mgr;
        RT = (RectTransform)transform;
        CG = GetComponent<CanvasGroup>();
        Image = coreImage;
        shadow = shadowImage;
    }

    // 이번 판의 조각으로 칠한다. 지난 판에 제자리에 놓였던 조각이라도 여기서 처음 상태로 돌아간다 —
    // 놓이면 꺼 둔 그림자와 레이캐스트를 되살리지 않으면 다음 판에서 집히지 않는다.
    public void Bind(int col, int row, Sprite sprite)
    {
        gridCol = col;
        gridRow = row;

        Image.sprite = sprite;
        if (shadow != null)
        {
            shadow.sprite = sprite;
            shadow.enabled = true;
        }

        CG.blocksRaycasts = true;
        Placed = false;
        canvas = GetComponentInParent<Canvas>();
    }

    public void MarkPlaced()
    {
        Placed = true;

        // 레이캐스트를 놓아 주면 위로 지나가는 조각을 가리지 않고, 다시 집히지도 않는다.
        CG.blocksRaycasts = false;
        // 판에 딱 붙은 조각이 그림자를 달고 있으면 아직 떠 있는 것처럼 보인다.
        if (shadow != null) shadow.enabled = false;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (Placed) return;
        manager?.OnPieceBeginDrag(this);
    }

    public void OnDrag(PointerEventData e)
    {
        if (Placed || manager == null) return;

        float scale = canvas != null ? canvas.scaleFactor : 1f;
        manager.OnPieceDrag(this, e.delta / scale);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (Placed) return;
        manager?.OnPieceEndDrag(this);
    }
}
