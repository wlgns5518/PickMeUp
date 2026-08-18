using UnityEngine;

public class DeadState : UnitBattleState
{
    private float elapsed;
    private float targetDuration;
    private bool durationSynced;
    private bool finalized;

    public DeadState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();
        context.StopMovement();
        context.DisableCollider();
        context.TriggerDead();
        // 레지스트리에서 빠지기 전에 알려야 같은 팀 전원이 아직 리스트에 남아 있다.
        context.NotifyDeath();
        UnitRegistry.Unregister(context);
        context.DisableAgentAfterDeath();

        elapsed = 0f;
        targetDuration = context.DeathAnimationDuration; // 클립 이름 매칭 기반 추정치
        durationSynced = false;
        finalized = false;
    }

    // 사망 애니메이션이 끝날 때까지만 돌고, 끝나면 Animator와 컴포넌트를 함께 끈다.
    // 예전에는 Enter에서 컴포넌트만 껐기 때문에 Animator가 계속 살아남아
    // 시체가 쌓일수록 애니메이션 비용이 영구히 누적됐다(133구 기준 1.0ms).
    public override void Update()
    {
        if (finalized) return;

        elapsed += Time.deltaTime;

        // 전이가 끝나면 실제 재생 중인 상태 길이로 한 번 보정한다.
        if (!durationSynced && context.TryGetDeathStateLength(out float actualLength))
        {
            durationSynced = true;
            targetDuration = actualLength;
        }

        if (elapsed < targetDuration) return;

        finalized = true;
        context.FinalizeDeath();
    }
}
