using UnityEngine;
using Random = UnityEngine.Random;

public partial class UnitController
{
    // "어디에 서서 싸울 것인가"를 스스로 고르는 부품(11층부터, 건물이 선 전장).
    //
    // 지휘관이 "버텨"를 외치지 않아도, 싸우는 사람은 스스로 좋은 땅을 찾는다 — 둘러싸일 것 같으면 벽에 등을 대고,
    // 길목이 가까우면 그 입구를 막는다. 여기서는 그 자리를 고르기만 한다(TerrainTactics). 고른 자리는 진형 유지
    // 명령의 자리와 똑같이 다룬다(CommandPart.SetTerrainAnchor) — 자리로 가서, 손 닿는 적만 상대하고, 밀려나면
    // 돌아온다(HoldPositionBehavior). 같은 동작을 두 번 만들지 않으려는 것이다.
    //
    // 명령이 먼저다. 지휘관의 명령(후퇴·진형 유지)이 서 있는 동안은 스스로 자리를 고르지 않는다.
    //
    // 자리를 붙드는 데는 끝이 있어야 한다. 좋은 땅에 서 있어도 적이 다른 동료만 노리면 그 자리는 구경석이다.
    //  - 손 닿는 적도, 나를 쫓는 적도 없이 IdleReleaseTime이 지나면 놓고 싸우러 간다.
    //  - 둘레에 적이 아예 없으면 놓는다(싸움이 다른 데로 옮겨 갔다).
    //  - 자리로 가다가 시간 안에 못 닿으면 놓는다(적에게 막혔다).
    //  - 물러나야 할 때(HP 위기·간격 후퇴)는 놓는다 — 자리를 지키는 가지가 물러나는 걸음을 도로 끌고 온다.
    // 놓은 뒤에는 잠시 다시 잡지 않는다 — 놓자마자 같은 자리를 다시 고르면 자리와 싸움 사이를 오간다.
    private sealed class TerrainPart
    {
        // 자리를 다시 따져 보는 간격(초). 붙들고 있는 동안은 더 드물게 본다.
        private const float ThinkInterval = 1.5f;
        private const float AnchoredThinkInterval = 3f;

        // 이 반경 안의 적을 "몰려오는 쪽"으로 본다.
        private const float ThreatRadius = 14f;

        // 지금 자리보다 이만큼 나아야 옮긴다. 이미 자리를 잡았으면 더 나아야 옮긴다(자리 사이를 오가지 않게).
        private const float MeleeMinGain = 1f;
        private const float RangedMinGain = 0.8f;
        private const float SwitchExtraGain = 0.8f;

        // 전열이 고르는 자리는 몰려오는 쪽에서 이보다 멀지 않다 — 싸움을 피해 뒤로 숨는 자리가 아니다.
        private const float MeleeMaxThreatDistance = 10f;

        private const float IdleReleaseTime = 2.5f;
        private const float NoThreatReleaseTime = 3f;
        private const float ReleaseCooldown = 5f;

        // 나를 쫓는 적이 이 거리 안에 있으면 자리에서 기다린다 — 곧 칼이 닿는다.
        private const float IncomingEnemyRange = 6f;

        // ---- 방어 진지(BattleTerrain.Posts) ----
        // 적이 아군의 이 배수 이상 남아 있으면 길목을 막고 기다린다. 적게 남으면 나가서 쓸어 담는다.
        // 한 번 진지에 들면 이 비율의 2/3 밑으로 떨어질 때까지 지킨다(경계에서 들락날락하지 않게).
        private const float OutnumberedRatio = 3f;
        private const float StayDefendingRatio = 2f;

        // 진지에서 이만큼(초) 동안 둘레 12m에 적이 한 마리도 없으면 이번 판에는 진지를 버린다 —
        // 적이 다른 길로 돌아 들어오지 않거나 길을 못 찾는 판에서 파티가 영영 서 있지 않게.
        private const float AbandonDefenseAfter = 30f;
        private const float DefenseQuietRadius = 12f;

        // 진지 안에서 설 자리. 전열은 입구에 한 줄(가운데·양옆), 넘치면 그 뒤에 한 줄.
        // 후방은 전열 뒤 4.5m에 한 줄(가운데·양옆), 넘치면 그 뒤로 1.5m씩.
        private static readonly float[] FrontLateral = { 0f, -1f, 1f };
        private const float FrontDepth = 0.6f;
        private const float FrontRowSpacing = 1.8f;
        private const float BackLateral = 2.4f;
        private const float BackDepth = 4.5f;
        private const float BackRowSpacing = 1.5f;

        private readonly UnitController u;
        private float nextThinkTime;
        private float cooldownUntil;
        private float idleSince = -1f;
        private float noThreatSince = -1f;

        private bool defending;
        private bool defenseAbandoned;
        private float quietSince = -1f;

        public TerrainPart(UnitController owner)
        {
            u = owner;
        }

