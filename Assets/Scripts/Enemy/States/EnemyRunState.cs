using UnityEngine;

public class EnemyRunState : EnemyStateBase
{
    public override void OnEnter()
    {
        Controller.PlayAnim("Run");
    }

    public override void OnUpdate()
    {
        if (BB.Target == null || BB.IsHit) return;

        Vector3 dir = BB.Target.position - Controller.transform.position;
        dir.y = 0f;

        if (BB.StuckTimer > Config.stuckThreshold)
            dir = Rotate(dir.normalized, Mathf.PI * 0.5f);

        MoveDirect(dir.normalized, Config.runSpeed);
    }

    public override string OnTransition()
    {
        if (BB.IsDead) return "hit";
        if (BB.IsHit) return "hit";
        if (!BB.IsTargetInDetectRange()) return "idle";
        if (BB.IsTargetInAttackRange()) return "attack";
        return null;
    }
}