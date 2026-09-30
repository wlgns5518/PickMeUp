using UnityEngine;
using Random = UnityEngine.Random;

public partial class UnitController
{
    // 막기를 맡는 부품: 날아오는 칼을 알아채고(인지), 반응 시간을 채우고, 자세를 들고,
    // 들어온 한 대를 받아내거나 흘려낸다(퍼펙트 가드).
    //
    // 자세를 들었는가(IsBlocking)는 본체에 남긴다. 피격·경직·동작 전환이 모두 그 값을 보고
    // InterruptCurrentAction이 내려놓기 때문이다. 여기는 "언제 들고, 들었을 때 무슨 일이 있는가"다.
    private sealed class GuardPart
    {
        // 준비 동작이 잠깐 비어도 경계를 유지하는 시간. 고블린의 준비 동작이 0.4초라
        // 스윙과 스윙 사이의 빈 구간을 넘길 만큼은 되어야 한다.
        private const float AlertGrace = 0.5f;

        // 흘려낸 순간의 화면 흔들림(0~1). CombatImpulse 주석의 "판을 가르는 순간"이다.
        private const float PerfectGuardShake = 0.45f;

        private readonly UnitController u;

        private int blockHitHash;
        private float blockHitDuration;

        private float lastBlockTime = -999f;
        // 막을 상대. 엔티티일 수도 있으므로 참조가 아니라 손잡이다.
        private TargetRef blockThreat;

        // 이번에 올린 자세가 흘려낼 수 있는 자세인가. 방패를 드는 그 순간에 한 번 정해진다 —
        // 맞을 때마다 굴리면 같은 자세로 여러 대를 받는 동안 결과가 오락가락한다.
        private bool perfectArmed;
        private float raisedTime = -999f;
        private float impactUntil;

        // 알아챈 위협. 게임오브젝트 적과 엔티티가 된 적을 가리지 않으므로 손잡이로 든다.
        private TargetRef noticedThreat;
        private float noticedTime;
        private float noticedDelay;
        private float alertGraceUntil;

        public GuardPart(UnitController owner)
        {
            u = owner;
        }

        public void CacheHashes()
        {
            blockHitHash = u.ResolveStateHash(u.blockHitStateName);
            blockHitDuration = blockHitHash != 0 ? u.GetAnimationClipDuration(u.blockHitStateName, 0.3f) : 0f;
        }

        // ------------------------------------------------------------ 인지

