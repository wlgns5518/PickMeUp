using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

// UnitController의 "한 번의 교전이 실제로 어떻게 굴러가는가" 쪽의 입구와 인스펙터 값.
//
// 칼이 오가는 순간에만 의미가 있는 일은 전부 Unit/Parts의 부품(UnitController 안에 둔 private 클래스)이
// 맡는다. 부품은 바깥 클래스의 private 필드를 그대로 읽으므로, 인스펙터 값(프리팹에 저장된 이름 그대로)은
// 여기와 본체에 남기고 규칙만 옮겼다:
//
//   SwingPart        — 콤보 선택, 준비/타격/회수 시계, 타격 판정, 파고들기
//   GuardPart        — 날아오는 칼 인지, 막기, 퍼펙트 가드
//   PosturePart      — 강인도·경직·면역          HitReactionPart — 방향별 피격·가드 브레이크 모션
//   HitStopPart      — 부딪힌 순간의 멈칫          SlowPart        — 둔화·피격 둔화
//   FootworkPart     — 스윙 사이의 간격 맞추기     LeapPart / ClingPart — 도약 공격, 목 물기
//   EngagementPart   — 교전 방위·교전 시간         Avoidance / HighGround / Stealth
//
// 모든 부품은 처음 물을 때 만든다(x ?? (x = new …)). 도메인 리로드 뒤에는 Awake 없이 필드가 비기 때문이다.
public partial class UnitController
{
    [Header("Reaction Animator States (없으면 기본 모션으로 대체)")]
    [Tooltip("정면에서 맞았을 때. 비워두거나 애니메이터에 없으면 hitStateName을 쓴다.")]
    [SerializeField] private string hitFrontStateName = "HitFront";
    [Tooltip("등 뒤에서 맞았을 때.")]
    [SerializeField] private string hitBackStateName = "HitBack";
    [SerializeField] private string hitLeftStateName = "HitLeft";
    [SerializeField] private string hitRightStateName = "HitRight";
    [Tooltip("방패/무기로 막아낸 순간의 반동. 없으면 방어 자세를 한 번 다시 잡는 것으로 대신한다.")]
    [SerializeField] private string blockHitStateName = "BlockHit";
    [Tooltip("가드가 뚫려 방패가 젖혀지는 동작. 없으면 경직 모션으로 대체한다.")]
    [SerializeField] private string blockBreakStateName = "BlockBreak";
    [Tooltip("자세가 완전히 무너져 아무것도 못 하는 동안의 모션. 없으면 피격 모션으로 대체한다.")]
    [SerializeField] private string staggerStateName = "Stagger";

    [Header("Footwork Animator States (없으면 걷기로 대체)")]
    [SerializeField] private string strafeLeftStateName = "StrafeLeft";
    [SerializeField] private string strafeRightStateName = "StrafeRight";
    [SerializeField] private string strafeBackStateName = "StrafeBack";

    [Tooltip("교전 중 제자리에 서 있을 때의 자세. 스윙과 스윙 사이의 짧은 틈에 쓰인다. " +
             "없으면 평소 Idle로 대체되는데, 그러면 칼을 거두고 긴장을 푼 자세가 되어 " +
             "공격이 끝날 때마다 멈칫하는 것처럼 보인다.")]
    [SerializeField] private string combatIdleStateName = "CombatIdle";

    [Header("Neck Cling (스킬을 쓰는 동안 상대에게 매달린다)")]
    [Tooltip("스킬 모션 내내 상대의 목에 달라붙을 것인가. 켜면 그 동안 NavMeshAgent를 끄고 " +
             "위치를 직접 잡는다 — 목은 NavMesh 위의 어떤 좌표로도 닿을 수 없기 때문이다.")]
    [SerializeField] private bool clingToNeckDuringSkill;
    [Tooltip("목을 문 채 상대의 몸 중심에서 떨어져 있는 거리(미터).")]
    [SerializeField] private float clingDistance = 0.42f;
    [Tooltip("이 유닛의 루트에서 입까지의 높이(미터). 이만큼 내려 잡아야 루트가 아니라 입이 목에 닿는다.")]
    [SerializeField] private float clingMouthHeight = 1.2f;
    [Tooltip("목까지 당겨 붙는 데 쓰는 시간(스킬 모션 길이에 대한 비율). 0이면 그 자리에서 순간이동한다.")]
    [SerializeField, Range(0f, 1f)] private float clingSnapTime = 0.25f;

