using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 로스터의 CharacterSO 스탯을 UnitController 전투 유닛에 적용해 씬에 배치하는 테스트용 스포너.
// Y Bot(아군)/Goblin(더미 적) 프리팹에는 이미 UnitController/TargetScanner가 붙어 있음.
public class CharacterBattleSpawner : MonoBehaviour
{
    [Header("Ally (Character Roster)")]
    [SerializeField] private UnitController allyUnitPrefab;
    [SerializeField] private CharacterSO[] allyCharacters;
    [SerializeField] private Transform[] allySpawnPoints;
    [SerializeField] private Vector3 allySpawnFallbackOffset = new Vector3(-2.5f, 0f, 0f);

    [Header("Enemy (Dummy)")]
    [SerializeField] private UnitController enemyUnitPrefab;
    [SerializeField] private Transform[] enemySpawnPoints;
    [SerializeField] private Vector3 enemySpawnFallbackOffset = new Vector3(2.5f, 0f, 0f);

    [Header("Floor Scaling")]
    [Tooltip("적 수 = 고른 층 + 이 값. 층이 오를수록 적 수도 같이 늘어난다(예: 1층=2마리, 9층=10마리).")]
    [SerializeField] private int enemyCountOffset = 1;
    [Tooltip("적 레벨 = 고른 층 + 이 범위(포함)에서 뽑은 오프셋. 몬스터마다 독립적으로 뽑는다.")]
    [SerializeField] private int enemyLevelOffsetMin = 1;
    [SerializeField] private int enemyLevelOffsetMax = 2;
    [Tooltip("적 레벨 1당 체력 증가 비율. 체력은 이 비율대로 계속 오르게 두고, 그만큼 플레이어 쪽은 " +
             "attackDamagePerStrength(레벨업으로 쌓이는 힘이 공격력에 반영되는 배율)로 따라잡게 " +
             "한다 — 몬스터를 약하게 만드는 대신 플레이어를 더 세게 만드는 방향.")]
    [SerializeField] private float enemyHpPerLevel = 0.18f;
    [Tooltip("적 레벨 1당 공격력 증가 비율.")]
    [SerializeField] private float enemyDamagePerLevel = 0.12f;
    [Tooltip("아군 한 명당 스폰 시점에 미리 배정하는 적 수의 상한(탱커부터 채움). " +
             "TargetScanner.maxAttackersPerAlly와 같은 값으로 맞춰 둘 것 — 전투 중 편향은 그쪽이 맡고, " +
             "여기는 스폰 첫 프레임에 몰려서 다 같은 아군을 고르는 문제를 막는 담당이다.")]
    [SerializeField] private int maxAttackersPerAlly = 2;

    [Header("Spawn Formation")]
    [Tooltip("아군이 뭉쳐서 소환될 중심. 비워두면 스포너 위치 + allySpawnFallbackOffset을 쓴다.")]
    [SerializeField] private Transform allySpawnCenter;
    [Tooltip("적이 뭉쳐서 소환될 중심. 비워두면 스포너 위치 + enemySpawnFallbackOffset을 쓴다.")]
    [SerializeField] private Transform enemySpawnCenter;
    [Tooltip("같은 팀 유닛 사이의 간격. 중심을 둘러싸는 고리의 반지름이 이 값의 배수로 커진다.")]
    [SerializeField] private float clusterSpacing = 1.6f;

