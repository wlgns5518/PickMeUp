using UnityEngine;
using UnityEngine.EventSystems;

// 카드를 끌어다 놓을 수 있는 화면. 편성 창(DeckBuildUI)과 합성 창(SynthesisUI)이
// 같은 드래그 부품을 쓰도록 공통으로 두었다.
//
// 자리 번호(slotIndex)의 뜻은 화면마다 다르다 — 편성 창은 출전 슬롯 번호, 합성 창은
// 주 카드/재료 자리다. 음수(CardDragSource.RosterSlot)만 두 화면에서 같은 뜻으로,
// "아래쪽 보유 목록"을 가리킨다.
public interface ICardDragHost
{
    // 손끝을 따라다니는 반투명 카드. 카드를 만드는 방법이 화면마다 달라 여기서 받아 온다.
    RectTransform CreateDragGhost(CharacterSO character);

    // 받아 주는 자리 위에 놓았다.
    void HandleDrop(CardDragSource source, int slotIndex);

    // 아무 자리에도 닿지 않은 채 손을 놓았다. 보통 "집어 온 자리를 비운다"로 처리한다.
    void HandleDropOutside(CardDragSource source);
}

// 카드를 집어 드는 쪽. 목록의 카드와 자리에 올라간 카드가 같은 컴포넌트를 쓴다.
// 어디서 집었는지는 SlotIndex로 구분한다 — 0 이상이면 자리, 음수면 보유 목록.
//
// 코드에서 만드는 UI라 인스펙터 배선이 없다. 화면 쪽이 Bind로 값을 넣어준다.
[DisallowMultipleComponent]
public class CardDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public const int RosterSlot = -1;

    // 드래그는 한 번에 하나뿐이라 드롭 대상 쪽에서 읽기 쉽게 정적으로 들고 있는다.
    public static CardDragSource Current { get; private set; }

    // 드롭 대상이 받아 갔는지. 아무 대상에도 닿지 않은 채 손을 놓으면 "빼기"로 본다.
    public static bool Handled { get; set; }

    public CharacterSO Character { get; private set; }
    public int SlotIndex { get; private set; } = RosterSlot;

    private ICardDragHost owner;
    private RectTransform ghost;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 지난 플레이의 파괴된 오브젝트가 남지 않도록 비운다.
        Current = null;
        Handled = false;
    }

    public void Bind(ICardDragHost host, CharacterSO character, int slotIndex)
    {
        owner = host;
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

        // 자리 밖 허공에 놓았다. 그때 무엇을 할지는 화면마다 다르므로 그쪽에 넘긴다.
        if (!Handled && owner != null) owner.HandleDropOutside(this);

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