        // 나를 노리고 칼을 들어올린 적을 매 프레임 지켜본다. TickCombat이 부른다.
        //
        // 이 눈은 손과 따로 움직여야 한다. 예전에는 CanBlock 안에서만 위협을 살폈는데,
        // CanBlock은 공격 잠금이 풀린 동안에만 불린다 — 아군은 시간의 8할을 스윙에 묶여 있으므로
        // 위협을 알아챌 기회 자체가 거의 없었다. 실측으로 아군의 자유 구간(0.13~0.31초)이
        // 반응 시간(0.11~0.25초)과 거의 같아서, 알아채고 반응을 마치기 전에 다시 휘두르기
        // 시작해 버렸다. 그래서 방어가 사실상 발동하지 않았다.
        //
        // 사람은 칼을 휘두르는 도중에도 날아오는 칼을 본다. 다만 손이 자유로워질 때까지
        // 대응하지 못할 뿐이다 — 그래서 인지(여기)와 실행(CanBlock)을 나눈다.
        public void TickAwareness()
        {
            // 애초에 막을 수 없는 유닛은 훑을 이유가 없다. 전 유닛이 매 프레임 적대 팀 전체를
            // 순회하면 유닛 수의 제곱으로 비용이 커진다(TargetScanner가 스캔 주기를 흩어 놓는 것과 같은 이유).
            if (!CanEverBlock())
            {
                noticedThreat = TargetRef.None;
                return;
            }

            TargetRef threat = UnitRegistry.FindTelegraphingAttacker(u);
            if (!threat.Exists)
            {
                // 칼을 든 놈이 잠깐 없어도 곧바로 경계를 풀지 않는다.
                //
                // 여럿에게 둘러싸이면 "지금 준비 동작 중인 적"이 프레임마다 바뀌고, 그 사이사이
                // 아무도 아닌 순간이 끼어든다. 그때마다 경계를 처음부터 다시 세우면 반응 시간이
                // 영영 끝나지 않아서, 둘러싸일수록 덜 막게 되는 거꾸로 된 결과가 나온다.
                // (실측: 고블린 10마리에 둘러싸인 탱커가 56대를 맞는 동안 막은 것은 5번뿐이었다.)
                if (Time.time >= alertGraceUntil) noticedThreat = TargetRef.None;
                return;
            }

            alertGraceUntil = Time.time + AlertGrace;

            if (threat == noticedThreat) return;

            if (noticedThreat.Exists)
            {
                // 이미 경계 중이다. 보는 대상만 바꾸고 반응 시간은 다시 재지 않는다.
                // 사람은 칼을 든 특정 한 명이 아니라 눈앞의 난투 전체를 경계한다 — 옆 놈으로
                // 시선이 옮겨갔다고 해서 처음부터 다시 놀라지는 않는다.
                noticedThreat = threat;
                return;
            }

            // 아무것도 없다가 처음 칼을 본 순간. 반응 시간은 유닛마다 다르게 뽑아 전원이 같은
            // 박자로 방패를 올리는 것을 막는다.
            noticedThreat = threat;
            noticedTime = Time.time;
            noticedDelay = u.stats.blockReactionTime * Random.Range(0.6f, 1.4f);
        }

        // 방어라는 수단 자체를 가진 상태인가. 지금 막을 이유가 있는지(위협)와는 별개다.
        private bool CanEverBlock()
        {
            if (u.IsDead) return false;
            // 손에 든 것으로 받아낼 수 있는 직군만 막는다.
            //
            // 마법사만 None이다. 맨손 시전이라 들어 올릴 것이 손에 없다 — 막지 못하는 것이
            // 이 직군이 파티에 묶여 있는 이유의 절반이다(나머지 절반은 영창 중 무방비).
            //
            // 나머지는 전부 막되, 어떻게 막느냐가 갈린다: 탱커는 방패, 검사는 패링, 그 외는
            // 들고 있는 무기(GuardStyle.Weapon)다. 마지막 것은 무기에 따라 성능이 크게 벌어진다
            // (JobProfile.WeaponGuardFactor) — 막아도 절반 넘게 들어오고 각도도 좁다.
            //
            // 막을 수 있는 공격은 전부 막는다. 재사용 대기가 없고(blockCooldown 0), 휘두르던 것도
            // 거두고 들어가며(AttackBehavior), 위협이 이어지는 동안은 자세를 유지한다(BlockBehavior).
            // 남는 조건은 몸이 정하는 것뿐이다 — 각도, 반응 시간, 그리고 자세가 살아 있는가.
            //
            // 적이 방어하지 않는 성질은 그대로 남는다: 고블린 프리팹의 guardStyle은 None이다.
            if (u.stats.guardStyle == GuardStyle.None) return false;
            if (u.blockAnimationHash == 0) return false;
            // 자세가 무너져 있는 동안은 방패를 들 수 없다. 예전에는 방어 지구력이 이 역할까지
            // 겸했지만(0이면 자세를 안 잡음), 지금은 강인도가 깨지면 곧바로 Stagger로 들어가므로
            // 그 상태만 막으면 된다.
            if (u.IsStaggered) return false;
            return Time.time >= lastBlockTime + u.stats.blockCooldown;
        }

        // 알아챈 위협에 반응까지 마쳤고, 그 위협이 아직 칼을 내지르지 않았는가.
        private bool HasReactedToThreat =>
            noticedThreat.Exists &&
            noticedThreat.IsAlive &&
            noticedThreat.IsTelegraphing &&
            Time.time >= noticedTime + noticedDelay;

