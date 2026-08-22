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

    public void Init(int col, int row, PuzzleGame mgr, Image coreImage, Image shadowImage)
    {
        gridCol = col;
        gridRow = row;
        manager = mgr;
        RT = (RectTransform)transform;
        CG = GetComponent<CanvasGroup>();
        Image = coreImage;
        shadow = shadowImage;
        canvas = GetComponentInParent<Canvas>();
        Placed = false;
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
