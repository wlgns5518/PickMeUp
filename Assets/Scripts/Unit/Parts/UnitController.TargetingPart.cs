using UnityEngine;

public partial class UnitController
{
    // 누구를 노릴지를 맡는 부품: 표적을 잡고, 갈아타고, 놓는다. 어그로(맞았을 때 돌아설지)와
    // "나를 노리고 있는 적 수" 장부도 여기서 함께 관리한다 — 표적이 바뀌는 곳에서만 그 숫자가 움직이기 때문이다.
    private sealed class TargetingPart
    {
        private readonly UnitController u;

        // ------------------------------------------------------------------
        // 나를 노리고 있는 적 수
        //
        // "이 유닛에게 몇 명이 붙어 있는가"는 타깃을 고를 때마다 후보마다 필요하다(뭉치기·혼잡도).
        // 그때그때 팀 전체를 훑으면 유닛 수의 세제곱으로 커진다 — 67유닛이면 스캔 한 주기에 7만 번,
        // 유닛이 두 배면 여덟 배가 된다. 그래서 세지 않고 들고 있는다.
        //
        // 더하고 빼는 자리는 네 곳뿐이다: 타깃을 잡을 때(Assign), 놓을 때(Clear),
        // 전장에 들어올 때(Register), 나갈 때(Unregister — 죽거나 꺼지면 여기로 온다).
        // 그 넷 밖에서 CurrentTarget을 건드리면 숫자가 어긋나므로 대입은 Assign 한 곳으로 모아 둔다.
        // ------------------------------------------------------------------
        private readonly int[] attackersByTeam = new int[3];
        private bool countedOnTarget;
        private float lastChangeTime = -999f;

        public TargetingPart(UnitController owner)
        {
            u = owner;
        }

        // 지금 이 유닛을 노리고 있는 team 소속 유닛 수.
        public int AttackersFrom(UnitTeam team) => attackersByTeam[(int)team];

        public void AddAttacker(UnitTeam team, int delta)
        {
            int index = (int)team;
            attackersByTeam[index] = Mathf.Max(0, attackersByTeam[index] + delta);
        }

        // 레지스트리에 들고 날 때 호출된다. 죽은 유닛은 레지스트리에서 빠지므로 자연히 숫자에서도 빠진다.
        public void HoldCount()
        {
            if (countedOnTarget || !u.CurrentTarget.Exists) return;

            countedOnTarget = true;
            u.CurrentTarget.AddAttacker(u.team, 1);
        }

        public void ReleaseCount()
        {
            if (!countedOnTarget) return;

            countedOnTarget = false;
            // 대상이 이미 파괴됐으면 숫자도 그와 함께 사라진 것이라 뺄 곳이 없다.
            if (u.CurrentTarget.Exists) u.CurrentTarget.AddAttacker(u.team, -1);
        }

        // CurrentTarget에 값을 넣는 유일한 자리. 여기서만 넣어야 "붙어 있는 적 수"가 어긋나지 않는다.
        public void Assign(TargetRef target)
        {
            ReleaseCount();
            u.CurrentTarget = target;
            HoldCount();
            lastChangeTime = Time.time;
#if UNITY_EDITOR
            u.currentTargetName = target.Exists ? target.DebugName : "";
#endif
        }

        public void Clear()
        {
            ReleaseCount();
            u.CurrentTarget = TargetRef.None;
#if UNITY_EDITOR
            u.currentTargetName = "";
#endif
        }

        // ------------------------------------------------------------ 잡기·갈아타기

        public bool TrySet(TargetRef target)
        {
            if (target == u.CurrentTarget && u.IsTargetValid()) return true;
            if (!target.IsAlive || !IsHostileTo(target)) return false;
            if (IsChangeLocked()) return false;
            if (u.IsCommandBlockingRetarget(target)) return false;

            if (u.IsTargetValid())
            {
                if (!ShouldRelease() && !IsClearlyBetter(target)) return false;
                if (Time.time < lastChangeTime + u.targetChangeInterval) return false;
            }

            Assign(target);
            return true;
        }