        // 나를 노리고 칼을 들어올린 적을 봤다. 반응 시간이 아직 안 끝났어도 참이다.
        // (UnitController.IsHoldingForGuard 주석 참조)
        public bool IsHoldingForGuard =>
            CanEverBlock() &&
            noticedThreat.Exists &&
            noticedThreat.IsAlive &&
            noticedThreat.IsTelegraphing;

        // ------------------------------------------------------------ 자세

        // 지금 방패를 올릴 수 있고, 올릴 이유도 있는가.
        //
        // "누가 나를 노리고 휘두르는가"를 여기서 찾지 않는다는 점이 중요하다. 그 감시는
        // TickAwareness가 매 프레임 따로 돌린다 — 이 메서드는 공격 잠금이 풀린 짧은
        // 순간에만 불리기 때문에, 여기서 위협을 처음 알아채고 반응 시간까지 채우려 하면
        // 손이 자유로운 시간이 모자라 방어가 거의 발동하지 않는다(그게 예전 동작이었다).
        // 여기서는 이미 알아채고 반응까지 끝난 위협이 있는지만 확인한다.
        public bool CanBlock()
        {
            blockThreat = TargetRef.None;
            if (!CanEverBlock()) return false;
            if (!HasReactedToThreat) return false;

            blockThreat = noticedThreat;
            return true;
        }

        // 방어 중 나를 노리는 적을 향해 돈다. 방어 중 회전은 평소보다 느리다(blockTurnSpeed < rotationSpeed) —
        // 위협이 등 뒤로 돌아가면 다 따라가지 못해서 정면 180도 판정에 허점이 생겨야
        // 방어가 지나치게 완벽해지지 않는다.
        public void FaceThreat() => u.Facing.Toward(blockThreat, u.blockTurnSpeed);

        // 방어 중 나를 노리고 휘두르는 적을 다시 찾는다. 진입 시점에 잡아 둔 위협은 금방 낡는다.
        // (CanBlock을 그대로 쓸 수 없는 이유: 방금 자세를 잡아 blockCooldown에 걸려 있어서
        //  방어 중에는 항상 false가 나온다.)
        public bool RefreshThreat()
        {
            blockThreat = UnitRegistry.FindTelegraphingAttacker(u);
            return blockThreat.Exists;
        }

        public void SetBlocking(bool isBlocking)
        {
            u.IsBlocking = isBlocking;
            impactUntil = 0f;
            if (isBlocking)
            {
                lastBlockTime = Time.time;
                // 방패를 올린 시각. 퍼펙트 가드 창의 기준점이 된다.
                raisedTime = Time.time;
                // 이번 자세로 흘려낼 수 있는지는 드는 순간에 정해진다 — 상대의 동작을 제대로
                // 읽었느냐이지, 맞을 때마다 다시 굴릴 문제가 아니다.
                perfectArmed = Random.value < u.stats.perfectGuardChance;
                u.PlayAnimation(u.blockAnimationHash, true);
            }
            else
            {
                u.PlayAnimation(u.idleAnimationHash, false);
            }
        }

        // 막아낸 순간의 반동. 전용 모션이 없으면 방어 자세를 한 번 다시 잡아 최소한
        // "무언가 부딪혔다"는 것은 읽히게 한다.
        public void PlayImpact()
        {
            if (blockHitHash != 0)
            {
                u.PlayAnimation(blockHitHash, true);
                impactUntil = Time.time + blockHitDuration;
                return;
            }

            u.PlayAnimation(u.blockAnimationHash, true);
        }

        // 반동 모션이 끝나면 방어 자세로 돌아간다. 그러지 않으면 BlockHit의 마지막 프레임에서 굳는다.
        public void TickPose()
        {
            if (!u.IsBlocking || impactUntil <= 0f) return;
            if (Time.time < impactUntil) return;

            impactUntil = 0f;
            u.PlayAnimation(u.blockAnimationHash, true);
        }

