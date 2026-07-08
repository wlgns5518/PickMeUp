using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 씬에 존재하는 적을 관리하는 싱글톤 레지스트리.
/// 적은 Awake에서 Register, 사망 시 Unregister.
/// </summary>
public class EnemyRegistry : MonoBehaviour
{
    public static EnemyRegistry Instance { get; private set; }

    private readonly List<IEnemy> _enemies = new();

    // 읽기 전용으로 외부 노출
    public IReadOnlyList<IEnemy> Enemies => _enemies;
    public int Version { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void Register(IEnemy enemy)
    {
        if (_enemies.Contains(enemy)) return;

        _enemies.Add(enemy);
        Version++;
    }

    public void Unregister(IEnemy enemy)
    {
        if (_enemies.Remove(enemy))
            Version++;
    }
}
