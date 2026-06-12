using UnityEngine;

public class EnemyWalkState : EnemyStateBase
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

        MoveDirect(dir.normalized, Config.walkSpeed);
    }

    public override string OnTransition()
    {
        if (BB.IsDead) return "hit";
        if (BB.IsHit) return "hit";
        if (!BB.IsTargetInDetectRange()) return "idle";
        if (BB.IsTargetInAttackRange()) return "attack";

        if (BB.Target != null)
        {
            float dist = Vector3.Distance(Controller.transform.position, BB.Target.position);
            if (dist > Config.detectRange * 0.6f) return "run";
        }

        return null;
    }
}