        // ------------------------------------------------------------ 퍼펙트 가드

        // 방패를 올린 직후의 짧은 창 안에 들어온 공격은 통째로 흘려낸다.
        // 미리 자세를 잡고 버티는 것과, 날아오는 칼에 맞춰 방패를 올리는 것은 달라야 한다.
        //
        // 조건이 둘인 이유: 타이밍만 보면 거의 모든 방어가 퍼펙트 가드가 된다. 방패는 적의
        // 준비 동작을 보고 blockReactionTime 뒤에 올라가는데, 그 반응 시간이 준비 동작 길이와
        // 비슷해서(칼 0.34초 vs 반응 0.11~0.25초) 자세를 잡은 시점이 늘 타격 직전이기 때문이다.
        // 그래서 자세를 드는 순간 굴린 "제대로 읽었는가"(perfectArmed)를 함께 본다.
        public bool TryPerfect(UnitController attacker, Unity.Entities.Entity attackerEntity)
        {
            UnitStats stats = u.stats;
            if (!perfectArmed || stats.perfectGuardWindow <= 0f) return false;
            if (Time.time > raisedTime + stats.perfectGuardWindow) return false;

            // 한 번 자세를 잡으면 한 번만 흘려낸다. 창 안에 두 대가 들어와도 두 번째는 그냥 막힌다.
            perfectArmed = false;

            PlayImpact();
            GameServices.Shake.Current.Emit(u, PerfectGuardShake);
            // 흘려낸 쪽도 잠깐 멈춰야 "쳐냈다"가 읽힌다. 흘려진 쪽은 자세가 통째로 무너진다.
            u.ApplyHitStop(stats.hitStopDuration * 2f, stats.hitStopScale);
            if (attacker != null && !attacker.IsDead)
            {
                attacker.ApplyHitStop(stats.hitStopDuration * 2f, stats.hitStopScale);
                attacker.Stagger(stats.perfectGuardStaggerDuration);
            }

            // 흘려낸 상대가 엔티티라면 브리지를 통해 무너뜨린다. 멈칫과 밀려남은 무너뜨리는 그 한 줄이
            // 함께 싣고 간다 — 엔티티는 Animator 대신 시뮬레이션 시간을 눌러 멈춘다(EnemyImpact).
            if (attackerEntity != Unity.Entities.Entity.Null)
            {
                EnemyWorldBridge.StaggerEnemy(attackerEntity, stats.perfectGuardStaggerDuration, u.transform.position,
                    stats.hitStopDuration * 2f, stats.hitStopScale);
            }

            // 패링은 여기서 끝나지 않는다. 쳐낸 그 자리에서 되받아치는 것이 패링의 값어치다 —
            // 방패는 막고 버티지만 검신은 궤적을 비틀어 상대의 빈틈을 만들고 그 틈으로 들어간다.
            //
            // 실제로 하는 일은 "다음 스윙까지의 호흡을 통째로 지운다"이다. 무너진 상대는
            // perfectGuardStaggerDuration(0.9초)만큼 서 있고, 그 시간을 온전히 쓰려면 방어를
            // 푸는 즉시 칼이 나가야 한다. 이게 없으면 흘려내 놓고 평소 박자대로 기다리다
            // 상대가 일어난 뒤에 휘두르게 된다.
            if (stats.counterAfterPerfectGuard)
            {
                u.Swing.ReadyNow();
                // 방어 쿨다운도 함께 지운다. 반격 뒤 곧바로 다음 궤적을 읽을 수 있어야
                // "공수 리듬을 타는 테크니션"이 된다.
                lastBlockTime = -999f;
            }

            return true;
        }

        public void Reset()
        {
            raisedTime = -999f;
            perfectArmed = false;
            impactUntil = 0f;
            noticedThreat = TargetRef.None;
        }
    }
}
