using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

// UnitController의 "한 번의 교전이 실제로 어떻게 굴러가는가" 쪽.
//
// 본체(UnitController.cs)는 스탯·타깃 선정·이동·애니메이션 재생처럼 전투가 아닌 상황에도
// 필요한 뼈대를 들고 있다. 여기 모아 둔 것은 칼이 오가는 순간에만 의미가 있는 것들이다:
//
//   1) 스윙 페이즈    — 준비 / 타격 / 회수를 나눈다. 예전에는 클립 전체가 통째로 "공격 중"이라
//                      이미 때리고 칼을 거두는 적도 "휘두르는 중"으로 잡혀 방어가 헛돌았다.
//   2) 타격 판정      — 애니메이션 이벤트 시점에 거리와 각도를 다시 본다. 예전에는 타깃이
//                      어디로 도망쳤든 무조건 맞았다(빗나감이라는 것이 없었다).
//   3) 발놀림/파고들기 — 사거리에 들어가면 못 박혀 서 있던 것을, 간격을 재고 옆으로 도는 쪽으로.
//   4) 방향 리액션    — 어디서 맞았는지에 따라 다른 모션. 막았을 때의 반동과 가드 브레이크도.
//   5) 히트스톱       — 칼이 살에 닿는 순간 아주 짧게 애니메이션을 눌러 붙인다.
//
// 파일을 나눈 건 본체가 이미 1400줄이기 때문이다. 같은 클래스이므로 서로의 private 필드를
// 그대로 쓴다 — 아래 코드가 attackLockedUntil이나 animator를 직접 만지는 이유다.
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

    private Vector3 footworkVelocity;

    private int hitFrontAnimationHash;
    private int hitBackAnimationHash;
    private int hitLeftAnimationHash;
    private int hitRightAnimationHash;
    private int blockHitAnimationHash;
    private int blockBreakAnimationHash;
    private int staggerAnimationHash;
    private int strafeLeftAnimationHash;
    private int strafeRightAnimationHash;
    private int strafeBackAnimationHash;
    private int combatIdleAnimationHash;

    // 이번 틈에 발놀림을 할지. 틈이 시작될 때(TriggerAttack) 한 번만 정한다 —
    // 매 프레임 "남은 시간"으로 판단하면 스윙 직전 minFootworkWindow 동안은 항상 멈춰 서게 되어,
    // 공격이 끝날 때마다 잠깐씩 굳는 것처럼 보인다.
    private bool footworkThisGap;

    private float blockHitAnimationDuration;
    private float staggerAnimationDuration;

    // --- 스윙 페이즈 ---
    // 이번 스윙의 타격 이벤트가 이미 지나갔는가. 준비 동작(아직 안 지나감)과 회수 동작(지나감)을
    // 가르는 유일한 근거다. 클립마다 이벤트 시각이 다르므로 시간으로 추정하지 않고 실제 이벤트로 안다.
    private bool hasStruckThisSwing;
    // 이 유닛이 한 번이라도 공격을 휘둘렀는가. 스킬을 여는 수로 쓰지 않기 위한 것이다
    // (CanUseSkill, IsComboRecoveryPoint 주석 참조).
    private bool hasSwungAtLeastOnce;
    private float nextSwingReadyTime;
    private float lungeRemaining;

    // --- 발놀림 ---
    private float strafeSign = 1f;
    private float nextStrafeFlipTime;

    // --- 히트스톱 ---
    private bool hitStopActive;
    private float hitStopUntil;

    // --- 방어/경직 ---
    // 이번에 올린 자세가 흘려낼 수 있는 자세인가. 방패를 드는 그 순간에 한 번 정해진다 —
    // 맞을 때마다 굴리면 같은 자세로 여러 대를 받는 동안 결과가 오락가락한다.
    private bool perfectGuardArmed;
    private float guardRaisedTime = -999f;
    private float blockImpactUntil;
    private float staggerEndTime;
    private float pendingStaggerDuration;

    // 예전에는 "공격 애니메이션이 재생 중"이면 전부 휘두르는 중으로 봤다. 도끼처럼 1.7초짜리
    // 클립은 절반 이상이 칼을 거두는 동작이라, 방어자가 이미 지나간 공격에 대고 방패를 들었다.
    public bool IsTelegraphing => IsAttackAnimationLocked && !hasStruckThisSwing;

    // 내지른 직후. 다음 동작으로 넘어가지도 못하고 막지도 못하는 구간이라 반격 기회가 된다.
    public bool IsInAttackRecovery => IsAttackAnimationLocked && hasStruckThisSwing;

    // 스윙과 스윙 사이의 호흡이 끝났는가. 클립이 끝나자마자 다음 스윙이 나가면 쉼 없이
    // 칼을 돌리는 기계처럼 보인다.
    public bool IsSwingReady => Time.time >= nextSwingReadyTime;

    // 옆걸음 클립을 하나라도 갖고 있는가. 없으면 발놀림이 간격 조절만 한다.
    public bool HasStrafeAnimation => strafeLeftAnimationHash != 0 || strafeRightAnimationHash != 0;

    public bool IsStaggered => Time.time < staggerEndTime;
    public float PendingStaggerDuration => pendingStaggerDuration;
    public float StaggerAnimationDuration => staggerAnimationDuration;

    // 히트스톱으로 애니메이션이 느려진 만큼 상태 타이머도 같이 느려져야 한다.
    // 그러지 않으면 모션은 아직 절반인데 상태가 먼저 끝나 다음 동작으로 튄다.
    public float AnimatorSpeed => animator != null && animator.enabled ? animator.speed : 1f;

    private void CacheCombatAnimationHashes()
    {
        hitFrontAnimationHash = ResolveStateHash(hitFrontStateName);
        hitBackAnimationHash = ResolveStateHash(hitBackStateName);
        hitLeftAnimationHash = ResolveStateHash(hitLeftStateName);
        hitRightAnimationHash = ResolveStateHash(hitRightStateName);
        blockHitAnimationHash = ResolveStateHash(blockHitStateName);
        blockBreakAnimationHash = ResolveStateHash(blockBreakStateName);
        staggerAnimationHash = ResolveStateHash(staggerStateName);
        strafeLeftAnimationHash = ResolveStateHash(strafeLeftStateName);
        strafeRightAnimationHash = ResolveStateHash(strafeRightStateName);
        strafeBackAnimationHash = ResolveStateHash(strafeBackStateName);
        combatIdleAnimationHash = ResolveStateHash(combatIdleStateName);

        blockHitAnimationDuration = blockHitAnimationHash != 0
            ? GetAnimationClipDuration(blockHitStateName, 0.3f)
            : 0f;
        staggerAnimationDuration = staggerAnimationHash != 0
            ? GetAnimationClipDuration(staggerStateName, 1f)
            : hitAnimationDuration;

        // 옆으로 도는 방향은 유닛마다 다르게 시작한다. 전부 같은 방향으로 돌면
        // 난전이 통째로 한쪽으로 흘러가 버린다.
        strafeSign = Random.value < 0.5f ? -1f : 1f;
        nextStrafeFlipTime = Time.time + Random.Range(0.5f, 1.5f) * Mathf.Max(0.1f, stats.strafeFlipInterval);
    }

    // ---------------------------------------------------------------- 타격 판정

    // 스윙이 실제로 닿는 거리. 사거리에 정지 거리와 여유를 더한 값으로,
    // IsTargetInAttackRange(공격을 시작할지 판단하는 쪽)보다 attackHitTolerance만큼 넓다.
    // 서로 조금씩 움직이는 중이라 시작 조건과 명중 조건이 똑같으면 정상적인 교전에서도
    // 헛스윙만 나온다.
    private float SwingReach => stats.attackRange + stats.moveStopDistance + stats.attackHitTolerance;

    private bool IsInsideSwingArc(UnitController candidate, float reach)
    {
        if (candidate == null) return false;

        Vector3 toCandidate = candidate.transform.position - transform.position;
        toCandidate.y = 0f;

        float sqrDistance = toCandidate.sqrMagnitude;
        if (sqrDistance > reach * reach) return false;
        if (sqrDistance <= 0.0001f) return true;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f) return true;

        float minDot = Mathf.Cos(Mathf.Clamp(stats.attackArcAngle * 0.5f, 0f, 180f) * Mathf.Deg2Rad);
        return Vector3.Dot(forward.normalized, toCandidate / Mathf.Sqrt(sqrDistance)) >= minDot;
    }

    // 타격 이벤트 시점에 "이 스윙이 누구를 맞혔는가"를 정한다.
    // 노리던 상대가 빠져나갔어도 궤적 안에 다른 적이 서 있으면 그쪽이 맞는다 —
    // 휘두른 칼은 눈앞에 있는 놈을 벤다.
    private UnitController ResolveSwingVictim()
    {
        float reach = SwingReach;

        if (IsTargetValid() && IsInsideSwingArc(CurrentTarget, reach)) return CurrentTarget;
        if (!stats.cleaveOffTarget) return null;

        // 원거리 유닛은 해당 없다. 활은 겨눈 하나를 쏘는 것이지 앞을 쓸어 베는 것이 아니라,
        // 노리던 상대가 빠졌으면 그냥 빗나가야 한다. 여기서 막지 않으면 사거리 9m짜리가
        // 정면 130도 안의 아무나 자동으로 맞히는, 사실상 공짜 재조준이 된다.
        if (stats.attackRange >= minKeepDistanceRange) return null;

        return UnitRegistry.FindEnemyInArc(this, reach, stats.attackArcAngle);
    }

    // 헛스윙. 피해가 없는 것으로 끝내지 않고 회수 시간을 늘려 벌을 준다 —
    // 그래야 사거리를 재는 것과 무작정 휘두르는 것이 갈린다.
    private void OnSwingMissed()
    {
        nextSwingReadyTime += stats.attackRecoveryTime * 0.6f;
        if (debugLogs) Debug.Log($"[UnitController] {name} 헛스윙 (사거리 {SwingReach:0.00}m / 각도 {stats.attackArcAngle:0}도)");
    }

    // ---------------------------------------------------------------- 회전 주도권

    // 몸을 돌리는 주체를 코드로 넘길지, NavMeshAgent에게 맡길지 정한다.
    //
    // 둘이 동시에 돌리면 유닛이 떤다. NavMeshAgent(updateRotation)는 "가고 있는 쪽"으로
    // 돌리고 FaceTarget은 "노리는 쪽"으로 돌리는데, 이 둘이 어긋나는 상황이 전투의 대부분이다:
    //  - 쫓아갈 때는 예측 위치로 달리면서 상대를 봐야 하고,
    //  - 옆으로 돌 때는 진행 방향이 아예 90도 옆이라 정반대로 당긴다.
    // 그래서 상대를 보는 상태(Chase/Attack/Block)에서는 코드가 회전을 통째로 가져오고,
    // 그 밖(이동·배회·회피)에서는 예전처럼 진행 방향을 보도록 에이전트에게 돌려준다.
    public void SetCodeDrivenFacing(bool codeDriven)
    {
        if (agent == null) return;
        agent.updateRotation = !codeDriven;
    }

    // ---------------------------------------------------------------- 간격

    // 상대와 실제로 붙을 수 있는 최소 거리.
    //
    // NavMesh 회피가 두 에이전트를 반지름 합보다 가깝게 두지 않기 때문에, 전투 로직이
    // 그보다 짧은 거리를 목표로 잡으면 "파고든다 → 회피가 밀어낸다"가 매 프레임 반복되어
    // 유닛이 떨리거나 뒤로 밀려나는 것처럼 보인다. 사거리에서 나온 값이든 비율에서 나온
    // 값이든, 전투 중의 목표 거리는 전부 이 값을 하한으로 깔고 계산해야 한다.
    public float SeparationFrom(UnitController other)
    {
        float mine = agent != null ? agent.radius : 0.5f;
        float theirs = other != null && other.Agent != null ? other.Agent.radius : 0.5f;
        return mine + theirs + Mathf.Max(0f, separationMargin);
    }

    // 지금 노리는 상대 기준. 타깃이 없으면 자기 반지름 두 배로 어림한다.
    public float SeparationFromTarget()
    {
        return SeparationFrom(CurrentTarget);
    }

    // ---------------------------------------------------------------- 파고들기

    // 준비 동작 동안 타깃 쪽으로 조금 파고든다. 예전에는 StopMovement로 완전히 못 박고
    // 휘둘렀기 때문에, 사거리 경계에서 시작한 스윙은 눈에 보이게 허공을 갈랐다.
    public void UpdateAttackLunge()
    {
        if (!IsTelegraphing) return;
        if (stats.lungeSpeed <= 0f || lungeRemaining <= 0f) return;
        if (!IsTargetValid()) return;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Vector3 toTarget = CurrentTarget.transform.position - transform.position;
        toTarget.y = 0f;

        float distance = toTarget.magnitude;
        // 파고들어 멈출 지점. 사거리 안쪽 깊숙이가 아니라 "칼이 편하게 닿는" 정도까지만 간다.
        // 회피가 허용하는 최소 간격이 하한이다 — 그보다 안쪽을 노리면 파고든 만큼 도로 밀려난다.
        float contactDistance = Mathf.Max(SeparationFromTarget(), stats.attackRange * 0.7f);
        if (distance <= contactDistance) return;

        float step = Mathf.Min(stats.lungeSpeed * Time.deltaTime, lungeRemaining, distance - contactDistance);
        if (step <= 0f) return;

        lungeRemaining -= step;
        agent.Move(toTarget / distance * step);
    }

    // ---------------------------------------------------------------- 발놀림

    // 다음 스윙을 기다리는 동안의 움직임. 간격을 맞추고 옆으로 돈다.
    // NavMeshAgent의 경로 탐색을 쓰지 않고 agent.Move로 직접 미는 이유는, 이 정도의
    // 짧은 조정에 매 프레임 SetDestination을 부르면 경로 계산만 잔뜩 쌓이기 때문이다.
    public void UpdateCombatFootwork()
    {
        if (!IsTargetValid()) return;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        // 이번 틈에 자리를 옮기기로 했는가. 짧은 틈(콤보 스텝 사이)에 옆으로 한 발 떼고
        // 곧바로 다시 휘두르면 발놀림이 아니라 잔떨림으로 보이므로, 긴 틈에서만 움직인다.
        //
        // 판단은 틈이 시작될 때 이미 끝나 있다(TriggerAttack). 예전에는 여기서 매 프레임
        // "남은 시간 < minFootworkWindow"를 다시 봤는데, 그러면 어떤 틈이든 스윙 직전
        // 0.35초는 반드시 제자리에 멈춰 서게 된다 — 공격 후 멈칫하던 것의 정체가 이거였다.
        if (!footworkThisGap)
        {
            StopFootwork();
            return;
        }

        float speed = stats.walkSpeed * stats.footworkSpeedRatio * EmotionMultiplier;
        if (speed <= 0.01f)
        {
            StopFootwork();
            return;
        }

        Vector3 toTarget = CurrentTarget.transform.position - transform.position;
        toTarget.y = 0f;

        float distance = toTarget.magnitude;
        if (distance <= 0.0001f) return;
        Vector3 forward = toTarget / distance;

        Vector3 direction = Vector3.zero;

        // 간격 조절. 너무 붙으면 물러서고 멀면 파고든다.
        // 목표 간격의 하한은 회피가 허용하는 최소 거리다(SeparationFrom 주석 참조).
        float idealDistance = Mathf.Max(SeparationFromTarget(),
            (stats.attackRange + stats.moveStopDistance) * stats.combatSpacingRatio);
        float gap = distance - idealDistance;

        // 비례 제어. 예전에는 Mathf.Sign으로 늘 최고 속도로 밀어서, 이상 간격을 지나칠 때마다
        // 방향이 뒤집혀 그 주위를 진동했다. 가까울수록 약하게 밀어야 그 자리에서 멎는다.
        if (Mathf.Abs(gap) > 0.12f) direction += forward * Mathf.Clamp(gap / 0.7f, -1f, 1f);

        // 옆으로 돌기. 방향은 주기적으로 뒤집어 한쪽으로만 도는 것을 막는다.
        //
        // 옆걸음 클립이 없는 리그(고블린)는 돌지 않고 간격만 맞춘다. 앞으로 달리는 다리로
        // 게걸음을 치면 발이 대놓고 미끄러지는데, 그건 안 도는 것보다 나쁘다.
        if (stats.strafeFlipInterval > 0f && HasStrafeAnimation)
        {
            if (Time.time >= nextStrafeFlipTime)
            {
                strafeSign = -strafeSign;
                nextStrafeFlipTime = Time.time + Random.Range(0.6f, 1.4f) * stats.strafeFlipInterval;
            }

            direction += Vector3.Cross(Vector3.up, forward) * (strafeSign * 0.85f);
        }

        // 목표 속도로 곧바로 튀지 않고 붙였다 뺀다. 정지 → 최고 속도가 한 프레임에 일어나면
        // 발이 땅을 딛기 전에 몸이 먼저 나간다.
        Vector3 desired = direction.sqrMagnitude > 0.0001f ? direction.normalized * speed : Vector3.zero;
        footworkVelocity = Vector3.MoveTowards(footworkVelocity, desired,
            Mathf.Max(0.01f, footworkAcceleration) * speed * Time.deltaTime);

        float currentSpeed = footworkVelocity.magnitude;
        if (currentSpeed <= 0.05f)
        {
            SetMoveAnimation(0f, false, false);
            return;
        }

        agent.Move(footworkVelocity * Time.deltaTime);
        PlayFootworkAnimation(footworkVelocity / currentSpeed, forward, currentSpeed);
    }

    // 발놀림을 멈춘다. 속도를 0으로 되돌려 두지 않으면 다음 번에 이전 방향으로 한 번 튄다.
    private void StopFootwork()
    {
        footworkVelocity = Vector3.zero;
        PlayCombatIdle();
    }

    // 교전 중 제자리에 설 때의 자세.
    //
    // 평소 Idle을 쓰면 안 된다. 그건 칼을 내리고 긴장을 푼 자세라, 스윙과 스윙 사이의
    // 0.2초짜리 틈마다 "공격 → 긴장 풀림 → 공격"이 반복되어 매번 멈칫하는 것처럼 보인다.
    // 전용 자세가 없는 리그(고블린)는 예전처럼 Idle로 떨어진다.
    public void PlayCombatIdle()
    {
        PlayAnimation(combatIdleAnimationHash != 0 ? combatIdleAnimationHash : idleAnimationHash, false);
    }

    // 이동 방향을 유닛 기준으로 풀어 스트레이프 클립을 고른다. 전용 클립이 없는 리그는
    // 예전처럼 걷기로 대신한다(발이 조금 미끄러지지만 서서 순간이동하는 것보다는 낫다).
    private void PlayFootworkAnimation(Vector3 move, Vector3 forward, float speed)
    {
        float forwardDot = Vector3.Dot(move, forward);
        float rightDot = Vector3.Dot(move, Vector3.Cross(Vector3.up, forward));

        int hash = 0;
        float clipSpeed = strafeClipSpeed;
        if (forwardDot < -0.5f)
        {
            hash = strafeBackAnimationHash;
            clipSpeed = strafeBackClipSpeed;
        }
        else if (Mathf.Abs(rightDot) > 0.4f) hash = rightDot > 0f ? strafeRightAnimationHash : strafeLeftAnimationHash;

        if (hash == 0)
        {
            SetMoveAnimation(speed, false, false);
            return;
        }

        // 클립마다 원래 나아가는 속도가 다르다. 실제 이동 속도를 그 값으로 나눠 배속을 준다.
        // 이 배속은 StrafeLeft/Right/Back 상태의 Speed Multiplier가 MoveSpeedMultiplier에 묶여
        // 있어야 실제로 먹는다 — 안 묶여 있으면 값만 넘어가고 클립은 제 속도로 재생된다.
        ApplyMoveAnimationSpeed(speed, clipSpeed);
        PlayAnimation(hash, false);
    }

    // 물러날 때의 다리. 뒷걸음 클립이 있으면 그것으로, 없는 리그는 예전처럼 달리기로 물러난다.
    // 회피 모션(Dodge)은 한 번 재생되고 끝나므로, 남은 거리는 이쪽이 이어받는다.
    public void PlayRetreatAnimation(float speed)
    {
        if (strafeBackAnimationHash == 0)
        {
            SetMoveAnimation(speed, true, false);
            return;
        }

        ApplyMoveAnimationSpeed(speed, strafeBackClipSpeed);
        PlayAnimation(strafeBackAnimationHash, false);
    }

    // ---------------------------------------------------------------- 방향 리액션

    // 공격자가 내 어느 쪽에 있는지에 따라 다른 피격 모션을 고른다.
    // 하나라도 없으면 그 방향만 기본 Hit으로 떨어진다 — 전부 갖추지 않아도 동작한다.
    private int ResolveDirectionalHitHash(Vector3 attackerPosition)
    {
        if (hitFrontAnimationHash == 0 && hitBackAnimationHash == 0 &&
            hitLeftAnimationHash == 0 && hitRightAnimationHash == 0)
        {
            return hitAnimationHash;
        }

        Vector3 toAttacker = attackerPosition - transform.position;
        toAttacker.y = 0f;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (toAttacker.sqrMagnitude <= 0.0001f || forward.sqrMagnitude <= 0.0001f)
        {
            return hitFrontAnimationHash != 0 ? hitFrontAnimationHash : hitAnimationHash;
        }

        float angle = Vector3.SignedAngle(forward, toAttacker, Vector3.up);
        float absAngle = Mathf.Abs(angle);

        int hash;
        if (absAngle <= 50f) hash = hitFrontAnimationHash;
        else if (absAngle >= 130f) hash = hitBackAnimationHash;
        else hash = angle > 0f ? hitRightAnimationHash : hitLeftAnimationHash;

        return hash != 0 ? hash : hitAnimationHash;
    }

    // 마지막으로 맞은 방향. TakeDamage가 기록하고 HitState가 모션을 고를 때 읽는다.
    // 상태에 인자를 넘기지 않고 여기 두는 이유는, 상태 객체가 유닛마다 하나씩 재사용되기 때문이다 —
    // Enter에 값을 실어 보낼 통로가 없다.
    private Vector3 lastHitAttackerPosition;
    private bool hasLastHitAttacker;

    private void RecordHitDirection(UnitController attacker)
    {
        hasLastHitAttacker = attacker != null;
        if (hasLastHitAttacker) lastHitAttackerPosition = attacker.transform.position;
    }

    // 어디서 맞았는지 모르는 경우(출혈 등)는 기본 피격 모션으로 친다.
    private int ResolveHitAnimationHash()
    {
        return hasLastHitAttacker ? ResolveDirectionalHitHash(lastHitAttackerPosition) : hitAnimationHash;
    }

    // ---------------------------------------------------------------- 반응 시간

    private UnitController noticedThreat;
    private float noticedThreatTime;
    private float noticedThreatDelay;

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
    private void TickThreatAwareness()
    {
        // 애초에 막을 수 없는 유닛은 훑을 이유가 없다. 전 유닛이 매 프레임 적대 팀 전체를
        // 순회하면 유닛 수의 제곱으로 비용이 커진다(TargetScanner가 스캔 주기를 흩어 놓는 것과 같은 이유).
        if (!CanEverBlock())
        {
            noticedThreat = null;
            return;
        }

        UnitController threat = UnitRegistry.FindTelegraphingAttacker(this);
        if (threat == null)
        {
            // 준비 동작이 끝났다(내질렀거나 끊겼다). 다음 스윙은 새 반응 판정을 받는다 —
            // 스윙 하나에 반응 굴림 하나.
            noticedThreat = null;
            return;
        }

        if (threat == noticedThreat) return;

        // 새 위협을 처음 본 순간. 반응 시간은 유닛마다 다르게 뽑아 전원이 같은 박자로
        // 방패를 올리는 것을 막는다.
        noticedThreat = threat;
        noticedThreatTime = Time.time;
        noticedThreatDelay = stats.blockReactionTime * Random.Range(0.6f, 1.4f);
    }

    // 방어라는 수단 자체를 가진 상태인가. 지금 막을 이유가 있는지(위협)와는 별개다.
    private bool CanEverBlock()
    {
        if (IsDead) return false;
        // 방어는 플레이어(아군)만 한다. 적은 방패/무기로 막지 않고 그대로 맞는다.
        if (team != UnitTeam.Ally) return false;
        if (blockAnimationHash == 0) return false;
        // 자세가 무너져 있는 동안은 방패를 들 수 없다. 예전에는 방어 지구력이 이 역할까지
        // 겸했지만(0이면 자세를 안 잡음), 지금은 강인도가 깨지면 곧바로 Stagger로 들어가므로
        // 그 상태만 막으면 된다.
        if (IsStaggered) return false;
        return Time.time >= lastBlockTime + stats.blockCooldown;
    }

    // 알아챈 위협에 반응까지 마쳤고, 그 위협이 아직 칼을 내지르지 않았는가.
    private bool HasReactedToThreat =>
        noticedThreat != null &&
        !noticedThreat.IsDead &&
        noticedThreat.IsTelegraphing &&
        Time.time >= noticedThreatTime + noticedThreatDelay;

    // ---------------------------------------------------------------- 퍼펙트 가드

    // 방패를 올린 직후의 짧은 창 안에 들어온 공격은 통째로 흘려낸다.
    // 미리 자세를 잡고 버티는 것과, 날아오는 칼에 맞춰 방패를 올리는 것은 달라야 한다.
    //
    // 조건이 둘인 이유: 타이밍만 보면 거의 모든 방어가 퍼펙트 가드가 된다. 방패는 적의
    // 준비 동작을 보고 blockReactionTime 뒤에 올라가는데, 그 반응 시간이 준비 동작 길이와
    // 비슷해서(칼 0.34초 vs 반응 0.11~0.25초) 자세를 잡은 시점이 늘 타격 직전이기 때문이다.
    // 그래서 자세를 드는 순간 굴린 "제대로 읽었는가"(perfectGuardArmed)를 함께 본다.
    private bool TryPerfectGuard(UnitController attacker)
    {
        if (!perfectGuardArmed || stats.perfectGuardWindow <= 0f) return false;
        if (Time.time > guardRaisedTime + stats.perfectGuardWindow) return false;

        // 한 번 자세를 잡으면 한 번만 흘려낸다. 창 안에 두 대가 들어와도 두 번째는 그냥 막힌다.
        perfectGuardArmed = false;

        PlayBlockImpact();
        // 흘려낸 쪽도 잠깐 멈춰야 "쳐냈다"가 읽힌다. 흘려진 쪽은 자세가 통째로 무너진다.
        ApplyHitStop(stats.hitStopDuration * 2f, stats.hitStopScale);
        if (attacker != null && !attacker.IsDead)
        {
            attacker.ApplyHitStop(stats.hitStopDuration * 2f, stats.hitStopScale);
            attacker.Stagger(stats.perfectGuardStaggerDuration);
        }

        if (debugLogs) Debug.Log($"[UnitController] {name} 퍼펙트 가드 — {(attacker != null ? attacker.name : "?")}의 공격을 흘려냄");
        return true;
    }

    // 타격이 닿은 순간 공격자와 피격자를 함께 눌러 붙인다.
    // 정지 시간은 맞은 쪽 기준이다 — 무거운 무기에 맞을수록 크게 흔들려야 하므로.
    private void ApplyImpactHitStop(UnitController attacker, float scale)
    {
        float duration = stats.hitStopDuration * scale;
        if (duration <= 0f) return;

        ApplyHitStop(duration, stats.hitStopScale);
        if (attacker != null && !attacker.IsDead) attacker.ApplyHitStop(duration, stats.hitStopScale);
    }

    // 막아낸 순간의 반동. 전용 모션이 없으면 방어 자세를 한 번 다시 잡아 최소한
    // "무언가 부딪혔다"는 것은 읽히게 한다.
    private void PlayBlockImpact()
    {
        if (blockHitAnimationHash != 0)
        {
            PlayAnimation(blockHitAnimationHash, true);
            blockImpactUntil = Time.time + blockHitAnimationDuration;
            return;
        }

        PlayAnimation(blockAnimationHash, true);
    }

    // 반동 모션이 끝나면 방어 자세로 돌아간다. 그러지 않으면 BlockHit의 마지막 프레임에서 굳는다.
    private void TickBlockPose()
    {
        if (!IsBlocking || blockImpactUntil <= 0f) return;
        if (Time.time < blockImpactUntil) return;

        blockImpactUntil = 0f;
        PlayAnimation(blockAnimationHash, true);
    }

    // ---------------------------------------------------------------- 경직

    // 자세가 완전히 무너진다. 가드 브레이크와, 퍼펙트 가드에 흘려진 공격자가 여기로 들어온다.
    // 대부분 다른 유닛이 이 유닛에게 거는 경로라(막은 쪽이 때린 쪽을 무너뜨린다)
    // 상태 전환은 상태머신의 지연 적용에 맡긴다.
    public void Stagger(float duration, bool fromGuardBreak = false)
    {
        if (IsDead || duration <= 0f) return;

        pendingStaggerDuration = duration;
        staggerEndTime = Time.time + duration;
        // InterruptCurrentAction이 IsBlocking을 내려버리므로, 어떤 이유로 무너졌는지는
        // 미리 여기 남겨 둬야 한다. StaggerState.Enter가 이 값으로 모션을 고른다.
        staggerFromGuardBreak = fromGuardBreak;
        InterruptCurrentAction();
        ChangeState(StaggerState);
    }

    public bool StaggerFromGuardBreak => staggerFromGuardBreak;
    private bool staggerFromGuardBreak;

    public void TriggerStagger(bool fromGuardBreak)
    {
        int hash = fromGuardBreak && blockBreakAnimationHash != 0 ? blockBreakAnimationHash : staggerAnimationHash;
        PlayAnimation(hash != 0 ? hash : hitAnimationHash, true);
    }

    // ---------------------------------------------------------------- 히트스톱

    // 칼이 닿은 순간 아주 짧게 애니메이션을 눌러 붙인다. 공격자와 피격자 양쪽에 걸어야
    // "부딪혔다"가 되지 — 한쪽만 멈추면 그냥 렉으로 보인다.
    public void ApplyHitStop(float duration, float scale)
    {
        if (duration <= 0f || IsDead) return;
        if (animator == null || !animator.enabled) return;

        float until = Time.time + duration;
        if (hitStopActive && until <= hitStopUntil) return;

        hitStopActive = true;
        hitStopUntil = until;
        animator.speed = Mathf.Clamp01(scale);

        // 공격 잠금은 실제 시각 기준이다. 애니메이션만 느려지고 잠금은 그대로면
        // 모션이 아직 남았는데 다음 스윙이 나가 동작이 겹친다. 잃어버린 만큼 뒤로 민다.
        float lost = duration * (1f - Mathf.Clamp01(scale));
        if (attackLockedUntil > Time.time) attackLockedUntil += lost;
        if (nextSwingReadyTime > Time.time) nextSwingReadyTime += lost;
    }

    private void TickHitStop()
    {
        if (!hitStopActive || Time.time < hitStopUntil) return;

        hitStopActive = false;
        if (animator != null) animator.speed = 1f;
    }

    public void ClearHitStop()
    {
        hitStopActive = false;
        hitStopUntil = 0f;
        if (animator != null && animator.enabled) animator.speed = 1f;
    }

    // 제자리에서 무언가를 하는 중인가. 이 상태들은 위치를 스스로 정하므로
    // NavMesh 쪽에 자리를 맡기지 않는다.
    private bool IsHoldingGround()
    {
        if (stateMachine == null) return false;

        IState<UnitController> current = stateMachine.CurrentState;
        return current == AttackState ||
               current == BlockState ||
               current == StaggerState ||
               current == HitState ||
               current == SkillState ||
               current == PotionState ||
               current == HealState;
    }

    // 지역 회피(RVO)와 회피 우선순위를 상황에 맞게 켜고 끈다.
    //
    // 이게 "계속 밀려나는" 문제의 진짜 원인이었다. NavMeshAgent의 지역 회피는 반지름이 겹치는
    // 에이전트를 매 프레임 서로 밀어내 떼어 놓는데, 근접전은 반지름 합(0.5+0.5=1.0m) 바로
    // 언저리에서 벌어지므로 이 밀어냄이 교전 내내 상시로 걸린다. 밀려난 만큼 transform은
    // 움직이지만 애니메이션은 그대로라, 발이 땅을 딛지 않은 채 미끄러지는 그림이 나온다.
    // 목표 거리를 회피 하한 위로 올려도 이건 사라지지 않는다 — 서로 조금만 다가서면
    // 다시 겹침 판정에 걸리기 때문이다.
    //
    // 교전 중에는 간격을 발놀림(UpdateCombatFootwork)이 직접 잡으므로 회피에게 맡길 이유가 없다.
    // 회피를 끈 유닛도 "다른 에이전트가 피해야 할 장애물"로는 그대로 남으므로, 달려오는 쪽이
    // 알아서 돌아간다 — 자리를 지키는 쪽만 밀리지 않게 된다.
    private void TickAvoidance()
    {
        if (agent == null || !agent.enabled) return;

        bool holdingGround = IsHoldingGround();

        ObstacleAvoidanceType desiredType = holdingGround
            ? ObstacleAvoidanceType.NoObstacleAvoidance
            : movingAvoidanceQuality;
        if (agent.obstacleAvoidanceType != desiredType) agent.obstacleAvoidanceType = desiredType;

        // 이동 중인 유닛끼리도 우선순위는 남는다(숫자가 작을수록 덜 밀린다).
        int desiredPriority = holdingGround ? engagedAvoidancePriority : movingAvoidancePriority;
        if (agent.avoidancePriority != desiredPriority) agent.avoidancePriority = desiredPriority;
    }

    // 매 프레임 돌려야 하는 전투 잔무. UnitController.Update가 상태머신보다 먼저 부른다.
    private void TickCombat()
    {
        TickHitStop();
        TickBlockPose();
        TickAvoidance();
        TickThreatAwareness();
    }

    // 죽은 유닛을 되살려 재사용하는 경로(Configure)를 위한 초기화.
    // 여기 남아 있던 값은 전부 "지난 전투의 마지막 순간"이라, 그대로 두면 스폰 직후
    // 경직 상태이거나 히트스톱으로 애니메이션이 멈춘 채 시작한다.
    private void ResetCombatRuntime()
    {
        hasStruckThisSwing = false;
        nextSwingReadyTime = 0f;
        lungeRemaining = 0f;
        staggerEndTime = 0f;
        pendingStaggerDuration = 0f;
        guardRaisedTime = -999f;
        perfectGuardArmed = false;
        blockImpactUntil = 0f;
        hasLastHitAttacker = false;
        noticedThreat = null;
        footworkThisGap = false;
        footworkVelocity = Vector3.zero;
        ClearHitStop();
    }
}
