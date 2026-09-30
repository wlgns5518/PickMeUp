using NUnit.Framework;

// 전투 정산의 셈법(BattleSettlement). 전투를 돌리지 않고 기여도 숫자만으로 확인한다.
public class BattleSettlementTests
{
    private static BattleReward Reward(int dealt, int kills, int taken, bool survived) =>
        new BattleReward { DamageDealt = dealt, Kills = kills, DamageTaken = taken, Survived = survived };

    [Test]
    public void MVP는_살아남은_사람_중_점수가_가장_높은_한_명이다()
    {
        var settlement = new BattleSettlement(new BattleRewardSettings { mvpKillWeight = 25f, mvpTankWeight = 0.3f });
        var result = new BattleResult();
        BattleReward fallenStar = Reward(1000, 10, 0, survived: false);
        BattleReward tank = Reward(10, 0, 400, survived: true);     // 10 + 120 = 130
        BattleReward striker = Reward(50, 3, 0, survived: true);    // 50 + 75 = 125
        result.Rewards.Add(fallenStar);
        result.Rewards.Add(tank);
        result.Rewards.Add(striker);

        settlement.SelectMvp(BattleOutcome.Victory, result);

        Assert.AreSame(tank, result.Mvp);
        Assert.IsTrue(tank.IsMvp);
        Assert.IsFalse(fallenStar.IsMvp, "쓰러진 동료는 아무리 잘 싸웠어도 MVP가 아니다.");
    }

    [Test]
    public void 지거나_아무도_기여하지_않은_판에는_MVP가_없다()
    {
        var settlement = new BattleSettlement(new BattleRewardSettings());

        var lost = new BattleResult();
        lost.Rewards.Add(Reward(500, 5, 0, survived: true));
        settlement.SelectMvp(BattleOutcome.Defeat, lost);
        Assert.IsNull(lost.Mvp);

        var idle = new BattleResult();
        idle.Rewards.Add(Reward(0, 0, 0, survived: true));
        settlement.SelectMvp(BattleOutcome.Victory, idle);
        Assert.IsNull(idle.Mvp);
    }

    [Test]
    public void 쓰러진_참가자는_경험치를_받지_않는다()
    {
        var settlement = new BattleSettlement(new BattleRewardSettings());
        var result = new BattleResult();
        BattleReward fallen = Reward(100, 2, 0, survived: false);
        fallen.LevelBefore = fallen.LevelAfter = 3;
        result.Rewards.Add(fallen);

        settlement.GrantExperience(BattleOutcome.Victory, result);

        Assert.AreEqual(0, fallen.ExpGained);
        Assert.AreEqual(3, fallen.LevelAfter);
    }
}
