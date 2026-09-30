using System;

// 플레이어 본인의 것 — 이름과 재화. 화면(상단바, 시설 화면)은 IWallet으로 본다.
public interface IWallet
{
    string Name { get; }
    long Balance(Currency currency);
    void Add(Currency currency, long amount);

    /// 모자라면 아무것도 빼지 않고 false.
    bool TrySpend(Currency currency, long amount);

    event Action Changed;
}

// 이름과 재화를 든 쪽. 저장은 모른다 — 바뀌면 Committed를 울리고, 조립하는 쪽(GameServices)이 저장한다.
public sealed class Wallet : PersistentState, IWallet
{
    // 이름을 정한 적이 없을 때. 원작에서 영웅을 부리는 쪽을 부르는 말이다.
    public const string DefaultName = "마스터";

    private readonly long starterGems;

    private string playerName;
    private long gold;
    private long gems;
    private bool starterGranted;

    public event Action Changed;

    /// starterGems: 처음 한 번 주는 젬(GameEconomy.StarterGems).
    public Wallet(long starterGems)
    {
        this.starterGems = starterGems;
    }

    // 시작 젬은 한 번만 준다. 이 칸이 생기기 전의 세이브도 여기서 한 번 받는다.
    protected override void OnLoaded()
    {
        if (starterGranted) return;
        starterGranted = true;
        gems += starterGems;
        Commit();
    }

    public string Name
    {
        get
        {
            EnsureLoaded();
            return string.IsNullOrWhiteSpace(playerName) ? DefaultName : playerName;
        }
    }

    public long Balance(Currency currency)
    {
        EnsureLoaded();
        return currency == Currency.Gem ? gems : gold;
    }

    public void Add(Currency currency, long amount)
    {
        if (amount <= 0) return;
        EnsureLoaded();

        if (currency == Currency.Gem) gems += amount;
        else gold += amount;
        Commit();
    }

    public bool TrySpend(Currency currency, long amount)
    {
        if (amount <= 0) return false;
        EnsureLoaded();

        if (currency == Currency.Gem)
        {
            if (gems < amount) return false;
            gems -= amount;
        }
        else
        {
            if (gold < amount) return false;
            gold -= amount;
        }
        Commit();
        return true;
    }

    private void Commit()
    {
        RaiseCommitted();
        Changed?.Invoke();
    }

    // ---- 세이브 연동 ------------------------------------------------------

    // 파일에서 읽은 값을 얹는다. 저장은 하지 않는다(방금 읽은 것을 다시 쓸 이유가 없다).
    public void Restore(string name, long savedGold, long savedGems, bool savedStarterGranted)
    {
        MarkLoaded();
        playerName = name;
        gold = Math.Max(0L, savedGold);
        gems = Math.Max(0L, savedGems);
        starterGranted = savedStarterGranted;
        Changed?.Invoke();
    }

    // 세이브를 지웠을 때. 들고 있던 값을 버리고 다음에 쓰일 때 (빈) 파일에서 다시 읽는다.
    public void Forget()
    {
        playerName = null;
        gold = 0;
        gems = 0;
        starterGranted = false;
        MarkUnloaded();
        Changed?.Invoke();
    }

    // 저장할 때 부른다. 이름을 정한 적이 없으면 비워 둔다 — 기본 이름을 파일에 박으면
    // 나중에 기본 이름을 바꿔도 옛 세이브는 옛 이름으로 남는다.
    public void Snapshot(out string name, out long savedGold, out long savedGems, out bool savedStarterGranted)
    {
        EnsureLoaded();
        name = playerName;
        savedGold = gold;
        savedGems = gems;
        savedStarterGranted = starterGranted;
    }
}
