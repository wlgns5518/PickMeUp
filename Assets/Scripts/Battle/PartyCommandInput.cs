using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 지휘관의 입력을 받아 PartyCommand로 넘기고, 지금 내려진 명령을 화면에 남긴다.
//
// 우클릭(또는 F) — 커서 아래의 적에게 집중 공격
// R — 긴급 후퇴     H — 진형 유지     C — 명령 거두기(집중 표적도 함께)
//
// 적은 엔티티라 콜라이더가 없다(EnemyWorldBridge 주석). 그래서 물리 레이캐스트로 고르지 않고
// 화면에 투영한 자리가 커서와 가장 가까운 적을 고른다. 누를 때 한 번만 훑으므로 1000마리여도 부담이 없다.
//
// BattleHud가 붙인다(씬마다 배치를 챙기지 않게). 수치를 다듬고 싶으면 HUD 오브젝트에 직접 붙이면 그 값이 쓰인다.
[DisallowMultipleComponent]
public class PartyCommandInput : MonoBehaviour
{
    [Header("Keys")]
    [SerializeField] private Key focusKey = Key.F;
    [SerializeField] private Key retreatKey = Key.R;
    [SerializeField] private Key holdKey = Key.H;
    [SerializeField] private Key resumeKey = Key.C;

    [Header("Retreat")]
    [Tooltip("후퇴할 때 적 무리 반대쪽으로 물러나는 거리(미터).")]
    [SerializeField, Min(1f)] private float retreatDistance = 10f;

    [Header("Pick")]
    [Tooltip("커서에서 이 거리(px, 1080p 기준) 안에 몸통이 투영된 적만 고른다.")]
    [SerializeField, Min(8f)] private float pickRadius = 70f;

    [Header("Marker")]
    [SerializeField] private Color markerColor = new Color(1f, 0.25f, 0.2f, 0.9f);
    [SerializeField, Min(0.1f)] private float markerRadius = 0.75f;

    // 투영할 높이. 발밑이 아니라 몸통을 겨눠야 화면에서 "이놈"을 누른 자리와 맞는다.
    private const float PickHeight = 0.9f;
    private const int MarkerSegments = 40;

    private Camera cachedCamera;
    private TMP_Text label;
    private LineRenderer marker;
    private Material markerMaterial;