    [Header("Stat Mapping (임시 공식 — 추후 밸런싱 예정)")]
    [SerializeField] private int baseHp = 60;
    [SerializeField] private int hpPerVitality = 6;
    [SerializeField] private int hpPerLevel = 3;
    // 적을 1~2방에 정리하도록 맞춘 값. 이 수치에 직업/무기 배율이 곱해지므로
    // 공격력이 가장 낮은 생산직(요리사)도 2방, 근접직은 1방이 나온다.
    [SerializeField] private int baseAttackDamage = 200;
    [Tooltip("힘 1당 붙는 공격력. 몬스터 체력이 레벨(층+1~2)당 18%씩 계속 오르는 걸 그대로 두는 대신, " +
             "레벨업으로 쌓이는 힘이 공격력에 크게 반영되게 해서 캐릭터가 강해질수록 확실히 한방에 " +
             "정리하게 만든다. 예전엔 힘 1당 +1(사실상 배율 없음)이었던 걸 5로 올렸다 — 최고층(레벨11, " +
             "체력 280) 기준으로도 근접직 힘 17~20 정도면 한방이 나오는 값이다.")]
    [SerializeField] private int attackDamagePerStrength = 5;
    [Tooltip("마나 = baseMana + 지능 x manaPerIntelligence. 지능이 높을수록 스킬을 자주 쓴다.")]
    [SerializeField] private int baseMana = 30;
    [SerializeField] private int manaPerIntelligence = 4;

    [Header("Debug (임시)")]
    [Tooltip("양쪽 진영의 최대 체력에 곱한다. 전투가 한두 방에 끝나서 흐름을 볼 수 없을 때 " +
             "길이만 늘려 보려고 둔 임시 손잡이다. 밸런싱은 baseAttackDamage/attackDamagePerStrength 쪽이 " +
             "맡아야 하므로, 확인이 끝나면 1로 되돌린다.")]
    [SerializeField, Min(0.01f)] private float debugHealthMultiplier = 100f;

    private void Start()
    {
        SpawnAllies();
        SpawnEnemies();
    }

    private void SpawnAllies()
    {
        if (allyUnitPrefab == null) return;

        // 메인 씬에서 카드로 고른 편성이 있으면 그쪽이 이번 출전 명단이다.
        // 인스펙터 배열은 편성 화면을 거치지 않고 전투 씬을 바로 재생할 때의 대비책으로 남는다.
        IReadOnlyList<CharacterSO> lineup = PartyDeck.Count > 0 ? PartyDeck.Members : allyCharacters;
        if (lineup == null) return;

        for (int i = 0; i < lineup.Count; i++)
        {
            CharacterSO so = lineup[i];
            if (so == null) continue;

            // 원작의 영구 죽음 — 한 번 죽은 캐릭터는 다시 출전하지 않는다.
            if (PartyRoster.IsFallen(so))
            {
                Debug.Log("[CharacterBattleSpawner] 영구 사망한 캐릭터라 출전에서 제외: " + so.characterName);
                continue;
            }

            Vector3 position = GetSpawnPosition(allySpawnPoints, i, allySpawnFallbackOffset);
            UnitController unit = SpawnUnit(allyUnitPrefab, UnitTeam.Ally, MapStats(so), position, so.characterName, so);

            // HP와 마나는 Configure가 만회복시키지만 스트레스만은 이어진다.
            // Configure가 hiddenStats 값으로 되돌려 놓으므로 반드시 그 뒤에 덮어써야 한다.
            if (unit != null && unit.Emotion != null)
            {
                unit.Emotion.Profile.stress = CharacterStress.Get(so);
            }
        }
    }

