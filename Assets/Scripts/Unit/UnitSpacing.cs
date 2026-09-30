using UnityEngine;
using UnityEngine.AI;

// 간격을 지키는 판단 한 벌 — 물러날지, 쫓기고 있는지, 전선에서 떨어졌는지, 어느 쪽으로 물러날지.
//
// 원거리 직군과 창수처럼 "거리로 먹고사는" 유닛이 쓴다. 예전에는 UnitController 한가운데에 있었는데,
// 이 질문들은 서로 같은 기준을 봐야 한다는 것(아래 ShouldKeepDistance 주석) 말고는 유닛의 다른 일과
// 얽힌 데가 없어서 따로 떼었다. 인스펙터 값은 프리팹에 이미 적혀 있으므로 UnitController에 그대로 두고
// 여기서는 그때그때 읽는다 — 플레이 중에 값을 고쳐도 바로 먹는다.
public sealed class UnitSpacing
{
    private readonly UnitController owner;

    public UnitSpacing(UnitController owner)
    {
        this.owner = owner;
    }

    private UnitStats Stats => owner.Stats;
    private Vector3 Position => owner.transform.position;

    // 이 유닛이 원거리에서 싸우는가.
    //
    // 예전에는 이 판단이 전부 "사거리 >= minKeepDistanceRange"라는 숫자 하나였다. 그러면
    // 무기에 따라 결과가 흔들린다 — 단검을 든 마법사가 근접으로 잡히고, 창수의 리치를 조금만
    // 올리면 발차기와 쓸어베기를 잃는다. 역할이 있으면 역할로 묻고, 없는 유닛(고블린,
    // 프리팹 직접 설정)만 예전 방식으로 떨어진다.
    public bool IsRangedFighter =>
        Stats.role != JobRole.None
            ? JobProfile.IsRangedRole(Stats.role)
            : Stats.attackRange >= owner.MinKeepDistanceRange;

    // 적이 이 거리 안으로 들어오면 물러선다. 0이면 붙어서 싸운다.
    // 역할이 정해 준 값이 있으면 그것을 쓰고, 없으면 예전의 "사거리 x 비율"로 떨어진다.
    public float KeepDistanceThreshold
    {
        get
        {
            if (Stats.keepDistanceRange > 0f) return Stats.keepDistanceRange;
            if (Stats.role != JobRole.None) return 0f;
            return Stats.attackRange >= owner.MinKeepDistanceRange ? Stats.attackRange * owner.KeepDistanceRatio : 0f;
        }
    }

    // 적에게 붙잡혀서 제 간격을 잃었는지 판단한다.
    // attackRange만 늘리면 "더 멀리서 공격을 시작"할 뿐, 이미 붙은 적에게서 물러나지는 않는다.
    // 실제로 돌려보니 사거리 9짜리가 1.4m에서 근접 유닛과 나란히 싸우고 있었다 —
    // 거리로 먹고사는 역할이 성립하려면 교전 중에도 거리를 다시 벌리는 판단이 따로 필요하다.
    //
    // 원거리 직군만의 것이 아니다. 창수는 근접 무기를 들고도 이 판단을 한다 —
    // 창의 리치는 붙이지 않았을 때만 우위이므로, 파고든 적을 밀어내며 찌르는 것이 그 직군이다.
    // 겨누는 상대가 아니라 "내 간격 안에 누가 들어왔는가"를 본다.
    //
    // 예전에는 CurrentTarget과의 거리만 쟀다. 파티 집중 사격이 들어온 뒤로 그게 무너졌다 —
    // 마법사의 표적은 8m 밖의 집중 표적인데 정작 위협은 발밑의 다른 고블린이라, 표적이 멀면
    // "안전하다"고 판단해 버렸다. 그래서 물러났다가 곧바로 되돌아와 다시 맞는 왕복이 생겼다.
    //
    // 물러날지 정하는 기준(여기)과 어느 쪽으로 물러날지 정하는 기준
    // (GetRetreatDirection), 영창을 접을지 정하는 기준(UnitController.ShouldAbandonCast)이
    // 전부 같은 것을 봐야 한다. 셋이 어긋나면 그 차이가 그대로 갈팡질팡으로 나타난다.
    //
    // 한 프레임 안에서는 답을 기억해 둔다. 한 틱에 여러 번 불리기 때문이다 —
    // 물러날지(UnitBehaviorTree.WantsRetreat), 어떻게 물러날지(RunsAway), 영창을 접을지
    // (ShouldAbandonCast), 그리고 물러나는 동작 자체(EvadeBehavior)가 전부 같은 질문을 한다.
    // 안이 적 전체를 훑는 선형 스캔이라 그 중복이 그대로 비용이 된다.
    //
    // 프레임 안에서 답이 바뀔 일은 없다. 유닛은 프레임당 한 번 움직이고, 이 질문은 전부
    // 그 이동이 끝난 뒤의 같은 brain.Tick() 안에서 나온다.
    private int keepDistanceFrame = -1;
    private bool keepDistanceCached;

