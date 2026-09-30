using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 게임 상태 저장소를 세이브 파일·정적 상태 없이 하나씩 떼어 본다.
// 예전에는 지갑 하나를 보려 해도 SaveSystem과 실제 세이브 파일이 함께 움직였다.
public class GameStateStoreTests
{
    private sealed class FakeWallet : IWallet
    {
        public long Gems;
        public string Name => "시험";
        public long Balance(Currency currency) => currency == Currency.Gem ? Gems : 0;
        public void Add(Currency currency, long amount) { if (currency == Currency.Gem) Gems += amount; }

        public bool TrySpend(Currency currency, long amount)
        {
            if (currency != Currency.Gem || Gems < amount) return false;
            Gems -= amount;
            return true;
        }

        public event Action Changed { add { } remove { } }
    }

    private static CharacterSO NewCharacter(string name)
    {
        var so = ScriptableObject.CreateInstance<CharacterSO>();
        so.name = name;
        so.characterName = name;
        return so;
    }

    [Test]
    public void 시작_젬은_처음_채울_때_한_번만_주고_저장을_부탁한다()
    {
        var wallet = new Wallet(starterGems: 3000);
        int loads = 0, commits = 0;
        wallet.BindLoader(() => loads++);
        wallet.Committed += () => commits++;

        Assert.AreEqual(3000, wallet.Balance(Currency.Gem));
        Assert.AreEqual(3000, wallet.Balance(Currency.Gem));
        Assert.AreEqual(1, loads);
        Assert.AreEqual(1, commits, "시작 젬 지급이 저장돼야 다음에 켤 때 또 받지 않는다.");
    }

    [Test]
    public void 파일에서_되살릴_때는_저장을_부탁하지_않는다()
    {
        var wallet = new Wallet(starterGems: 3000);
        int commits = 0;
        wallet.Committed += () => commits++;

        wallet.Restore("A", 100, 5, savedStarterGranted: true);

        Assert.AreEqual(100, wallet.Balance(Currency.Gold));
        Assert.AreEqual(0, commits);
    }

    [Test]
    public void 시설_레벨은_받은_지갑에서만_값을_낸다()
    {
        var wallet = new FakeWallet { Gems = GameEconomy.FacilityUpgradeCost(2) };
        var levels = new FacilityLevelStore(wallet);
        int commits = 0;
        levels.Committed += () => commits++;

        Assert.IsTrue(levels.TryUpgrade(VillageBlockout.Kind.Summoning, out _));
        Assert.AreEqual(2, levels.Get(VillageBlockout.Kind.Summoning));
        Assert.AreEqual(0, wallet.Gems);
        Assert.AreEqual(1, commits);

        Assert.IsFalse(levels.TryUpgrade(VillageBlockout.Kind.Summoning, out string reason));
        StringAssert.Contains("젬이 부족합니다", reason);
        Assert.AreEqual(2, levels.Get(VillageBlockout.Kind.Summoning), "못 냈으면 레벨도 그대로다.");
    }

    [Test]
    public void 편성은_받은_사망_기록과_파티_규칙을_따른다()
    {
        var fallen = new FallenRecord();
        var deck = new PartyDeckStore(fallen, index => index == 0);
        CharacterSO alive = NewCharacter("A");
        CharacterSO dead = NewCharacter("B");
        fallen.MarkFallen(dead);

        Assert.IsTrue(deck.Add(alive));
        Assert.IsFalse(deck.Add(dead), "쓰러진 사람은 편성에 오르지 않는다.");

        deck.SetActive(1);
        Assert.AreEqual(0, deck.ActiveIndex, "잠긴 파티로는 옮겨 가지 않는다.");
    }

    [Test]
    public void 편성은_세이브를_읽기_전에는_저장을_부탁하지_않는다()
    {
        var deck = new PartyDeckStore(new FallenRecord(), _ => true);
        int commits = 0;
        deck.Committed += () => commits++;

        deck.Add(NewCharacter("A"));
        Assert.AreEqual(0, commits);

        deck.Restore(new List<IReadOnlyList<CharacterSO>>(), 0);
        deck.Add(NewCharacter("B"));
        Assert.AreEqual(1, commits);
    }

    [Test]
    public void 명단에서_빠지면_딸린_것을_치운_뒤에_화면에_알린다()
    {
        var roster = new OwnedRosterStore();
        CharacterSO hero = NewCharacter("A");
        roster.Add(hero);

        var order = new List<string>();
        roster.Removed += c => order.Add("removed:" + c.name);
        roster.Changed += () => order.Add("changed");

        Assert.IsTrue(roster.Remove(hero));
        CollectionAssert.AreEqual(new[] { "removed:A", "changed" }, order);
    }

    [Test]
    public void 스트레스_회복은_받은_시계로_계산한다()
    {
        var clock = new FixedClock { Seconds = 3600 * 2 };
        var ledger = new StressLedger(clock) { RecoveryPerHour = 10f };
        CharacterSO hero = NewCharacter("A");

        ledger.Restore(hero, 50f);

        Assert.AreEqual(30f, ledger.Get(hero), 0.001f);
    }

    private sealed class FixedClock : IStressClock
    {
        public double Seconds;
        public long StampTicks => 1;
        public void Stamp() => Seconds = 0;
        public void RestoreStamp(long utcTicks) { }
        public double SecondsSinceStamp() => Seconds;
    }
}