    private void SpawnEnemies()
    {
        if (enemyUnitPrefab == null) return;

        // 메인 씬에서 고른 층이 난이도를 정한다. 적 수는 층수 + enemyCountOffset로 늘어난다.
        int floor = Mathf.Max(FloorProgress.FirstFloor, FloorProgress.SelectedFloor);
        int count = floor + enemyCountOffset;

        // TargetScanner의 어그로/뭉침 편향만으로는 스폰 첫 프레임에 전원이 같은 아군을
        // 동시에 고르는 걸 못 막는다 — 그 시점엔 서로 아직 아무도 타깃을 정하지 않아서
        // "이미 몇 명 붙었는지" 편향이 읽을 정보 자체가 없다(전부 0으로 보임). 그래서
        // 스폰 시점에 직접 순서대로 배정해 처음부터 1~2마리씩 갈라놓는다. 편향은 이후
        // 타깃을 잃었을 때(적 사망 등) 다시 고르는 상황에서 계속 역할을 한다 — 그때는
        // 이미 붙어 있는 정보가 실제로 존재하므로 정상 작동한다.
        List<UnitController> targetSlots = BuildInitialTargetSlots(count);

        for (int i = 0; i < count; i++)
        {
            int enemyLevel = floor + Random.Range(enemyLevelOffsetMin, enemyLevelOffsetMax + 1);
            Vector3 position = GetEnemySpawnPosition(i);
            UnitController enemy = SpawnUnit(enemyUnitPrefab, UnitTeam.Enemy, BuildEnemyStats(enemyLevel), position, "Goblin_" + (i + 1));

            if (enemy != null && i < targetSlots.Count)
            {
                enemy.SetTarget(targetSlots[i]);
            }
        }
    }

    // 탱커부터 상한까지 채우고, 남는 슬롯은 나머지 아군에게 라운드로빈으로 분배한다.
    // 예: 탱커 1명 + 나머지 2명, 상한 2면 [탱커,탱커,A,B,A,B] 순서로 적을 배정한다.
    // 파티가 작아서 상한 x 인원수를 넘는 적이 남으면(예: 2인 파티에 적 5마리), 남는 몫은
    // 탱커로 되돌아가 몰리지 않도록 전체 아군을 고르게 한 바퀴 더 돌려 채운다.
    private List<UnitController> BuildInitialTargetSlots(int enemyCount)
    {
        var slots = new List<UnitController>();
        IReadOnlyList<UnitController> allies = UnitRegistry.Allies;
        if (allies.Count == 0) return slots;

        // 탱커가 받는 슬롯 수는 전투 중 편향이 쓰는 상한과 같은 정의를 쓴다
        // (UnitRegistry.EffectiveAttackerCap — 위협 가중치의 제곱근만큼 늘어난다).
        // 예전에는 여기만 maxAttackersPerAlly 고정이라, 스폰 직후 탱커가 2마리만 받고
        // 나머지가 곧바로 후방으로 흩어졌다 — 전투 중 상한(탱커 4)과 어긋나 있었다.
        for (int i = 0; i < allies.Count; i++)
        {
            UnitController ally = allies[i];
            if (ally == null || !ally.Stats.isTank) continue;

            int cap = UnitRegistry.EffectiveAttackerCap(ally, maxAttackersPerAlly);
            for (int slot = 0; slot < cap; slot++) slots.Add(ally);
        }

        for (int round = 0; round < maxAttackersPerAlly; round++)
        {
            for (int i = 0; i < allies.Count; i++)
            {
                UnitController ally = allies[i];
                if (ally == null || ally.Stats.isTank) continue;
                slots.Add(ally);
            }
        }

        // 탱커가 없거나 상한이 0으로 설정된 경우의 안전장치 — 그래도 아무나 배정은 돼야 한다.
        if (slots.Count == 0) slots.AddRange(allies);

        // 상한 x 인원수보다 적이 많으면(작은 파티) 남는 몫을 앞에서부터 나머지 연산으로 채우면
        // 매번 탱커(0번 슬롯)로 되돌아가 몰린다. 그 대신 전체 아군을 순서대로 한 바퀴씩 더 돌려
        // 넘치는 만큼을 고르게 나눈다.
        int allyIndex = 0;
        while (slots.Count < enemyCount)
        {
            slots.Add(allies[allyIndex % allies.Count]);
            allyIndex++;
        }

        return slots;
    }

    // 층이 높아지면 스폰 지점 수를 금방 넘어선다. 남는 적은 배치 중심을 둘러싸고 뭉친다.
    private Vector3 GetEnemySpawnPosition(int index)
    {
        if (enemySpawnPoints != null && index < enemySpawnPoints.Length && enemySpawnPoints[index] != null)
            return enemySpawnPoints[index].position;

        Vector3 origin = enemySpawnCenter != null
            ? enemySpawnCenter.position
            : transform.position + enemySpawnFallbackOffset;
        return origin + FormationOffset(index, clusterSpacing);
    }

