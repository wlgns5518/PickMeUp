using UnityEngine;

// 방패/무기를 들어 막고 있는 상태.
//
// 예전에는 blockDuration만큼 무조건 버텼다. 실제로는 "칼이 오니까 든다 / 지나갔으니 내린다"에
// 가깝고, 고정 시간으로 버티면 이미 지나간 공격에 대고 계속 방패를 든 채 서 있게 된다.
// 지금은 최대 시간은 blockDuration으로 두되, 노리던 위협이 사라지면 그전에 자세를 푼다.
public class BlockState : UnitBattleState
{
    private float stateTimer;
    private float minHoldTimer;

    // 자세를 잡자마자 위협 검사가 한 번 헛돌면(공격자가 그 프레임에 스윙을 끝냈다든가)
    // 방패를 들었다 내리는 것이 한 프레임 만에 끝나 깜빡이는 것처럼 보인다.
    // 최소한 이 시간은 들고 있는다 — 퍼펙트 가드 창(stats.perfectGuardWindow)보다 길어야
    // 흘려낼 기회 자체가 생긴다.
    private const float MinGuardHold = 0.3f;

    public BlockState(UnitController context) : base(context)
    {
    }

    // 자리를 지키고 받아내는 동작이다.
    public override bool HoldsGround => true;

    public override bool AcceptsCombatRedirect => false;

    public override void Enter()
    {
        base.Enter();
        // 상대를 보고 싸우는 동안은 회전을 코드가 잡는다(SetCodeDrivenFacing 주석 참조).
        context.SetCodeDrivenFacing(true);
        stateTimer = context.Stats.blockDuration;
        minHoldTimer = Mathf.Max(MinGuardHold, context.Stats.perfectGuardWindow);

        if (!context.HasUsableTarget())
        {
            context.ChangeState(context.SearchState);
            return;
        }

        context.StopMovement();
        context.SetBlocking(true);
        context.FaceBlockThreat();
    }

    public override void Update()
    {
        // 위협을 매 프레임 다시 찾는다. 진입 때 잡아 둔 적이 이미 스윙을 끝냈거나 쓰러졌는데
        // 계속 그쪽을 보고 서 있으면, 정작 옆에서 들어오는 공격을 정면 판정 밖에서 맞는다.
        bool stillThreatened = context.RefreshBlockThreat();
        context.FaceBlockThreat();

        minHoldTimer -= Time.deltaTime;

        // 막을 것이 아직 날아오는 동안은 시간을 깎지 않는다.
        //
        // blockDuration을 하드 상한으로 쓰면 여럿에게 둘러싸였을 때 그 시간이 다한 순간
        // 자세가 풀리고, 이어지던 다음 칼을 그대로 맞는다 — 재사용 대기가 0이라 곧바로 다시
        // 들 수는 있지만 그 사이 한 대가 들어간다. 규칙이 "막을 수 있는 공격은 전부 막는다"라면
        // 여기서 끊을 이유가 없다. 이 값은 마지막 칼이 지나간 뒤 자세를 내리기까지의 여유다.
        if (stillThreatened) stateTimer = context.Stats.blockDuration;
        else stateTimer -= Time.deltaTime;

        if (stateTimer <= 0f)
        {
            ReturnToCombat();
            return;
        }

        if (minHoldTimer > 0f) return;

        // 최소 시간을 채웠는데 나를 노리고 휘두르는 적이 더는 없다 — 계속 들고 있을 이유가 없다.
        // 오래 들고 있어 봐야 그동안 공격을 못 할 뿐이다.
        if (!stillThreatened) ReturnToCombat();
    }

    public override void Exit()
    {
        context.SetBlocking(false);
        base.Exit();
    }
}
