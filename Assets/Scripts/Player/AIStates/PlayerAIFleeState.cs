using UnityEngine;

public class PlayerAIFleeState : State<PlayerAIContext>
{
    private PlayerAIAliveState parent;
    private float potionTimer;

    public PlayerAIFleeState(PlayerAIContext context, PlayerAIAliveState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        potionTimer = 0f;
        context.Play("Run");
    }

    public override void Update()
    {
        if (context.target == null || context.IsSafeHp())
        {
            parent.GoToIdle();
            return;
        }

        Vector3 away = context.transform.position - context.target.Position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f)
            away = -context.transform.forward;

        Vector3 fleeTarget = context.transform.position + away.normalized * context.config.fleeDistance;
        context.MoveTo(fleeTarget, context.RunSpeed);                     // ← config → context

        potionTimer += Time.deltaTime;
        if (potionTimer >= context.config.potionCooldown)
        {
            potionTimer = 0f;
            context.stats?.HealHp(context.PotionHealAmount);             // ← config → context
            context.onPotion?.Invoke();
        }
    }

    public override void Exit()
    {
        potionTimer = 0f;
    }
}