    [Header("Leap Attack Animator State (없으면 도약하지 않는다)")]
    [Tooltip("도약해 덤벼드는 모션. 이 상태가 없으면 stats.leapAttackRange를 올려도 도약하지 않는다.")]
    [SerializeField] private string leapAttackStateName = "";
    [Tooltip("도약의 정점 높이(미터). 모델만 뜨고 판정과 경로는 땅에 남는다.")]
    [SerializeField] private float leapAttackHeight = 0.5f;
    [Tooltip("클립의 몇 지점에서 발이 땅을 떠나는가(0~1). 그 앞은 웅크리는 동작이다.")]
    [SerializeField, Range(0f, 1f)] private float leapLaunchRatio = 0.15f;
    [Tooltip("클립의 몇 지점에서 착지하는가(0~1). 타격 이벤트가 이 근처에 있어야 " +
             "칼이 닿는 순간과 도착하는 순간이 맞는다.")]
    [SerializeField, Range(0f, 1f)] private float leapLandRatio = 0.45f;

    [Header("Spacing")]
    // NavMesh 회피는 두 에이전트를 반지름 합(둘 다 0.5면 1.0m)보다 가깝게 두지 않는다.
    // 그보다 짧은 거리를 목표로 잡으면 파고드는 족족 회피가 도로 밀어낸다 — 실제로
    // 고블린(사거리 1.2)의 파고들기 목표가 0.84m라서, 붙는 순간 계속 밀려나고 있었다.
    [Tooltip("회피가 강제하는 최소 간격(두 에이전트의 반지름 합)에 더해 두는 여유(미터). " +
             "전투 중의 모든 목표 거리는 이 값 아래로 내려가지 않는다.")]
    [SerializeField] private float separationMargin = 0.15f;

    [Header("Attack Facing")]
    [Tooltip("스윙을 시작하려면 상대를 이 각도(도) 안쪽으로 마주 보고 있어야 한다. " +
             "타격 판정 각도(stats.attackArcAngle)의 절반보다 확실히 좁게 잡을 것 — 스윙이 " +
             "끝날 때까지 상대가 움직이므로 여유가 필요하다.")]
    [SerializeField, Range(5f, 90f)] private float attackFacingTolerance = 35f;

    [Header("Footwork Timing")]
    [Tooltip("발놀림을 시작하는 최소 여유 시간(초). 콤보 스텝 사이의 0.2초짜리 틈에 옆으로 " +
             "한 발 뗐다가 곧바로 다시 휘두르면 발놀림이 아니라 경련으로 보인다. " +
             "이 값보다 짧은 틈에는 자세만 잡고 서 있는다.")]
    [SerializeField] private float minFootworkWindow = 0.35f;
    [Tooltip("발놀림 속도가 붙고 빠지는 데 걸리는 시간(초). 0이면 즉시 최고 속도로 튄다.")]
    [SerializeField] private float footworkAcceleration = 6f;

