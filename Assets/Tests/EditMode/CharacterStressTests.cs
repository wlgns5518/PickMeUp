using NUnit.Framework;
using UnityEngine;

// 스트레스 회복이 "읽는 시점에 계산"으로 바뀌면서 생긴 규칙들.
// 특히 저장 값이 정산 시각 기준이라는 점(Set과 Restore의 차이)이 어긋나면
// 자리비움 회복이 통째로 사라지거나 같은 몫을 두 번 깎게 된다.
public class CharacterStressTests
{
    private CharacterSO character;
    private float savedRecoveryPerHour;
    private float savedMaxCatchUpHours;

    private static CharacterSO NewCharacter(int innateStress)
    {
        var so = ScriptableObject.CreateInstance<CharacterSO>();
        so.name = "StressTestCharacter";
        so.hiddenStats = new HiddenStats { stress = innateStress };
        return so;
    }

    [SetUp]
    public void SetUp()
    {
        savedRecoveryPerHour = CharacterStress.RecoveryPerHour;
        savedMaxCatchUpHours = CharacterStress.MaxCatchUpHours;
        CharacterStress.Clear();
        character = NewCharacter(20);
    }

    [TearDown]
    public void TearDown()
    {
        CharacterStress.Clear();
        CharacterStress.RecoveryPerHour = savedRecoveryPerHour;
        CharacterStress.MaxCatchUpHours = savedMaxCatchUpHours;
        if (character != null) Object.DestroyImmediate(character);
    }

    [Test]
    public void 기록이_없으면_타고난_스트레스를_돌려준다()
    {
        Assert.AreEqual(20f, CharacterStress.Get(character), 0.001f);
        Assert.IsFalse(CharacterStress.Has(character));
    }

    [Test]
    public void 회복이_꺼져_있으면_넣은_값이_그대로_읽힌다()
    {
        CharacterStress.RecoveryPerHour = 0f;
        CharacterStress.Set(character, 55f);

        Assert.AreEqual(55f, CharacterStress.Get(character), 0.001f);
        Assert.IsTrue(CharacterStress.Has(character));
    }

    [Test]
    public void 스트레스는_음수가_되지_않는다()
    {
        CharacterStress.RecoveryPerHour = 0f;
        CharacterStress.Set(character, -30f);

        Assert.AreEqual(0f, CharacterStress.Get(character), 0.001f);
    }

    // "n시간 전에 정산했다"는 상황을 만든다. 실제 시계를 쓰기 때문에
    // 경과 시간을 이렇게 직접 얹지 않으면 테스트 안에서는 늘 0에 가깝다.
    private static void PretendHoursPassed(double hours)
    {
        StressClock.RestoreStamp(System.DateTime.UtcNow.AddHours(-hours).Ticks);
    }

    [Test]
    public void 시간이_지나면_읽는_값이_줄어든다()
    {
        CharacterStress.RecoveryPerHour = 0f;
        CharacterStress.Set(character, 80f);

        CharacterStress.RecoveryPerHour = 10f;
        CharacterStress.MaxCatchUpHours = 12f;
        PretendHoursPassed(3d);

        Assert.AreEqual(50f, CharacterStress.Get(character), 0.05f, "3시간 x 10 = 30만큼 줄어든다");
    }

    [Test]
    public void 정산은_밀린_회복을_값에_확정한다()
    {
        CharacterStress.RecoveryPerHour = 0f;
        CharacterStress.Set(character, 80f);

        CharacterStress.RecoveryPerHour = 10f;
        CharacterStress.MaxCatchUpHours = 12f;
        PretendHoursPassed(3d);
        CharacterStress.Settle();

        // 정산 뒤에는 시각이 지금으로 옮겨졌으므로, 같은 몫을 한 번 더 깎지 않는다.
        Assert.AreEqual(50f, CharacterStress.Get(character), 0.05f);
    }

    [Test]
    public void 상한이_한꺼번에_다_깎이는_것을_막는다()
    {
        CharacterStress.RecoveryPerHour = 0f;
        CharacterStress.Set(character, 100f);

        CharacterStress.RecoveryPerHour = 1f;
        CharacterStress.MaxCatchUpHours = 12f;
        PretendHoursPassed(100d); // 나흘 넘게 비웠어도 12시간분만 반영된다.

        Assert.AreEqual(88f, CharacterStress.Get(character), 0.05f);
    }

    [Test]
    public void 값을_덮어쓰면_밀린_회복이_새_값에_겹치지_않는다()
    {
        CharacterStress.RecoveryPerHour = 10f;
        CharacterStress.MaxCatchUpHours = 12f;
        CharacterStress.Set(character, 80f);

        PretendHoursPassed(5d);
        CharacterStress.Set(character, 90f); // 전투가 끝나 새 값을 기록한 상황

        Assert.AreEqual(90f, CharacterStress.Get(character), 0.05f,
            "방금 기록한 값에 남의 경과분이 겹쳐서는 안 된다");
    }

    [Test]
    public void 세이브_복원은_그_뒤로_흐른_시간을_반영한다()
    {
        // Set은 "지금 이 값", Restore는 "정산 시각 기준의 값"이다.
        // 이 구분이 무너지면 게임을 꺼둔 동안의 회복이 통째로 사라진다.
        CharacterStress.RecoveryPerHour = 10f;
        CharacterStress.MaxCatchUpHours = 12f;

        PretendHoursPassed(4d); // 4시간 전에 저장하고 게임을 껐다
        CharacterStress.Restore(character, 70f);

        Assert.AreEqual(30f, CharacterStress.Get(character), 0.05f);
    }
}
