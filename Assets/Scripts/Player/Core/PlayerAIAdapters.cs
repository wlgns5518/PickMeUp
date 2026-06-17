// ============================================================
//  PlayerAIAdapters.cs
//  프로젝트에 맞게 수정해서 사용하세요.
//
//  포함 내용:
//  - PlayerStats        : IPlayerStats 구현 예시
//  - EnemyAdapter       : IEnemy 구현 예시 (Enemy GameObject에 부착)
//  - ProjectileAdapter  : IDangerousProjectile 구현 예시
//  - EnemyManager       : PlayerAIController에 적 목록을 주입하는 매니저 예시
// ============================================================
using System.Collections.Generic;
using UnityEngine;

// ════════════════════════════════════════════════════════
//  PlayerStats  —  IPlayerStats 구현 예시
//  (본인 프로젝트의 HP 시스템으로 교체 가능)
// ════════════════════════════════════════════════════════
public class PlayerStats : MonoBehaviour, IPlayerStats
{
    [SerializeField] private int _maxHp = 100;
    [SerializeField] private int _hp    = 100;

    public int Hp    => _hp;
    public int MaxHp => _maxHp;

    public void HealHp(int amount)
    {
        _hp = Mathf.Min(_hp + amount, _maxHp);
    }

    public void TakeDamage(int damage)
    {
        _hp = Mathf.Max(_hp - damage, 0);
    }
}

// ════════════════════════════════════════════════════════
//  EnemyAdapter  —  IEnemy 구현 예시
//  (본인 프로젝트의 Enemy 컴포넌트에 IEnemy를 직접 구현하거나
//   이 어댑터를 Enemy GameObject에 부착하세요)
// ════════════════════════════════════════════════════════
public class EnemyAdapter : MonoBehaviour, IEnemy
{
    [SerializeField] private int _hp    = 50;
    [SerializeField] private int _maxHp = 50;

    public GameObject  GameObject => gameObject;
    public Vector3     Position   => transform.position;
    public float       Hp         => _hp;
    public bool        IsDead     => _hp <= 0;

    public void TakeDamage(int damage)
    {
        _hp = Mathf.Max(_hp - damage, 0);
        if (IsDead) OnDeath();
    }

    private void OnDeath()
    {
        // 사망 처리 (애니메이션, 드롭 등)
        Destroy(gameObject, 1f);
    }
}


// ════════════════════════════════════════════════════════
//  EnemyManager  —  PlayerAIController에 적 목록 주입 예시
//  씬에 하나만 배치하세요.
// ════════════════════════════════════════════════════════
public class EnemyManager : MonoBehaviour
{
    [SerializeField] private PlayerAIController _playerAI;

    private readonly List<IEnemy>               _enemies     = new();
    private readonly List<IDangerousProjectile> _projectiles = new();

    private void Update()
    {
        // ── 적 목록 갱신 ──────────────────────────────
        _enemies.Clear();
        foreach (var e in FindObjectsByType<EnemyAIController>(FindObjectsSortMode.None))
            _enemies.Add(e);

        // 샘플 EnemyAdapter를 쓰는 씬 호환성 유지
        foreach (var e in FindObjectsByType<EnemyAdapter>(FindObjectsSortMode.None))
            _enemies.Add(e);

        // ── 투사체 목록 갱신 ──────────────────────────
        _projectiles.Clear();
        foreach (var p in FindObjectsByType<ProjectileAdapter>(FindObjectsSortMode.None))
            _projectiles.Add(p);

        // ── PlayerAIController에 주입 ─────────────────
        _playerAI.SetEnemies(_enemies);
        _playerAI.SetProjectiles(_projectiles);
    }

    // ── 적 등록/해제 (이벤트 방식, 선택) ──────────────
    public void RegisterEnemy(IEnemy e)     => _enemies.Add(e);
    public void UnregisterEnemy(IEnemy e)   => _enemies.Remove(e);
    public void RegisterProjectile(IDangerousProjectile p)   => _projectiles.Add(p);
    public void UnregisterProjectile(IDangerousProjectile p) => _projectiles.Remove(p);
}
