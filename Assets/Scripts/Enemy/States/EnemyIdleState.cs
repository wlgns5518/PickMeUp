using UnityEngine;

public class EnemyIdleState : EnemyStateBase
{
    private float timer;

    public override void OnEnter()
    {
        timer = 0f;
        StopMove();
        Controller.PlayAnim("Idle");
    }

    public override void OnUpdate() => timer += Time.deltaTime;

    public override string OnTransition()
    {
        if (BB.IsDead) return "hit";
        if (BB.IsHit) return "hit";
        if (BB.IsTargetInDetectRange()) return "detect";
        if (timer >= Config.patrolWaitTime) return "patrol";
        return null;
    }
}