using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 씬에 존재하는 위험 투사체를 관리하는 싱글톤 레지스트리.
/// 투사체는 생성 시 Register, 소멸 시 Unregister.
/// </summary>
public class ProjectileRegistry : MonoBehaviour
{
    public static ProjectileRegistry Instance { get; private set; }

    private readonly List<IDangerousProjectile> _projectiles = new();

    public IReadOnlyList<IDangerousProjectile> Projectiles => _projectiles;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void Register(IDangerousProjectile projectile)
    {
        if (!_projectiles.Contains(projectile))
            _projectiles.Add(projectile);
    }

    public void Unregister(IDangerousProjectile projectile)
    {
        _projectiles.Remove(projectile);
    }
}
