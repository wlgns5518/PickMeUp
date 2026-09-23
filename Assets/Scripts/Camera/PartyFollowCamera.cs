using System.Collections.Generic;
using UnityEngine;

// 파티 전체의 중앙이 아니라 "지금 보고 있는 한 명"을 따라간다.
//
// 중앙값을 따라가면 아군이 흩어질수록 카메라가 아무도 없는 빈 땅을 비추게 되고,
// 정작 보고 싶은 캐릭터는 화면 끝에 걸린다. 전투 시작 시에는 첫 번째 아군을 잡고,
// 왼쪽 파티 UI를 누르면 그 캐릭터로 시점을 옮긴다. 보고 있던 아군이 쓰러지면
// 잠깐 그 자리를 비춘 뒤 가장 가까이 있는 살아 있는 아군에게 넘어간다.
public class PartyFollowCamera : MonoBehaviour
{
    [Header("Follow")]
    [SerializeField] private Vector3 offset = new Vector3(8.958223f, 5.990178f, -0.102579117f);
    [SerializeField] private float followSmoothTime = 1f;
    [SerializeField] private float axisMoveThreshold = 1f;

    [Header("Look At")]
    [SerializeField] private Vector3 lookRotationEuler = new Vector3(36.1318054f, 269.175232f, 0f);
    [SerializeField] private float rotationSpeed = 5f;

    [Header("Refocus")]
    [Tooltip("보고 있던 아군이 쓰러진 뒤 다른 아군으로 넘어가기까지 기다리는 시간(초). 쓰러지는 모습은 보여 준다.")]
    [SerializeField] private float refocusDelayAfterDeath = 1f;

    private Vector3 followVelocity;
    private Vector3 lastKnownPosition;
    private bool hasPosition;
    private Vector3 anchoredPosition;
    private bool hasAnchor;

    // 흔들림을 뺀 카메라 자세. 따라가기(SmoothDamp)는 이 값 위에서만 돈다.
    //
    // transform에 흔들림을 더한 채로 다음 프레임의 SmoothDamp가 그 값을 출발점으로 삼으면,
    // 흔들림이 따라가기 속도에 섞여 들어가 카메라가 충격 방향으로 흘러가 버린다.
    private Vector3 smoothedPosition;
    private Quaternion smoothedRotation;
    private bool hasSmoothedPose;

    private UnitController focusTarget;

    // 잡았을 때 살아 있던 대상만 쓰러지면 넘어간다. 쓰러진 동료의 슬롯을 일부러 눌러 본 경우엔 그대로 둔다.
    private bool focusedWhileAlive;
    // 보고 있던 아군이 쓰러진 것을 처음 본 시각. 쓰러지지 않았으면 음수.
    private float focusDownTime = -1f;

    // 지금 카메라가 잡고 있는 아군. UI가 어느 슬롯을 강조할지 판단할 때도 쓴다.
    public UnitController FocusTarget => focusTarget;

    private void Awake()
    {
        // 큰 한 방의 흔들림을 받을 자리. 씬마다 카메라에 붙여 두지 않아도 되게 여기서 챙긴다 —
        // 수치를 다듬고 싶으면 카메라에 CombatImpulse를 직접 붙이면 그 값이 쓰인다.
        if (GetComponent<CombatImpulse>() == null) gameObject.AddComponent<CombatImpulse>();
    }

    private void OnEnable()
    {
        hasSmoothedPose = false;
        BattleManager.OnBattleStarted += HandleBattleStarted;

        // 도메인 리로드나 늦은 활성화로 시작 이벤트를 놓쳤을 수 있다. 이미 전투 중이면 여기서 잡는다.
        if (BattleManager.Instance != null && BattleManager.Instance.IsRunning) HandleBattleStarted();
    }

    private void OnDisable()
    {
        BattleManager.OnBattleStarted -= HandleBattleStarted;
    }

    // 전투 시작 시점에는 파티 명단의 첫 번째 아군을 본다.
    // UI 슬롯도 같은 명단(AllyRoster) 순서로 만들어지므로 "맨 위 슬롯 = 시작 시점 카메라"가 된다.
    private void HandleBattleStarted()
    {
        if (focusTarget != null) return;

        BattleManager manager = BattleManager.Instance;
        if (manager == null) return;

        IReadOnlyList<UnitController> roster = manager.AllyRoster;
        for (int i = 0; i < roster.Count; i++)
        {
            if (roster[i] == null) continue;
            Focus(roster[i]);
            return;
        }
    }

