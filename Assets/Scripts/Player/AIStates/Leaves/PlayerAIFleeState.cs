using UnityEngine;

public class PlayerAIFleeState : State<PlayerAIContext>
{
    private PlayerAIMoveGroupState parent;
    private float potionTimer;
    private float fleeTimer;

    public PlayerAIFleeState(PlayerAIContext context, PlayerAIMoveGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        potionTimer = 0f;
        fleeTimer = 0f;
        context.Play("Run");
    }

    public override void Update()
    {
        if (context.target == null) return;

        fleeTimer += Time.deltaTime;
        if (fleeTimer >= context.config.maxKiteTime)
        {
            parent.FinishFlee();
            return;
        }

        Vector3 away = context.transform.position - context.target.Position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f)
            away = -context.transform.forward;

        Vector3 fleeTarget = context.transform.position + away.normalized * context.config.fleeDistance;
        context.MoveTo(fleeTarget, context.RunSpeed);

        potionTimer += Time.deltaTime;
        if (potionTimer >= context.config.potionCooldown)
        {
            potionTimer = 0f;
            context.stats?.HealHp(context.PotionHealAmount);
            context.onPotion?.Invoke();
        }
    }

    public override void Exit()
    {
        potionTimer = 0f;
        fleeTimer = 0f;
    }
}
