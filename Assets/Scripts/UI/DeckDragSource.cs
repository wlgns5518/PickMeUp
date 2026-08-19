using UnityEngine;
using UnityEngine.EventSystems;

// 카드를 집어 드는 쪽. 보유 목록의 카드와 출전 슬롯의 카드가 같은 컴포넌트를 쓴다.
// 어디서 집었는지는 SlotIndex로 구분한다 — 0 이상이면 출전 슬롯, 음수면 보유 목록.
//
// 코드에서 만드는 UI라 인스펙터 배선이 없다. DeckBuildUI가 Bind로 값을 넣어준다.
[DisallowMultipleComponent]
public class DeckDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public const int RosterSlot = -1;

    // 드래그는 한 번에 하나뿐이라 드롭 대상 쪽에서 읽기 쉽게 정적으로 들고 있는다.
    public static DeckDragSource Current { get; private set; }

    // 드롭 대상이 받아 갔는지. 아무 대상에도 닿지 않은 채 손을 놓으면 "빼기"로 본다.
    public static bool Handled { get; set; }

    public CharacterSO Character { get; private set; }
    public int SlotIndex { get; private set; } = RosterSlot;

    private DeckBuildUI owner;
    private RectTransform ghost;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 지난 플레이의 파괴된 오브젝트가 남지 않도록 비운다.
        Current = null;
        Handled = false;
    }

    public void Bind(DeckBuildUI deckUi, CharacterSO character, int slotIndex)
    {
        owner = deckUi;
        Character = character;
        SlotIndex = slotIndex;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (owner == null || Character == null) return;

        Current = this;
        Handled = false;
        ghost = owner.CreateDragGhost(Character);
        MoveGhost(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        MoveGhost(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (ghost != null) Destroy(ghost.gameObject);
        ghost = null;

        // 슬롯 밖 허공에 놓으면 출전에서 뺀다. 목록으로 되돌리려고 정확히 조준하게 만들 이유가 없다.
        if (!Handled && SlotIndex >= 0 && owner != null) owner.RemoveFromParty(SlotIndex);

        Current = null;
        Handled = false;
    }

    private void MoveGhost(PointerEventData eventData)
    {
        if (ghost == null) return;

        // 캔버스 스케일러가 화면 해상도에 따라 배율을 바꾸므로 스크린 좌표를 그대로 쓸 수 없다.
        var parent = ghost.parent as RectTransform;
        if (parent == null) return;

        Vector2 local;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera, out local))
            ghost.anchoredPosition = local;
    }
}
