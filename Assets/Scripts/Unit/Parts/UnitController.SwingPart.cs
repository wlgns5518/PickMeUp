using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

public partial class UnitController
{
    // 기본공격(스윙) 한 번이 어떻게 굴러가는지를 맡는 부품.
    //
    //   1) 무엇으로 칠지  — 콤보 순환이 기본이고, 상대가 방패를 올렸거나 무너지기 직전이면 발차기,
    //                       무너져 있으면 콤보 마지막 단으로 건너뛴다(Choose).
    //   2) 스윙 시계      — 준비 / 타격 / 회수를 나눈다. 예전에는 클립 전체가 통째로 "공격 중"이라
    //                       이미 때리고 칼을 거두는 적도 "휘두르는 중"으로 잡혀 방어가 헛돌았다.
    //   3) 타격 판정      — 애니메이션 이벤트 시점에 거리와 각도를 다시 본다. 예전에는 타깃이
    //                       어디로 도망쳤든 무조건 맞았다(빗나감이라는 것이 없었다).
    //   4) 파고들기       — 준비 동작 동안 교전 간격까지만 반 발 들어간다.
    private sealed class SwingPart
    {
        // 한 방의 무게(히트스톱 시간·밀림 배율). 평타가 1이다.
        private const float FinisherImpactWeight = 1.4f;
        private const float KickImpactWeight = 1.3f;
        private const float ShieldBashImpactWeight = 1.8f;

        // 화면 흔들림 세기(0~1). CombatImpulse 주석의 "판을 가르는 순간"만 흔든다.
        private const float ShieldBashShake = 0.35f;
        private const float SkillHitShake = 0.5f;

        // 내지른 뒤 칼을 거두는 동작을 끊고 방어로 넘어가기 전에 남겨 둘 시간(초).
        // 타격 프레임 직후 곧바로 끊으면 칼이 닿기도 전에 방패가 올라간 것처럼 보인다.
        private const float GuardFollowThrough = 0.12f;

        private readonly UnitController u;

        private int[] comboHashes;
        private float[] comboDurations;
        private int comboIndex;
        private int kickHash;
        private float kickDuration;

        // 공격 잠금이 풀리는 시각과, 다음 스윙까지의 호흡이 끝나는 시각.
        private float lockedUntil;
        private float nextReadyTime;

        // 이번 스윙의 타격 이벤트가 이미 지나갔는가. 준비 동작(아직 안 지나감)과 회수 동작(지나감)을
        // 가르는 유일한 근거다. 클립마다 이벤트 시각이 다르므로 시간으로 추정하지 않고 실제 이벤트로 안다.
        private bool hasStruck;
        // 준비 동작에서 거둔 스윙. 섞여 나가던 클립의 타격 이벤트가 뒤늦게 와도 무시한다.
        private bool cancelled;
        // 이번 스윙이 상대에게 닿은(또는 헛친) 시각. 회수 동작을 언제부터 끊을 수 있는지 잰다.
        private float lastStrikeTime = -999f;
        private float lungeRemaining;

        // 이번 스윙이 발차기인가 / 콤보 마무리인가. 피해와 강인도 계산이 갈린다.
        private bool pendingIsKick;
        private bool pendingIsFinisher;

        // 지난번에 빠진 뒤로 실제로 한 번이라도 휘둘렀는가(UnitController.ShouldStalk 주석 참조).
        private bool hasSwungSinceStalk = true;

        public SwingPart(UnitController owner)
        {
            u = owner;
        }

        // 공격 모션이 하나라도 있는가. 없으면 CanAttack이 false가 되어 공격 상태로 들어가지 않는다.
        public bool HasAnimation { get; private set; }

        // 이 유닛이 한 번이라도 공격을 휘둘렀는가. 스킬을 여는 수로 쓰지 않기 위한 것이다
        // (CanUseSkill, IsComboRecoveryPoint 주석 참조).
        public bool HasSwungAtLeastOnce { get; private set; }

        public bool HasSwungSinceStalk => hasSwungSinceStalk;

        public bool IsLocked => Time.time < lockedUntil;

