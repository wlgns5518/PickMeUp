using UnityEngine;

/// <summary>
/// 위험 투사체 GameObject에 부착.
/// 생성 시 ProjectileRegistry에 자동 등록, 소멸 시 자동 해제.
/// </summary>
public class ProjectileAdapter : MonoBehaviour, IDangerousProjectile
{
    [Tooltip("이 투사체가 향하는 타겟 GameObject")]
    [SerializeField] private GameObject _targetObject;

    public Vector3    Position     => transform.position;
    public GameObject TargetObject => _targetObject;

    public void SetTarget(GameObject target) => _targetObject = target;

    private void Awake()
    {
        ProjectileRegistry.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        ProjectileRegistry.Instance?.Unregister(this);
    }
}
