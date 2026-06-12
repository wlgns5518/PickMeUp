using UnityEngine;

public class PlayerAIHitState : State<PlayerAIContext>
{
    private PlayerAIAttackGroupState parent;
    private string currentClip;

    private static readonly string[] HitClips = { "Hit1", "Hit2" };

    public PlayerAIHitState(PlayerAIContext context, PlayerAIAttackGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        currentClip = HitClips[Random.Range(0, HitClips.Length)];
        context.Play(currentClip, true);
    }

    public override void Update()
    {
        if (!context.IsAnimationFinished(currentClip)) return;
        parent.GoToAttack();
    }

    public override void Exit() { }
}