    // 중심 주위를 한 겹씩 둘러싸는 배치. 0번이 중앙에 서고 1번부터 고리를 채운다.
    //
    // 예전에는 아군을 한 줄로 늘어놓고(offset x (index+1)) 적은 반경 12m에 무작위로 흩뿌렸다.
    // 그러면 인원이 늘수록 팀이 맵 전체로 퍼져 서로를 찾아 헤매느라 전투가 시작되지 않는다.
    // 고리 배치는 인원이 몇이든 지름이 천천히 커져서 한 덩어리로 남는다.
    private static Vector3 FormationOffset(int index, float spacing)
    {
        if (index <= 0) return Vector3.zero;

        int ring = 1;
        int consumed = 1; // 중앙 한 자리
        while (consumed + ring * 6 <= index)
        {
            consumed += ring * 6;
            ring++;
        }

        int slotsInRing = ring * 6;
        int slot = index - consumed;
        float angle = slot / (float)slotsInRing * Mathf.PI * 2f;
        float radius = ring * Mathf.Max(0.1f, spacing);
        return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
    }

    // 프리팹 스탯을 복사해 레벨 보정을 얹는다. 원본을 그대로 쓰면 모든 적이 같은 객체를 공유해
    // 한 마리가 맞은 피해가 전부에게 반영된다.
    private UnitStats BuildEnemyStats(int enemyLevel)
    {
        UnitStats source = enemyUnitPrefab != null ? enemyUnitPrefab.Stats : null;
        UnitStats stats = source != null ? source.Clone() : new UnitStats();

        int steps = Mathf.Max(0, enemyLevel - 1);
        stats.maxHp = Mathf.Max(1, Mathf.RoundToInt(stats.maxHp * (1f + enemyHpPerLevel * steps) * debugHealthMultiplier));
        stats.attackDamage = Mathf.Max(1, Mathf.RoundToInt(stats.attackDamage * (1f + enemyDamagePerLevel * steps)));
        stats.skillDamage = Mathf.Max(1, Mathf.RoundToInt(stats.skillDamage * (1f + enemyDamagePerLevel * steps)));
        return stats;
    }

