using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

// 마을 카메라. 가상 카메라 두 대를 번갈아 켠다.
//
//  - VCam_FreeLook: 마을을 둘러보는 평소 시점. 땅 위의 한 점(pivot)을 기울기(pitch)·회전(yaw)·거리로 내려다본다.
//                   손가락으로 끄는 것은 카메라가 아니라 이 pivot이다. 카메라는 pivot에서 자세를 다시 계산해 따라온다.
//  - VCam_Focus:    건물·영웅을 골랐을 때의 시점. 대상 기준으로 기울기·회전·거리·FOV를 따로 가진다.
//
// 두 카메라 사이를 부드럽게 넘어가는 일은 Main Camera의 CinemachineBrain이 한다(우선순위가 높은 쪽으로 블렌드).
// 이 스크립트는 두 카메라의 자세를 계산해 놓고 Focus 쪽 우선순위만 올렸다 내린다.
//
// 가상 카메라에는 Position/Rotation Control 부품을 붙이지 않는다. 자세는 여기서 Transform에 직접 쓴다 —
// 부품으로 맡기면 기울기·거리를 인스펙터 숫자 하나로 바꾸는 일이 부품마다 다른 이름으로 흩어진다.
//
// 입력은 새 Input System으로 받는다(이 프로젝트는 구형 Input Manager가 꺼져 있어 Input.GetTouch가 동작하지 않는다).
// 에디터·PC에서는 왼쪽 끌기 = 한 손가락 드래그, 휠 = 핀치로 대신한다.
[DisallowMultipleComponent]
public class VillageCameraController : MonoBehaviour
{
    [Header("Virtual Cameras")]
    [Tooltip("마을 전체를 둘러보는 평소 시점 카메라.")]
    [SerializeField] private CinemachineCamera freeLookCamera;

    [Tooltip("건물·영웅을 골랐을 때 켜지는 카메라.")]
    [SerializeField] private CinemachineCamera focusCamera;

    [Tooltip("평소 시점 카메라의 우선순위.")]
    [SerializeField] private int freeLookPriority = 10;

    [Tooltip("포커스 중 포커스 카메라의 우선순위. 평소 시점보다 높아야 전환된다.")]
    [SerializeField] private int focusActivePriority = 20;

    [Tooltip("포커스가 아닐 때 포커스 카메라의 우선순위. 평소 시점보다 낮아야 한다.")]
    [SerializeField] private int focusIdlePriority = 0;

    [Header("Blend")]
    [Tooltip("켜 두면 시작할 때 Brain의 기본 블렌드를 아래 값으로 덮어쓴다. Brain에서 직접 다듬고 싶으면 끈다.")]
    [SerializeField] private bool applyBlendToBrain = true;

    [Tooltip("두 시점 사이를 넘어가는 시간(초).")]
    [SerializeField, Min(0f)] private float blendTime = 0.8f;

    [Tooltip("블렌드 곡선 모양.")]
    [SerializeField] private CinemachineBlendDefinition.Styles blendStyle = CinemachineBlendDefinition.Styles.EaseInOut;

    [Header("FreeLook Angle")]
    [Tooltip("X축 기울기(도). 90이면 바로 위에서 내려다보고, 작을수록 옆에서 본다.")]
    [SerializeField, Range(5f, 89f)] private float pitch = 50f;

    [Tooltip("Y축 회전(도). 드래그 방향도 이 값을 따라 돈다.")]
    [SerializeField, Range(-180f, 180f)] private float yaw = 0f;

    [Tooltip("pivot에서 카메라까지의 거리(m).")]
    [SerializeField, Min(1f)] private float distance = 250f;

    [Tooltip("시작할 때 카메라가 바라보는 땅 위의 점. 기본값은 기울기 50·거리 250·시야각 45에서 성벽 안 전체가 5:3 화면에 들어오는 자리 — " +
             "가까운 남쪽 벽이 화면 아래에서 크게 잡혀 가운데보다 남쪽을 본다.")]
    [SerializeField] private Vector3 startPivot = new Vector3(0f, 0f, -25f);

    [Header("Drag")]
    [Tooltip("드래그 감도. 1이면 손가락 아래의 땅이 손가락을 그대로 따라온다.")]
    [SerializeField, Min(0f)] private float dragSpeed = 1f;

    [Tooltip("이만큼(픽셀) 움직이기 전까지는 드래그로 치지 않는다. 건물을 누르려다 살짝 밀린 손가락이 화면을 흔들지 않게 한다.")]
    [SerializeField, Min(0f)] private float dragThreshold = 12f;

