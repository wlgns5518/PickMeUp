using UnityEngine;

public partial class UnitController
{
    // 자세(강인도·경직)를 맡는 부품. 강인도가 깨지는 것, 자세가 통째로 무너지는 것(경직),
    // 그리고 무한 경직을 막는 면역 시간이 여기 한곳에 있다.
    private sealed class PosturePart
    {
        private readonly UnitController u;

        private float staggerEndTime;
        private float pendingDuration;
        // 강인도가 깨진(또는 붙잡혀 무너진) 뒤 한동안은 다시 깨지지 않는다.
        private float poiseImmuneUntil;

        public PosturePart(UnitController owner)
        {
            u = owner;
        }

        public bool IsStaggered => Time.time < staggerEndTime;
        public float PendingDuration => pendingDuration;

        // InterruptCurrentAction이 IsBlocking을 내려버리므로, 어떤 이유로 무너졌는지는
        // 미리 여기 남겨 둬야 한다. StaggerBehavior가 이 값으로 모션을 고른다.
        public bool FromGuardBreak { get; private set; }

        // 자세가 완전히 무너진다. 가드 브레이크와, 퍼펙트 가드에 흘려진 공격자가 여기로 들어온다.
        // 대부분 다른 유닛이 이 유닛에게 거는 경로라(막은 쪽이 때린 쪽을 무너뜨린다)
        // 실제 동작 전환은 표시만 세워 두고 다음 틱의 트리에 맡긴다.
        public void Stagger(float duration, bool fromGuardBreak)
        {
            if (u.IsDead || duration <= 0f) return;

            pendingDuration = duration;
            staggerEndTime = Time.time + duration;
            FromGuardBreak = fromGuardBreak;
            u.InterruptCurrentAction();
            u.RequestStagger();
        }

        // 붙잡아 무너뜨리는 공격이 부른다(고블린의 무는 공격). 강인도를 깎아 놓고 깨지기를
        // 기다리는 것이 아니라, 맞은 그 자리에서 자세를 무너뜨린다.
        //
        // 그래도 무한 경직 규칙은 그대로 지킨다. 강인도가 깨졌을 때와 똑같이 면역 시간을 켜므로,
        // 다섯 마리가 번갈아 물어도 한 번 물린 뒤 몇 초는 제 발로 서 있게 된다 — 이 검사가
        // 없으면 고블린 수가 곧 경직 시간이 되어, 무리에 둘러싸인 순간 아무것도 못 하고 죽는다.
        //
        // 돌려주는 값은 "실제로 무너뜨렸는가". 면역 중이었으면 아무 일도 없다.
        public bool TryForce(float duration)
        {
            if (u.IsDead || duration <= 0f) return false;
            if (Time.time < poiseImmuneUntil) return false;

            // 강인도가 깨진 것과 같은 처리를 한다. 무너진 채로 강인도만 가득 남아 있으면
            // 일어나자마자 또 한 번 버틸 수 있게 되어, 무너뜨린 쪽이 손해를 본다.
            BreakPoise();
            Stagger(duration, false);
            return true;
        }

        // 강인도: 면역 중이 아니면 이번 피격으로 깎는다. 깨졌으면 true.
        //
        // 막아낸 타격도 그대로 깎는다는 것이 핵심이다. 피해는 0이지만 버티는 힘은 닳으므로,
        // 방어가 공짜 무적이 되지 않는다 — 예전에는 이 역할을 방어 지구력(Block Stamina)이라는
        // 별도 자원이 맡았는데, 강인도와 하는 일이 같아서 하나로 합쳤다.
        public bool TryDamagePoise(float poiseDamage)
        {
            if (Time.time < poiseImmuneUntil || poiseDamage <= 0f) return false;

            UnitStats stats = u.stats;
            stats.currentPoise -= poiseDamage;
            if (stats.currentPoise > 0f) return false;

            BreakPoise();
            return true;
        }

        public void Reset()
        {
            staggerEndTime = 0f;
            pendingDuration = 0f;
            poiseImmuneUntil = 0f;
        }

        private void BreakPoise()
        {
            u.stats.ResetPoise();
            poiseImmuneUntil = Time.time + u.stats.poiseBreakImmunity;
        }
    }
}