    public bool ShouldKeepDistance()
    {
        // 유지 거리가 없는 직군(근접 대부분)은 여기서 끝난다. 이 게이트가 메모보다 앞에
        // 있어야 한다 — Time.frameCount는 네이티브 호출이라 11ns쯤 드는데, 그 뒤에 두면
        // 스캔을 아예 하지 않는 유닛이 2ns짜리 검사를 13ns에 하게 된다(실측).
        float threshold = KeepDistanceThreshold;
        if (threshold <= 0f) return false;

        int frame = Time.frameCount;
        if (keepDistanceFrame == frame) return keepDistanceCached;

        keepDistanceFrame = frame;
        keepDistanceCached = UnitRegistry.CountEnemiesAround(owner, Position, threshold) > 0;
        return keepDistanceCached;
    }

    // 달아나기를 멈춰도 되는 거리 = 유지 거리 x 이 배수.
    //
    // 유지 거리(2.6m)에 딱 맞춰 멈추면 그 경계에서 도망과 복귀가 계속 뒤집힌다 —
    // 물러나자마자 다시 붙잡혀 또 물러나는 것이 "공격하려다 버벅이는" 그림이었다.
    // 넉넉히 떼어놓고 멈춰야 돌아와서 실제로 영창을 시작할 여유가 생긴다.
    private const float ChaseEscapeRatio = 2.5f;

    // 쫓는 놈이 잠깐 안 보여도 곧바로 멈추지 않는 유예. 방어 쪽의 AlertGrace와 같은 장치다.
    //
    // 이게 없으면 "그 적이 나를 타깃으로 들고 있는가"가 프레임마다 뒤집힌다. 적은 제 주기마다
    // 표적을 다시 고르고(TargetScanner / EnemyTargetingSystem), 그 사이사이 아무도 나를 물지
    // 않는 순간이 끼어든다. 그때마다 도주가 그 자리에서 끝나 버리므로, 달아나다 멈춰 서서
    // 돌아보고 다시 달아나는 그림이 된다 — 실제로 떨어진 거리는 얼마 되지 않는데도.
    //
    // 고블린이 4.0m/s로 꾸준히 쫓아오고 마법사가 4.66m/s로 달아나므로 벌어지는 속도는
    // 0.66m/s뿐이다. 유예 없이 한 번 끊기면 그때까지 번 거리를 통째로 잃는다.
    private const float ChaseGrace = 0.6f;
    private float chaseGraceUntil = -999f;

    // 나를 노리고 쫓아오는 적이 아직 붙어 있는가. 달아나기(fleeByRunning)를 계속할지 정한다.
    //
    // 거리만 보지 않고 "그 적이 나를 타깃으로 들고 있는가"까지 본다. 쫓아오던 놈이 표적을
    // 바꾸면(탱커가 도발로 끌어갔거나 다른 아군을 물었으면) 도망칠 이유가 사라진다 —
    // 다만 그 판단은 위 유예를 지나고 나서야 내린다.
    public bool IsBeingChased()
    {
        float threshold = KeepDistanceThreshold;
        if (threshold <= 0f) return false;

        if (UnitRegistry.HasEnemyChasing(owner, threshold * ChaseEscapeRatio))
        {
            chaseGraceUntil = Time.time + ChaseGrace;
            return true;
        }

        return Time.time < chaseGraceUntil;
    }

    // 전선에서 떨어져 나왔는가. 프레임당 한 번만 재고 그 답을 재사용한다 —
    // 후퇴 판단이 한 프레임에 여러 번 물어보는데, 매번 팀 전체를 훑을 이유는 없다.
    private int regroupCheckFrame = -1;
    private bool regroupCheckResult;