    [Tooltip("손을 뗀 뒤 미끄러지는 관성이 줄어드는 빠르기. 클수록 빨리 멈추고, 0이면 관성이 없다.")]
    [SerializeField, Min(0f)] private float inertiaDamping = 6f;

    [Header("Map Bounds (XZ)")]
    [Tooltip("pivot이 갈 수 있는 X·Z 최소값.")]
    [SerializeField] private Vector2 boundsMin = new Vector2(-120f, -120f);

    [Tooltip("pivot이 갈 수 있는 X·Z 최대값.")]
    [SerializeField] private Vector2 boundsMax = new Vector2(120f, 120f);

    [Header("Zoom (FOV)")]
    [Tooltip("가장 가깝게 당겼을 때의 시야각.")]
    [SerializeField, Range(5f, 120f)] private float minFov = 20f;

    [Tooltip("가장 멀리 뺐을 때의 시야각.")]
    [SerializeField, Range(5f, 120f)] private float maxFov = 60f;

    [Tooltip("시작 시야각.")]
    [SerializeField, Range(5f, 120f)] private float startFov = 45f;

    [Tooltip("두 손가락 사이가 화면 높이만큼 벌어질 때 바뀌는 시야각(도).")]
    [SerializeField, Min(0f)] private float pinchZoomSpeed = 60f;

    [Tooltip("휠 한 칸에 바뀌는 시야각(도).")]
    [SerializeField, Min(0f)] private float scrollZoomStep = 3f;

    [Tooltip("시야각이 목표값을 따라가는 빠르기. 클수록 즉각적이다.")]
    [SerializeField, Min(0.1f)] private float zoomSharpness = 12f;

    [Header("Focus")]
    [Tooltip("포커스 시 기울기(도). 평소보다 낮추면 로우앵글이 된다.")]
    [SerializeField, Range(-30f, 89f)] private float focusPitch = 20f;

    [Tooltip("켜 두면 포커스 회전을 지금 평소 시점의 회전 + 아래 오프셋으로 잡는다. 끄면 아래 값을 절대 각도로 쓴다.")]
    [SerializeField] private bool focusKeepsCurrentYaw = true;

    [Tooltip("포커스 시 회전(도). 위 옵션에 따라 오프셋 또는 절대 각도.")]
    [SerializeField, Range(-180f, 180f)] private float focusYaw = 0f;

    [Tooltip("대상에서 카메라까지의 거리(m).")]
    [SerializeField, Min(0.5f)] private float focusDistance = 35f;

    [Tooltip("대상 발밑에서 이만큼 위를 바라본다(m). 건물 가운데나 영웅 가슴 높이.")]
    [SerializeField] private float focusHeightOffset = 4f;

    [Tooltip("포커스 시 시야각.")]
    [SerializeField, Range(5f, 120f)] private float focusFov = 35f;

    [Tooltip("켜 두면 포커스 중에 대상이 움직이면 따라간다(걸어 다니는 영웅).")]
    [SerializeField] private bool followFocusTarget = true;

    [Tooltip("켜 두면 포커스 중에 드래그하면 평소 시점으로 돌아간다. 끄면 포커스 중에는 드래그를 무시한다.")]
    [SerializeField] private bool dragExitsFocus = true;

    /// 이번 손짓이 드래그나 핀치였는지. 손을 뗄 때 들어오는 건물 클릭을 걸러 낼 때 쓴다
    /// (건물 위에서 끌기 시작해 같은 건물 위에서 손을 떼면 EventSystem은 클릭으로 친다).
    /// 다음 손짓이 시작될 때 false로 돌아간다.
    public static bool GestureMovedCamera { get; private set; }

    /// 지금 포커스 중인지.
    public bool IsFocused => focused;

    /// 지금 포커스 중인 대상. 없으면 null.
    public Transform FocusTarget => focusTarget;

    private Vector3 pivot;          // 평소 시점이 바라보는 땅 위의 점
    private float targetFov;        // 줌 입력이 정한 목표 시야각
    private float currentFov;       // 부드럽게 따라가는 실제 시야각
    private Vector3 inertiaVelocity; // 손을 뗀 뒤 pivot이 미끄러지는 속도(m/s)
    private Transform focusTarget;
    private bool focused;           // 대상이 파괴되면 focusTarget이 null이 되므로 켜짐 여부를 따로 든다

