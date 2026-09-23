using TMPro;
using UnityEngine;

// 마을 시설을 눌러 여는 화면의 여닫기 뼈대.
//
// 소환소·합성소·장비창·제작소·파티 편성·층 선택은 안에 들어가는 내용만 다를 뿐, 화면으로서 하는 일은 똑같다.
// 캔버스를 한 번 세우고, 화면 루트를 껐다 켠다. 여기서 정하는 것은 그 "열리고 닫히는 방식" 하나뿐이다.
// 모양(배경·머리줄·팝업 층)은 디자인 시스템의 UiScreen이, 무엇을 그릴지와 언제 갱신할지는 각 화면이 정한다.
//
// 화면에는 Update가 없다. 창마다 매 프레임 무언가를 두드리면 화면이 전부 닫혀 있어도 그 수만큼 Update가 돈다.
public abstract class FacilityWindow : MonoBehaviour, IFacilityWindow
{
    [Header("Font")]
    [Tooltip("한글 폰트. 비워두면 프로젝트 기본 폰트를 찾아 쓴다.")]
    [SerializeField] protected TMP_FontAsset koreanFont;

    protected Canvas canvas;
    protected RectTransform canvasRect;
    protected GameObject popupRoot;
    protected TMP_FontAsset resolvedFont;

    // 이 화면이 세우는 캔버스 오브젝트 이름. 다시 세울 때 남아 있는 옛 캔버스를 찾는 열쇠다.
    protected abstract string CanvasName { get; }

    // 화면끼리 겹칠 때의 앞뒤. 두 화면이 함께 열릴 일은 없지만 순서를 정해 두면 겹쳐도 헷갈리지 않는다.
    protected abstract int SortingOrder { get; }

    // 캔버스와 화면을 실제로 만드는 곳. EnsureBuilt가 한 번만 부른다.
    protected abstract void BuildWindow();

    // 여닫는 순간에 갱신할 것이 화면마다 다르다(목록 다시 그리기, 올려 둔 것 내려놓기 …).
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
        // 닫힌 화면의 캔버스도 끈다. 비어 있어도 켜진 캔버스는 매 프레임 배치 갱신을 한 번씩 돌아서,
        // 닫힌 시설 화면 일곱 개가 마을에 가만히 있는 동안에도 프레임마다 약 25µs를 먹었다(캔버스 하나 3~4µs, 2026-09-23 실측).
        if (canvas != null) canvas.enabled = open;
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

    // 화면을 담을 캔버스를 세운다. BuildWindow가 맨 먼저 부른다.
    protected void BuildCanvas()
    {
        canvas = HudFactory.CreateScreenCanvas(transform, CanvasName, SortingOrder, out canvasRect);
    }
}
