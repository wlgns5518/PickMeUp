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

    [Header("Stat Mapping (임시 공식 — 추후 밸런싱 예정)")]
    [SerializeField] private int baseHp = 60;
    [SerializeField] private int hpPerVitality = 6;
    [SerializeField] private int hpPerLevel = 3;
    [SerializeField] private int baseAttackDamage = 6;

    private void Start()
    {
        SpawnAllies();
        SpawnEnemies();
    }

    private void SpawnAllies()
    {
        if (allyUnitPrefab == null || allyCharacters == null) return;

        for (int i = 0; i < allyCharacters.Length; i++)
        {
            CharacterSO so = allyCharacters[i];
            if (so == null) continue;

            Vector3 position = GetSpawnPosition(allySpawnPoints, i, allySpawnFallbackOffset);
            SpawnUnit(allyUnitPrefab, UnitTeam.Ally, MapStats(so), position, so.characterName);
        }
    }

    private void SpawnEnemies()
    {
        if (enemyUnitPrefab == null) return;

        int count = enemySpawnPoints != null && enemySpawnPoints.Length > 0 ? enemySpawnPoints.Length : dummyEnemyCount;
        for (int i = 0; i < count; i++)
        {
            Vector3 position = GetSpawnPosition(enemySpawnPoints, i, enemySpawnFallbackOffset);
            SpawnUnit(enemyUnitPrefab, UnitTeam.Enemy, null, position, $"Goblin_{i + 1}");
        }
    }

    private Vector3 GetSpawnPosition(Transform[] points, int index, Vector3 fallbackOffset)
    {
        if (points != null && index < points.Length && points[index] != null)
            return points[index].position;

        return transform.position + fallbackOffset * (index + 1);
    }

    private void SpawnUnit(UnitController prefab, UnitTeam team, UnitStats stats, Vector3 position, string unitName)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            position = hit.position;

        UnitController instance = Instantiate(prefab, position, Quaternion.identity);
        instance.Configure(team, stats);
        if (!string.IsNullOrEmpty(unitName)) instance.name = unitName;
    }

    // CharacterSO 스탯 → UnitStats 매핑
    private UnitStats MapStats(CharacterSO so)
    {
        var stats = new UnitStats
        {
            maxHp = baseHp + so.stats.vitality * hpPerVitality + so.level * hpPerLevel,
            attackDamage = baseAttackDamage + so.stats.strength,
        };
        stats.skillDamage = stats.attackDamage * 2;
        stats.walkSpeed += so.stats.agility * 0.02f;
        stats.runSpeed += so.stats.agility * 0.04f;
        stats.ResetHp();
        return stats;
    }
}
