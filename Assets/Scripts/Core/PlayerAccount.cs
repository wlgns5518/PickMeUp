using System;
using UnityEngine;

public enum Currency
{
    Gold,
    // 유료 재화.
    Gem,
}

// 플레이어 본인의 것 — 이름과 재화. 마을 상단바(TopBarHud)가 그대로 읽어 보여 준다.
//
// MaterialInventory와 같은 규칙을 따른다. 처음 쓰일 때 세이브 파일에서 스스로 읽어 오고, 바뀔 때마다
// 곧바로 저장한다. 전투 씬이 한 번도 읽지 않은 채 로스터를 저장해도 0골드로 덮어쓰지 않으려면
// 누가 먼저 읽든 파일에 있던 값이 먼저 올라와 있어야 한다.
public static class PlayerAccount
{
    // 이름을 정한 적이 없을 때. 원작에서 영웅을 부리는 쪽을 부르는 말이다.
    public const string DefaultName = "마스터";

    private static string playerName;
    private static long gold;
    private static long gems;
    private static bool starterGranted;
    private static bool loaded;

    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 이전 플레이의 값이 남지 않도록 비운다. 실제 값은 세이브에서 다시 읽는다.
        playerName = null;
        gold = 0;
        gems = 0;
        starterGranted = false;
        loaded = false;
        Changed = null;
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;

        // 읽는 도중 Restore가 다시 이리로 들어오지 않도록 먼저 세운다.
        loaded = true;
        SaveSystem.LoadAccount();

        // 시작 젬은 한 번만 준다. 이 칸이 생기기 전의 세이브도 여기서 한 번 받는다.
        if (starterGranted) return;
        starterGranted = true;
        gems += GameEconomy.StarterGems;
        SaveSystem.SaveAccount();
        Changed?.Invoke();
    }

    public static string Name
    {
        get
        {
            EnsureLoaded();
            return string.IsNullOrWhiteSpace(playerName) ? DefaultName : playerName;
        }
    }

    public static long Balance(Currency currency)
    {
        EnsureLoaded();
        return currency == Currency.Gem ? gems : gold;
    }

    public static void Add(Currency currency, long amount)
    {
        if (amount <= 0) return;
        EnsureLoaded();

        if (currency == Currency.Gem) gems += amount;
        else gold += amount;
        Commit();
    }

    /// 모자라면 아무것도 빼지 않고 false.
    public static bool TrySpend(Currency currency, long amount)
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

    private static void Commit()
    {
        SaveSystem.SaveAccount();
        Changed?.Invoke();
    }

    // SaveSystem이 파일에서 읽은 값을 얹는다. 저장은 하지 않는다(방금 읽은 것을 다시 쓸 이유가 없다).
    public static void Restore(string name, long savedGold, long savedGems, bool savedStarterGranted)
    {
        loaded = true;
        playerName = name;
        gold = Math.Max(0L, savedGold);
        gems = Math.Max(0L, savedGems);
        starterGranted = savedStarterGranted;
        Changed?.Invoke();
    }

    // 세이브를 지웠을 때. 들고 있던 값을 버리고 다음에 쓰일 때 (빈) 파일에서 다시 읽는다.
    public static void Forget()
    {
        playerName = null;
        gold = 0;
        gems = 0;
        starterGranted = false;
        loaded = false;
        Changed?.Invoke();
    }

    // 저장할 때 SaveSystem이 부른다. 이름을 정한 적이 없으면 비워 둔다 — 기본 이름을 파일에 박으면
    // 나중에 기본 이름을 바꿔도 옛 세이브는 옛 이름으로 남는다.
    public static void Snapshot(out string name, out long savedGold, out long savedGems, out bool savedStarterGranted)
    {
        EnsureLoaded();
        name = playerName;
        savedGold = gold;
        savedGems = gems;
        savedStarterGranted = starterGranted;
    }
}