        // 예전에는 "공격 애니메이션이 재생 중"이면 전부 휘두르는 중으로 봤다. 도끼처럼 1.7초짜리
        // 클립은 절반 이상이 칼을 거두는 동작이라, 방어자가 이미 지나간 공격에 대고 방패를 들었다.
        public bool IsTelegraphing => IsLocked && !hasStruck;

        // 내지른 직후. 다음 동작으로 넘어가지도 못하고 막지도 못하는 구간이라 반격 기회가 된다.
        public bool IsInRecovery => IsLocked && hasStruck;

        // 스윙과 스윙 사이의 호흡이 끝났는가.
        public bool IsReady => Time.time >= nextReadyTime;

        // 콤보가 한 바퀴를 다 돌아 다음 스윙이 다시 1번부터 시작하는 시점.
        public bool IsComboRecoveryPoint => HasSwungAtLeastOnce && comboIndex == 0;

        // 휘두르던 것을 거두고 막을 수 있는가(UnitController.CanCancelSwingIntoGuard 주석 참조).
        public bool CanCancelIntoGuard =>
            IsTelegraphing ||
            (IsInRecovery && Time.time >= lastStrikeTime + GuardFollowThrough);

        // 스윙이 실제로 닿는 거리. 사거리에 정지 거리와 여유를 더한 값으로,
        // IsTargetInAttackRange(공격을 시작할지 판단하는 쪽)보다 attackHitTolerance만큼 넓다.
        // 서로 조금씩 움직이는 중이라 시작 조건과 명중 조건이 똑같으면 정상적인 교전에서도
        // 헛스윙만 나온다.
        public float Reach => u.stats.attackRange + u.stats.moveStopDistance + u.stats.attackHitTolerance;

        // ------------------------------------------------------------ 클립

        // 무기를 갈아 끼우면 Attack1~N 클립이 다른 Override Controller로 바뀐다. 상태 이름(해시)은
        // 그대로지만 클립 길이는 무기마다 다르므로, WeaponEquipper.WeaponAnimatorChanged를 받을 때마다
        // 다시 재 보아야 잠금 시각이 실제 재생 시간과 어긋나지 않는다.
        public void CacheCombo()
        {
            // 무기마다 원본 팩이 주는 공격 클립 수가 다르다(단검 3 ~ 양손검 11).
            // 무기 컨트롤러가 물려 있으면 그 개수를 그대로 쓰고, 없으면(맨손이거나 이 리그가
            // 무기 컨트롤러를 안 쓰는 유닛) 프리팹에 적힌 기본값을 쓴다.
            WeaponEquipper equipment = u.equipment;
            int steps = equipment != null && equipment.WeaponAttackCount > 0
                ? equipment.WeaponAttackCount
                : Mathf.Max(1, u.attackComboLength);

            if (comboHashes == null || comboHashes.Length != steps)
            {
                comboHashes = new int[steps];
                comboDurations = new float[steps];
            }

            HasAnimation = false;
            for (int i = 0; i < steps; i++)
            {
                string name = steps > 1 ? u.attackStateName + (i + 1) : u.attackStateName;
                // 실제로 애니메이터에 있는 단계만 남긴다. 무기가 선언한 콤보 수보다 컨트롤러의
                // 상태가 적으면 존재하지 않는 상태로 CrossFade해서 그 단계에서 유닛이 굳는다.
                comboHashes[i] = u.ResolveStateHash(name);
                comboDurations[i] = u.GetAnimationClipDuration(name, 1f);
                if (comboHashes[i] != 0) HasAnimation = true;
            }

            comboIndex = 0;
            HasSwungAtLeastOnce = false;
        }

        public void CacheKick()
        {
            kickHash = u.ResolveStateHash(u.kickStateName);
            kickDuration = u.GetAnimationClipDuration(u.kickStateName, 0.8f);
        }

        // ------------------------------------------------------------ 휘두르기

