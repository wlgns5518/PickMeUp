// 쓰러진 뒤. 트리 맨 위에 있으므로 무엇을 하고 있었든 여기로 온다.
public class DeadBehavior : UnitBehavior
{
    private float elapsed;
    private float targetDuration;
    private bool durationSynced;
    private bool finalized;

    public DeadBehavior(UnitController context) : base(context)
    {
    }

    public override bool AcceptsCombatRedirect => false;

    protected override void OnEnter()
    {
        // 죽는 순간 남아 있던 반응 요청(피격·경직)은 버린다. 시체가 뒤늦게 움찔할 이유가 없다.
        unit.ClearPendingReactions();

        unit.StopMovement();
        unit.DisableCollider();
        unit.TriggerDead();
        // 레지스트리에서 빠지기 전에 알려야 같은 팀 전원이 아직 리스트에 남아 있다.
        unit.NotifyDeath();
        UnitRegistry.Unregister(unit);
        unit.DisableAgentAfterDeath();

        elapsed = 0f;
        targetDuration = unit.DeathAnimationDuration; // 클립 이름 매칭 기반 추정치
        durationSynced = false;
        finalized = false;
    }

    // 사망 애니메이션이 끝날 때까지만 돌고, 끝나면 Animator와 컴포넌트를 함께 끈다.
    // 예전에는 진입할 때 컴포넌트만 껐기 때문에 Animator가 계속 살아남아
    // 시체가 쌓일수록 애니메이션 비용이 영구히 누적됐다(133구 기준 1.0ms).
    protected override BTStatus OnTick()
    {
        if (finalized) return BTStatus.Running;

        // 마지막 일격의 히트스톱이 아직 걸려 있을 수 있다. 실제 시간으로만 세면 쓰러지는
        // 모션이 절반쯤 남았는데 Animator를 꺼서 시체가 넘어지다 만 자세로 굳는다.
        elapsed += AnimationDeltaTime;

        // 전이가 끝나면 실제 재생 중인 상태 길이로 한 번 보정한다.
        if (!durationSynced && unit.TryGetDeathStateLength(out float actualLength))
        {
            durationSynced = true;
            targetDuration = actualLength;
        }

        if (elapsed < targetDuration) return BTStatus.Running;

        finalized = true;
        unit.FinalizeDeath();

        // 끝난 뒤에도 Running으로 남는다. Success를 돌려주면 트리가 아래 가지를 보러 내려가
        // 시체가 다시 적을 찾기 시작한다 — 죽음은 끝나지 않는 동작이다.
        return BTStatus.Running;
    }
}