    // 파티 UI 클릭으로 호출. 앵커를 즉시 새 대상에 맞춰야 임계값에 걸려 제자리에 머무는 일이 없다.
    // 위치 자체는 SmoothDamp로 따라가므로 화면은 순간이동하지 않고 부드럽게 흘러간다.
    public void Focus(UnitController unit)
    {
        if (unit == null) return;

        SetFocusTarget(unit);
        anchoredPosition = unit.transform.position;
        lastKnownPosition = anchoredPosition;
        hasAnchor = true;
        hasPosition = true;
    }

    private void SetFocusTarget(UnitController unit)
    {
        focusTarget = unit;
        focusedWhileAlive = !unit.IsDead;
        focusDownTime = -1f;
    }

    private void LateUpdate()
    {
        FollowSurvivorIfFocusDown();

        if (TryGetFocusPosition(out Vector3 position))
        {
            lastKnownPosition = position;
            hasPosition = true;
        }
        else if (!hasPosition)
        {
            return;
        }
        else
        {
            // 대상이 사라졌고 넘겨받을 아군도 없다(전멸). 마지막 자리를 그대로 비춘다.
            position = lastKnownPosition;
        }

        if (!hasAnchor)
        {
            anchoredPosition = position;
            hasAnchor = true;
        }
        else
        {
            Vector3 delta = position - anchoredPosition;
            if (Mathf.Abs(delta.x) >= axisMoveThreshold ||
                Mathf.Abs(delta.y) >= axisMoveThreshold ||
                Mathf.Abs(delta.z) >= axisMoveThreshold)
            {
                anchoredPosition = position;
            }
        }

        if (!hasSmoothedPose)
        {
            smoothedPosition = transform.position;
            smoothedRotation = transform.rotation;
            hasSmoothedPose = true;
        }

        Vector3 targetPosition = anchoredPosition + offset;
        smoothedPosition = Vector3.SmoothDamp(smoothedPosition, targetPosition, ref followVelocity, followSmoothTime);

        Quaternion targetRotation = Quaternion.Euler(lookRotationEuler);
        smoothedRotation = Quaternion.Slerp(smoothedRotation, targetRotation, rotationSpeed * Time.deltaTime);

        // 흔들림은 지금 보고 있는 캐릭터에게 일어난 한 방에서만 나온다(CombatImpulse.Emit이 거른다).
        if (CombatImpulse.TrySample(position, out Vector3 shakeOffset, out Quaternion shakeRotation))
        {
            transform.SetPositionAndRotation(smoothedPosition + shakeOffset, smoothedRotation * shakeRotation);
            return;
        }

        transform.SetPositionAndRotation(smoothedPosition, smoothedRotation);
    }

    private bool TryGetFocusPosition(out Vector3 position)
    {
        position = Vector3.zero;

        // 아직 아무도 잡지 못한 경우(전투 시작 전 등)에는 살아있는 아군 아무나 붙잡아 화면을 채운다.
        if (focusTarget == null && !TryFocusFirstLivingAlly()) return false;
        if (focusTarget == null) return false;

        position = focusTarget.transform.position;
        return true;
    }

    private bool TryFocusFirstLivingAlly()
    {
        IReadOnlyList<UnitController> allies = UnitRegistry.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            UnitController ally = allies[i];
            if (ally == null || ally.IsDead || !ally.isActiveAndEnabled) continue;

            SetFocusTarget(ally);
            return true;
        }
        return false;
    }

    // 보고 있던 아군이 쓰러지면 refocusDelayAfterDeath만큼 그 자리를 비추다가 가장 가까운 생존자로 넘어간다.
    // 명단 순서가 아니라 거리로 고르는 것은 화면이 가장 덜 흘러가게 하려는 것이다.
    // 살아 있는 아군이 없으면(전멸) 넘어가지 않고 마지막 자리에 머문다.
    private void FollowSurvivorIfFocusDown()
    {
        if (focusTarget == null || !focusedWhileAlive || !focusTarget.IsDead) return;

        if (focusDownTime < 0f) focusDownTime = Time.time;
        if (Time.time - focusDownTime < refocusDelayAfterDeath) return;

        UnitController survivor = FindNearestLivingAlly(focusTarget.transform.position);
        if (survivor != null) Focus(survivor);
    }

    private static UnitController FindNearestLivingAlly(Vector3 from)
    {
        UnitController nearest = null;
        float nearestSqr = float.MaxValue;

        IReadOnlyList<UnitController> allies = UnitRegistry.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            UnitController ally = allies[i];
            if (ally == null || ally.IsDead || !ally.isActiveAndEnabled) continue;

            float sqr = (ally.transform.position - from).sqrMagnitude;
            if (sqr >= nearestSqr) continue;

            nearest = ally;
            nearestSqr = sqr;
        }
        return nearest;
    }
}