        public void Tick()
        {
            CommandPart command = u.Command;
            if (!CanUseTerrain(command))
            {
                Release(0f);
                return;
            }

            // 수에서 크게 밀리면 파티가 길목 하나를 막는다. 그동안은 아래 낱낱의 자리 고르기를 하지 않는다.
            if (TryHoldPost(command)) return;

            if (command.HasTerrainAnchor && ShouldRelease(command, out float cooldown))
            {
                Release(cooldown);
                return;
            }

            if (Time.time < nextThinkTime) return;
            nextThinkTime = Time.time + (command.HasTerrainAnchor ? AnchoredThinkInterval : ThinkInterval) *
                            Random.Range(0.8f, 1.2f);

            if (!command.HasTerrainAnchor && Time.time < cooldownUntil) return;

            // 손이 묶여 있는 동안은 자리를 옮길 수 없다. 다음 박자에 다시 본다.
            if (u.IsAttackAnimationLocked || u.IsStaggered || u.IsCasting) return;
            if (u.emotion != null && u.emotion.IsActionBlocked) return;

            // 이미 자리를 잡았으면 그 자리에 선 뒤에야 더 나은 곳을 찾는다. 가는 도중에 목적지를 바꾸면 헤맨다.
            if (command.HasTerrainAnchor && !command.IsAtHoldAnchor) return;

            if (!TryGetThreat(out Vector3 threat)) return;

            bool ranged = u.IsRangedFighter;
            TerrainTactics.Weights weights = ranged ? TerrainTactics.Ranged : TerrainTactics.Melee;
            float minGain = (ranged ? RangedMinGain : MeleeMinGain) + (command.HasTerrainAnchor ? SwitchExtraGain : 0f);
            float maxThreatDistance = ranged ? Mathf.Max(2f, u.stats.attackRange * 0.9f) : MeleeMaxThreatDistance;

            if (!TerrainTactics.TryFindBetterSpot(u, threat, weights, minGain, PartyCenter(), maxThreatDistance,
                    out Vector3 spot)) return;

            command.SetTerrainAnchor(spot);
            idleSince = -1f;
            noThreatSince = -1f;
        }

        // 지형을 따질 처지인가. 명령이 서 있으면 명령이 먼저고, 치고 빠지는 직군(암살자)은 자리를 지키지 않는다.
        // 물러나야 할 때도 놓는다 — 그 판단은 행동 트리의 후퇴 가지(WantsRetreat)와 같은 것을 본다.
        private bool CanUseTerrain(CommandPart command)
        {
            if (u.IsDead || u.team != UnitTeam.Ally || !TerrainTactics.IsReady) return false;
            if (command.Order != PartyOrder.Engage) return false;
            if (u.stats.stalkAfterCombo) return false;
            if (u.ShouldRetreatForSurvival() || u.ShouldKeepDistance()) return false;
            return true;
        }

        private bool ShouldRelease(CommandPart command, out float cooldown)
        {
            cooldown = ReleaseCooldown;

            // 가다가 막혔다.
            if (command.HoldReturnTimedOut) return true;

            // 싸움이 다른 데로 옮겨 갔다.
            if (!UnitRegistry.TryGetEnemyCentroidAround(u, u.transform.position, ThreatRadius, out _))
            {
                if (noThreatSince < 0f) noThreatSince = Time.time;
                if (Time.time - noThreatSince >= NoThreatReleaseTime)
                {
                    cooldown = 0f;
                    return true;
                }
            }
            else
            {
                noThreatSince = -1f;
            }

            // 자리에 섰는데 칠 것도, 나를 노리고 오는 것도 없다 — 구경석이다.
            bool engaged = u.HasUsableTarget() && u.IsTargetInAttackRange();
            bool incoming = UnitRegistry.HasEnemyChasing(u, IncomingEnemyRange);
            if (command.IsAtHoldAnchor && !engaged && !incoming)
            {
                if (idleSince < 0f) idleSince = Time.time;
                if (Time.time - idleSince >= IdleReleaseTime) return true;
            }
            else
            {
                idleSince = -1f;
            }

            return false;
        }