        public void Trigger()
        {
            UnitStats stats = u.stats;

            // 무엇으로 칠지는 지금 상황이 정한다. 콤보 순환은 그중 기본값일 뿐이다(Choose).
            Choice choice = Choose();

            pendingIsKick = choice.IsKick;
            lockedUntil = Time.time + choice.Duration;

            // 콤보 마무리 보너스는 "여러 단을 끝까지 이어붙인 것"에 대한 보상이다.
            // 공격 모션이 하나뿐인 유닛(고블린)은 이어붙일 콤보가 없으므로 해당 없다.
            pendingIsFinisher = choice.IsFinisher;

            // 이번 스윙은 아직 아무도 때리지 않았다. 이 플래그가 준비 동작(막을 수 있는 구간)과
            // 회수 동작(무방비 구간)을 가른다 — 클립 길이로 추정하지 않고 타격 이벤트로 안다.
            hasStruck = false;
            cancelled = false;
            HasSwungAtLeastOnce = true;

            // 물러난 뒤 한 발은 쐈다는 표시. 원거리 유닛이 Attack↔Evade만 오가는 것을 막는다.
            u.MarkAttackedSinceEvade();

            // 이번 빠지기 뒤로 실제로 휘둘렀다는 표시. 이게 없으면 빠지기가 무한 루프가 된다.
            hasSwungSinceStalk = true;

            // 칼을 뽑는 순간 모습이 드러난다. 치고 빠지는 리듬이 여기서 시작된다.
            u.BreakStealth();

            // 시위를 당기기 시작한다. 앞선 화살이 떠나면서 감춰 둔 화살을 다시 물린다.
            if (u.equipment != null) u.equipment.BeginDraw();

            lungeRemaining = stats.lungeMaxDistance;

            // 다음 스윙까지의 호흡. 콤보를 완주한 뒤에는 길게 숨을 고르고, 그 밖에는 짧게 끊는다.
            float recovery = pendingIsFinisher ? stats.comboFinisherRecoveryTime : stats.attackRecoveryTime;
            recovery *= 1f + Random.Range(-stats.attackRecoveryRandomness, stats.attackRecoveryRandomness);
            recovery = Mathf.Max(0f, recovery);
            nextReadyTime = lockedUntil + recovery;

            // 이번 틈에 자리를 옮길지 여기서 한 번만 정한다(FootworkPart.PlanGap 주석 참조).
            u.Footwork.PlanGap(recovery);

            u.PlayAnimation(choice.Hash, true);
            comboIndex = choice.NextComboIndex;
        }

        // 도약도 스윙이다. 준비/회수 구분과 전역 전이(회복약·치료)의 "휘두르는 중에는
        // 끊지 않는다"가 전부 이 잠금에 걸려 있으므로 평타와 똑같이 걸어 둔다.
        public void BeginLeap(float duration)
        {
            lockedUntil = Time.time + duration;
            hasStruck = false;
            HasSwungAtLeastOnce = true;
            pendingIsKick = false;
            // 덤벼드는 한 수는 콤보 마무리와 같은 무게로 친다 — 강인도를 크게 깎아
            // 붙자마자 이어지는 콤보가 통째로 들어갈 자리를 만든다.
            pendingIsFinisher = true;
            // 도약 중에는 파고들지 않는다. 도약 자체가 파고드는 동작이다.
            lungeRemaining = 0f;
            u.MarkAttackedSinceEvade();
        }

        public void MarkStalkStarted() => hasSwungSinceStalk = false;

        // 이번 스윙에 낼 것 하나.
        private struct Choice
        {
            public int Hash;
            public float Duration;
            public bool IsKick;
            public bool IsFinisher;
            public int NextComboIndex;
        }

