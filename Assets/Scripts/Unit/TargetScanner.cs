using UnityEngine;

public class TargetScanner : MonoBehaviour
{
    [SerializeField] private float scanInterval = 0.2f;
    [SerializeField] private float viewAngle = 120f;
    [SerializeField] private float closeVisibleRange = 2f;
    [SerializeField] private float eyeHeight = 1.2f;
    [SerializeField] private LayerMask obstacleMask = ~0;

    [Tooltip("이미 아군이 붙어 있는 적을 이만큼(미터) 더 가깝게 쳐서, 아군끼리 각자 다른 적으로 흩어지지 않고 " +
             "같은 적에게 모여 싸우게 만든다. 아군 유닛에만 적용되고(적은 항상 0), 0이면 순수 최단거리로 되돌아간다.")]
    [SerializeField] private float allyGroupingBonusPerAlly = 3f;

    [Tooltip("어그로 세기. 후보 아군의 위협 가중치(UnitStats.threatWeight)에 이 값을 곱해 " +
             "그만큼(미터) 더 가깝게 친다 — 탱커(3.2)가 앞을 막고 사제(0.3)가 뒤에서 안전해지는 역할 분담. " +
             "적 유닛에만 적용되고(아군은 항상 0), 0이면 끈다.")]
    [SerializeField] private float threatBonusScale = 1.5f;

    [Tooltip("지금 싸우고 있는 타깃을 이만큼(미터) 더 가깝게 쳐서 계속 유지하려는 성향을 준다. " +
             "그룹핑/탱커 편향과 겹치면 다른 적이 조금 더 가까워졌다고 갈팡질팡 갈아타지 않게 막아준다. " +
             "아군·적 모두에 적용된다.")]
    [SerializeField] private float currentTargetStickiness = 5f;

    [Tooltip("한 아군에게 동시에 붙을 수 있는 적 수의 상한. 이미 이 수만큼 물고 있는 아군은 " +
             "crowdingPenalty만큼 더 멀게 쳐서, 새로 타깃을 찾는 적이 덜 붙잡힌 아군에게 가도록 한다 " +
             "— 몬스터와 대부분 1대1~2로 붙게 유도한다. 0이면 끈다.")]
    [SerializeField] private int maxAttackersPerAlly = 2;
    [SerializeField] private float crowdingPenalty = 10f;

    [Tooltip("교전 중에도 이 간격(초)마다 대상 선정을 다시 굴린다. 0이면 끈다.\n\n" +
             "이게 없으면 어그로가 첫 조우에만 작동한다 — 한 번 물면 그 대상이 죽을 때까지 " +
             "고정이라, 탱커가 바로 옆에 서 있어도 아무도 옮겨가지 않는다. " +
             "실측에서 탱커가 1마리만 붙들고 검사가 3마리를 떠안아 받은 피해의 47%를 혼자 먹었다.\n\n" +
             "갈아타기가 산만해지지 않는 것은 기존 타깃에 붙는 유지 편향(currentTargetStickiness)이 " +
             "그대로 걸리기 때문이다 — 지금 상대보다 그만큼 더 나은 후보가 있을 때만 옮긴다.")]
    [SerializeField, Min(0f)] private float aggroReviewInterval = 2.5f;

    private UnitController owner;
    private UnitController target;
    private float scanTimer;
    private float nextAggroReviewTime;
    // 팀 게시판에서 마지막으로 받아 간 소식 번호. 같은 소식을 두 번 반영하지 않기 위한 것.
    private int lastThreatVersion;

    public UnitController Target => target;
    // 실제로 쓰이는 시야각. 직업이 정해 준 값이 있으면 그쪽이다(궁수만 넓다).
    public float ViewAngle => EffectiveViewAngle;

    private void Awake()
    {
        owner = GetComponent<UnitController>();
        ScatterSchedule();
    }

    // 주기 작업의 위상을 유닛마다 흩어 놓는다.
    // 그러지 않으면 모든 유닛이 같은 프레임에 전면 탐색(레이캐스트 포함)과 팀 전파를 동시에 돌린다.
    // 실측(67유닛): 전 유닛의 scanTimer가 0.0205로 완전히 같았고,
    // 그 프레임에 전면 탐색 0.40ms + 팀 전파 0.37ms = 0.77ms가 한꺼번에 몰렸다.
    // 둘 다 유닛 수의 제곱으로 커지는 작업이라 유닛이 늘수록 스파이크가 급격히 나빠진다.
    private void ScatterSchedule()
    {
        scanTimer = Random.Range(0f, scanInterval);
        nextAggroReviewTime = Time.time + Random.Range(0f, Mathf.Max(0.01f, aggroReviewInterval));
    }