    [Header("Avoidance")]
    [Tooltip("이동 중일 때 쓰는 지역 회피 품질. 교전 중에는 이것과 무관하게 회피를 꺼서 " +
             "제자리를 지키게 한다(TickAvoidance 주석 참조).")]
    [SerializeField] private ObstacleAvoidanceType movingAvoidanceQuality =
        ObstacleAvoidanceType.GoodQualityObstacleAvoidance;
    // NavMeshAgent는 숫자가 작을수록 우선순위가 높다(덜 밀린다).
    [Tooltip("제자리에서 싸우는 유닛의 회피 우선순위. 낮을수록 자리를 지킨다.")]
    [SerializeField, Range(0, 99)] private int engagedAvoidancePriority = 35;
    [Tooltip("이동 중인 유닛의 회피 우선순위. 높을수록 교전 중인 유닛에게 길을 비켜 준다.")]
    [SerializeField, Range(0, 99)] private int movingAvoidancePriority = 60;
    [Tooltip("회피 우선순위를 유닛마다 흩어 놓는 폭. 0이면 같은 처지의 유닛이 전부 같은 값을 " +
             "쓰는데, 우선순위가 같은 둘은 서로를 대칭으로 피하려 들어 같은 방향으로 함께 " +
             "비켰다가 되돌아오기를 반복한다 — 무리 지어 몰려갈 때 제자리에서 떠는 그 움직임이다. " +
             "값을 조금만 흩어 놓으면 둘 중 하나가 먼저 양보해서 교착이 풀린다.")]
    [SerializeField, Range(0, 20)] private int avoidancePrioritySpread = 8;

    // ---------------------------------------------------------------- 스윙(SwingPart)

    private SwingPart swingPart;
    private SwingPart Swing => swingPart ?? (swingPart = new SwingPart(this));

    // 준비 동작 중인가. 칼을 든 적만 막을 이유가 된다 — 이미 칼을 거두는 적(회수)에게 방패를 들면 헛돈다.
    public bool IsTelegraphing => Swing.IsTelegraphing;

    // 내지른 직후. 다음 동작으로 넘어가지도 못하고 막지도 못하는 구간이라 반격 기회가 된다.
    public bool IsInAttackRecovery => Swing.IsInRecovery;

    // 스윙과 스윙 사이의 호흡이 끝났는가. 클립이 끝나자마자 다음 스윙이 나가면 쉼 없이
    // 칼을 돌리는 기계처럼 보인다.
    public bool IsSwingReady => Swing.IsReady;

    // 히트스톱으로 애니메이션이 느려진 만큼 상태 타이머도 같이 느려져야 한다.
    // 그러지 않으면 모션은 아직 절반인데 상태가 먼저 끝나 다음 동작으로 튄다.
    public float AnimatorSpeed => animator != null && animator.enabled ? animator.speed : 1f;

    private void CacheCombatAnimationHashes()
    {
        HitReaction.CacheHashes();
        Guard.CacheHashes();
        Leap.CacheHashes();
        Gait.CacheHashes();

        // 적의 어느 쪽으로 파고들지는 유닛마다 갈라 놓는다. 전원이 같은 쪽으로 돌면
        // 난전이 통째로 한쪽으로 흘러가 버린다(EngagementPart.RollFlankSide).
        Engagement.RollFlankSide();
    }

    // ---------------------------------------------------------------- 회전 주도권

    // 몸을 돌리는 주체를 코드로 넘길지, NavMeshAgent에게 맡길지 정한다.
    // 둘이 동시에 돌리면 유닛이 떤다(FacingPart.SetCodeDriven 주석 참조).
    public void SetCodeDrivenFacing(bool codeDriven) => Facing.SetCodeDriven(codeDriven);

    // ---------------------------------------------------------------- 간격

    // 상대와 실제로 붙을 수 있는 최소 거리.
    //
    // NavMesh 회피가 두 에이전트를 반지름 합보다 가깝게 두지 않기 때문에, 전투 로직이
    // 그보다 짧은 거리를 목표로 잡으면 "파고든다 → 회피가 밀어낸다"가 매 프레임 반복되어
    // 유닛이 떨리거나 뒤로 밀려나는 것처럼 보인다. 사거리에서 나온 값이든 비율에서 나온
    // 값이든, 전투 중의 목표 거리는 전부 이 값을 하한으로 깔고 계산해야 한다.
    public float SeparationFrom(TargetRef other)
    {
        float mine = agent != null ? agent.radius : 0.5f;
        float theirs = other.IsUnit && other.Unit.Agent != null ? other.Unit.Agent.radius : 0.5f;
        return mine + theirs + Mathf.Max(0f, separationMargin);
    }

