using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

// 전장에 선 유닛들의 명부와, 그 명부를 훑는 질의들.
//
// 한 클래스지만 파일은 하는 일마다 나눠 둔다 — 명부 자체(여기), 파티 집중 표적(.Focus), 시야(.Sight),
// 겨눌 적 고르기(.Targeting), 둘레의 적 세기·모으기(.Proximity), 치유·보호막 대상(.Support).
// 질의들이 명부와 판정 도우미(GetList, GetHostileLists, IsValidTarget …)를 같이 쓰므로 클래스를 쪼개면
// 그 도우미를 전부 바깥에 열어야 한다. 그래서 UnitController처럼 partial로 나눈다.
public static partial class UnitRegistry
{
    private static readonly List<UnitController> allies = new List<UnitController>(32);
    private static readonly List<UnitController> enemies = new List<UnitController>(32);
    private static readonly List<UnitController> neutrals = new List<UnitController>(16);

    // 살아있는 유닛에 속한 콜라이더 전부. 시야 레이가 유닛 몸통을 벽으로 착각하지 않도록
    // 걸러내는 데 쓴다. 콜라이더 참조로 직접 조회하므로 계층을 거슬러 올라갈 필요가 없다.
    private static readonly HashSet<Collider> unitColliders = new HashSet<Collider>();

    public static IReadOnlyList<UnitController> Allies => allies;
    public static IReadOnlyList<UnitController> Enemies => enemies;

