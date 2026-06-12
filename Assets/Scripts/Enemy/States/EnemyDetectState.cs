using UnityEngine;

public class EnemyDetectState : EnemyStateBase
{
    private float timer;

    public override void OnEnter()
    {
        timer = 0f;
        StopMove();

        if (BB.Target != null)
        {
            Vector3 dir = BB.Target.position - Controller.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                Controller.transform.forward = dir.normalized;
        }

        Controller.PlayAnim("Idle");
    }

    public override void OnUpdate() => timer += Time.deltaTime;

    public override string OnTransition()
    {
        if (BB.IsDead) return "hit";
        if (BB.IsHit) return "hit";
        if (timer >= Config.detectDuration)
            return BB.IsTargetInAttackRange() ? "attack" : "run";
        return null;
    }
}