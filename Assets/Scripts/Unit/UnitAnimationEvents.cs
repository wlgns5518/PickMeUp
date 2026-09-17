using System;
using UnityEngine;

// 공격 클립의 애니메이션 이벤트를 받아 C# 이벤트로 넘기는 자리. Animator와 같은 오브젝트에 붙는다.
//
// 애니메이션 이벤트는 함수 이름(문자열)으로 걸린다. 예전에는 그 이름이 곧 UnitController의
// 공개 메서드(ApplyAttackDamage)였다 — 클립 71개가 전투 로직의 메서드 이름을 직접 부르고 있었고,
// 이름을 바꾸거나 판정 순서를 손보는 순간 클립이 조용히 끊겼다(받는 쪽이 없으면 경고만 뜬다).
//
// 여기서 그 둘을 떼어 놓는다:
//  - 클립이 아는 것은 이 컴포넌트의 이름 둘뿐이다. 이름은 에셋에 박혀 있으므로 바꾸지 않는다.
//  - 전투 쪽은 C# 이벤트를 구독한다. "공격하기로 했다"(행동 트리의 AttackBehavior → TriggerAttack)와
//    "칼이 닿았다"(이 이벤트 → UnitController.ResolveAttackHit)가 서로의 존재를 모른다.
//
// SendMessage/BroadcastMessage는 쓰지 않는다. 구독은 Awake에서 한 번, 캐시해 둔 델리게이트로만 하므로
// 이벤트가 올 때마다 새로 할당되는 것이 없다.
[DisallowMultipleComponent]
public class UnitAnimationEvents : MonoBehaviour
{
    // 휘두른 칼(화살·발차기 포함)이 닿는 프레임.
    public event Action AttackHit;

    // 스킬 모션의 타격 프레임.
    public event Action SkillHit;

    // 클립 이벤트가 부르는 이름. 에셋(.anim)의 functionName과 글자 하나까지 같아야 한다.
    public void ApplyAttackDamage() => AttackHit?.Invoke();

    public void ApplySkillDamage() => SkillHit?.Invoke();
}
