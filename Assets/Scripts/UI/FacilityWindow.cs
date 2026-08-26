using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 마을 시설을 눌러 여는 창의 공통 뼈대.
//
// 소환소·합성소·시공의 틈·장비제작소·층 선택은 안에 들어가는 내용만 다를 뿐,
// 창으로서 하는 일은 똑같다. 캔버스를 한 번 세우고, 팝업을 껐다 켜고, 배너 시간을 굴린다.
// 그 다섯 벌이 각자 같은 코드를 들고 있어서, 창을 하나 더 만들 때마다 다시 베껴 써야 했고
// 도메인 리로드로 남은 캔버스를 치우는 처리 같은 것을 빠뜨리면 그 창만 조용히 어긋났다.
//
// 여기서 정하는 것은 "창이 열리고 닫히는 방식" 하나뿐이다.
// 무엇을 그릴지(BuildWindow)와 언제 무엇을 갱신할지(Show/Hide)는 각 창이 그대로 정한다 —
// 창마다 열기 전에 목록을 다시 만들거나, 닫을 때 올려둔 카드를 내려놓는 등 할 일이 다르기 때문이다.
public abstract class FacilityWindow : MonoBehaviour, IFacilityWindow
{
    [Header("Font")]
    [Tooltip("한글 폰트. 비워두면 프로젝트 기본 폰트를 찾아 쓴다.")]
    [SerializeField] protected TMP_FontAsset koreanFont;

    protected Canvas canvas;
    protected RectTransform canvasRect;
    protected GameObject popupRoot;
    protected TMP_FontAsset resolvedFont;

    // 이 창이 세우는 캔버스 오브젝트 이름. 다시 세울 때 남아 있는 옛 캔버스를 찾는 열쇠다.
    protected abstract string CanvasName { get; }

    // 창끼리 겹칠 때의 앞뒤. 두 창이 함께 열릴 일은 없지만 순서를 정해 두면 겹쳐도 헷갈리지 않는다.
    protected abstract int SortingOrder { get; }

    // 캔버스와 팝업을 실제로 만드는 곳. EnsureBuilt가 한 번만 부른다.
    protected abstract void BuildWindow();

    // 여닫는 순간에 갱신할 것이 창마다 다르다(목록 다시 만들기, 올려둔 카드 내려놓기 …).
    public abstract void Show();
    public abstract void Hide();

    public bool IsOpen => popupRoot != null && popupRoot.activeSelf;

    public void Toggle()
    {
        if (IsOpen) Hide();
        else Show();
    }

    protected virtual void SetOpen(bool open)
    {
        if (popupRoot != null) popupRoot.SetActive(open);
    }

    protected void EnsureBuilt()
    {
        if (canvas != null) return;

        resolvedFont = HudFactory.ResolveFont(koreanFont, this);

        // 참조만 잃고 남아 있는 이전 캔버스를 먼저 치운다(도메인 리로드 대비).
        Transform stale = transform.Find(CanvasName);
        if (stale != null) DestroyImmediate(stale.gameObject);

        BuildWindow();
    }

    // 창을 담을 캔버스를 세운다. BuildWindow가 맨 먼저 부른다.
    protected void BuildCanvas()
    {
        canvas = HudFactory.CreateScreenCanvas(transform, CanvasName, SortingOrder, out canvasRect);
    }

    // 팝업 루트와 그 뒤의 배경막까지 만들어 돌려준다. 창 내용은 받은 popup 안에 이어 붙이면 된다.
    //
    // 배경막과 창 패널은 형제로 둔다. 패널을 배경막의 자식으로 넣으면 패널 안을 누른 클릭이
    // 배경막까지 거슬러 올라가 창이 곧바로 닫힌다.
    protected RectTransform BuildPopupRoot()
    {
        RectTransform popup = HudFactory.CreateGroup(canvasRect, "Popup");
        HudFactory.Stretch(popup);
        popup.pivot = new Vector2(0.5f, 0.5f);
        popupRoot = popup.gameObject;

        Image backdrop = HudFactory.CreateImage(popup, "Backdrop", BattleHudPalette.PanelBackdrop);
        // 창 밖을 누르면 닫는다. 열려 있는 동안 뒤쪽 세계로 새는 클릭도 여기서 막힌다.
        backdrop.raycastTarget = true;
        HudFactory.Stretch(backdrop.rectTransform);

        var button = backdrop.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(Hide);

        return popup;
    }

    // ---- 보유 명단이 바뀌었을 때 ------------------------------------------
    //
    // 카드를 늘어놓는 창(편성·합성)은 인원이 바뀌면 칸 수도 카드 크기도 달라져 통째로 다시 지어야 한다.
    // 다만 닫혀 있는 동안에는 미뤄 둔다 — 10연차 소환은 한 명씩 열 번 늘어나므로,
    // 그때마다 아무도 보지 않는 캔버스를 다시 지으면 그대로 멈춤이 된다.

    // 카드를 몇 장 깔아 두고 지었는지. 인원이 이 수에서 달라지면 슬롯을 새로 깔아야 한다.
    protected int builtRosterCount = -1;

    // 닫혀 있는 동안 인원이 바뀌었다. 다음에 열 때 다시 짓는다.
    protected bool rosterDirty;

    protected void RebuildRoster()
    {
        rosterDirty = false;

        bool wasOpen = IsOpen;
        // canvas를 놓으면 EnsureBuilt가 남아 있는 캔버스를 치우고 처음부터 다시 만든다.
        canvas = null;
        EnsureBuilt();
        AfterRosterRebuilt();
        SetOpen(wasOpen);
    }

    // 다시 지은 뒤 화면 값을 맞출 것이 있으면 여기서.
    protected virtual void AfterRosterRebuilt()
    {
    }

    // 배너는 MonoBehaviour가 아니라 코루틴을 쓸 수 없다. 시간을 창이 대신 굴려 준다.
    private void Update()
    {
        TickWindow(Time.deltaTime);
    }

    protected virtual void TickWindow(float deltaTime)
    {
    }
}
