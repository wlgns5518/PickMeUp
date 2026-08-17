using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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

    private PuzzleGame manager;
    private Canvas canvas;

    public void Init(int col, int row, PuzzleGame mgr, Image coreImage)
    {
        gridCol = col;
        gridRow = row;
        manager = mgr;
        RT = (RectTransform)transform;
        CG = GetComponent<CanvasGroup>();
        Image = coreImage;
        canvas = GetComponentInParent<Canvas>();
    }

    public void OnBeginDrag(PointerEventData e)
    {
        manager?.OnPieceBeginDrag(this);
    }

    public void OnDrag(PointerEventData e)
    {
        if (manager == null) return;
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        manager.OnPieceDrag(this, e.delta / scale);
    }

    public void OnEndDrag(PointerEventData e)
    {
        manager?.OnPieceEndDrag(this);
    }
}