    // 손짓 하나(첫 손가락이 닿은 순간부터 모든 손가락이 떨어질 때까지)의 상태
    private bool gestureActive;
    private bool gestureOverUI;     // UI 위에서 시작한 손짓이면 끝날 때까지 카메라를 움직이지 않는다
    private bool dragging;          // 문턱을 넘어 드래그로 인정됐는지
    private float dragAccumulated;  // 문턱을 넘기 전까지 움직인 거리(픽셀)
    private float lastPinchDistance; // 직전 프레임의 두 손가락 사이 거리(픽셀). 0이면 이번 프레임에 핀치가 막 시작됨

    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private PointerEventData uiProbe;

    private void Awake()
    {
        pivot = ClampToBounds(startPivot);
        targetFov = currentFov = Mathf.Clamp(startFov, minFov, maxFov);

        // Brain 블렌드 설정: 두 가상 카메라 사이 전환의 시간과 곡선
        if (applyBlendToBrain)
        {
            var brain = FindAnyObjectByType<CinemachineBrain>();
            if (brain != null)
                brain.DefaultBlend = new CinemachineBlendDefinition(blendStyle, blendTime);
            else
                Debug.LogWarning("[VillageCameraController] 씬에 CinemachineBrain이 없어 가상 카메라가 화면에 반영되지 않습니다.", this);
        }

        if (freeLookCamera == null || focusCamera == null)
            Debug.LogWarning("[VillageCameraController] VCam_FreeLook / VCam_Focus 칸이 비어 있습니다.", this);

        // 시작 우선순위: 평소 시점이 이기게 둔다
        SetPriorities(false);
        ApplyFreeLookPose();
    }

    private void OnEnable()
    {
        // EnhancedTouch는 켠 횟수를 세므로 다른 스크립트가 켜 둔 것을 여기서 꺼 버리지 않는다
        EnhancedTouchSupport.Enable();
    }

    private void OnDisable()
    {
        EnhancedTouchSupport.Disable();
        EndGesture();
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // 1) 입력 읽기: 터치가 있으면 터치, 없으면 마우스
        if (!ReadTouches())
            ReadMouse();

        // 2) 관성: 손을 뗀 뒤 pivot을 조금 더 미끄러뜨린다
        if (!gestureActive && inertiaDamping > 0f && inertiaVelocity.sqrMagnitude > 0.0001f && !IsFocused)
        {
            pivot = ClampToBounds(pivot + inertiaVelocity * dt);
            inertiaVelocity *= Mathf.Exp(-inertiaDamping * dt);
        }

        // 3) 줌: 시야각이 목표값을 부드럽게 따라간다
        currentFov = Mathf.Lerp(currentFov, targetFov, 1f - Mathf.Exp(-zoomSharpness * dt));

        // 4) 두 카메라 자세 갱신. 인스펙터에서 각도를 바꾸면 실행 중에도 바로 반영된다
        ApplyFreeLookPose();
        if (focused)
        {
            // 대상이 사라졌으면(구역을 다시 지었거나 영웅이 퇴장) 평소 시점으로 돌아간다
            if (focusTarget == null) ResetView();
            else if (followFocusTarget) ApplyFocusPose();
        }
    }

    // ───────────────────────── 공개 API ─────────────────────────

    /// 대상을 포커스한다. 포커스 카메라 자세를 잡고 우선순위를 올리면 Brain이 블렌드한다.
    public void Focus(Transform target)
    {
        if (target == null) return;

        focusTarget = target;
        focused = true;
        inertiaVelocity = Vector3.zero; // 미끄러지던 평소 시점을 멈춰, 돌아왔을 때 떠난 자리 그대로 있게 한다
        ApplyFocusPose();
        SetPriorities(true);
    }

    /// 평소 시점으로 돌아간다. pivot·각도·줌은 포커스 전에 두었던 그대로다.
    public void ResetView()
    {
        focusTarget = null;
        focused = false;
        SetPriorities(false);
    }

    /// 평소 시점을 특정 지점으로 옮긴다(예: 새로 지은 건물 보여 주기). 포커스 중이면 먼저 풀린다.
    public void MoveTo(Vector3 worldPoint)
    {
        ResetView();
        inertiaVelocity = Vector3.zero;
        pivot = ClampToBounds(worldPoint);
    }

    // ───────────────────────── 입력 ─────────────────────────

