// 플레이어 본인의 칸 — 이름과 재화.
internal sealed class AccountSaveSection : ISaveSection
{
    private readonly Wallet wallet;

    public AccountSaveSection(Wallet wallet)
    {
        this.wallet = wallet;
    }

    public void WriteTo(SaveData data)
    {
        wallet.Snapshot(out string name, out long gold, out long gems, out bool starterGranted);
        data.account = new AccountRecord { name = name, gold = gold, gems = gems, starterGranted = starterGranted };
    }

    // 세이브가 없거나 깨졌으면 기본 이름에 재화 0으로 시작한다.
    public void ReadFrom(SaveData data)
    {
        AccountRecord record = data?.account ?? new AccountRecord();
        wallet.Restore(record.name, record.gold, record.gems, record.starterGranted);
    }

    public void Forget() => wallet.Forget();
}
