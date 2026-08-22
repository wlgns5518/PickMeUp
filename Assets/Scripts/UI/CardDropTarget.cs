using UnityEngine;
using UnityEngine.EventSystems;

// 카드를 받는 쪽. 자리(SlotIndex >= 0)와 보유 목록 바닥(SlotIndex < 0)이 같은 컴포넌트를 쓴다.
//
// 드롭을 받으려면 이 오브젝트의 그래픽이 레이캐스트 대상이어야 하고,
// 끌고 다니는 유령 카드는 반대로 레이캐스트를 막지 않아야 한다(CanvasGroup으로 끈다).
[DisallowMultipleComponent]
public class CardDropTarget : MonoBehaviour, IDropHandler
{
    public int SlotIndex { get; private set; } = CardDragSource.RosterSlot;

    private ICardDragHost owner;

    public void Bind(ICardDragHost host, int slotIndex)
    {
        owner = host;
        SlotIndex = slotIndex;
    }

    public void OnDrop(PointerEventData eventData)
    {
        CardDragSource source = CardDragSource.Current;
        if (source == null || owner == null) return;

        // 받아 갔다고 알려야 손을 놓는 쪽에서 "허공에 버림"으로 오해하지 않는다.
        CardDragSource.Handled = true;
        owner.HandleDrop(source, SlotIndex);
    }
}
