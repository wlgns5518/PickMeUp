using UnityEngine;

// 마법사가 마력을 모으는 동안 머무는 상태.
//
// SkillState와 따로 둔 이유는 원작 설정 때문이다. 마법은 버튼 하나로 나가는 정해진 액티브가
// 아니라 연산하고 영창해 구현하는 현상이라, "스킬 하나를 재생한다"는 SkillState의 구조로는
// 담기지 않는다. 여기서는 무엇을 쓸지 먼저 정하고(SelectSpell), 그 마법이 요구하는 시간만큼
// 서서 마력을 모은 뒤에야 현상이 나간다(ExecuteSpell).
//
// 이 상태의 값어치는 "느리다"는 것 자체다. 서 있는 동안 받는 피해가 2.2배이고
// (UnitStats.castVulnerabilityMultiplier) 한 대만 맞아도 통째로 끊긴다. 그래서 마법사 혼자서는
// 큰 마법을 끝낼 수 없고, 탱커가 만든 방어선 안에서만 판을 끝내는 한 방이 나온다 —
// 원작에서 마법사가 파티에 묶여 있는 이유가 정확히 이것이다.
public class CastState : UnitBattleState
{
    private float stateTimer;

    public CastState(UnitController context) : base(context)
    {
    }

    public override void Enter()
    {
        base.Enter();

        // 무엇을 쓸지는 들어오는 순간에 정한다. 영창 도중에 다시 고르면
        // 상황이 바뀔 때마다 마법이 갈아 끼워져 영창이 영영 끝나지 않는다.
        SpellSpec spell;
        Vector3 aimPoint;
        if (!context.SelectSpell(out spell, out aimPoint))
        {
            // 들어올 때는 조건이 맞았는데 그 사이 사라졌다(적이 죽었거나 흩어졌다).
            ReturnToCombat();
            return;
        }

        // 영창 중에는 무방비다. 이동과 공격 잠금을 끊어야 이전 행동이 겹쳐 이어지지 않는다.
        context.InterruptCurrentAction();
        context.StopMovement();
        // 마력을 모으는 동안에도 겨눈 쪽을 본다. 회전은 코드가 잡는다.
        context.SetCodeDrivenFacing(true);

        context.BeginSpellCast(spell, aimPoint);
        stateTimer = context.CurrentCastDuration;
    }

    public override void Update()
    {
        // 영창 중에도 상대를 마주 본다. 착탄 지점은 이미 잠겨 있으므로 조준이 바뀌지는 않는다 —
        // 등을 보인 채 마력을 모으는 그림이 나오지 않게 하는 것이 목적이다.
        context.FaceTarget();

        // 히트스톱으로 애니메이션이 눌리면 이 타이머도 같이 느려져야 한다.
        stateTimer -= AnimationDeltaTime;
        if (stateTimer > 0f) return;

        // 끝까지 버텼다. 여기서 마력이 현상이 된다.
        context.ExecuteSpell();
        ReturnToCombat();
    }

    public override void Exit()
    {
        // 끝까지 못 갔으면(피격으로 끊겼거나 죽었으면) 여기로 온다.
        // ExecuteSpell이 이미 영창을 닫았으면 아무 일도 하지 않는다.
        context.CancelSpellCast();
        base.Exit();
    }
}