        // 기본공격을 상황으로 고른다.
        //
        // 예전에는 콤보 인덱스만 돌렸다. 무기를 든 손이 늘 같은 순서로 움직이니, 상대가 방패를
        // 올리고 있든 자세가 무너져 있든 똑같은 스윙이 나갔다 — 고를 것이 없는 전투였다.
        //
        // 규칙은 위에서부터 본다. 어느 것도 걸리지 않으면 콤보 순환으로 떨어지므로,
        // 규칙을 전부 지우면 예전 동작 그대로가 된다.
        private Choice Choose()
        {
            int step = comboIndex % comboHashes.Length;

            Choice choice;
            choice.Hash = comboHashes[step];
            choice.Duration = comboDurations[step];
            choice.IsKick = false;
            choice.IsFinisher = comboHashes.Length > 1 && step == comboHashes.Length - 1;
            choice.NextComboIndex = (step + 1) % comboHashes.Length;

            // ① 상대가 방패를 올렸다 → 발차기.
            //
            // 무기 스윙은 막히면 피해가 통째로 흘러가고 강인도만 조금 깎는다. 같은 스윙을 계속
            // 넣으면 가드가 열릴 때까지 아무 일도 일어나지 않는다. 발차기는 그 강인도를 크게
            // 깎아(poiseDamageKick) 가드 자체를 연다.
            //
            // 다만 지금 콘텐츠에서는 이 규칙이 거의 잠들어 있다 — GuardPart.CanEverBlock 주석대로
            // "방어는 아군만" 하기 때문에, 아군이 때리는 적은 애초에 방패를 올리지 않는다.
            // 막는 적이 생기면 그때 저절로 살아난다.
            if (CanKick() && IsTargetGuarding()) return KickChoice();

            // ② 한 번 더 밀면 무너진다 → 발차기로 끊는다.
            //
            // 평타로는 모자라고 발차기면 깨지는 구간이 있다(평타 15 / 발차기 45). 그 구간에서
            // 평타를 넣으면 상대는 버티고, 발차기를 넣으면 그 자리에서 무너져 다음 콤보가 통째로
            // 들어간다. 적이 방어를 하지 않는 지금, 실제로 발차기가 나가는 것은 대개 이 규칙이다.
            if (CanKick() && IsTargetPoiseRipe()) return KickChoice();

            // ③ 상대가 무너져 있다 → 콤보의 마지막 단으로 건너뛴다.
            //
            // 무너진 몇 초는 상대가 내준 진짜 빈틈이고 받는 피해도 커진다(staggerDamageMultiplier).
            // 그 자리에 콤보 1단을 넣으면 가장 약한 스윙으로 가장 좋은 기회를 쓰는 셈이 된다.
            // ②가 무너뜨린 직후가 대개 여기로 이어진다.
            if (comboHashes.Length > 1 && IsTargetStaggered())
            {
                int last = comboHashes.Length - 1;
                choice.Hash = comboHashes[last];
                choice.Duration = comboDurations[last];
                choice.IsFinisher = true;
                choice.NextComboIndex = 0;
            }

            return choice;
        }

        private Choice KickChoice()
        {
            Choice choice;
            choice.Hash = kickHash;
            choice.Duration = kickDuration;
            choice.IsKick = true;
            choice.IsFinisher = false;
            // 발차기는 콤보의 일부가 아니다. 순서를 소비하지 않고 제자리에 둬서,
            // 무너뜨린 뒤에 하던 콤보를 그대로 이어 붙인다.
            choice.NextComboIndex = comboIndex;
            return choice;
        }

        // 발차기 한 번이면 무너지지만 평타로는 안 되는 구간에 상대가 들어와 있는가.
        // 기준은 때리는 쪽의 수치다 — 강인도를 깎는 것은 이 유닛의 발과 무기이기 때문이다.
        private bool IsTargetPoiseRipe()
        {
            UnitStats stats = u.stats;
            if (!u.IsTargetValid()) return false;
            if (stats.poiseDamageKick <= stats.poiseDamagePerHit) return false;

            float poise = u.CurrentTarget.CurrentPoise;
            return poise > stats.poiseDamagePerHit && poise <= stats.poiseDamageKick;
        }

        // 발차기를 쓸 수 있는가. 모션이 있어야 하고, 근접 유닛이어야 한다 —
        // 원거리 직군은 애초에 발이 닿지 않는 거리에서 싸운다.
        // 창수는 거리를 유지하지만 근접이므로 발차기를 쓴다(IsRangedFighter 주석 참조).
        private bool CanKick() => kickHash != 0 && !u.IsRangedFighter;

        private bool IsTargetGuarding() => u.IsTargetValid() && u.CurrentTarget.IsBlocking;

