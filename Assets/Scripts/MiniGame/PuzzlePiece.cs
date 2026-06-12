using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Image), typeof(RectTransform), typeof(CanvasGroup))]
public class PuzzlePiece : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public int gridCol;
    public int gridRow;

    public RectTransform RT { get; private set; }
    public CanvasGroup   CG { get; private set; }
    public Image         Image { get; private set; }

    private PuzzleGame manager;
    private Canvas canvas;

    public void Init(int col, int row, PuzzleGame mgr)
    {
        gridCol = col;
        gridRow = row;
        manager = mgr;
        RT = (RectTransform)transform;
        CG = GetComponent<CanvasGroup>();
        Image = GetComponent<Image>();
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
