using UnityEngine;

public class PlayerAIHitState : State<PlayerAIContext>
{
    private PlayerAIAttackGroupState parent;
    private string currentClip;
    private bool hasHitAnimation;

    private static readonly string[] HitClips = { "Hit" };

    public PlayerAIHitState(PlayerAIContext context, PlayerAIAttackGroupState parent) : base(context)
    {
        this.parent = parent;
    }

    public override void Enter()
    {
        context.StopMoving();
        currentClip = HitClips[Random.Range(0, HitClips.Length)];
        hasHitAnimation = context.Play(currentClip, true);
    }

    public override void Update()
    {
        if (!hasHitAnimation)
        {
            parent.FinishHit();
            return;
        }

        if (!context.IsAnimationFinished(currentClip)) return;
        parent.FinishHit();
    }

    public override void Exit() { }
}