    // 터치 입력. 손가락이 하나라도 있으면 true를 돌려 마우스 처리를 건너뛴다.
    private bool ReadTouches()
    {
        var touches = Touch.activeTouches;
        if (touches.Count == 0)
        {
            // 마우스도 안 눌려 있을 때만 손짓을 끝낸다(에디터에서 마우스로 끄는 중일 수 있다)
            if (gestureActive && !IsMousePressed()) EndGesture();
            return false;
        }

        // 손짓 시작: 첫 손가락이 UI 위에 닿았는지 이때 한 번만 판정한다
        if (!gestureActive) BeginGesture(touches[0].screenPosition);
        if (gestureOverUI) return true;

        if (touches.Count >= 2)
        {
            // 2지 핀치: 두 손가락 사이 거리 변화로 시야각을 바꾼다
            float d = Vector2.Distance(touches[0].screenPosition, touches[1].screenPosition);
            if (lastPinchDistance > 0f)
                Zoom(-(d - lastPinchDistance) / Screen.height * pinchZoomSpeed);
            lastPinchDistance = d;

            // 핀치도 카메라를 움직인 손짓이다. 손을 뗄 때 건물 클릭으로 새지 않게 한다
            GestureMovedCamera = true;
            dragging = false;
            inertiaVelocity = Vector3.zero;
        }
        else
        {
            // 1지 드래그. 핀치하다 한 손가락을 떼도 남은 손가락의 이번 프레임 이동량만 쓰므로 화면이 튀지 않는다
            lastPinchDistance = 0f;
            Drag(touches[0].delta);
        }

        return true;
    }