        private bool IsTargetStaggered() => u.IsTargetValid() && u.CurrentTarget.IsStaggered;

        // ------------------------------------------------------------ 시계

        // 하던 동작이 끊겼다(InterruptCurrentAction). 잠금을 푼다.
        public void ReleaseLock() => lockedUntil = 0f;

        // 히트스톱으로 잃어버린 시간만큼 잠금과 호흡을 뒤로 민다.
        //
        // 공격 잠금은 실제 시각 기준이다. 애니메이션만 느려지고 잠금은 그대로면
        // 모션이 아직 남았는데 다음 스윙이 나가 동작이 겹친다.
        public void DelayBy(float lost)
        {
            if (lockedUntil > Time.time) lockedUntil += lost;
            if (nextReadyTime > Time.time) nextReadyTime += lost;
        }

        // 다음 스윙까지의 호흡을 통째로 지운다. 패링 직후의 되받아치기가 쓴다(GuardPart.TryPerfect).
        public void ReadyNow()
        {
            nextReadyTime = 0f;
            lockedUntil = 0f;
        }

        // 방어 자세로 들어가며 휘두르던 스윙을 버린다. BlockBehavior가 자세를 잡기 직전에 부른다.
        //
        // 잠금을 풀지 않으면 방패를 든 채로도 IsAttackAnimationLocked가 참으로 남아, 방어가 끝난 뒤
        // AttackBehavior가 이미 버린 스윙을 "휘두르는 중"으로 알고 기다린다. 준비 동작에서 버린 칼은
        // 타격 이벤트가 섞여 들어와도 닿지 않아야 한다(cancelled, ResolveHit).
        public void CancelForGuard()
        {
            if (!IsLocked) return;

            if (!hasStruck) cancelled = true;
            lockedUntil = 0f;
            lungeRemaining = 0f;
        }

        // ------------------------------------------------------------ 파고들기

        // 준비 동작 동안 타깃 쪽으로 조금 파고든다. 예전에는 StopMovement로 완전히 못 박고
        // 휘둘렀기 때문에, 사거리 경계에서 시작한 스윙은 눈에 보이게 허공을 갈랐다.
        public void UpdateLunge()
        {
            UnitStats stats = u.stats;
            if (!IsTelegraphing) return;
            if (stats.lungeSpeed <= 0f || lungeRemaining <= 0f) return;
            if (!u.IsTargetValid()) return;
            NavMeshAgent agent = u.agent;
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

            Vector3 toTarget = u.CurrentTarget.Position - u.transform.position;
            toTarget.y = 0f;

            float distance = toTarget.magnitude;
            // 파고들어 멈출 지점은 "교전 간격"이다(EngageDistance 주석 참조).
            // 여기까지만 간다는 것이 중요하다 — 이미 그 간격에 서 있으면 한 발도 움직이지 않고
            // 제자리에서 벤다. 파고들기는 상대가 물러나 칼이 닿지 않게 됐을 때를 위한 것이지,
            // 스윙마다 앞으로 밀고 들어가라는 것이 아니다.
            float contactDistance = u.EngageDistance;
            if (distance <= contactDistance) return;

            float step = Mathf.Min(stats.lungeSpeed * Time.deltaTime, lungeRemaining, distance - contactDistance);
            if (step <= 0f) return;

            lungeRemaining -= step;
            agent.Move(toTarget / distance * step);
        }

        // ------------------------------------------------------------ 타격 판정

