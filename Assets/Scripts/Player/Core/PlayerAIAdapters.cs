using UnityEngine;

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

public class EnemyAdapter : MonoBehaviour, IEnemy
{
    [SerializeField] private int _hp    = 50;
    [SerializeField] private int _maxHp = 50;

    public GameObject GameObject => gameObject;
    public Vector3    Position   => transform.position;
    public float      Hp         => _hp;
    public bool       IsDead     => _hp <= 0;

    public void TakeDamage(int damage)
    {
        _hp = Mathf.Max(_hp - damage, 0);
        if (IsDead) OnDeath();
    }

    private void Awake()
    {
        EnemyRegistry.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        EnemyRegistry.Instance?.Unregister(this);
    }

    private void OnDeath()
    {
        EnemyRegistry.Instance?.Unregister(this);
        Destroy(gameObject, 1f);
    }
}
