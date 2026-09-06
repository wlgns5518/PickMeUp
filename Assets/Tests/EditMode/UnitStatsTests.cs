using NUnit.Framework;

// 피해 계산은 밸런싱하면서 가장 자주 손대는 수식인데, 검증할 방법이 전투를 돌려 보는 것뿐이었다.
// 경감이 겹치는 순서(방어 → 상시)와, 완전 무효가 정말 0이 되는지가 특히 중요하다.
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
    public void 완전히_막아내면_피해가_0이다()
    {
        // 탱커의 방패가 이 값이다(blockDamageReduction 1). 완벽하게 받아낸 한 대에 피가
        // 깎이면 그 판단 자체가 무의미해진다.
        //
        // 교착에 빠지지 않는 이유는 강인도다 — 막은 타격도 강인도는 그대로 깎으므로 몇 번
        // 막다 보면 가드가 뚫리고 몇 초를 통째로 무너진 채 서 있게 된다.
        UnitStats stats = NewStats();
        stats.blockDamageReduction = 1f;

        stats.TakeDamage(40, true);

        Assert.AreEqual(100, stats.currentHp);
    }

    [Test]
    public void 경감으로_0이_된_타격은_0이_들어간다()
    {
        // 예전에는 여기에 "유효타는 최소 1" 하한이 있어 1이 들어갔다. 그 하한은 방어 쪽에서
        // 이미 빠져 있었고(위 테스트), 같은 논리가 상시 경감에도 적용돼 걷어냈다.
        // 완전 무효는 어디서 왔든 완전 무효다.
        UnitStats stats = NewStats();
        stats.damageReduction = 0.9f;

        // 1 → 상시 90% → 반올림하면 0.
        stats.TakeDamage(1);

        Assert.AreEqual(100, stats.currentHp);
    }

    [Test]
    public void 경감을_뚫는_타격은_그대로_남는다()
    {
        // 하한을 걷어냈다고 큰 타격까지 0이 되지는 않는다. 상시 경감은 0.9가 상한이므로
        // 피해가 5 이상이면 반올림해도 반드시 1 이상이 남는다.
        UnitStats stats = NewStats();
        stats.damageReduction = 0.9f;

        stats.TakeDamage(50);

        Assert.AreEqual(95, stats.currentHp);
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