    // 지금 노리는 상대 기준. 타깃이 없으면 자기 반지름 두 배로 어림한다.
    public float SeparationFromTarget()
    {
        return SeparationFrom(CurrentTarget);
    }

    // 교전 중 유지하려는 간격.
    //
    // 발놀림과 파고들기가 반드시 같은 값을 봐야 한다. 예전에는 파고들기만 더 안쪽
    // (회피가 허용하는 최소 간격)을 노렸다 — 실측으로 아군은 1.52m에 서려 하는데 파고들기는
    // 1.15m를 향했고, 그 1.15m가 마침 회피의 하한이라 파고든 만큼 그대로 도로 밀려났다.
    // 그래서 스윙마다 앞뒤로 미끄러졌다. 제자리에서 베는 모션이라 그 미끄러짐이 그대로 보인다.
    private float EngageDistance => Mathf.Max(
        SeparationFromTarget(),
        (stats.attackRange + stats.moveStopDistance) * stats.combatSpacingRatio);

    // 유지하려는 간격 안에(여유 포함) 아직 적이 남아 있는가.
    //
    // 여유를 두는 이유: 임계에 딱 맞춰 판단하면 그 경계에서 "물러남 → 안전 → 파고듦 → 다시 위협"이
    // 반복된다. 물러날 때보다 조금 더 멀어져야 다시 다가가게 해서 그 진동을 없앤다.
    private bool IsThreatWithinSpacing()
    {
        float threshold = KeepDistanceThreshold;
        if (threshold <= 0f) return false;

        return UnitRegistry.CountEnemiesAround(this, transform.position, threshold * 1.3f) > 0;
    }

    // ---------------------------------------------------------------- 파고들기

    // 준비 동작 동안 타깃 쪽으로 조금 파고든다. 예전에는 StopMovement로 완전히 못 박고
    // 휘둘렀기 때문에, 사거리 경계에서 시작한 스윙은 눈에 보이게 허공을 갈랐다.
    public void UpdateAttackLunge() => Swing.UpdateLunge();

    // ---------------------------------------------------------------- 도약 공격(LeapPart)

    private LeapPart leapPart;
    private LeapPart Leap => leapPart ?? (leapPart = new LeapPart(this));

    // 아직 칼이 닿지 않는 거리에서 몸을 던져 붙을 수 있는가(LeapPart 주석 참조).
    public bool CanLeapAttack() => Leap.CanLeap();

    public float LeapAttackAnimationDuration => Leap.Duration;

    // 클립에서 발이 땅을 떠나는 지점. 그 앞은 웅크림이라 LeapAttackBehavior가 그동안만 상대를 본다.
    public float LeapLaunchRatio => leapLaunchRatio;

    public void TriggerLeapAttack() => Leap.Trigger();

    // LeapAttackBehavior가 매 프레임 부른다. 받는 값은 클립의 진행도(0~1).
    public void UpdateLeap(float normalizedTime) => Leap.Update(normalizedTime);

    // 도약이 끝났거나 도중에 끊겼다. 위치의 주도권을 에이전트에게 돌려준다.
    public void EndLeap() => Leap.End();

    // ---------------------------------------------------------------- 목 물기(ClingPart)

    private ClingPart clingPart;
    private ClingPart Cling => clingPart ?? (clingPart = new ClingPart(this));

    // 스킬을 쓰는 동안 상대의 목에 매달린다(ClingPart 주석 참조).
    public void BeginCling() => Cling.Begin();

    // SkillBehavior가 매 프레임 부른다. progress는 스킬 모션의 진행도(0~1).
    public void UpdateCling(float progress) => Cling.Update(progress);

    public void EndCling() => Cling.End();

    // ---------------------------------------------------------------- 발놀림(FootworkPart)

    private FootworkPart footworkPart;
    private FootworkPart Footwork => footworkPart ?? (footworkPart = new FootworkPart(this));