    // 다른 정적 저장소(PartyDeck, CharacterStress...)와 같은 이유로 플레이 시작마다 비운다.
    // 여기만 빠져 있었다: 도메인 리로드를 끄면 이전 플레이에서 파괴된 유닛이 리스트에 남고,
    // HasLivingEnemy가 리스트 개수만 보기 때문에 아무도 없는 맵에서 계속 적을 찾아 헤매게 된다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        allies.Clear();
        enemies.Clear();
        neutrals.Clear();
        unitColliders.Clear();
        for (int i = 0; i < focusTargets.Length; i++) focusTargets[i] = TargetRef.None;
    }

    public static void Register(UnitController unit)
    {
        if (unit == null) return;

        List<UnitController> list = GetList(unit.Team);
        if (list.Contains(unit)) return;

        list.Add(unit);
        // 들고 있던 타깃이 있으면 그 대상의 "붙어 있는 적 수"에 다시 포함된다.
        unit.HoldTargetCount();
    }

    // 콜라이더 등록은 팀 리스트가 아니라 오브젝트 수명에 묶는다.
    // 죽어서 레지스트리에서 빠진 시체도 여전히 씬에 서 있으므로, 팀 리스트와 함께 지워버리면
    // 쌓인 시체가 갑자기 시야를 막는 벽이 된다.
    public static void RegisterColliders(UnitController unit)
    {
        if (unit == null || unit.BodyColliders == null) return;

        Collider[] colliders = unit.BodyColliders;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null) unitColliders.Add(colliders[i]);
        }
    }

    public static void UnregisterColliders(UnitController unit)
    {
        if (unit == null || unit.BodyColliders == null) return;

        Collider[] colliders = unit.BodyColliders;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null) unitColliders.Remove(colliders[i]);
        }
    }

    // 팀 리스트 직접 조회. 감정 전파처럼 "같은 팀 전원"을 훑어야 하는 곳에서 쓴다.
    public static IReadOnlyList<UnitController> GetTeam(UnitTeam team)
    {
        return GetList(team);
    }

    public static void Unregister(UnitController unit)
    {
        if (unit == null) return;

        allies.Remove(unit);
        enemies.Remove(unit);
        neutrals.Remove(unit);
        // 죽거나 꺼진 유닛은 더 이상 누구를 물고 있는 것으로 세지 않는다.
        unit.ReleaseTargetCount();
    }

    // 이미 그 적을 타깃으로 삼고 있는 attackerTeam 소속 유닛 수. 대상 선정에 편향을 줘서
    // 아군이 각자 다른 적으로 흩어지지 않고 같은 적에게 모이도록 만드는 데 쓴다.
    // 이 대상을 노리고 있는 attackerTeam 유닛 수.
    //
    // 예전에는 부를 때마다 팀 전체를 훑었다. 타깃을 고를 때 후보마다 부르는 값이라
    // 유닛 수의 세제곱으로 커졌다(67유닛 기준 스캔 한 주기에 약 7만 회, 두 배면 여덟 배).
    // 지금은 타깃을 잡고 놓는 순간에 대상이 직접 세어 두므로 조회는 배열 한 번이다.
    public static int CountAlliesTargeting(UnitTeam attackerTeam, UnitController target)
    {
        return target != null ? target.AttackersFrom(attackerTeam) : 0;
    }

    // 팀 전원에게 타깃을 밀어 넣던 AlertTeam은 TeamThreatBoard로 옮겼다.
    // 알림 한 번이 팀 인원 수만큼의 호출이라 난전에서 유닛 수의 제곱으로 커졌고,
    // 그 비용이 발견한 프레임에 통째로 몰렸다. 지금은 게시판에 쓰고(O(1)),
    // 각 유닛이 자기 스캔 주기에 읽어 간다.

    // Idle/Search/Move 상태가 매 프레임 모든 유닛에서 호출하는 핫패스.
    // allies/enemies/neutrals 리스트는 죽거나 비활성화된 유닛이 OnDisable→Unregister로
    // 즉시 제거되므로 항상 "살아있는 유닛만" 담고 있다 → 리스트 순회 없이 개수만으로 판단 가능(O(1)).
    public static bool HasLivingEnemy(UnitController requester)
    {
        if (requester == null) return false;

        // 엔티티가 된 적도 함께 본다. 이게 없으면 게임오브젝트 적이 전멸한 순간
        // 아군이 대기로 떨어져, 눈앞에 고블린 1000마리를 두고 가만히 서 있게 된다.
        switch (requester.Team)
        {
            case UnitTeam.Ally: return enemies.Count > 0 || EnemyWorldBridge.HasLivingEnemy();
            case UnitTeam.Enemy: return allies.Count > 0;
            default: return allies.Count > 0 || enemies.Count > 0 || EnemyWorldBridge.HasLivingEnemy();
        }
    }

    // 엔티티가 된 적을 함께 훑어야 하는 요청인가. 게임오브젝트끼리의 적대는 AreEnemies가 본다.
    //
    // 엔티티는 전부 Enemy 팀이다. 그래서 적대하는 쪽은 아군과 중립뿐이고, 게임오브젝트로
    // 남은 적(프리팹에 직접 스탯을 넣는 고블린)에게는 같은 편이라 훑을 이유가 없다.
    // 팀을 묻지 않고 브리지에 그냥 넘기면 그 고블린이 제 무리를 적으로 세어, 광역기 자리도
    // 도망 방향도 전부 뒤집힌다.
    //
    // 방어와 카이팅 쪽(FindTelegraphingAttacker, HasEnemyChasing)은 이 검사를 쓰지 않는다.
    // 그쪽은 브리지가 겨눔을 아군 인덱스로 기록하므로, 아군 스냅샷에 없는 유닛은 애초에
    // 물어볼 방법이 없어서 -1로 떨어진다.
    private static bool SeesEnemyEntities(UnitController requester)
    {
        return requester != null && requester.Team != UnitTeam.Enemy;
    }

    public static bool AreEnemies(UnitController a, UnitController b)
    {
        if (a == null || b == null || a == b) return false;
        if (a.Team == UnitTeam.Neutral) return b.Team != UnitTeam.Neutral;
        if (b.Team == UnitTeam.Neutral) return false;
        return a.Team != b.Team;
    }

    // 요청자 팀에 적대적인 리스트만 돌려준다. 예전에는 세 리스트를 모두 훑고 AreEnemies로
    // 걸러냈는데, 자기 팀 리스트(보통 가장 큰 리스트)를 통째로 헛도는 셈이었다.
    private static void GetHostileLists(UnitTeam team, out List<UnitController> first, out List<UnitController> second)
    {
        switch (team)
        {
            case UnitTeam.Ally:
                first = enemies;
                second = neutrals;
                break;
            case UnitTeam.Enemy:
                first = allies;
                second = neutrals;
                break;
            default: // Neutral은 비-중립 전체가 적
                first = allies;
                second = enemies;
                break;
        }
    }

    private static bool IsValidTarget(UnitController requester, UnitController target)
    {
        return requester != null &&
               target != null &&
               requester != target &&
               target.isActiveAndEnabled &&
               !target.IsDead &&
               !IsHiddenFrom(requester, target);
    }

    // 은신한 유닛은 적에게 보이지 않는다 — 겨눌 수도, 쫓아갈 수도 없다.
    //
    // 여기(IsValidTarget) 한 곳에 걸면 탐색·시야·어그로 경로 전부에 한 번에 먹는다.
    // 이미 그 유닛을 겨누고 있던 적도 다음 스캔에서 표적을 잃고 다른 데로 간다
    // (TargetScanner.IsCurrentTargetValid가 같은 판정을 쓴다) — 은신이 곧 이탈이 되는 셈이다.
    //
    // 다만 코앞까지 오면 들킨다. 붙어도 안 보이면 게릴라가 아니라 유령이 된다.
    public static bool IsHiddenFrom(UnitController observer, UnitController target)
    {
        if (observer == null || target == null) return false;
        if (!target.IsStealthed) return false;
        if (!AreEnemies(observer, target)) return false;

        float reveal = target.Stats.stealthRevealRange;
        if (reveal <= 0f) return true;

        return (target.transform.position - observer.transform.position).sqrMagnitude > reveal * reveal;
    }

    private static List<UnitController> GetList(UnitTeam team)
    {
        switch (team)
        {
            case UnitTeam.Ally:
                return allies;
            case UnitTeam.Enemy:
                return enemies;
            default:
                return neutrals;
        }
    }
}
