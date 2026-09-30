using System.Collections.Generic;

// 끝난 전투를 셈하는 곳 — 기여도 옮겨 담기, MVP, 경험치와 스킬 해금, 승리 보상.
//
// 예전에는 BattleManager가 전투의 시작·끝 판정과 함께 이 셈까지 들고 있었다. 셈법은 밸런싱 대상이라
// 자주 고치는데, 고칠 때마다 씬 전환·이벤트 구독 코드 사이를 헤집어야 했고 따로 시험해 볼 수도 없었다.
// 여기는 유닛이 남긴 숫자와 정산 공식(BattleRewardSettings)만 본다.
public sealed class BattleSettlement
{
    private readonly BattleRewardSettings settings;
    private readonly ICharacterProgress progress;
    private readonly IMaterialStore materials;
    private readonly IWallet wallet;
    private readonly IFloorProgress floors;

    // 상태는 받아서 쓴다. 비워 두면 게임이 쓰는 것(GameServices)을 쓴다 — 테스트는 따로 만든 것을 넘긴다.
    public BattleSettlement(BattleRewardSettings settings, ICharacterProgress progress = null,
        IMaterialStore materials = null, IWallet wallet = null, IFloorProgress floors = null)
    {
        this.settings = settings ?? new BattleRewardSettings();
        this.progress = progress ?? GameServices.Progress;
        this.materials = materials ?? GameServices.Materials;
        this.wallet = wallet ?? GameServices.Account;
        this.floors = floors ?? GameServices.Floors;
    }

    /// 판을 끝낸 순서대로 부른다: 기여도 → MVP → 경험치 → (이겼으면) 보상.
    public void Settle(BattleOutcome outcome, IReadOnlyList<UnitController> allies, int floor, BattleResult result)
    {
        RecordContributions(allies, result);
        SelectMvp(outcome, result);
        GrantExperience(outcome, result);
        if (outcome == BattleOutcome.Victory) PayVictory(floor, result);
    }

    // 참전한 아군 전원의 기여도를 결과로 옮겨 담는다. 쓰러진 동료도 포함된다 —
    // 정산에서는 빠지지만 기여도 자체는 결과창이 보여줄 수 있어야 한다.
    // 유닛 인스턴스는 곧 정리될 수 있으므로 결과창이 읽을 값은 여기서 복사해 둔다.
    public void RecordContributions(IReadOnlyList<UnitController> allies, BattleResult result)
    {
        for (int i = 0; i < allies.Count; i++)
        {
            UnitController unit = allies[i];
            if (unit == null) continue;

            // 정산에서 빠지는 참가자도 레벨 칸은 채워 둔다. 0으로 남겨두면 결과창이
            // "레벨 0에서 0으로"라는 없는 값을 읽게 된다.
            int level = unit.SourceCharacter != null ? progress.LevelOf(unit.SourceCharacter) : 0;

            result.Rewards.Add(new BattleReward
            {
                Character = unit.SourceCharacter,
                DisplayName = unit.SourceCharacter != null && !string.IsNullOrEmpty(unit.SourceCharacter.characterName)
                    ? unit.SourceCharacter.characterName
                    : unit.name,
                Kills = unit.Kills,
                DamageDealt = unit.DamageDealt,
                DamageTaken = unit.DamageTaken,
                Survived = !unit.IsDead,
                LevelBefore = level,
                LevelAfter = level,
            });
        }
    }

    // MVP는 승리했을 때, 살아남은 참가자 중에서만 뽑는다.
    // 전멸한 판에서 최우수를 가리는 것도, 실려 나간 동료를 그 판의 최우수로 세우는 것도
    // 이 게임에서는 말이 되지 않는다 — 끝까지 서 있는 것이 이 판의 목표다.
    public void SelectMvp(BattleOutcome outcome, BattleResult result)
    {
        if (outcome != BattleOutcome.Victory || result.Rewards.Count == 0) return;

        BattleReward best = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < result.Rewards.Count; i++)
        {
            BattleReward reward = result.Rewards[i];
            if (!reward.Survived) continue;

            float score = MvpScore(reward);
            if (score <= bestScore) continue;

            bestScore = score;
            best = reward;
        }

        // 아무도 피해를 주지도 받지도 않은 판(예: 적이 스스로 사라진 경우)에는 MVP를 비워 둔다.
        if (best == null || bestScore <= 0f) return;

        best.IsMvp = true;
        result.Mvp = best;
    }

    // 생존자끼리만 비교하므로 생존 가산점은 없다 — 모두가 받으면 순위가 바뀌지 않는다.
    public float MvpScore(BattleReward reward)
    {
        return reward.DamageDealt
               + reward.Kills * settings.mvpKillWeight
               + reward.DamageTaken * settings.mvpTankWeight;
    }

    // 정산. 살아서 판을 끝낸 참가자만 대상이다 — 쓰러진 채 끝난 동료는 이번 판에서
    // 경험치도, 스킬 해금도 받지 못한다. 원작의 죽음이 그렇듯 "쓰러졌다"가 곧 손실이어야 한다.
    //
    // 경험치는 "판을 끝까지 버텼는가"와 "몇을 쓰러뜨렸는가"로만 매긴다. 가한 피해는 세지 않는다 —
    // 피해량은 결국 직업과 무기가 정하는 값이라, 같은 판을 같이 뛰어도 화력이 센 직업만
    // 계속 앞서 나가고 탱커·지원가는 영영 뒤처진다. 그건 활약이 아니라 배역의 차이다.
    public void GrantExperience(BattleOutcome outcome, BattleResult result)
    {
        int baseExp = outcome == BattleOutcome.Victory
            ? settings.expOnVictory
            : settings.expOnDefeat;

        for (int i = 0; i < result.Rewards.Count; i++)
        {
            BattleReward reward = result.Rewards[i];
            if (reward.Character == null || !reward.Survived) continue;

            int exp = baseExp + reward.Kills * settings.expPerKill;
            if (reward.IsMvp) exp += settings.mvpExpBonus;

            reward.ExpGained = exp;
            reward.LevelBefore = progress.LevelOf(reward.Character);
            progress.GainExp(reward.Character, exp);
            reward.LevelAfter = progress.LevelOf(reward.Character);

            // 레벨과 스탯이 오른 바로 뒤에 조건을 본다. 이 순서라야 "레벨 20 도달" 같은 조건이
            // 그 레벨을 넘긴 판에서 곧바로 열린다 — 한 판 늦게 열리면 왜 열렸는지 알 수 없다.
            SkillUnlocks.Evaluate(reward.Character, reward.UnlockedSkills);
        }
    }

    // 이긴 층은 해금 상태에 남긴다. 층은 자동으로 이어지지 않고,
    // 플레이어가 메인 씬에서 다시 고르는 구조라 여기서는 기록만 한다.
    public void PayVictory(int floor, BattleResult result)
    {
        // 재료는 해금을 기록하기 전에 굴린다. 방금 깬 층의 등급으로 받아야 한다.
        MaterialDrops.Roll(floor, settings, result.Materials);
        materials.AddRange(result.Materials);
        result.Gold = GameEconomy.FloorClearGold(floor);
        wallet.Add(Currency.Gold, result.Gold);

        // 새 콘텐츠가 열렸는지는 깨기 전후의 진행도로 가른다. 이미 깼던 층을 다시 깨면 아무것도 열리지 않는다.
        int clearedBefore = floors.HighestCleared;
        floors.MarkCleared(floor);
        DungeonCatalog.CollectNewlyUnlocked(clearedBefore, floors.HighestCleared, result.UnlockedDungeons);
    }
}
