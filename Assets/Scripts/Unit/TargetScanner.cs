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
        if (Time.time < nextAggroReviewTime) return;

        // 주기도 스캔과 같은 이유로 유닛마다 흩어 놓는다(ScatterSchedule 주석 참조).
        nextAggroReviewTime = Time.time + aggroReviewInterval * Random.Range(0.8f, 1.2f);

        UnitController candidate = ScanForTarget();
        if (candidate == null)
        {
            // 다시 굴렸더니 아무것도 안 잡혔다면 원래 타깃을 그대로 들고 간다 —
            // 시야가 잠깐 끊긴 것뿐일 수 있고, 여기서 놓으면 교전이 통째로 끊긴다.
            target = owner.CurrentTarget;
            return;
        }

        if (candidate == owner.CurrentTarget) return;

        // 편향이 이미 반영된 결과이므로 거리 기반 재검사를 다시 걸지 않는다(TryRetarget 주석 참조).
        if (!owner.TryRetarget(candidate)) target = owner.CurrentTarget;
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
        return UnitRegistry.FindNearestVisibleEnemy(
            owner, owner.Stats.detectRange, EffectiveViewAngle, GetCloseVisibleRange(), eyeHeight, obstacleMask,
            new UnitRegistry.TargetBias(
                GroupingBonus(),
                ThreatBonusScale(),
                owner.Stats.backlinePreference,
                owner.Stats.peelBonus,
                currentTargetStickiness,
                MaxAttackersPerTarget(),
                crowdingPenalty));
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