    // 한 번 흩어놔도 여러 유닛이 같은 프레임에 스캔을 마치면 다시 위상이 붙는다.
    // 재설정할 때마다 약간의 흔들림을 줘서 다시 뭉치지 않게 한다.
    private float NextScanDelay()
    {
        return scanInterval * Random.Range(0.85f, 1.15f);
    }

    public void Initialize(UnitController owner)
    {
        this.owner = owner;

        // 이미 올라와 있는 지난 소식부터 훑지 않도록 시작 번호를 현재로 맞춘다.
        // 스폰 직후에는 자기 눈으로 찾는 편이 자연스럽다.
        if (owner != null) lastThreatVersion = TeamThreatBoard.VersionOf(owner.Team);
    }

    public void Tick()
    {
        if (owner == null || owner.IsDead)
        {
            target = null;
            return;
        }

        scanTimer -= Time.deltaTime;
        if (scanTimer > 0f)
        {
            // 주기 사이에는 죽음/비활성화처럼 값싼 조건만 확인한다.
            // 시야·거리·레이캐스트를 포함한 전체 탐색은 아래 스캔 주기에서만 수행 —
            // 타깃이 없다고 매 프레임 전체 유닛을 훑으면 유닛 수의 제곱으로 비용이 커진다.
            if (target != null && (target.IsDead || !target.isActiveAndEnabled)) target = null;
            return;
        }

        scanTimer = NextScanDelay();

        if (!IsCurrentTargetValid())
        {
            target = null;
        }
        // Line-of-sight is a raycast, so it's only re-checked here at the scan cadence
        // rather than every frame in IsCurrentTargetValid.
        else if (!HasLineOfSight(target))
        {
            target = null;
        }

        if (target == null)
        {
            target = ScanForTarget();
        }
        else
        {
            ReviewAggro();
        }

        ReportThreat();
        ConsumeTeamThreat();
    }

    // 교전 중 대상 재평가.
    //
    // 예전에는 타깃을 잃었을 때만 다시 골랐다. 그래서 위협 가중치·혼잡도·후방 침투 편향이
    // 전부 "첫 조우" 한 번에만 반영되고, 그 뒤로는 처음 물린 상대에게 죽을 때까지 고정이었다.
    // 실측에서 고블린 9마리 전원이 유효 타깃을 들고 있어 아무도 재평가하지 않았고,
    // 그 결과 탱커(위협 3.2)가 1마리, 검사(1.0)가 3마리를 떠안았다 — 역할이 뒤집힌 것이다.
    //
    // 재평가라고 해서 매번 갈아타지는 않는다. ScanForTarget이 기존 타깃에 유지 편향
    // (currentTargetStickiness)을 얹어 계산하므로, 그만큼 확실히 나은 후보가 있을 때만 바뀐다.
    private void ReviewAggro()
    {
        if (aggroReviewInterval <= 0f || owner == null) return;

        // 아래 두 경우는 재검토 주기(2.5초)를 기다리지 않는다.
        //
        // 주기를 기다리면, 마침 그 순간에 휘두르는 중일 때(근접은 대부분 그렇다) TryRetarget이
        // 실패하고 다음 기회까지 또 2.5초를 흘려보낸다. 그 사이 유닛은 계속 엉뚱한 적을 본다.
        //
        //  1) 쫓아온 적이 내 간격 안에 있는데 그놈을 겨누고 있지 않다 — 가장 급한 경우다.
        //     쫓기기 시작한 순간 바로 돌아서야 견제가 된다.
        //  2) 집중 딜러가 파티 집중 표적을 벗어나 있다 — 화력이 흩어진 상태다.
        UnitController presser = ClosestPressuringEnemy();
        bool urgent = presser != null && owner.CurrentTarget != presser;

        if (!urgent)
        {
            urgent = owner.Stats.focusBonus > 0f
                     && owner.CurrentTarget != UnitRegistry.GetFocusTarget(owner.Team);
        }

        if (!urgent && Time.time < nextAggroReviewTime) return;

        // 주기도 스캔과 같은 이유로 유닛마다 흩어 놓는다(ScatterSchedule 주석 참조).
        nextAggroReviewTime = Time.time + aggroReviewInterval * Random.Range(0.8f, 1.2f);

        UnitController candidate = ScanForTarget();
        if (candidate == null)
        {
            // 다시 굴렸더니 아무것도 안 잡혔다면 원래 타깃을 그대로 들고 간다 —
            // 시야가 잠깐 끊긴 것뿐일 수 있고, 여기서 놓으면 교전이 통째로 끊긴다.
            target = owner.CurrentTarget.Unit;
            return;
        }

        if (owner.CurrentTarget.Unit == candidate) return;

        // 편향이 이미 반영된 결과이므로 거리 기반 재검사를 다시 걸지 않는다(TryRetarget 주석 참조).
        if (!owner.TryRetarget(candidate)) target = owner.CurrentTarget.Unit;
    }