    // 다음 스윙을 기다리는 동안의 움직임. 간격을 맞춘다(FootworkPart 주석 참조).
    public void UpdateCombatFootwork() => Footwork.Update();

    // 교전 중 제자리에 설 때의 자세. 평소 Idle은 칼을 내린 자세라 쓰지 않는다(GaitPart.PlayCombatIdle 주석 참조).
    public void PlayCombatIdle() => Gait.PlayCombatIdle();

    // 나아가는 쪽에 맞는 다리를 고른다. 발놀림과 접근이 같이 쓴다.
    //
    // runWhenForward: 앞으로 가는 구간을 달리기로 칠지. 발놀림은 걷기고(제자리에서 재는
    // 동작이라 달리면 안 된다), 접근은 달리기다.
    public void PlayMoveAnimationForDirection(Vector3 move, Vector3 forward, float speed, bool runWhenForward = true) =>
        Gait.PlayForDirection(move, forward, speed, runWhenForward);

    // 물러날 때의 다리. 뒷걸음 클립이 있으면 그것으로, 없는 리그는 달리기로 물러난다.
    public void PlayRetreatAnimation(float speed) => Gait.PlayRetreat(speed);

    // ---------------------------------------------------------------- 피격 모션(HitReactionPart)

    private HitReactionPart hitReactionPart;
    private HitReactionPart HitReaction => hitReactionPart ?? (hitReactionPart = new HitReactionPart(this));

    // ---------------------------------------------------------------- 막기(GuardPart)
    //
    // 날아오는 칼을 알아채고(인지) 반응 시간을 채운 뒤 자세를 든다. 인지와 실행을 나눈 이유,
    // 퍼펙트 가드의 두 조건은 GuardPart 주석에 있다.

    private GuardPart guardPart;
    private GuardPart Guard => guardPart ?? (guardPart = new GuardPart(this));

    // 지금 방패를 올릴 수 있고, 올릴 이유도 있는가. 막을 상대를 함께 잡아 둔다 —
    // 그래서 순수 판정이 아니며 UnitBehaviorTree가 반드시 첫 줄에서 부른다.
    public bool CanBlock() => Guard.CanBlock();

    // 방어 중 나를 노리는 적을 향해 돈다. BlockBehavior가 매 프레임 불러 방패 방향을 맞춘다.
    public void FaceBlockThreat() => Guard.FaceThreat();

    // 방어 중 나를 노리고 휘두르는 적을 다시 찾는다. 돌려주는 값은 "아직 막을 것이 남았는가".
    public bool RefreshBlockThreat() => Guard.RefreshThreat();

    public void SetBlocking(bool isBlocking) => Guard.SetBlocking(isBlocking);

    // ---------------------------------------------------------------- 막기가 먼저다

    // 나를 노리고 칼을 들어올린 적을 봤다. 반응 시간이 아직 안 끝났어도 참이다.
    //
    // 이 동안은 새 스윙을 시작하지 않는다(CanAttack·스킬·도약). 막는 것이 치는 것보다 먼저다.
    // 예전에는 반응 시간이 도는 사이에도 칼이 나갔다 — 실측(12층, 5인 90초): 맞은 303대 중 179대(59%)가
    // 제 스윙에 묶여 있던 중이었고 막으면서 받은 것은 12대였다. 적의 준비 동작은 0.4초인데 아군의
    // 스윙은 0.5~1.7초라, 한 번 휘두르기 시작하면 그 사이에 들어오는 칼을 받을 방법이 없었다.
    // 이제는 상대가 칼을 드는 동안은 기다렸다 막고, 그 칼이 지나간 뒤(상대의 회수 동작)에 친다 —
    // 원작의 "적의 턴에는 받고 후딜에만 넣는다"가 이것이다.
    public bool IsHoldingForGuard => Guard.IsHoldingForGuard;

