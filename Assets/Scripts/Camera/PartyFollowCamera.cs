using System.Collections.Generic;
using UnityEngine;

// 파티 전체의 중앙이 아니라 "지금 보고 있는 한 명"을 따라간다.
//
// 중앙값을 따라가면 아군이 흩어질수록 카메라가 아무도 없는 빈 땅을 비추게 되고,
// 정작 보고 싶은 캐릭터는 화면 끝에 걸린다. 전투 시작 시에는 첫 번째 아군을 잡고,
// 왼쪽 파티 UI를 누르면 그 캐릭터로 시점을 옮긴다.
public class PartyFollowCamera : MonoBehaviour
{
    [Header("Follow")]
    [SerializeField] private Vector3 offset = new Vector3(8.958223f, 5.990178f, -0.102579117f);
    [SerializeField] private float followSmoothTime = 1f;
    [SerializeField] private float axisMoveThreshold = 1f;

    [Header("Look At")]
    [SerializeField] private Vector3 lookRotationEuler = new Vector3(36.1318054f, 269.175232f, 0f);
    [SerializeField] private float rotationSpeed = 5f;

    private Vector3 followVelocity;
    private Vector3 lastKnownPosition;
    private bool hasPosition;
    private Vector3 anchoredPosition;
    private bool hasAnchor;

    private UnitController focusTarget;

    // 지금 카메라가 잡고 있는 아군. UI가 어느 슬롯을 강조할지 판단할 때도 쓴다.
    public UnitController FocusTarget => focusTarget;

    private void OnEnable()
    {
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

        focusTarget = unit;
        anchoredPosition = unit.transform.position;
        lastKnownPosition = anchoredPosition;
        hasAnchor = true;
        hasPosition = true;
    }

    private void LateUpdate()
    {
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
            // 보고 있던 캐릭터가 쓰러져도 시점을 빼앗지 않는다. 마지막 자리를 그대로 비춘다.
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

        Vector3 targetPosition = anchoredPosition + offset;
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref followVelocity, followSmoothTime);

        Quaternion targetRotation = Quaternion.Euler(lookRotationEuler);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
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

            focusTarget = ally;
            return true;
        }
        return false;
    }
}