        // 방어 진지 — 배치가 알려 준 길목(BattleTerrain.MainPost)을 파티가 함께 막는다.
        //
        // 원작의 대군전은 "넓은 데서 맞붙지 않는다"가 첫째다. 다섯이 서른을 탁 트인 들판에서 받으면 사방에서 칼이
        // 들어오지만, 4m 문 앞에 서면 한꺼번에 덤빌 수 있는 수가 두셋으로 줄어든다. 전열은 입구를 막고,
        // 후방은 그 뒤에서 문으로 들어서는 놈을 쏜다.
        //
        // 자리는 파티 안의 순번으로 나눈다(같은 직군끼리 등록 순서). 모두가 같은 규칙으로 계산하므로 따로
        // 조율하지 않아도 겹치지 않는다. 손 닿는 적과는 그 자리에서 싸운다(HoldPositionBehavior).
        private bool TryHoldPost(CommandPart command)
        {
            if (defenseAbandoned)
            {
                defending = false;
                return false;
            }

            int postIndex = BattleTerrain.MainPost;
            if (postIndex < 0 || postIndex >= BattleTerrain.Posts.Count) return StopDefending(command);

            EnemyWorldBridge.SumEnemyHealth(out _, out _, out int enemies);
            int allies = CountLivingAllies(out int rank, out bool ranged);
            float ratio = defending ? StayDefendingRatio : OutnumberedRatio;
            if (allies <= 0 || enemies < allies * ratio) return StopDefending(command);

            // 진지 둘레가 오래 조용하면 이번 판은 진지를 버린다.
            if (defending)
            {
                if (UnitRegistry.TryGetEnemyCentroidAround(u, u.transform.position, DefenseQuietRadius, out _))
                {
                    quietSince = -1f;
                }
                else
                {
                    if (quietSince < 0f) quietSince = Time.time;
                    if (Time.time - quietSince >= AbandonDefenseAfter)
                    {
                        defenseAbandoned = true;
                        return StopDefending(command);
                    }
                }
            }

            BattleTerrain.DefensePost post = BattleTerrain.Posts[postIndex];
            Vector3 spot = PostSpot(post, rank, ranged);
            if (!command.HasTerrainAnchor || (command.HoldAnchor - spot).sqrMagnitude > 0.25f)
                command.SetTerrainAnchor(spot);

            if (!defending) quietSince = -1f;
            defending = true;
            return true;
        }

        private bool StopDefending(CommandPart command)
        {
            if (defending)
            {
                defending = false;
                command.ClearTerrainAnchor();
            }
            return false;
        }

        // 살아 있는 아군 수와, 그중 나와 같은 직군(전열/후방)에서 내 순번.
        private int CountLivingAllies(out int rank, out bool ranged)
        {
            ranged = u.IsRangedFighter;
            rank = 0;
            int count = 0;
            var mates = UnitRegistry.GetTeam(u.team);
            for (int i = 0; i < mates.Count; i++)
            {
                UnitController mate = mates[i];
                if (mate == null || mate.IsDead || !mate.isActiveAndEnabled) continue;
                count++;
                if (mate == u) continue;
                // 나보다 앞에 등록된 같은 직군만 센다.
                if (mate.IsRangedFighter == ranged && IndexOf(mates, mate) < IndexOf(mates, u)) rank++;
            }
            return count;
        }

        private static int IndexOf(System.Collections.Generic.IReadOnlyList<UnitController> list, UnitController unit)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == unit) return i;
            return -1;
        }

        // 진지 안에서 순번 rank가 설 자리. 서지 못할 곳(벽 속)이면 입구 가운데 줄 쪽으로 당긴다.
        private static Vector3 PostSpot(BattleTerrain.DefensePost post, int rank, bool ranged)
        {
            Vector3 inward = -post.outward;
            var lateral = new Vector3(-post.outward.z, 0f, post.outward.x);
            float halfWidth = Mathf.Max(0f, post.width * 0.5f - 0.7f);

            float side, depth;
            if (!ranged)
            {
                int row = rank / FrontLateral.Length;
                side = FrontLateral[rank % FrontLateral.Length] * halfWidth;
                depth = FrontDepth + row * FrontRowSpacing;
            }
            else
            {
                int row = rank / 3;
                int column = rank % 3;
                side = column == 0 ? 0f : column == 1 ? -BackLateral : BackLateral;
                depth = BackDepth + row * BackRowSpacing;
            }

            Vector3 spot = post.position + inward * depth + lateral * side;
            for (int i = 0; i < 4 && BattleGround.SampleClearance(spot) < 0.6f; i++)
                spot = Vector3.Lerp(spot, post.position + inward * depth, 0.5f);
            return spot;
        }

        // 적이 몰려오는 쪽. 둘레의 적 무게중심, 없으면 겨누는 상대.
        private bool TryGetThreat(out Vector3 threat)
        {
            if (UnitRegistry.TryGetEnemyCentroidAround(u, u.transform.position, ThreatRadius, out threat)) return true;

            if (u.IsTargetValid())
            {
                threat = u.CurrentTarget.Position;
                return true;
            }

            return false;
        }

        // 살아 있는 동료들의 가운데. 혼자 동떨어진 자리를 고르지 않게 하는 기준이다.
        private Vector3 PartyCenter()
        {
            var mates = UnitRegistry.GetTeam(u.team);
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < mates.Count; i++)
            {
                UnitController mate = mates[i];
                if (mate == null || mate.IsDead || !mate.isActiveAndEnabled) continue;
                sum += mate.transform.position;
                count++;
            }
            return count > 0 ? sum / count : u.transform.position;
        }

        private void Release(float cooldown)
        {
            CommandPart command = u.Command;
            if (!command.HasTerrainAnchor) return;

            command.ClearTerrainAnchor();
            cooldownUntil = Time.time + cooldown;
            idleSince = -1f;
            noThreatSince = -1f;
        }

        public void Reset()
        {
            defending = false;
            defenseAbandoned = false;
            quietSince = -1f;
            nextThinkTime = 0f;
            cooldownUntil = 0f;
            idleSince = -1f;
            noThreatSince = -1f;
        }
    }
}
