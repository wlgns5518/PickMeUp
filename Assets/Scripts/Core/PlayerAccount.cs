using System;

public enum Currency
{
    Gold,
    // 유료 재화.
    Gem,
}

// 플레이어 본인의 것 — 이름과 재화. 마을 상단바(TopBarHud)가 그대로 읽어 보여 준다.
//
// 입구일 뿐이다. 값과 규칙은 Wallet이, 세이브와의 연결은 GameServices가 든다.
// 생성자로 의존성을 받을 수 있는 코드는 IWallet(GameServices.Account)을 받는다.
public static class PlayerAccount
{
    // 이름을 정한 적이 없을 때. 원작에서 영웅을 부리는 쪽을 부르는 말이다.
    public const string DefaultName = Wallet.DefaultName;

    private static Wallet State => GameServices.AccountState;

    public static event Action Changed
    {
        add => State.Changed += value;
        remove => State.Changed -= value;
    }

    public static string Name => State.Name;

    public static long Balance(Currency currency) => State.Balance(currency);

    public static void Add(Currency currency, long amount) => State.Add(currency, amount);

    /// 모자라면 아무것도 빼지 않고 false.
    public static bool TrySpend(Currency currency, long amount) => State.TrySpend(currency, amount);

    // 세이브가 파일에서 읽은 값을 얹는다. 저장은 하지 않는다.
    public static void Restore(string name, long savedGold, long savedGems, bool savedStarterGranted) =>
        State.Restore(name, savedGold, savedGems, savedStarterGranted);

    // 세이브를 지웠을 때. 들고 있던 값을 버리고 다음에 쓰일 때 (빈) 파일에서 다시 읽는다.
    public static void Forget() => State.Forget();

    public static void Snapshot(out string name, out long savedGold, out long savedGems, out bool savedStarterGranted) =>
        State.Snapshot(out name, out savedGold, out savedGems, out savedStarterGranted);
}
