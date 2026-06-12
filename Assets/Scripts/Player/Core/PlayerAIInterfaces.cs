using UnityEngine;

public interface IPlayerStats
{
    int Hp { get; }
    int MaxHp { get; }
    void HealHp(int amount);
}

public interface IEnemy
{
    GameObject GameObject { get; }
    Vector3 Position { get; }
    float Hp { get; }
    bool IsDead { get; }
}

public interface IDangerousProjectile
{
    Vector3 Position { get; }
    GameObject TargetObject { get; }
}