    // 스폰 순간부터 상대 진영을 보게 한다.
    //
    // 예전에는 전원 Quaternion.identity라 적이 어디 있든 +Z를 보고 시작했다. 최대 180도를
    // 어긋난 채로 달려가고, 도착해서야 몸을 돌리기 시작하니 첫 교전의 회전이 어색했다
    // (타격 판정이 정면 부채꼴을 보게 된 뒤로는 첫 공격이 빗나가는 원인이기도 했다).
    private Quaternion FacingOpposingSide(UnitTeam team, Vector3 position)
    {
        Vector3 opposingCenter = team == UnitTeam.Ally ? EnemyCenter() : AllyCenter();

        Vector3 direction = opposingCenter - position;
        direction.y = 0f;
        // 두 진영 중심이 같은 자리로 잡혀 있으면(설정 누락) 방향을 정할 근거가 없다.
        return direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction.normalized)
            : Quaternion.identity;
    }

    private Vector3 AllyCenter()
    {
        if (allySpawnCenter != null) return allySpawnCenter.position;
        if (allySpawnPoints != null && allySpawnPoints.Length > 0 && allySpawnPoints[0] != null) return allySpawnPoints[0].position;
        return transform.position + allySpawnFallbackOffset;
    }

    private Vector3 EnemyCenter()
    {
        if (enemySpawnCenter != null) return enemySpawnCenter.position;
        if (enemySpawnPoints != null && enemySpawnPoints.Length > 0 && enemySpawnPoints[0] != null) return enemySpawnPoints[0].position;
        return transform.position + enemySpawnFallbackOffset;
    }

    private Vector3 GetSpawnPosition(Transform[] points, int index, Vector3 fallbackOffset)
    {
        if (points != null && index < points.Length && points[index] != null)
            return points[index].position;

        Vector3 origin = allySpawnCenter != null
            ? allySpawnCenter.position
            : transform.position + fallbackOffset;
        return origin + FormationOffset(index, clusterSpacing);
    }

    private UnitController SpawnUnit(UnitController prefab, UnitTeam team, UnitStats stats, Vector3 position, string unitName, CharacterSO source = null)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            position = hit.position;

        UnitController instance = Instantiate(prefab, position, FacingOpposingSide(team, position));
        instance.Configure(team, stats, source);
        if (!string.IsNullOrEmpty(unitName)) instance.name = unitName;
        return instance;
    }

    // 아군이 실제로 손에 들고 나갈 무기의 분류.
    //
    // 장비를 하나도 고르지 않은 캐릭터는 아군 프리팹의 기본 무기(낡은 철검)를 쥐고 나간다(WeaponEquipper).
    // 수치를 so.MainHandType 그대로 뽑으면 검을 든 채로 맨손 배율을 맞게 되므로 여기서 같은 무기를 본다.
    // 적은 이 경로를 타지 않는다(BuildEnemyStats) — 맨손 고블린은 그대로 맨손이다.
    private WeaponType AllyMainHandType(CharacterSO so)
    {
        WeaponType type = so.MainHandType;
        if (type != WeaponType.None || allyUnitPrefab == null) return type;

        var equipment = allyUnitPrefab.GetComponent<WeaponEquipper>();
        WeaponDefinition fallback = equipment != null ? equipment.DefaultMainHand : null;
        return fallback != null ? fallback.type : type;
    }

    // CharacterSO 스탯 → UnitStats 매핑.
    // 기본 능력치를 먼저 뽑고, 그 위에 직업과 장비 보정을 얹는다.
    // 이 순서 덕분에 같은 지능이라도 마법사가 든 마나가 더 크고, 같은 힘이라도 두손검이 더 아프다.
    private UnitStats MapStats(CharacterSO so)
    {
        JobCombatProfile job = JobProfile.For(so.job);
        WeaponCombatProfile weapon = JobProfile.For(AllyMainHandType(so));
        bool hasShield = so.HasShield;

        var stats = new UnitStats
        {
            maxHp = baseHp + so.Vitality * hpPerVitality + so.Level * hpPerLevel,
            attackDamage = baseAttackDamage + so.Strength * attackDamagePerStrength,
        };

        stats.maxHp = Mathf.Max(1, Mathf.RoundToInt(stats.maxHp * job.HpMultiplier * debugHealthMultiplier));
        stats.attackDamage = Mathf.Max(1, Mathf.RoundToInt(stats.attackDamage * job.AttackMultiplier * weapon.AttackMultiplier));
        stats.skillDamage = stats.attackDamage * 2;

        stats.maxMana = Mathf.RoundToInt((baseMana + so.Intelligence * manaPerIntelligence) * job.ManaMultiplier);

        // 사거리: 직업 사거리에 무기 배율을 곱하되, 무기가 강제하는 범위를 벗어나지 않는다.
        // 활은 아래로(최소 9m), 근접 무기는 위로(제 리치까지) 붙잡는다 — 그렇지 않으면
        // 사거리가 긴 직업이 단검을 들고도 제자리에서 허공을 그으며 피해를 넣는다.
        stats.attackRange = Mathf.Clamp(job.AttackRange * weapon.RangeMultiplier, weapon.MinRange, weapon.MaxRange);
        // 자기 사거리 밖을 못 보면 원거리 유닛이 영원히 접근만 하다 끝난다.
        stats.detectRange = Mathf.Max(job.DetectRange, stats.attackRange + JobProfile.DetectRangeMargin);

        // 원거리 유닛은 한 번 물러설 때 실제로 사거리를 되찾을 만큼 크게 벌려야 한다.
        // 기본값(2.5)으로는 사거리 9짜리가 물러나도 여전히 품 안이라 계속 붙잡힌다.
        stats.evadeRange = Mathf.Max(stats.evadeRange, stats.attackRange * 0.45f);

        stats.walkSpeed = (stats.walkSpeed + so.Agility * 0.02f) * job.SpeedMultiplier;
        stats.runSpeed = (stats.runSpeed + so.Agility * 0.04f) * job.SpeedMultiplier;

        // 공격 속도는 쿨다운이 아니라 스킬 재사용 간격으로만 표현한다.
        // (평타 간격은 애니메이션 길이가 정하므로 여기서 건드릴 수 없다)
        stats.skillCooldown /= Mathf.Max(0.1f, weapon.AttackSpeedMultiplier);

        stats.damageReduction = job.DamageReduction + (hasShield ? JobProfile.ShieldDamageReduction : 0f);
        stats.damageReduction = Mathf.Clamp(stats.damageReduction, 0f, 0.9f);
        if (hasShield) stats.blockDamageReduction = Mathf.Clamp01(stats.blockDamageReduction + JobProfile.ShieldBlockBonus);

        // 서포터만 아군을 회복시킬 수 있다.
        stats.canHealAllies = job.IsHealer;
        // 방패를 든 캐릭터는 직업과 무관하게 적이 우선 노린다 — 방패를 앞에 세우고
        // 나머지가 뒤에서 때리는 진형은 여기서 시작된다.
        stats.isTank = so.job == JobType.Tank || hasShield;

        ApplyRole(stats, job, hasShield, so.mainHand);

        // 마법사가 평생 다루는 속성 하나. 이 값이 그가 쓸 수 있는 마법 전부를 정한다(SpellCatalog).
        // 마법사가 아닌 직업은 None이라 영창 경로 자체를 타지 않는다.
        stats.affinity = so.job == JobType.Mage ? so.Affinity : MagicAffinity.None;

        stats.ResetHp();
        stats.ResetMana();
        return stats;
    }

    // 후방 직군이 지키려는 최소 간격(미터). 고블린의 타격 도달(1.85m) 바깥으로 잡았다.
    private const float BacklineSafeDistance = 2.6f;

    // 직업이 정한 전술 성향을 전투 스탯으로 옮겨 담는다.
    //
    // 여기서 옮기지 않으면 JobProfile의 역할 값이 아무 데도 닿지 않는다 — AI(UnitController,
    // 상태, UnitRegistry)는 CharacterSO도 JobType도 모르고 UnitStats만 본다. 그 경계를
    // 유지하는 이유는 적(고블린)처럼 로스터를 거치지 않는 유닛이 있기 때문이다.
    // 그런 유닛은 이 함수를 타지 않으므로 전부 기본값(JobRole.None)이라 예전과 똑같이 싸운다.
    private void ApplyRole(UnitStats stats, JobCombatProfile job, bool hasShield, WeaponType weapon)
    {
        stats.role = job.Role;
        stats.threatWeight = job.ThreatWeight;
        stats.backlinePreference = job.BacklinePreference;
        stats.peelBonus = job.PeelBonus;
        stats.focusBonus = job.FocusBonus;
        stats.engageAngle = job.EngageAngle;
        stats.viewAngle = job.ViewAngle;
        stats.bleedChanceOnHit = job.BleedChanceOnHit;
        stats.slowOnHitDuration = job.SlowOnHitDuration;
        stats.slowOnHitMultiplier = job.SlowOnHitMultiplier;
        stats.castVulnerabilityMultiplier = job.CastVulnerability;
        // 마법사만 달려서 달아난다. 붙잡히면 할 수 있는 것이 없는 직군이라,
        // 뒷걸음으로 재는 대신 아예 떼어놓고 다시 영창하는 편이 낫다.
        stats.fleeByRunning = job.Role == JobRole.Caster;

        // 유지 거리는 비율이 아니라 미터로 확정해서 넘긴다. 사거리는 이미 무기 보정까지
        // 끝난 값이라(위 Clamp), 여기서 굳혀 두면 AI가 매 프레임 다시 곱하지 않아도 된다.
        stats.keepDistanceRange = job.KeepDistanceRatio > 0f
            ? stats.attackRange * job.KeepDistanceRatio
            : 0f;

        // 후방 직군은 무기가 짧아도 근접 난투에는 들어가지 않는다.
        //
        // 유지 거리를 제 사거리에만 매어 두면 무기 하나로 역할이 무너진다 — 사제에게 둔기를
        // 들리면 사거리가 1.71m로 깎이면서 유지 거리가 0.6m가 되어, 힐러가 고블린 코앞
        // 0.95m에서 싸우고 있는 것을 실측으로 확인했다. 몸으로 버티는 직군이 아닌데도 그렇다.
        //
        // 그래서 "적의 타격이 닿지 않는 거리"를 바닥으로 깐다. 다만 제 사거리의 0.9배를 넘기지는
        // 않는다 — 넘기면 물러나야 할 거리가 때릴 수 있는 거리보다 멀어져 영원히 도망만 다니게 된다.
        if (JobProfile.IsRangedRole(job.Role) && stats.keepDistanceRange > 0f)
        {
            stats.keepDistanceRange = Mathf.Max(
                stats.keepDistanceRange,
                Mathf.Min(BacklineSafeDistance, stats.attackRange * 0.9f));
        }

        // 창수가 물러나는 폭은 원거리 직군보다 훨씬 짧다.
        //
        // 위쪽 공통 계산(evadeRange = Max(2.5, 사거리 x 0.45))은 사거리 9m짜리 궁수가 한 번에
        // 사거리를 되찾도록 잡은 값이다. 리치 2.9m인 창수에게 그대로 적용하면 한 번 물러설 때마다
        // 제 사거리 밖(4.3m)까지 빠져 다시 걸어 들어와야 한다 — 밀어내며 찌르는 것이 아니라
        // 도망쳤다 돌아오는 것이 된다. 창끝이 닿는 거리 안에서 한 발짝 무르는 정도로 줄인다.
        if (job.Role == JobRole.Reach)
        {
            stats.evadeRange = stats.attackRange * 0.6f;
        }

        // 방패를 든 캐릭터는 직업이 무엇이든 방패로 막는다 — 손에 든 것이 방식을 정한다.
        // 그래서 방패를 든 검사는 패링이 아니라 방패 가드가 된다(그게 실제로 하는 동작이다).
        stats.guardStyle = hasShield && job.Guard != GuardStyle.Shield ? GuardStyle.Shield : job.Guard;
        ApplyGuardStyle(stats, weapon);
    }

    // 막는 방식에 따라 방어 관련 수치를 통째로 갈아 끼운다.
    //
    // 탱커의 "막기"와 검사의 "흘려내기"는 같은 동작의 강약이 아니라 서로 다른 기술이다:
    //  - 방패는 정면 반구를 넓게 오래 가린다. 대신 궤적을 읽어 튕겨내는 일은 잘 못한다.
    //  - 패링은 좁고 짧다. 대신 읽어내면 그 자리에서 반격이 열린다 — 그게 검사가
    //    탱커보다 얇은 몸으로 전열에 설 수 있는 이유다.
    //
    // 재사용 대기시간은 전 방식에서 0이다. 막을 수 있는 공격은 전부 막는다는 규칙이라,
    // "방금 막아서 지금은 못 막는다"가 없다. 막을 수 있는지는 자세를 다시 잡는 시간이 아니라
    // 몸과 무기가 정한다 — 그 궤도가 각도 안에 들어왔는가(guardArcAngle), 알아채고 손이
    // 따라갈 시간이 있었는가(blockReactionTime), 자세가 무너져 있지 않은가(IsStaggered).
    // 그 셋을 통과하면 언제나 막는다.
    private static void ApplyGuardStyle(UnitStats stats, WeaponType weapon)
    {
        switch (stats.guardStyle)
        {
            case GuardStyle.Shield:
                stats.guardArcAngle = 180f;
                stats.blockDuration = 1.0f;
                stats.blockCooldown = 0f;
                stats.perfectGuardChance = 0.22f;
                stats.counterAfterPerfectGuard = false;
                break;

            // 손에 있는 것을 급히 들어 받아낸다. 패링과 달리 훈련된 기술이 아니라서
            // 좁고 짧고 자주 못 쓰며, 충격도 상당히 넘어온다. 그 위에 무기별 성능이 곱해진다 —
            // 창수는 자루로 제법 걸치고, 궁수는 활대가 부러질 각오로 겨우 막는다.
            case GuardStyle.Weapon:
            {
                float factor = JobProfile.WeaponGuardFactor(weapon);
                // 들 것이 없으면(맨손/시전) 애초에 막지 못한다. CanEverBlock이 None으로 걸러낸다.
                if (factor <= 0f)
                {
                    stats.guardStyle = GuardStyle.None;
                    break;
                }

                // 각도가 좁으면 자세를 잡고도 옆에서 들어온 칼을 그대로 맞는다.
                // (실측: 무방비로 맞은 113대 중 20대가 방어 각도 밖이었고, 그중 7대는
                //  분명히 자세를 잡고 있던 중이었다.) 몸을 돌려 마주 보는 만큼은 쳐낼 수
                //  있어야 해서 정면 반구의 3분의 1~4분의 3까지 잡는다.
                stats.guardArcAngle = Mathf.Lerp(70f, 130f, factor);
                // 위협이 이어지는 동안은 이 시간에 관계없이 계속 든다(BlockState 참조).
                // 여기서 정하는 것은 "마지막 칼이 지나간 뒤 얼마나 더 들고 있는가"다.
                stats.blockDuration = Mathf.Lerp(0.22f, 0.40f, factor);
                stats.blockCooldown = 0f;
                // 읽어서 튕겨내는 것은 이 직군들의 기술이 아니다. 어쩌다 맞아떨어질 뿐이다.
                stats.perfectGuardChance = Mathf.Lerp(0.04f, 0.15f, factor);
                stats.perfectGuardWindow = 0.16f;
                // 막아도 절반 넘게 들어온다. 몸으로 버티는 직군이 아니라는 것이 여기서 드러난다.
                stats.blockDamageReduction = Mathf.Lerp(0.20f, 0.50f, factor);
                // 손에 익은 동작이 아니라 반응이 한 박자 늦다.
                stats.blockReactionTime = Mathf.Lerp(0.30f, 0.20f, factor);
                stats.counterAfterPerfectGuard = false;
                break;
            }

            case GuardStyle.Parry:
                // 검신을 쓰는 만큼 무기 받아내기보다는 넓다. 방패의 정면 반구에는 못 미친다.
                stats.guardArcAngle = 140f;
                // 오래 들고 있지 못한다. 검신으로 버티는 자세가 아니라 쳐내는 한 동작이다.
                stats.blockDuration = 0.5f;
                stats.blockCooldown = 0f;
                // 궤적을 읽는 것이 이 직군의 기술이다.
                stats.perfectGuardChance = 0.55f;
                stats.perfectGuardWindow = 0.24f;
                // 쳐내도 완전히 흘리지는 못한다 — 방패와 달리 충격이 팔로 넘어온다.
                stats.blockDamageReduction = 0.7f;
                stats.blockReactionTime = 0.15f;
                stats.counterAfterPerfectGuard = true;
                break;

            default:
                // 막지 않는 직군은 방어 수치를 만질 이유가 없다. CanEverBlock이 guardStyle로 걸러낸다.
                break;
        }
    }
}