    public bool IsSeparatedFromLine
    {
        get
        {
            float regroupDistance = owner.RegroupDistance;
            if (regroupDistance <= 0f) return false;
            if (regroupCheckFrame == Time.frameCount) return regroupCheckResult;

            regroupCheckFrame = Time.frameCount;

            UnitController nearest = UnitRegistry.FindNearestAlly(owner);
            if (nearest == null)
            {
                // 혼자 남았으면 돌아갈 전선이 없다. 평소대로 적에게서 물러난다.
                regroupCheckResult = false;
                return false;
            }

            Vector3 offset = nearest.transform.position - Position;
            offset.y = 0f;
            regroupCheckResult = offset.sqrMagnitude > regroupDistance * regroupDistance;
            return regroupCheckResult;
        }
    }

    // 간격을 벌릴 때 실제로 물러날 방향.
    //
    // 평소에는 적의 반대쪽이다. 다만 전선에서 떨어져 나온 상태라면 아군 쪽으로 물러난다 —
    // 그러지 않으면 물러남이 곧 이탈이 되어, 적 하나를 끌고 맵 끝까지 나가면서 파티가 쪼개진다
    // (실측: 궁수가 45m). 원작에서도 검사가 반격을 받으면 "탱커의 방패 뒤나 후방으로" 물러나지,
    // 아무 데로나 빠지지 않는다. 물러나면서 전열에 합류하는 쪽이 두 마리 토끼를 다 잡는다.
    // 마지막으로 정한 후퇴 방향과 그 시각. 짧은 시간 안에 다시 물러날 때는 이 방향을 그대로 쓴다.
    private Vector3 lastRetreatDirection;
    private float lastRetreatDirectionTime = -999f;

    public Vector3 GetRetreatDirection()
    {
        // 방금 정한 방향이 있으면 그대로 간다.
        //
        // 간격을 되찾는 후퇴는 짧게 여러 번 끊어 일어난다(물러났다 → 돌아왔다 → 또 붙잡혔다).
        // 그때마다 방향을 새로 계산하면 매번 다른 쪽으로 튀어서 갈팡질팡하는 것처럼 보인다.
        // 한 번 정한 쪽으로 잠깐이라도 꾸준히 가야 "물러난다"로 읽힌다.
        float hold = owner.RetreatDirectionHold;
        if (hold > 0f &&
            Time.time < lastRetreatDirectionTime + hold &&
            lastRetreatDirection.sqrMagnitude > 0.0001f)
        {
            return lastRetreatDirection;
        }

        Vector3 direction = ResolveRetreatDirection();

        lastRetreatDirection = direction;
        lastRetreatDirectionTime = Time.time;
        return direction;
    }

    // 죽은 유닛을 되살려 재사용할 때(UnitController.ResetCombatRuntime) 지난 전투의 후퇴 방향을 버린다.
    public void ForgetRetreatDirection()
    {
        lastRetreatDirection = Vector3.zero;
        lastRetreatDirectionTime = -999f;
    }