    // 마우스 입력(에디터·PC). 왼쪽 끌기 = 드래그, 휠 = 줌.
    private void ReadMouse()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        // 휠 줌: UI(스크롤 목록 등) 위에서는 굴려도 카메라를 건드리지 않는다
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f && !IsOverUI(mouse.position.ReadValue()))
            Zoom(-Mathf.Sign(scroll) * scrollZoomStep);

        if (mouse.leftButton.wasPressedThisFrame)
            BeginGesture(mouse.position.ReadValue());

        if (mouse.leftButton.isPressed)
        {
            if (gestureActive && !gestureOverUI)
                Drag(mouse.delta.ReadValue());
        }
        else if (gestureActive)
        {
            EndGesture();
        }
    }

    private static bool IsMousePressed()
    {
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
    }

    private void BeginGesture(Vector2 screenPosition)
    {
        gestureActive = true;
        gestureOverUI = IsOverUI(screenPosition);
        dragging = false;
        dragAccumulated = 0f;
        lastPinchDistance = 0f;
        GestureMovedCamera = false;
        inertiaVelocity = Vector3.zero; // 다시 짚으면 미끄러지던 화면이 멈춘다
    }

    private void EndGesture()
    {
        gestureActive = false;
        gestureOverUI = false;
        dragging = false;
        lastPinchDistance = 0f;
    }

    // ───────────────────────── 드래그 · 줌 ─────────────────────────

    // 화면에서 움직인 픽셀만큼 pivot을 땅 위에서 옮긴다.
    private void Drag(Vector2 screenDelta)
    {
        // 드래그 문턱: 살짝 밀린 탭은 드래그로 치지 않는다
        if (!dragging)
        {
            dragAccumulated += screenDelta.magnitude;
            if (dragAccumulated < dragThreshold) return;
            dragging = true;
            GestureMovedCamera = true;
        }

        // 포커스 중 드래그: 옵션에 따라 평소 시점으로 돌아가거나 무시한다
        if (IsFocused)
        {
            if (dragExitsFocus) ResetView();
            return;
        }

        if (screenDelta.sqrMagnitude < 0.0001f) return;

        // 화면 1픽셀이 pivot 거리에서 몇 m인지. 시야각이 좁아질수록(확대) 같은 손짓에 덜 움직인다
        float worldPerPixel = 2f * distance * Mathf.Tan(currentFov * 0.5f * Mathf.Deg2Rad) / Screen.height;

        // 카메라가 기울어 있으면 화면 세로 1픽셀이 땅 위에서는 더 길다. 너무 눕힌 각도에서 폭주하지 않게 하한을 둔다
        float groundStretch = 1f / Mathf.Max(Mathf.Sin(pitch * Mathf.Deg2Rad), 0.2f);

        // 현재 Yaw 기준 오른쪽·앞쪽 방향(수평면). 카메라를 돌려도 손가락 방향과 화면 이동 방향이 맞는다
        Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 right = yawRotation * Vector3.right;
        Vector3 forward = yawRotation * Vector3.forward;

        // 손가락이 땅을 잡아끄는 느낌이 되도록 부호를 뒤집는다(오른쪽으로 밀면 마을이 오른쪽으로 따라온다)
        Vector3 move = -(right * screenDelta.x + forward * (screenDelta.y * groundStretch)) * (worldPerPixel * dragSpeed);

        Vector3 before = pivot;
        pivot = ClampToBounds(pivot + move);

        // 관성용 속도: 경계에 막힌 만큼은 빼고 실제로 움직인 양으로 잰다
        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        inertiaVelocity = (pivot - before) / dt;
    }

    // 시야각 목표값을 바꾼다. 포커스 중에는 포커스 카메라가 자기 시야각을 쓰므로 받지 않는다.
    private void Zoom(float fovDelta)
    {
        if (IsFocused) return;
        targetFov = Mathf.Clamp(targetFov + fovDelta, minFov, maxFov);
    }

    // ───────────────────────── 자세 계산 ─────────────────────────

    // 평소 시점: pivot에서 (pitch, yaw) 방향으로 distance만큼 물러난 자리에서 pivot을 바라본다.
    private void ApplyFreeLookPose()
    {
        if (freeLookCamera == null) return;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 position = pivot - rotation * Vector3.forward * distance;
        freeLookCamera.transform.SetPositionAndRotation(position, rotation);
        freeLookCamera.Lens.FieldOfView = currentFov;
    }

    // 포커스 시점: 대상 머리 위 지점을 포커스 전용 각도·거리에서 바라본다.
    private void ApplyFocusPose()
    {
        if (focusCamera == null || focusTarget == null) return;

        float y = focusKeepsCurrentYaw ? yaw + focusYaw : focusYaw;
        Quaternion rotation = Quaternion.Euler(focusPitch, y, 0f);
        Vector3 lookPoint = focusTarget.position + Vector3.up * focusHeightOffset;
        Vector3 position = lookPoint - rotation * Vector3.forward * focusDistance;
        focusCamera.transform.SetPositionAndRotation(position, rotation);
        focusCamera.Lens.FieldOfView = focusFov;
    }

    private void SetPriorities(bool focused)
    {
        if (freeLookCamera != null) freeLookCamera.Priority = freeLookPriority;
        if (focusCamera != null) focusCamera.Priority = focused ? focusActivePriority : focusIdlePriority;
    }

    private Vector3 ClampToBounds(Vector3 p)
    {
        p.x = Mathf.Clamp(p.x, boundsMin.x, boundsMax.x);
        p.z = Mathf.Clamp(p.z, boundsMin.y, boundsMax.y);
        return p;
    }

    // ───────────────────────── UI 판정 ─────────────────────────

    // 화면의 이 점이 UI(Canvas 그래픽) 위인지.
    //
    // EventSystem.IsPointerOverGameObject()를 그대로 쓰지 않는다. Main Camera에 PhysicsRaycaster가 붙어 있어
    // (시설 클릭용) 그 함수는 지형·건물 콜라이더 위에서도 true를 돌려준다 — 마을 어디를 짚어도 "UI 위"가 되어
    // 드래그가 아예 안 된다. 그래서 같은 레이캐스트 결과를 받아 Canvas(GraphicRaycaster)가 맞힌 것만 UI로 친다.
    // 손짓이 시작될 때 한 번만 부르므로 비용은 무시할 만하다.
    private bool IsOverUI(Vector2 screenPosition)
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null) return false;

        uiProbe ??= new PointerEventData(eventSystem);
        uiProbe.Reset();
        uiProbe.position = screenPosition;

        uiHits.Clear();
        eventSystem.RaycastAll(uiProbe, uiHits);
        for (int i = 0; i < uiHits.Count; i++)
        {
            if (uiHits[i].module is GraphicRaycaster) return true;
        }
        return false;
    }

    private void OnValidate()
    {
        // 최소·최대가 뒤집히지 않게 맞춘다
        if (maxFov < minFov) maxFov = minFov;
        if (boundsMax.x < boundsMin.x) boundsMax.x = boundsMin.x;
        if (boundsMax.y < boundsMin.y) boundsMax.y = boundsMin.y;

#if UNITY_EDITOR
        // 편집 중에도 각도·거리·시야각을 바꾸면 Game 뷰에 바로 보이게 평소 시점 카메라를 옮겨 둔다.
        // OnValidate 안에서 Transform을 바로 건드리면 경고가 나므로 한 박자 미룬다.
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || Application.isPlaying) return;
                pivot = ClampToBounds(startPivot);
                currentFov = Mathf.Clamp(startFov, minFov, maxFov);
                ApplyFreeLookPose();
            };
        }
#endif
    }

    // 씬 뷰에 이동 경계와 pivot을 그린다
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 center = new Vector3((boundsMin.x + boundsMax.x) * 0.5f, startPivot.y, (boundsMin.y + boundsMax.y) * 0.5f);
        Vector3 size = new Vector3(boundsMax.x - boundsMin.x, 0.1f, boundsMax.y - boundsMin.y);
        Gizmos.DrawWireCube(center, size);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(Application.isPlaying ? pivot : startPivot, 2f);
    }
}