        // 어그로 재평가(TargetScanner.ReviewAggro)가 고른 새 타깃으로 갈아탄다.
        //
        // TrySet과 나눠 둔 이유는 "더 나은가"를 재는 기준이 다르기 때문이다.
        // TrySet은 순수 거리로 25% 더 가까운지를 본다(IsClearlyBetter). 그 기준으로는
        // 바로 옆에 선 탱커로 영영 옮겨가지 못한다 — 거리가 같으니까. 여기 들어오는 후보는
        // 위협 가중치·혼잡도·유지 편향이 이미 다 반영된 결과라, 거리로 한 번 더 거르면
        // 편향이 통째로 무효가 된다.
        //
        // 대신 지켜야 할 것은 지킨다: 휘두르는 중이거나 영창 중에는 바꾸지 않고,
        // 최소 전환 간격(targetChangeInterval)도 그대로 건다.
        public bool TryRetarget(TargetRef target)
        {
            if (!target.Exists || target == u.CurrentTarget) return false;
            if (!target.IsAlive || !IsHostileTo(target)) return false;

            // 스윙 도중에 노리는 상대가 바뀌면 이미 나간 칼이 엉뚱한 곳을 향한다.
            // 스윙과 스윙 사이의 틈에서만 갈아탄다.
            //
            // 영창 중에는 막지 않는다. 광역 마법의 착탄 지점은 영창을 시작할 때 이미 잠겨 있고
            // (castingAimPoint) 단일 마법은 표적이 바뀌어도 그쪽으로 날아갈 뿐이라 깨지는 것이 없다.
            // 반대로 여기서 막으면 마법사는 거의 늘 영창 중이라 파티 집중 표적으로 영영 옮겨가지
            // 못한다 — 실측에서 마법사만 혼자 다른 적을 때리고 있던 원인이 이것이었다.
            if (u.IsAttackAnimationLocked) return false;
            if (Time.time < lastChangeTime + u.targetChangeInterval) return false;
            if (u.IsCommandBlockingRetarget(target)) return false;

            Assign(target);
            u.ClearMoveDestination();
            return true;
        }

        // 엔티티가 된 적 중에서 겨눌 상대를 찾는다.
        //
        // 스캐너(TargetScanner)는 게임오브젝트만 훑는다. 시야 레이캐스트와 팀 리스트 순회를
        // 전제로 만들어진 것이라 1000마리에 그대로 걸면 그것만으로 프레임이 끝나기 때문이다.
        // 그래서 엔티티 쪽은 브리지의 값 배열에서 고른다 — 거리와 이미 붙은 아군 수만 본다.
        public bool TryAcquireEntity()
        {
            if (u.team == UnitTeam.Enemy) return false;
            if (EnemyWorldBridge.EnemyCount == 0) return false;

            if (!EnemyWorldBridge.TryFindBestEnemy(u.transform.position, u.stats.detectRange,
                    out Unity.Entities.Entity enemy))
            {
                return false;
            }

            return TrySet(new TargetRef(enemy));
        }

        public bool ShouldRelease()
        {
            TargetRef current = u.CurrentTarget;
            return !current.Exists || !current.IsAlive;
        }

        public bool HasUsable()
        {
            if (u.IsTargetValid() && !ShouldRelease()) return true;

            Clear();
            return false;
        }

        public void ReceiveShared(TargetRef target)
        {
            if (u.IsDead || !target.Exists || !target.IsAlive || !IsHostileTo(target)) return;
            if (IsChangeLocked()) return;
            if (u.IsCommandBlockingRetarget(target)) return;

            bool accepted = HasUsable() ? TrySet(target) : ForceSetShared(target);
            if (!accepted) return;

            u.ClearMoveDestination();

            // 표적만 갈아 끼우고 하던 동작은 그대로 끝내는 것들이 있다
            // (UnitBehavior.AcceptsCombatRedirect). 대표적으로 도약 공격 — 이미 몸이 떠 있는데
            // 여기서 접어 버리면 LeapAttackBehavior가 내려놓기 전에 빠져나가 모델이 뜬 채로 남는다.
            UnitBehavior current = u.RunningBehavior;
            if (current != null && !current.AcceptsCombatRedirect) return;

            // 어디로 갈지는 지목하지 않는다. 접어 두면 다음 틱에 트리가 새 표적을 놓고
            // 처음부터 다시 고른다 — 사거리 안이면 공격, 아니면 추격이다.
            u.InterruptBehavior();
        }

        private bool ForceSetShared(TargetRef target)
        {
            if (!target.IsAlive || !IsHostileTo(target)) return false;

            Assign(target);
            return true;
        }

