using UnityEngine;

public class TargetScanner : MonoBehaviour
{
    [SerializeField] private float scanInterval = 0.2f;
    [SerializeField] private float viewAngle = 120f;
    [SerializeField] private float closeVisibleRange = 2f;

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
            target = UnitRegistry.FindNearestVisibleEnemy(owner, owner.Stats.detectRange, viewAngle, GetCloseVisibleRange());
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
        target = UnitRegistry.FindNearestVisibleEnemy(owner, owner.Stats.detectRange, viewAngle, GetCloseVisibleRange());
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

    private float GetCloseVisibleRange()
    {
        if (owner == null) return closeVisibleRange;
        return Mathf.Max(closeVisibleRange, owner.Stats.attackRange + owner.Stats.moveStopDistance);
    }
}
