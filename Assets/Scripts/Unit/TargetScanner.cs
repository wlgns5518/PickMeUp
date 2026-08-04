using UnityEngine;

public class TargetScanner : MonoBehaviour
{
    [SerializeField] private float scanInterval = 0.2f;
    [SerializeField] private float viewAngle = 120f;
    [SerializeField] private float closeVisibleRange = 2f;
    [SerializeField] private float eyeHeight = 1.2f;
    [SerializeField] private LayerMask obstacleMask = ~0;

    private UnitController owner;
    private UnitController target;
    private float scanTimer;

    public UnitController Target => target;
    public float ViewAngle => viewAngle;

    private void Awake()
    {
        owner = GetComponent<UnitController>();
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
        if (scanTimer > 0f && IsCurrentTargetValid()) return;

        scanTimer = scanInterval;

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

        if (target != null)
        {
            UnitRegistry.AlertTeam(owner, target);
        }
    }

    public UnitController FindTargetNow()
    {
        if (owner == null) return null;

        scanTimer = scanInterval;
        target = UnitRegistry.FindNearestVisibleEnemy(owner, owner.Stats.detectRange, viewAngle, GetCloseVisibleRange(), eyeHeight, obstacleMask);
        if (target != null)
        {
            UnitRegistry.AlertTeam(owner, target);
        }

        return target;
    }

    public bool IsVisible(UnitController candidate)
    {
        if (owner == null || candidate == null) return false;
        return UnitRegistry.IsVisibleTo(owner, candidate, owner.Stats.detectRange, viewAngle, GetCloseVisibleRange());
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