    public UnitController FindTargetNow()
    {
        if (owner == null) return null;

        scanTimer = NextScanDelay();
        target = ScanForTarget();
        ReportThreat();

        return target;
    }

    private UnitController ScanForTarget()
    {
        // 나를 쫓아와 간격을 무너뜨린 적이 있으면 그놈부터 쏜다.
        //
        // 거리로 먹고사는 직군(궁수·마법사·사제·창수)에게는 붙은 적이 곧 가장 급한 문제다.
        // 파티 집중 표적이 아무리 중요해도, 코앞까지 온 놈을 두고 12m 밖을 겨누는 것은
        // 카이팅이 아니라 그냥 맞아 주는 것이다. 물러나면서 쫓아오는 놈을 쏘는 것이 견제다.
        //
        // 이 판단은 물러남·영창 접기·발놀림이 쓰는 것과 같은 기준(내 간격 안의 적)을 쓴다.
        // 넷이 같은 것을 봐야 "물러나는 이유"와 "쏘는 대상"이 어긋나지 않는다.
        UnitController presser = ClosestPressuringEnemy();
        if (presser != null) return presser;

        // 집중 딜러는 파티가 정한 표적을 먼저 본다.
        //
        // 거리 편향(FocusBonus)만으로는 모이지 않았다. 편향은 "몇 미터 더 가깝게 친다"일 뿐이라
        // 집중 표적이 12m 밖이고 다른 적이 2m 앞이면 9m를 깎아도 여전히 가까운 쪽이 이긴다.
        // 실측에서 딜러 셋이 [집중] 성향을 갖고도 서로 다른 적을 때리고 있었다.
        //
        // 그래서 편향이 아니라 우선 선택으로 바꾼다: 닿을 수 있는 집중 표적이 있으면 그쪽이다.
        // 닿을 수 없으면(사거리 밖이거나 벽 너머) 평소대로 고른다 — 도달하지 못할 표적을
        // 붙들고 있으면 그것대로 아무것도 못 한다.
        UnitController focus = ReachableFocusTarget();
        if (focus != null) return focus;

        return UnitRegistry.FindNearestVisibleEnemy(
            owner, owner.Stats.detectRange, EffectiveViewAngle, GetCloseVisibleRange(), eyeHeight, obstacleMask,
            new UnitRegistry.TargetBias(
                GroupingBonus(),
                ThreatBonusScale(),
                owner.Stats.backlinePreference,
                owner.Stats.peelBonus,
                owner.Stats.focusBonus,
                currentTargetStickiness,
                MaxAttackersPerTarget(),
                crowdingPenalty));
    }

    // 간격 판정에 쓰는 공용 버퍼. 스캔마다 리스트를 새로 만들지 않는다.
    private static readonly System.Collections.Generic.List<UnitController> PressureBuffer =
        new System.Collections.Generic.List<UnitController>(8);

    // 내 간격 안까지 들어온 적 중 가장 급한 하나. 없으면 null.
    //
    // 나를 노리고 온 놈(쫓아오는 적)을 먼저 고르고, 그런 놈이 없으면 그냥 가장 가까운 놈을
    // 고른다 — 나를 노리지 않더라도 내 간격 안에 있는 이상 물러나게 만드는 원인은 같다.
    private UnitController ClosestPressuringEnemy()
    {
        if (owner == null) return null;

        float threshold = owner.Stats.keepDistanceRange;
        if (threshold <= 0f) return null;

        UnitRegistry.FindEnemiesAround(owner, owner.transform.position, threshold, PressureBuffer);
        if (PressureBuffer.Count == 0) return null;

        UnitController chasing = null;
        float chasingSqr = float.MaxValue;
        UnitController nearest = null;
        float nearestSqr = float.MaxValue;
        Vector3 origin = owner.transform.position;

        for (int i = 0; i < PressureBuffer.Count; i++)
        {
            UnitController candidate = PressureBuffer[i];
            if (candidate == null) continue;

            float sqr = (candidate.transform.position - origin).sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = candidate;
            }

            if (candidate.CurrentTarget != owner) continue;
            if (sqr >= chasingSqr) continue;

            chasingSqr = sqr;
            chasing = candidate;
        }