        // 저쪽이 때려도 되는 상대인가.
        //
        // 게임오브젝트끼리는 예전처럼 팀으로 가른다. 엔티티는 적 팀 전용이라 팀을 물어볼 것도 없이
        // "내가 적이 아니면 적"이다 — 고블린이 고블린을 때리는 경우는 만들지 않았다.
        public bool IsHostileTo(TargetRef target)
        {
            if (target.IsUnit) return UnitRegistry.AreEnemies(u, target.Unit);
            return target.IsEntity && u.team != UnitTeam.Enemy;
        }

        // ------------------------------------------------------------ 어그로

        // 맞았을 때 때린 쪽으로 돌아설지 정한다.
        //
        // 예전에는 한 대만 맞으면 무조건 그쪽으로 돌아섰다. 그 한 줄이 어그로 체계를 통째로
        // 무의미하게 만들고 있었다 — 탱커가 아무리 시선을 붙들어도 뒤에서 궁수가 한 발 쏘는
        // 순간 몬스터가 궁수에게 달려갔다. 원작에서 방어선이 하는 일이 정확히 그 반대다.
        //
        // 지금은 위협 가중치를 비교한다. 나를 붙들고 있는 상대보다 무겁게 치는 쪽이 때렸을 때만
        // 돌아선다. 그래서:
        //  - 탱커(3.2)와 맞붙은 몬스터는 궁수(0.4)의 화살이나 암살자(0.55)의 단검에 돌아서지 않는다.
        //    "공격 후 어그로를 탱커에게 넘기고 빠진다"가 여기서 성립한다.
        //  - 반대로 후방을 물고 있는 몬스터는 탱커가 한 대 치면 곧바로 탱커에게 끌려온다(도발).
        //  - 역할이 없는 유닛끼리는 가중치가 모두 1이라 항상 성립한다 — 예전 동작 그대로다.
        public void ReactToAttacker(TargetRef attacker)
        {
            if (!attacker.Exists || !attacker.IsAlive || !IsHostileTo(attacker)) return;
            if (attacker == u.CurrentTarget && u.IsTargetValid()) return;
            if (u.IsCommandBlockingRetarget(attacker)) return;
            if (!ShouldSwitchAggroTo(attacker)) return;

            Assign(attacker);
            u.ClearMoveDestination();
        }

        private bool ShouldSwitchAggroTo(TargetRef attacker)
        {
            // 붙들고 있는 상대가 없으면 때린 쪽을 본다. 판단할 다른 근거가 없다.
            if (!u.IsTargetValid()) return true;

            // 파티 집중 표적을 때리는 중이면 맞아도 흔들리지 않는다.
            //
            // 이게 없으면 집중 사격이 성립하지 않는다. 적은 전부 위협 가중치가 같아서(고블린 1.0)
            // 아래 비교가 늘 참이 되고, 딜러는 맞을 때마다 때린 적에게 끌려간다 —
            // 실측에서 창수가 집중 표적에 붙었다가 피격 한 번에 매번 다른 적으로 떨어져 나갔다.
            // 화력을 한 곳에 모으기로 한 이상, 맞는 것은 감수해야 하는 대가다(그러라고 탱커가 있다).
            // 다만 이미 누군가 내 간격 안까지 들어왔다면 집중을 고집하지 않는다.
            // 붙은 놈을 두고 먼 표적을 겨누는 것은 집중이 아니라 그냥 맞아 주는 것이다.
            if (u.stats.focusBonus > 0f &&
                u.CurrentTarget == UnitRegistry.GetFocusTarget(u.team) &&
                !u.ShouldKeepDistance())
            {
                return false;
            }

            return attacker.ThreatWeight >= u.CurrentTarget.ThreatWeight;
        }

        // 답은 동작이 들고 있다(UnitBehavior.LocksTarget). 어느 동작이 해당하는지는
        // 그 동작의 파일에 적혀 있다.
        private bool IsChangeLocked()
        {
            UnitBehavior current = u.RunningBehavior;
            return current != null && current.LocksTarget;
        }

        private bool IsClearlyBetter(TargetRef target)
        {
            if (!u.CurrentTarget.Exists || !target.Exists) return true;

            float ratio = u.targetSwitchDistanceRatio;
            float currentSqrDistance = u.SqrDistanceToTarget();
            float newSqrDistance = (target.Position - u.transform.position).sqrMagnitude;
            return newSqrDistance < currentSqrDistance * ratio * ratio;
        }
    }
}
