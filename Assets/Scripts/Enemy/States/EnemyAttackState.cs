using UnityEngine;

public class EnemyAttackState : EnemyStateBase
{
    private float minMotionEndTime;

    public override void OnEnter()
    {
        minMotionEndTime = BB.Time + Config.attackMotionDuration;
        BB.LastAttackTime = BB.Time;
        StopMove();

        if (BB.Target != null)
        {
            Vector3 dir = BB.Target.position - Controller.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                Controller.transform.forward = dir.normalized;
        }

        Controller.PlayAnimForce("LightAttack");
        Controller.OnAttack(BB.Target);
    }

    public override void OnUpdate()
    {
    }

    public override string OnTransition()
    {
        if (BB.Time < minMotionEndTime) return null;
        if (!Controller.IsCurrentAnimationFinished("LightAttack")) return null;
        if (BB.IsDead) return "hit";
        if (BB.IsHit) return "hit";
        if (!BB.IsTargetInDetectRange()) return "idle";
        if (BB.IsTargetInAttackRange() && BB.CanAttack()) return "attack";
        return "run";
    }
}