        // 공격 클립의 타격 프레임. 클립 이벤트 → UnitAnimationEvents.AttackHit → 여기.
        //
        // 공격을 "하기로 한 것"과 "닿았는가"가 여기서 갈린다. 앞의 것은 행동 트리가 정하고
        // (AttackBehavior → TriggerAttack), 이 메서드는 그 결정을 모른 채 지금 이 순간의 거리와 각도만 본다.
        //
        // 예전에는 여기서 IsTargetValid만 보고 CurrentTarget에게 무조건 피해를 넣었다. 스윙이
        // 시작된 뒤 상대가 5m 밖으로 달아나도, 등 뒤로 돌아가도 그대로 맞았다 — 빗나감이라는
        // 것이 없으니 거리도 각도도 전투에서 아무 의미가 없었다. 이제 이 시점에 다시 잰다.
        public void ResolveHit()
        {
            UnitStats stats = u.stats;

            // 시체가 휘두르던 칼의 이벤트가 뒤늦게 도착할 수 있다. 죽었으면 아무 일도 없다.
            if (u.IsDead) return;

            // 준비 동작에서 거두고 방패를 든 스윙이다(CancelForGuard). 섞여 나가던 클립의 이벤트일 뿐이다.
            if (cancelled) return;

            // 여기를 지나면 준비 동작이 끝나고 회수 동작이 시작된다.
            MarkStruck();

            TargetRef victim = ResolveVictim();
            // 발차기는 베는 대신 무너뜨린다 — 피해는 낮고 강인도 피해는 크다.
            float poiseDamage = pendingIsKick
                ? stats.poiseDamageKick
                : stats.poiseDamagePerHit + (pendingIsFinisher ? stats.poiseDamageComboFinisherBonus : 0f);
            int damage = u.ScaleDamage(pendingIsKick
                ? Mathf.RoundToInt(stats.attackDamage * stats.kickDamageMultiplier)
                : stats.attackDamage);

            // 활은 맞았는지를 근접과 똑같이 여기서 정하되, 그 한 방을 화살에 실어 보낸다.
            // 빗나간 스윙도 화살은 떠난다 — 허공으로 날아가는 화살이 곧 빗나갔다는 표시다.
            bool fired = u.TryFireProjectile(victim, damage, poiseDamage, false);

            if (!victim.Exists)
            {
                OnMissed();
                return;
            }

            // 탱커의 발차기는 실드 배시다. 밀어내는 것 자체가 목적이라 넉백을 함께 건다 —
            // "다가오는 적의 기세를 꺾고 뒤로 밀쳐낸다"가 원작에서 탱커가 방어선을 유지하는 방식이고,
            // 그 사이에 후방이 영창을 끝내거나 사선을 확보한다.
            // 다른 직군의 발차기는 예전처럼 가드를 여는 용도로만 쓰인다(넉백 없음).
            bool shieldBash = pendingIsKick && stats.role == JobRole.Vanguard;

            // 화살이 떠났으면 피해는 투사체가 도착할 때 들어간다(WeaponProjectile).
            if (fired) return;

            // 한 방의 무게. 몸을 실은 실드 배시가 가장 무겁고, 콤보를 끝까지 이어 붙인 마무리가 그다음이다.
            float impactWeight = shieldBash ? ShieldBashImpactWeight
                : pendingIsKick ? KickImpactWeight
                : pendingIsFinisher ? FinisherImpactWeight
                : 1f;

            victim.TakeDamage(damage, u, shieldBash, false, poiseDamage, impactWeight);

            // 방어선이 적의 기세를 꺾는 순간은 화면에도 남긴다.
            if (shieldBash) GameServices.Shake.Current.Emit(u, ShieldBashShake);
        }