        PressureBuffer.Clear();
        return chasing != null ? chasing : nearest;
    }

    // 지금 닿을 수 있는 파티 집중 표적. 없으면 null.
    //
    // 시야각(cone)은 보지 않고 탐지 거리와 시야선만 본다. 유닛은 어차피 표적 쪽으로 몸을 돌리므로,
    // "지금 등지고 있다"는 이유로 집중 표적을 놓치면 파티가 다시 흩어진다.
    private UnitController ReachableFocusTarget()
    {
        if (owner == null || owner.Stats.focusBonus <= 0f) return null;

        UnitController focus = UnitRegistry.GetFocusTarget(owner.Team);
        if (focus == null || focus == owner || focus.IsDead || !focus.isActiveAndEnabled) return null;
        if (!UnitRegistry.AreEnemies(owner, focus)) return null;

        Vector3 offset = focus.transform.position - owner.transform.position;
        offset.y = 0f;
        float detect = owner.Stats.detectRange;
        if (offset.sqrMagnitude > detect * detect) return null;

        return UnitRegistry.HasLineOfSight(owner.transform.position, focus.transform.position, eyeHeight, obstacleMask)
            ? focus
            : null;
    }

    // 직업이 정해 준 시야각이 있으면 그것을 쓴다. 정찰을 겸하는 궁수만 넓다(200도) —
    // 팀에서 가장 먼저 적을 발견해 게시판(TeamThreatBoard)에 올리는 역할이 여기서 나온다.
    // 값이 없는 유닛(고블린, 프리팹 직접 설정)은 이 컴포넌트의 기본값 그대로다.
    private float EffectiveViewAngle => owner != null && owner.Stats.viewAngle > 0f ? owner.Stats.viewAngle : viewAngle;

    // 흩어짐 방지 편향은 아군에게만 준다. 적까지 서로 뭉치게 하면 한 아군에게 몰려드는
    // 난이도 변화가 생기는데, 이번 요청은 "플레이어가 흩어지지 않는 것"만 원했다.
    private float GroupingBonus()
    {
        return owner != null && owner.Team == UnitTeam.Ally ? allyGroupingBonusPerAlly : 0f;
    }

    // 어그로도 방향이 하나뿐이다 — 적이 아군 탱커를 우선 노리게 한다. 아군이 적 탱커를
    // 우선 노려야 할 이유는 없으므로(그러면 오히려 잡기 어려운 적부터 때리게 된다) 적에게만 준다.
    private float ThreatBonusScale()
    {
        return owner != null && owner.Team == UnitTeam.Enemy ? threatBonusScale : 0f;
    }

    // 교전 수 상한도 적에게만 준다 — 아군이 몬스터 하나에 몰려 때리는 건 의도된 동작(그룹핑 편향)이라
    // 반대로 아군을 몰아 때리는 쪽만 분산시킨다.
    private int MaxAttackersPerTarget()
    {
        return owner != null && owner.Team == UnitTeam.Enemy ? maxAttackersPerAlly : 0;
    }

    public bool IsVisible(UnitController candidate)
    {
        if (owner == null || candidate == null) return false;
        return UnitRegistry.IsVisibleTo(owner, candidate, owner.Stats.detectRange, EffectiveViewAngle, GetCloseVisibleRange());
    }

    // 발견한 적을 팀 게시판에 올린다. 값 하나만 갱신하므로 팀 인원과 무관하게 일정 비용이다.
    // 예전에는 여기서 팀 전원을 순회하며 직접 알렸고, 그 비용이 발견한 프레임에 통째로 몰렸다.
    private void ReportThreat()
    {
        if (target == null || owner == null) return;
        TeamThreatBoard.Report(owner.Team, target);
    }

    // 아직 받아 가지 않은 팀 소식이 있으면 이번 스캔에서 반영한다.
    // 유닛마다 스캔 시각이 흩어져 있으므로 반응도 자연스럽게 흩어진다.
    private void ConsumeTeamThreat()
    {
        if (owner == null || owner.IsDead) return;
        if (!TeamThreatBoard.TryConsume(owner.Team, ref lastThreatVersion, out UnitController shared)) return;
        if (shared == target) return;

        owner.ReceiveSharedTarget(shared);
    }

    private bool IsCurrentTargetValid()
    {
        if (target == null || target.IsDead) return false;
        return IsVisible(target);
    }

    private bool HasLineOfSight(UnitController candidate)
    {
        if (owner == null || candidate == null) return false;
        return UnitRegistry.HasLineOfSight(owner.transform.position, candidate.transform.position, eyeHeight, obstacleMask);
    }

    private float GetCloseVisibleRange()
    {
        if (owner == null) return closeVisibleRange;
        return Mathf.Max(closeVisibleRange, owner.Stats.attackRange + owner.Stats.moveStopDistance);
    }
}
