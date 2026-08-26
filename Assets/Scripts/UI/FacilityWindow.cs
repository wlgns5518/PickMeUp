using TMPro;
using UnityEngine;

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

    // 배너는 MonoBehaviour가 아니라 코루틴을 쓸 수 없다. 시간을 창이 대신 굴려 준다.
    private void Update()
    {
        TickWindow(Time.deltaTime);
    }

    protected virtual void TickWindow(float deltaTime)
    {
    }
}