        // 스킬 모션의 타격 프레임. 클립 이벤트 → UnitAnimationEvents.SkillHit → 여기.
        public void ResolveSkillHit()
        {
            UnitStats stats = u.stats;
            if (u.IsDead) return;

            MarkStruck();

            TargetRef victim = ResolveVictim();
            int damage = u.ScaleDamage(stats.skillDamage);

            // 붙잡아 무너뜨리는 스킬(고블린의 무는 공격)인가.
            //
            // 그렇다면 강인도 피해는 넘기지 않는다. 둘 다 넣으면 이 한 방으로 강인도가 깨지면서
            // 면역 시간이 켜지고, 바로 뒤에 오는 TryForceStagger가 그 면역에 막힌다 — 물어뜯었는데
            // 정작 무너뜨리지 못하고 평범한 피격 반응으로 끝난다. 무너뜨리는 것 자체가 이 스킬의
            // 강인도 효과이므로 둘 중 하나만 쓴다.
            bool locksVictim = stats.skillStaggerDuration > 0f;
            float poiseDamage = locksVictim ? 0f : stats.poiseDamageSkill;

            // 스킬도 평타와 같은 규칙을 따른다 — 원거리 무기면 그 한 방을 투사체에 실어 보낸다.
            // 그러지 않으면 화살은 평타에만 날아가고, 스킬은 허공을 향해 피해만 들어간다.
            // (붙잡아 무너뜨리는 스킬은 지금 전부 근접이라 이 갈래로 오지 않는다. 원거리에
            //  같은 성질을 주려면 무너뜨리는 시점이 투사체가 닿는 순간이어야 한다.)
            bool fired = u.TryFireProjectile(victim, damage, poiseDamage, true);

            if (!victim.Exists)
            {
                OnMissed();
                return;
            }

            if (fired) return;

            victim.TakeDamage(damage, u, true, true, poiseDamage, SkillImpactWeight);
            GameServices.Shake.Current.Emit(u, SkillHitShake);
            if (!locksVictim) return;

            // 흘려내기(퍼펙트 가드)에 걸렸으면 무너지는 쪽은 이쪽이다. TakeDamage 안에서
            // GuardPart.TryPerfect가 나를 경직시켰으므로 그것으로 안다 — 그 위에 상대까지 무너뜨리면
            // 읽어내고 쳐낸 보람이 사라진다.
            if (u.IsStaggered) return;

            victim.TryForceStagger(stats.skillStaggerDuration);
        }

        private void MarkStruck()
        {
            hasStruck = true;
            lastStrikeTime = Time.time;
        }

        // 타격 이벤트 시점에 "이 스윙이 누구를 맞혔는가"를 정한다.
        // 노리던 상대가 빠져나갔어도 궤적 안에 다른 적이 서 있으면 그쪽이 맞는다 —
        // 휘두른 칼은 눈앞에 있는 놈을 벤다.
        private TargetRef ResolveVictim()
        {
            float reach = Reach;

            if (u.IsTargetValid() && IsInsideArc(u.CurrentTarget, reach)) return u.CurrentTarget;
            if (!u.stats.cleaveOffTarget) return TargetRef.None;

            // 원거리 직군은 해당 없다. 활은 겨눈 하나를 쏘는 것이지 앞을 쓸어 베는 것이 아니라,
            // 노리던 상대가 빠졌으면 그냥 빗나가야 한다. 여기서 막지 않으면 사거리 9m짜리가
            // 정면 130도 안의 아무나 자동으로 맞히는, 사실상 공짜 재조준이 된다.
            // 창수는 거리를 두고 싸우지만 근접이라 여기 걸리지 않는다 — 휘두른 창은 앞을 쓴다.
            if (u.IsRangedFighter) return TargetRef.None;

            return UnitRegistry.FindEnemyInArc(u, reach, u.stats.attackArcAngle);
        }

        private bool IsInsideArc(TargetRef candidate, float reach)
        {
            if (!candidate.Exists) return false;

            Vector3 toCandidate = candidate.Position - u.transform.position;
            toCandidate.y = 0f;

            float sqrDistance = toCandidate.sqrMagnitude;
            if (sqrDistance > reach * reach) return false;
            if (sqrDistance <= 0.0001f) return true;

            Vector3 forward = u.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f) return true;

            float minDot = Mathf.Cos(Mathf.Clamp(u.stats.attackArcAngle * 0.5f, 0f, 180f) * Mathf.Deg2Rad);
            return Vector3.Dot(forward.normalized, toCandidate / Mathf.Sqrt(sqrDistance)) >= minDot;
        }

        // 헛스윙. 피해가 없는 것으로 끝내지 않고 회수 시간을 늘려 벌을 준다 —
        // 그래야 사거리를 재는 것과 무작정 휘두르는 것이 갈린다.
        private void OnMissed()
        {
            nextReadyTime += u.stats.attackRecoveryTime * 0.6f;
        }

        // 죽은 유닛을 되살려 재사용하는 경로(Configure)를 위한 초기화.
        public void Reset()
        {
            hasStruck = false;
            nextReadyTime = 0f;
            lungeRemaining = 0f;
            cancelled = false;
            lastStrikeTime = -999f;
        }
    }
}