    // 실제로 갈 수 있는 방향이 정해졌으면 그것을 유지 대상으로 삼는다.
    //
    // 벽에 막혀 방향을 틀었을 때 이걸 부르지 않으면, 유지 창(retreatDirectionHold) 동안
    // 막힌 원래 방향이 계속 돌아와 다음 도망도 같은 벽으로 향한다.
    public void CommitRetreatDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f) return;

        lastRetreatDirection = direction.normalized;
        lastRetreatDirectionTime = Time.time;
    }

    // 물러날 자리를 실제로 갈 수 있는 곳으로 잡는다.
    //
    // 원하는 쪽이 벽이면 그대로는 못 간다. 목적지가 벽 안이라 NavMesh 위로 끌어당겨지면
    // 지금 서 있는 자리 바로 옆이 되고, 유닛은 "도착했다"고 판단해 제자리에 선다.
    // 게다가 방향 유지(retreatDirectionHold)가 그 막힌 방향을 계속 돌려주므로 영영 못 움직인다.
    //
    // 그래서 원하는 쪽부터 좌우로 벌려 가며 훑어, 실제로 거리를 벌 수 있는 첫 방향을 고른다.
    // 사람도 벽에 막히면 벽을 따라 비스듬히 빠지지 벽에 붙어 버티지 않는다.
    // 어느 쪽도 뚫리지 않았으면 false — 부르는 쪽이 도망 대신 다른 수를 잡아야 한다.
    public bool TryFindRetreatSpot(Vector3 preferred, float distance, out Vector3 direction, out Vector3 destination)
    {
        Vector3 position = Position;
        direction = preferred;
        destination = position;

        preferred.y = 0f;
        if (preferred.sqrMagnitude <= 0.0001f) return false;
        preferred.Normalize();

        // 원하는 쪽 → 좌우로 조금씩 → 끝내 안 되면 반대쪽까지.
        for (int i = 0; i < RetreatFanAngles.Length; i++)
        {
            float angle = RetreatFanAngles[i];
            Vector3 candidate = Quaternion.AngleAxis(angle, Vector3.up) * preferred;
            Vector3 spot = position + candidate * distance;

            // 목표 지점 근처에만 붙인다. 넉넉히 잡으면 엉뚱한 곳으로 끌려가 뒤로 가려다
            // 앞으로 가는 자리가 잡힐 수 있다.
            if (!NavMesh.SamplePosition(spot, out NavMeshHit hit, RetreatSampleRadius, NavMesh.AllAreas)) continue;

            Vector3 gained = hit.position - position;
            gained.y = 0f;
            // 실제로 벌어지는 거리가 있어야 의미가 있다. 벽에 붙은 자리는 여기서 걸린다.
            if (gained.sqrMagnitude < MinRetreatGain * MinRetreatGain) continue;

            direction = gained.normalized;
            destination = hit.position;
            return true;
        }

        return false;
    }

    // 원하는 쪽에서 얼마나 벌려 가며 찾을지. 0이 원하는 방향 그대로다.
    private static readonly float[] RetreatFanAngles = { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 150f, -150f, 180f };
    // 후보 지점을 NavMesh 위로 끌어당길 때 허용할 반경.
    private const float RetreatSampleRadius = 1.5f;
    // 이만큼도 못 벌면 막힌 것으로 본다.
    private const float MinRetreatGain = 1.2f;

    private Vector3 ResolveRetreatDirection()
    {
        Vector3 position = Position;

        // 무엇으로부터 물러나는가 — 겨누고 있는 상대가 아니라 실제로 품 안에 들어온 적들이다.
        //
        // 예전에는 CurrentTarget의 반대쪽으로 물러났다. 그런데 물러나기로 정하는 기준은
        // "내 간격 안에 아무 적이나 들어왔는가"(ShouldAbandonCast)여서 둘이 어긋난다.
        // 파티 집중 사격이 들어온 뒤로 그 어긋남이 커졌다 — 마법사의 CurrentTarget은 8m 밖의
        // 집중 표적인 경우가 많은데, 정작 물러나게 만든 것은 발밑의 고블린이다.
        // 그 상태로 "표적 반대쪽"으로 뛰면 위협 쪽으로 뛰어드는 일까지 생기고,
        // 표적이 바뀔 때마다 방향이 통째로 홱 돈다.
        Vector3 threatCenter = Vector3.zero;
        float threshold = KeepDistanceThreshold;
        bool hasThreat = threshold > 0f &&
                         UnitRegistry.TryGetEnemyCentroidAround(owner, position, threshold, out threatCenter);

        if (!hasThreat)
        {
            // 품 안에는 아무도 없다. 그러면 겨누는 상대가 유일한 근거다(예전 동작).
            threatCenter = owner.CurrentTarget.Exists
                ? owner.CurrentTarget.Position
                : position + owner.transform.forward;
        }

        Vector3 away = position - threatCenter;
        away.y = 0f;
        if (away.sqrMagnitude <= 0.0001f) away = -owner.transform.forward;
        away.Normalize();

        if (!IsSeparatedFromLine) return away;

        // 전선에서 떨어져 나온 상태라면 아군 쪽으로 물러난다 — 그러지 않으면 물러남이 곧 이탈이
        // 되어, 적 하나를 끌고 맵 끝까지 나가면서 파티가 쪼개진다(실측: 궁수가 45m).
        UnitController anchor = UnitRegistry.FindNearestAlly(owner);
        if (anchor == null) return away;

        Vector3 toAnchor = anchor.transform.position - position;
        toAnchor.y = 0f;
        if (toAnchor.sqrMagnitude <= 0.0001f) return away;

        return toAnchor.normalized;
    }
}
