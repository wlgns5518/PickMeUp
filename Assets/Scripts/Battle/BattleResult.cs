using System.Collections.Generic;

public enum BattleOutcome
{
    InProgress,
    Victory,  // 적 전멸
    Defeat,   // 아군 전멸
    Draw,     // 같은 프레임에 양쪽 전멸
}

// 전투 한 판의 결과. UI와 보상 처리가 같은 객체를 읽는다.
public class BattleResult
{
    public BattleOutcome Outcome { get; internal set; } = BattleOutcome.InProgress;
    public float Duration { get; internal set; }
    public int AllyDeaths { get; internal set; }
    public int EnemyDeaths { get; internal set; }
    public int AllySurvivors { get; internal set; }

    // 이번 전투에서 영구 사망한 로스터 캐릭터. 유닛 인스턴스는 곧 파괴되므로
    // 참조 대신 원본 CharacterSO를 들고 있어야 결과창이 안전하게 읽을 수 있다.
    public readonly List<CharacterSO> FallenCharacters = new List<CharacterSO>();

    // 참전한 아군 전원의 기여도와 정산 결과. 쓰러진 동료도 포함된다.
    public readonly List<BattleReward> Rewards = new List<BattleReward>();

    // 승리했을 때만 정해진다. Rewards 안의 원소를 그대로 가리킨다.
    public BattleReward Mvp { get; internal set; }

    // 이번 판에 받은 제작 재료. 이긴 판에만 채워진다(MaterialDrops). 창고에는 이미 들어가 있다.
    public readonly List<CraftMaterial> Materials = new List<CraftMaterial>();

    // 이번 판에 받은 골드. 이긴 판에만 채워진다(GameEconomy.FloorClearGold). 지갑에는 이미 들어가 있다.
    public long Gold { get; internal set; }

    // 이 판으로 새로 열린 콘텐츠(DungeonCatalog). 이미 열려 있던 층을 다시 깨면 비어 있다.
    public readonly List<DungeonKind> UnlockedDungeons = new List<DungeonKind>();

    public string KoreanOutcome
    {
        get
        {
            switch (Outcome)
            {
                case BattleOutcome.Victory: return "승리";
                case BattleOutcome.Defeat: return "전멸";
                case BattleOutcome.Draw: return "무승부";
                default: return "전투 중";
            }
        }
    }

    internal void Reset()
    {
        Outcome = BattleOutcome.InProgress;
        Duration = 0f;
        AllyDeaths = 0;
        EnemyDeaths = 0;
        AllySurvivors = 0;
        FallenCharacters.Clear();
        Rewards.Clear();
        Mvp = null;
        Materials.Clear();
        Gold = 0;
        UnlockedDungeons.Clear();
    }
}
