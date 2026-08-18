using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// 전투 HUD의 단일 진입점. 씬에 이 컴포넌트 하나만 올려두면
// 캔버스부터 왼쪽 파티 패널과 결과창까지 전부 코드에서 만든다.
//
// 화면 구성은 원작 게임 화면을 따른다 — 유닛 머리 위에는 아무것도 그리지 않고,
// 아군 상태는 왼쪽 파티 패널에서만 읽는다.
[DisallowMultipleComponent]
public class BattleHud : MonoBehaviour
{
    [Header("Font")]
    [Tooltip("한글이 포함되므로 한국어 SDF 폰트를 지정해야 한다 (Assets/Fonts/NotoSansKR-Black SDF).")]
    [SerializeField] private TMP_FontAsset koreanFont;

    [Header("Party Panel")]
    [Tooltip("화면 왼쪽 위 모서리로부터의 여백(px, 1920x1080 기준).")]
    [SerializeField] private Vector2 partyPanelMargin = new Vector2(28f, 28f);

    [Header("Result")]
    [SerializeField] private bool showResultPanel = true;

    private Canvas canvas;
    private RectTransform canvasRect;
    private PartyStatusPanel partyPanel;
    private BattleResultPanel resultPanel;
    private TMP_FontAsset resolvedFont;
    private PartyFollowCamera followCamera;

    private void Awake()
    {
        EnsureBuilt();
    }

    // 캔버스와 위젯은 코드로 만들기 때문에 참조가 전부 비직렬화 필드에 들어 있다.
    // 플레이 중 스크립트를 고쳐 도메인 리로드가 일어나면 그 참조만 날아가고 오브젝트는 씬에 남는다.
    // Awake는 그때 다시 불리지 않으므로 OnEnable에서도 한 번 더 확인해 재건한다.
    private void EnsureBuilt()
    {
        if (canvas != null) return;

        resolvedFont = HudFactory.ResolveFont(koreanFont, this);

        // 참조만 잃고 남아 있는 이전 캔버스를 먼저 치운다. Destroy는 프레임 끝까지 미뤄져
        // 같은 프레임에 다시 찾아지므로 여기서는 즉시 파괴해야 한다.
        Transform stale = transform.Find("BattleHudCanvas");
        if (stale != null) DestroyImmediate(stale.gameObject);

        EnsureEventSystem();
        BuildCanvas();
        partyPanel = PartyStatusPanel.Create(canvasRect, resolvedFont, partyPanelMargin);
        partyPanel.UnitClicked += HandlePartySlotClicked;
        resultPanel = BattleResultPanel.Create(canvasRect, resolvedFont);
    }

    // 파티 슬롯을 누르면 그 캐릭터로 시점을 옮긴다.
    private void HandlePartySlotClicked(UnitController unit)
    {
        PartyFollowCamera cam = ResolveCamera();
        if (cam == null) return;

        cam.Focus(unit);
        partyPanel?.SetSelected(unit);
    }

    // 카메라는 씬에 하나뿐이고 HUD보다 먼저 사라질 수 있어 매번 유효성을 확인한다.
    private PartyFollowCamera ResolveCamera()
    {
        if (followCamera == null) followCamera = FindAnyObjectByType<PartyFollowCamera>();
        return followCamera;
    }

    // 둘 다 정적 이벤트라 BattleManager 인스턴스를 찾을 필요가 없다.
    // 덕분에 Awake/Start 순서에 의존하지 않고, 도메인 리로드 뒤에도 OnEnable에서 그대로 다시 붙는다.
    private void OnEnable()
    {
        EnsureBuilt();
        BattleManager.OnBattleStarted += HandleBattleStarted;
        BattleManager.OnBattleEnded += HandleBattleEnded;

        // 도메인 리로드로 패널을 새로 만든 경우 전투는 이미 시작돼 있어 OnBattleStarted가 다시 오지 않는다.
        // 그대로 두면 파티 패널이 빈 채로 남으므로 여기서 한 번 더 붙여준다.
        if (BattleManager.Instance != null && BattleManager.Instance.IsRunning) HandleBattleStarted();
    }

    private void OnDisable()
    {
        BattleManager.OnBattleStarted -= HandleBattleStarted;
        BattleManager.OnBattleEnded -= HandleBattleEnded;
    }

    private void LateUpdate()
    {
        // 카메라가 스스로 잡은 대상(전투 시작 시 첫 아군)도 강조에 반영되도록 매 프레임 맞춰준다.
        PartyFollowCamera cam = ResolveCamera();
        if (cam != null) partyPanel?.SetSelected(cam.FocusTarget);

        // 유닛이 이번 프레임의 피해/감정 변화를 모두 반영한 뒤에 읽는다.
        // 슬롯은 파티 인원수(보통 5명)뿐이라 매 프레임 훑어도 부담이 없다.
        partyPanel?.Refresh();
    }

    private void BuildCanvas()
    {
        var canvasGo = new GameObject("BattleHudCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.layer = LayerMask.NameToLayer("UI");

        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 기존 씬 UI(미니게임 캔버스 등) 위에 그려지도록 한 단계 위에 둔다.
        canvas.sortingOrder = 100;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // 파티 슬롯을 눌러 카메라를 옮기려면 레이캐스터가 필요하다.
        // 예전에는 결과창이 아래쪽 UI 입력을 가로챌까 봐 붙이지 않았는데, HudFactory가 만드는
        // 이미지/텍스트는 전부 raycastTarget=false라 실제로 클릭을 받는 건 파티 슬롯의
        // 판정용 이미지 하나뿐이다. 결과창은 여전히 입력을 가로채지 않는다.
        canvasGo.AddComponent<GraphicRaycaster>();

        canvasRect = (RectTransform)canvasGo.transform;
    }

    // UI 클릭은 EventSystem이 있어야 전달된다. 씬마다 배치를 챙기면 한 곳만 빠져도
    // "클릭이 안 되는" 증상으로 나타나므로, HUD를 코드로 짓는 것과 같은 이유로 여기서 보장한다.
    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (FindAnyObjectByType<EventSystem>() != null) return;

        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        go.transform.SetParent(null);
    }

    // 전투 시작 시점의 아군 목록을 그대로 슬롯에 고정한다.
    // 이후 아군이 죽어 레지스트리에서 빠져도 슬롯은 남아 전투 불능으로 표시된다.
    private void HandleBattleStarted()
    {
        // UnitRegistry.Allies는 죽은 유닛이 빠지는 살아있는 목록이라 슬롯 고정용으로 쓸 수 없다.
        // BattleManager가 시작 시점에 붙잡아 둔 명단을 단일 출처로 쓴다.
        if (BattleManager.Instance == null) return;
        partyPanel?.Bind(BattleManager.Instance.AllyRoster);
    }

    private void HandleBattleEnded(BattleResult result)
    {
        if (!showResultPanel || resultPanel == null) return;
        resultPanel.Show(result);
    }
}
