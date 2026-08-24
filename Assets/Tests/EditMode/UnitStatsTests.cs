using NUnit.Framework;

// 피해 계산은 밸런싱하면서 가장 자주 손대는 수식인데, 검증할 방법이 전투를 돌려 보는 것뿐이었다.
// 경감이 겹쳤을 때 0이 되지 않는다는 규칙(전투가 끝나지 않는 것을 막는 안전장치)이 특히 중요하다.
public class UnitStatsTests
{
    private static UnitStats NewStats(int maxHp = 100)
    {
        var stats = new UnitStats { maxHp = maxHp, damageReduction = 0f, blockDamageReduction = 0.5f };
        stats.ResetHp();
        stats.ResetMana();
        return stats;
    }

    [Test]
    public void 경감이_없으면_받은_피해가_그대로_들어간다()
    {
        UnitStats stats = NewStats();
        stats.TakeDamage(30);

        Assert.AreEqual(70, stats.currentHp);
    }

    [Test]
    public void 상시_경감이_적용된다()
    {
        UnitStats stats = NewStats();
        stats.damageReduction = 0.5f;

        stats.TakeDamage(40);

        Assert.AreEqual(80, stats.currentHp);
    }

    [Test]
    public void 방어_자세와_상시_경감은_함께_곱해진다()
    {
        UnitStats stats = NewStats();
        stats.damageReduction = 0.5f;
        stats.blockDamageReduction = 0.5f;

        stats.TakeDamage(40, true);

        // 40 → 방어 50% → 20 → 상시 50% → 10
        Assert.AreEqual(90, stats.currentHp);
    }

    [Test]
    public void 경감이_아무리_높아도_유효타는_최소_1이_들어간다()
    {
        // 이 규칙이 없으면 단단한 유닛끼리 만났을 때 서로 0을 때려 전투가 영원히 끝나지 않는다.
        UnitStats stats = NewStats();
        stats.damageReduction = 0.9f;
        stats.blockDamageReduction = 1f;

        stats.TakeDamage(1, true);

        Assert.AreEqual(99, stats.currentHp);
    }

    [Test]
    public void 피해가_0이면_아무_일도_일어나지_않는다()
    {
        UnitStats stats = NewStats();
        stats.TakeDamage(0);

        Assert.AreEqual(100, stats.currentHp);
    }

    [Test]
    public void HP는_0_밑으로_내려가지_않는다()
    {
        UnitStats stats = NewStats();
        stats.TakeDamage(9999);

        Assert.AreEqual(0, stats.currentHp);
        Assert.IsTrue(stats.IsDead);
    }

    [Test]
    public void 죽은_뒤에는_더_이상_피해를_받지_않는다()
    {
        UnitStats stats = NewStats();
        stats.TakeDamage(9999);
        stats.TakeDamage(50);

        Assert.AreEqual(0, stats.currentHp);
    }

    [Test]
    public void 회복은_최대치를_넘지_않고_실제_회복량을_돌려준다()
    {
        UnitStats stats = NewStats();
        stats.TakeDamage(30);

        Assert.AreEqual(30, stats.Heal(50), "70에서 100까지 30만 회복된다");
        Assert.AreEqual(100, stats.currentHp);
        Assert.AreEqual(0, stats.Heal(10), "가득 찬 상태에서는 0");
    }

    [Test]
    public void 회복약은_개수가_있을_때만_소모된다()
    {
        UnitStats stats = NewStats();
        stats.potionCount = 1;
        stats.TakeDamage(60);

        Assert.IsTrue(stats.ConsumePotion(out int healedHp, out _));
        Assert.Greater(healedHp, 0);
        Assert.AreEqual(0, stats.potionCount);
        Assert.IsFalse(stats.ConsumePotion(out _, out _), "남은 개수가 없으면 실패한다");
    }

    [Test]
    public void 마나는_음수가_되지_않는다()
    {
        UnitStats stats = NewStats();
        stats.maxMana = 20;
        stats.ResetMana();

        stats.SpendMana(50);

        Assert.AreEqual(0, stats.currentMana);
        Assert.IsFalse(stats.HasMana(1));
    }

    [Test]
    public void 복제본은_원본과_수치를_공유하지_않는다()
    {
        // 프리팹의 stats는 모든 인스턴스가 공유하는 객체다. 층 보정에서 이걸 놓치면
        // 적 한 마리가 맞은 피해가 전부에게 반영된다.
        UnitStats original = NewStats();
        UnitStats copy = original.Clone();

        copy.TakeDamage(40);

        Assert.AreEqual(100, original.currentHp);
        Assert.AreEqual(60, copy.currentHp);
    }
}
