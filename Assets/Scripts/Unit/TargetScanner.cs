using UnityEngine;

public class TargetScanner : MonoBehaviour
{
    [SerializeField] private float scanInterval = 0.2f;
    [SerializeField] private float viewAngle = 120f;
    [SerializeField] private float closeVisibleRange = 2f;
    [SerializeField] private float eyeHeight = 1.2f;
    [SerializeField] private LayerMask obstacleMask = ~0;
    [Tooltip("같은 타깃을 팀에 다시 공유하는 최소 간격. 타깃이 바뀌면 즉시 공유한다.")]
    [SerializeField] private float alertInterval = 1f;

    private UnitController owner;
    private UnitController target;
    private float scanTimer;
    private UnitController lastAlertedTarget;
    private float nextAlertTime;

    public UnitController Target => target;
    public float ViewAngle => viewAngle;

    private void Awake()
    {
        owner = GetComponent<UnitController>();
        ScatterSchedule();
    }

    // 주기 작업의 위상을 유닛마다 흩어 놓는다.
    // 그러지 않으면 모든 유닛이 같은 프레임에 전면 탐색(레이캐스트 포함)과 팀 전파를 동시에 돌린다.
    // 실측(67유닛): 전 유닛의 scanTimer가 0.0205로 완전히 같았고,
    // 그 프레임에 전면 탐색 0.40ms + 팀 전파 0.37ms = 0.77ms가 한꺼번에 몰렸다.
    // 둘 다 유닛 수의 제곱으로 커지는 작업이라 유닛이 늘수록 스파이크가 급격히 나빠진다.
    private void ScatterSchedule()
    {
        scanTimer = Random.Range(0f, scanInterval);
        nextAlertTime = Time.time + Random.Range(0f, alertInterval);
    }

    // 한 번 흩어놔도 여러 유닛이 같은 프레임에 스캔을 마치면 다시 위상이 붙는다.
    // 재설정할 때마다 약간의 흔들림을 줘서 다시 뭉치지 않게 한다.
    private float NextScanDelay()
    {
        return scanInterval * Random.Range(0.85f, 1.15f);
    }

    public void Initialize(UnitController owner)
    {
        this.owner = owner;
    }

    public void Tick()
    {
        if (owner == null || owner.IsDead)
        {
            target = null;
            return;
        }

        scanTimer -= Time.deltaTime;
        if (scanTimer > 0f)
        {
            // 주기 사이에는 죽음/비활성화처럼 값싼 조건만 확인한다.
            // 시야·거리·레이캐스트를 포함한 전체 탐색은 아래 스캔 주기에서만 수행 —
            // 타깃이 없다고 매 프레임 전체 유닛을 훑으면 유닛 수의 제곱으로 비용이 커진다.
            if (target != null && (target.IsDead || !target.isActiveAndEnabled)) target = null;
            return;
        }

        scanTimer = NextScanDelay();

        if (!IsCurrentTargetValid())
        {
            target = null;
        }
        // Line-of-sight is a raycast, so it's only re-checked here at the scan cadence
        // rather than every frame in IsCurrentTargetValid.
        else if (!HasLineOfSight(target))
        {
            target = null;
        }

        if (target == null)
        {
            target = UnitRegistry.FindNearestVisibleEnemy(owner, owner.Stats.detectRange, viewAngle, GetCloseVisibleRange(), eyeHeight, obstacleMask);
        }

        AlertTeamIfNeeded();
    }

    public UnitController FindTargetNow()
    {
        if (owner == null) return null;

        scanTimer = NextScanDelay();
        target = UnitRegistry.FindNearestVisibleEnemy(owner, owner.Stats.detectRange, viewAngle, GetCloseVisibleRange(), eyeHeight, obstacleMask);
        AlertTeamIfNeeded();

        return target;
    }

    public bool IsVisible(UnitController candidate)
    {
        if (owner == null || candidate == null) return false;
        return UnitRegistry.IsVisibleTo(owner, candidate, owner.Stats.detectRange, viewAngle, GetCloseVisibleRange());
    }

    // AlertTeam은 팀 전원을 순회하므로 스캔마다 부르면 유닛 수의 제곱만큼 비용이 든다.
    // 새 타깃을 처음 발견했을 때는 즉시 알리고, 같은 타깃 재공유는 alertInterval로 제한한다.
    private void AlertTeamIfNeeded()
    {
        if (target == null)
        {
            lastAlertedTarget = null;
            return;
        }

        if (target == lastAlertedTarget && Time.time < nextAlertTime) return;

        lastAlertedTarget = target;
        nextAlertTime = Time.time + alertInterval;
        UnitRegistry.AlertTeam(owner, target);
    }

    private bool IsCurrentTargetValid()
    {
        if (target == null || target.IsDead) return false;
        return IsVisible(target);
    }

    private bool HasLineOfSight(UnitController candidate)
    {
        if (owner == null || candidate == null) return false;
        return UnitRegistry.HasLineOfSight(owner.transform.position, candidate.transform.position, eyeHeight, obstacleMask);
    }

    private float GetCloseVisibleRange()
    {
        if (owner == null) return closeVisibleRange;
        return Mathf.Max(closeVisibleRange, owner.Stats.attackRange + owner.Stats.moveStopDistance);
    }
}