    // HUD가 캔버스를 다 지은 뒤 부른다.
    public void Bind(RectTransform canvasRect, TMP_FontAsset font)
    {
        if (label != null || canvasRect == null) return;

        label = HudFactory.CreateText(canvasRect, "CommandLabel", font, 22f, new Color(1f, 1f, 1f, 0.85f));
        var rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 24f);
        rect.sizeDelta = new Vector2(1400f, 60f);
        RefreshLabel();
    }

    private void OnEnable()
    {
        PartyCommand.Reset();
        PartyCommand.Changed += RefreshLabel;
        RefreshLabel();
    }

    private void OnDisable()
    {
        PartyCommand.Changed -= RefreshLabel;
        if (marker != null) marker.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        // 런타임에 만든 재질은 씬이 바뀌어도 저절로 지워지지 않는다(메모리 누수 점검 기준 참조).
        if (markerMaterial != null) Destroy(markerMaterial);
        if (marker != null) Destroy(marker.gameObject);
    }

    private void Update()
    {
        PartyCommand.Tick();

        BattleManager battle = BattleManager.Instance;
        if (battle == null || !battle.IsRunning) return;

        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;

        bool pickPressed = mouse != null && mouse.rightButton.wasPressedThisFrame;
        if (keyboard != null && keyboard[focusKey].wasPressedThisFrame) pickPressed = true;
        if (pickPressed && mouse != null && !IsPointerOverUi()) TryFocusAt(mouse.position.ReadValue());

        if (keyboard == null) return;

        if (keyboard[retreatKey].wasPressedThisFrame) PartyCommand.OrderRetreat(retreatDistance);
        if (keyboard[holdKey].wasPressedThisFrame) PartyCommand.OrderHold();
        if (keyboard[resumeKey].wasPressedThisFrame)
        {
            PartyCommand.Resume();
            PartyCommand.ClearFocus();
        }
    }

    private void LateUpdate()
    {
        TargetRef focus = PartyCommand.FocusTarget;
        if (!focus.IsAlive)
        {
            if (marker != null && marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
            return;
        }

        EnsureMarker();
        if (!marker.gameObject.activeSelf) marker.gameObject.SetActive(true);

        Transform t = marker.transform;
        t.position = focus.Position + Vector3.up * 0.05f;
        t.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
    }

    // ---------------------------------------------------------------- 고르기

    private void TryFocusAt(Vector2 pointer)
    {
        Camera cam = ResolveCamera();
        if (cam == null) return;

        float radius = pickRadius * (Screen.height / 1080f);
        float bestSqr = radius * radius;
        TargetRef best = TargetRef.None;

        int count = EnemyWorldBridge.EnemyCount;
        for (int i = 0; i < count; i++)
        {
            EnemyWorldBridge.EnemyState enemy = EnemyWorldBridge.GetEnemy(i);
            if (!enemy.IsAlive) continue;

            if (TryScreenDistanceSqr(cam, (Vector3)enemy.position + Vector3.up * PickHeight, pointer, out float sqr) &&
                sqr < bestSqr)
            {
                bestSqr = sqr;
                best = new TargetRef(enemy.entity);
            }
        }

        // 게임오브젝트로 남은 적(아군을 적으로 돌리는 기믹 등)도 같은 규칙으로 고른다.
        var units = UnitRegistry.Enemies;
        for (int i = 0; i < units.Count; i++)
        {
            UnitController unit = units[i];
            if (unit == null || unit.IsDead) continue;

            if (TryScreenDistanceSqr(cam, unit.AimPoint, pointer, out float sqr) && sqr < bestSqr)
            {
                bestSqr = sqr;
                best = unit;
            }
        }

        if (best.Exists) PartyCommand.FocusFire(best);
    }

    private static bool TryScreenDistanceSqr(Camera cam, Vector3 world, Vector2 pointer, out float sqr)
    {
        Vector3 screen = cam.WorldToScreenPoint(world);
        sqr = float.MaxValue;
        if (screen.z <= 0f) return false;

        float dx = screen.x - pointer.x;
        float dy = screen.y - pointer.y;
        sqr = dx * dx + dy * dy;
        return true;
    }

    private static bool IsPointerOverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private Camera ResolveCamera()
    {
        if (cachedCamera == null || !cachedCamera.isActiveAndEnabled) cachedCamera = Camera.main;
        return cachedCamera;
    }

    // ---------------------------------------------------------------- 표시

    // 명령이 바뀔 때만 글자를 다시 쓴다(PartyCommand.Changed).
    private void RefreshLabel()
    {
        if (label == null) return;

        string order;
        switch (PartyCommand.Order)
        {
            case PartyOrder.Retreat: order = "<color=#FFB347>후퇴 중</color>"; break;
            case PartyOrder.Hold: order = "<color=#7FD4FF>진형 유지</color>"; break;
            default: order = "자유 교전"; break;
        }

        string focus = PartyCommand.FocusTarget.IsAlive ? "  ·  <color=#FF6A5A>집중 공격</color>" : "";
        label.text = "지휘: " + order + focus +
                     "\n<size=70%><alpha=#99>[우클릭/F] 집중 공격   [R] 후퇴   [H] 진형 유지   [C] 명령 해제</size>";
    }

    private void EnsureMarker()
    {
        if (marker != null) return;

        var go = new GameObject("FocusMarker");
        marker = go.AddComponent<LineRenderer>();
        marker.useWorldSpace = false;
        marker.loop = true;
        marker.positionCount = MarkerSegments;
        marker.widthMultiplier = 0.06f;
        marker.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        marker.receiveShadows = false;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            markerMaterial = new Material(shader);
            marker.sharedMaterial = markerMaterial;
        }

        marker.startColor = markerColor;
        marker.endColor = markerColor;

        for (int i = 0; i < MarkerSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / MarkerSegments;
            marker.SetPosition(i, new Vector3(Mathf.Cos(angle) * markerRadius, 0f, Mathf.Sin(angle) * markerRadius));
        }
    }
}