    // 휘두르던 것을 거두고 막을 수 있는가.
    //
    // 내지르기 전(준비 동작)과, 내지르고 칼을 거두는 동작(회수) 모두다. 예전에는 준비 동작만 거둘 수
    // 있었는데, 무거운 무기일수록 클립의 절반 넘게가 회수라(도끼 1.67초) 그 구간에 들어온 칼은 전부
    // 맞았다. 칼을 거두는 중에 방패를 끌어올리는 것은 훈련받은 사람이면 누구나 하는 동작이다.
    // 끊지 못하는 것은 실제로 칼이 나가는 그 짧은 순간(타격 직후 SwingPart.GuardFollowThrough)뿐이다.
    public bool CanCancelSwingIntoGuard => Swing.CanCancelIntoGuard;

    // 방어 자세로 들어가며 휘두르던 스윙을 버린다. BlockBehavior가 자세를 잡기 직전에 부른다.
    //
    // 잠금을 풀지 않으면 방패를 든 채로도 IsAttackAnimationLocked가 참으로 남아, 방어가 끝난 뒤
    // AttackBehavior가 이미 버린 스윙을 "휘두르는 중"으로 알고 기다린다. 준비 동작에서 버린 칼은
    // 타격 이벤트가 섞여 들어와도 닿지 않아야 한다(SwingPart.ResolveHit).
    public void CancelSwingForGuard() => Swing.CancelForGuard();

    // ---------------------------------------------------------------- 강인도·경직(PosturePart)

    private PosturePart posturePart;
    private PosturePart Posture => posturePart ?? (posturePart = new PosturePart(this));

    public bool IsStaggered => Posture.IsStaggered;
    public float PendingStaggerDuration => Posture.PendingDuration;
    public bool StaggerFromGuardBreak => Posture.FromGuardBreak;

    // 자세가 완전히 무너진다(PosturePart.Stagger 주석 참조).
    public void Stagger(float duration, bool fromGuardBreak = false) => Posture.Stagger(duration, fromGuardBreak);

    // 붙잡아 무너뜨리는 공격이 부른다. 면역 중이었으면 false(PosturePart.TryForce 주석 참조).
    public bool TryForceStagger(float duration) => Posture.TryForce(duration);

    public void TriggerStagger(bool fromGuardBreak) => HitReaction.PlayStagger(fromGuardBreak);

    // ---------------------------------------------------------------- 둔화·피격 둔화(SlowPart)

    private SlowPart slowPart;
    private SlowPart Slow => slowPart ?? (slowPart = new SlowPart(this));

    // 지금 걸려 있는 둔화 배율. 이동 속도와 걸음 재생 배속 양쪽에 곱해진다.
    public float SlowMultiplier => Slow.SlowMultiplier;

    public float FlinchMultiplier => Slow.FlinchMultiplier;

    public void ApplyHitFlinch() => Slow.ApplyFlinch();

    public void ApplySlow(float duration, float multiplier) => Slow.ApplySlow(duration, multiplier);

    // ---------------------------------------------------------------- 영창

    // 마력을 모으는 중인가. 치유와 스킬 시전이 여기 들어온다.
    //
    // 이 플래그 하나가 원작의 "영창 중 무방비"를 성립시킨다 — TakeDamage가 이걸 보고
    // castVulnerabilityMultiplier를 곱하고, 피격은 어차피 InterruptCurrentAction으로
    // 시전을 끊는다. 즉 후방 시전자는 탱커가 벌어 준 시간 안에서만 영창을 끝낼 수 있다.
    public bool IsCasting { get; private set; }

    public void BeginCast() => IsCasting = true;
    public void EndCast() => IsCasting = false;

    // ---------------------------------------------------------------- 교전 방위·교전 시간(EngagementPart)

    private EngagementPart engagementPart;
    private EngagementPart Engagement => engagementPart ?? (engagementPart = new EngagementPart(this));

    // 파고들 방위를 가진 직군인가(EngagementPart.HasPreference 주석 참조).
    public bool HasEngagePreference => Engagement.HasPreference;

    // 다음 접근에서 방위를 새로 고르게 한다. 접근을 시작하는 쪽이 부른다.
    public void ClearEngageBearing() => Engagement.ClearBearing();

