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
    [SerializeField] private int dummyEnemyCount = 1;

    [Header("Floor Scaling")]
    [Tooltip("고른 층이 하나 높아질 때마다 늘어나는 적의 수.")]
    [SerializeField] private int enemiesPerFloor = 2;
    [Tooltip("층당 적 체력 증가 비율.")]
    [SerializeField] private float enemyHpPerFloor = 0.18f;
    [Tooltip("층당 적 공격력 증가 비율.")]
    [SerializeField] private float enemyDamagePerFloor = 0.12f;

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
    [Tooltip("마나 = baseMana + 지능 x manaPerIntelligence. 지능이 높을수록 스킬을 자주 쓴다.")]
    [SerializeField] private int baseMana = 30;
    [SerializeField] private int manaPerIntelligence = 4;

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

        // 메인 씬에서 고른 층이 난이도를 정한다.
        int floor = Mathf.Max(FloorProgress.FirstFloor, FloorProgress.SelectedFloor);
        int baseCount = enemySpawnPoints != null && enemySpawnPoints.Length > 0 ? enemySpawnPoints.Length : dummyEnemyCount;
        int count = baseCount + enemiesPerFloor * (floor - FloorProgress.FirstFloor);

        for (int i = 0; i < count; i++)
        {
            Vector3 position = GetEnemySpawnPosition(i);
            SpawnUnit(enemyUnitPrefab, UnitTeam.Enemy, BuildEnemyStats(floor), position, "Goblin_" + (i + 1));
        }
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

    // 프리팹 스탯을 복사해 층 보정을 얹는다. 원본을 그대로 쓰면 모든 적이 같은 객체를 공유해
    // 한 마리가 맞은 피해가 전부에게 반영된다.
    private UnitStats BuildEnemyStats(int floor)
    {
        UnitStats source = enemyUnitPrefab != null ? enemyUnitPrefab.Stats : null;
        UnitStats stats = source != null ? source.Clone() : new UnitStats();

        int steps = Mathf.Max(0, floor - FloorProgress.FirstFloor);
        stats.maxHp = Mathf.Max(1, Mathf.RoundToInt(stats.maxHp * (1f + enemyHpPerFloor * steps)));
        stats.attackDamage = Mathf.Max(1, Mathf.RoundToInt(stats.attackDamage * (1f + enemyDamagePerFloor * steps)));
        stats.skillDamage = Mathf.Max(1, Mathf.RoundToInt(stats.skillDamage * (1f + enemyDamagePerFloor * steps)));
        return stats;
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

        UnitController instance = Instantiate(prefab, position, Quaternion.identity);
        instance.Configure(team, stats, source);
        if (!string.IsNullOrEmpty(unitName)) instance.name = unitName;
        return instance;
    }

    // CharacterSO 스탯 → UnitStats 매핑.
    // 기본 능력치를 먼저 뽑고, 그 위에 직업과 장비 보정을 얹는다.
    // 이 순서 덕분에 같은 지능이라도 마법사가 든 마나가 더 크고, 같은 힘이라도 두손검이 더 아프다.
    private UnitStats MapStats(CharacterSO so)
    {
        JobCombatProfile job = JobProfile.For(so.job);
        WeaponCombatProfile weapon = JobProfile.For(so.MainHandType);
        bool hasShield = so.HasShield;

        var stats = new UnitStats
        {
            maxHp = baseHp + so.stats.vitality * hpPerVitality + so.level * hpPerLevel,
            attackDamage = baseAttackDamage + so.stats.strength,
        };

        stats.maxHp = Mathf.Max(1, Mathf.RoundToInt(stats.maxHp * job.HpMultiplier));
        stats.attackDamage = Mathf.Max(1, Mathf.RoundToInt(stats.attackDamage * job.AttackMultiplier * weapon.AttackMultiplier));
        stats.skillDamage = stats.attackDamage * 2;

        stats.maxMana = Mathf.RoundToInt((baseMana + so.stats.intelligence * manaPerIntelligence) * job.ManaMultiplier);

        // 사거리: 직업 사거리에 무기 배율을 곱하되, 활처럼 무기가 강제하는 최소치가 있으면 그쪽을 따른다.
        stats.attackRange = Mathf.Max(job.AttackRange * weapon.RangeMultiplier, weapon.MinRange);
        // 자기 사거리 밖을 못 보면 원거리 유닛이 영원히 접근만 하다 끝난다.
        stats.detectRange = Mathf.Max(job.DetectRange, stats.attackRange + JobProfile.DetectRangeMargin);

        // 원거리 유닛은 한 번 물러설 때 실제로 사거리를 되찾을 만큼 크게 벌려야 한다.
        // 기본값(2.5)으로는 사거리 9짜리가 물러나도 여전히 품 안이라 계속 붙잡힌다.
        stats.evadeRange = Mathf.Max(stats.evadeRange, stats.attackRange * 0.45f);

        stats.walkSpeed = (stats.walkSpeed + so.stats.agility * 0.02f) * job.SpeedMultiplier;
        stats.runSpeed = (stats.runSpeed + so.stats.agility * 0.04f) * job.SpeedMultiplier;

        // 공격 속도는 쿨다운이 아니라 스킬 재사용 간격으로만 표현한다.
        // (평타 간격은 애니메이션 길이가 정하므로 여기서 건드릴 수 없다)
        stats.skillCooldown /= Mathf.Max(0.1f, weapon.AttackSpeedMultiplier);

        stats.damageReduction = job.DamageReduction + (hasShield ? JobProfile.ShieldDamageReduction : 0f);
        stats.damageReduction = Mathf.Clamp(stats.damageReduction, 0f, 0.9f);
        if (hasShield) stats.blockDamageReduction = Mathf.Clamp01(stats.blockDamageReduction + JobProfile.ShieldBlockBonus);

        // 서포터만 아군을 회복시킬 수 있다.
        stats.canHealAllies = job.IsHealer;

        stats.ResetHp();
        stats.ResetMana();
        return stats;
    }
}
