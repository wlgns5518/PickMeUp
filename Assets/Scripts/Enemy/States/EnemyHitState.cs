using UnityEngine;

public class EnemyHitState : EnemyStateBase
{
    private float timer;

    public override void OnEnter()
    {
        timer = 0f;
        StopMove();
        if (BB.IsDead)
            Controller.PlayAnimForce("Death");
        else
            Controller.PlayAnimForce("Hit");
    }

    public override void OnUpdate() => timer += Time.deltaTime;

    public override string OnTransition()
    {
        if (timer < Config.hitDuration) return null;
        if (BB.IsDead) return null;
        if (BB.IsTargetInDetectRange()) return BB.IsTargetInAttackRange() ? "attack" : "run";
        return "idle";
    }
}