    // 접근 중에 실제로 향할 지점. 타깃 위치가 아니라 "타깃 주위에서 내가 서고 싶은 자리"다.
    public Vector3 GetEngageDestination(float standoffDistance) => Engagement.GetDestination(standoffDistance);

    // ---------------------------------------------------------------- 히트스톱(HitStopPart)

    private HitStopPart hitStopPart;
    private HitStopPart HitStop => hitStopPart ?? (hitStopPart = new HitStopPart(this));

    // 칼이 닿은 순간 아주 짧게 애니메이션을 눌러 붙인다(HitStopPart 주석 참조).
    public void ApplyHitStop(float duration, float scale) => HitStop.Apply(duration, scale);

    public void ClearHitStop() => HitStop.Clear();

    // 손에 쥔 것으로 직접 쳤다고 볼 거리 안인가. 화살·마법탄을 쏜 쪽은 멈추지 않는다.
    public bool IsWithinHitStopReach(Vector3 victimPosition) => HitStop.IsWithinReach(victimPosition);

    // 엔티티를 쳤다(TargetRef.TakeDamage). 친 쪽만 멈춘다.
    public void OnStruckEntity(Vector3 victimPosition, float impactWeight) =>
        HitStop.OnStruckEntity(victimPosition, impactWeight);

    // ---------------------------------------------------------------- 지역 회피(AvoidancePart)

    private AvoidancePart avoidancePart;
    private AvoidancePart Avoidance => avoidancePart ?? (avoidancePart = new AvoidancePart(this));

    // ---------------------------------------------------------------- 고지 선점(HighGroundPart)

    private HighGroundPart highGroundPart;
    private HighGroundPart HighGround => highGroundPart ?? (highGroundPart = new HighGroundPart(this));

    // 쏘기 좋은 높은 자리가 있으면 그리로 간다(궁수).
    public bool TryFindHighGround(Vector3 desiredSpot, out Vector3 highGround) =>
        HighGround.TryFind(desiredSpot, out highGround);

    // ---------------------------------------------------------------- 은신(StealthPart)

    private StealthPart stealthPart;
    private StealthPart Stealth => stealthPart ?? (stealthPart = new StealthPart(this));

    public bool IsStealthed => Stealth.IsStealthed;

    // 은신이 풀리는 두 순간: 내가 때렸을 때와 내가 맞았을 때.
    public void BreakStealth() => Stealth.Break();

    // 매 프레임 돌려야 하는 전투 잔무. UnitController.Update가 행동 트리보다 먼저 부른다.
    private void TickCombat()
    {
        Stealth.Tick();
        HitStop.Tick();
        Guard.TickPose();
        Avoidance.Tick();
        Guard.TickAwareness();
        Slow.Tick();
        Engagement.TickDwell();
    }

    // 죽은 유닛을 되살려 재사용하는 경로(Configure)를 위한 초기화.
    // 여기 남아 있던 값은 전부 "지난 전투의 마지막 순간"이라, 그대로 두면 스폰 직후
    // 경직 상태이거나 히트스톱으로 애니메이션이 멈춘 채 시작한다.
    private void ResetCombatRuntime()
    {
        Swing.Reset();
        Posture.Reset();
        Guard.Reset();
        HitReaction.Reset();
        EndDodgeMove();
        Footwork.Reset();
        Engagement.Reset();
        Spacing.ForgetRetreatDirection();
        // 재사용되는 유닛이 도약 도중에 회수됐다면 모델이 떠 있는 채로 남는다.
        Leap.Reset();
        // 목을 문 채로 회수됐다면 NavMesh가 꺼진 채로 남는다.
        EndCling();
        // 남은 스킬 사용 횟수는 stats 쪽에 있고, 전투마다 프리팹에서 복제되므로 저절로 다시 찬다.
        Skill.Reset();
        Slow.Reset();
        IsCasting = false;
        SupportCast.Reset();
        ResetMagicRuntime();
        ResetCommandRuntime();
        HitStop.Clear();
    }
}
