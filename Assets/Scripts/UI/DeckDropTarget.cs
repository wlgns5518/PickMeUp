using UnityEngine;
using UnityEngine.EventSystems;

// 카드를 받는 쪽. 출전 슬롯(SlotIndex >= 0)과 보유 목록 바닥(SlotIndex < 0)이 같은 컴포넌트를 쓴다.
//
// 드롭을 받으려면 이 오브젝트의 그래픽이 레이캐스트 대상이어야 하고,
// 끌고 다니는 유령 카드는 반대로 레이캐스트를 막지 않아야 한다(DeckBuildUI에서 CanvasGroup으로 끈다).
[DisallowMultipleComponent]
public class DeckDropTarget : MonoBehaviour, IDropHandler
{
    public int SlotIndex { get; private set; } = DeckDragSource.RosterSlot;

    private DeckBuildUI owner;

    public void Bind(DeckBuildUI deckUi, int slotIndex)
    {
        owner = deckUi;
        SlotIndex = slotIndex;
    }

    public void OnDrop(PointerEventData eventData)
    {
        DeckDragSource source = DeckDragSource.Current;
        if (source == null || owner == null) return;

        // 받아 갔다고 알려야 손을 놓는 쪽에서 "허공에 버림"으로 오해하지 않는다.
        DeckDragSource.Handled = true;
        owner.HandleDrop(source, SlotIndex);
    }
}
