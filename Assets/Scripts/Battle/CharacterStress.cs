// 캐릭터별 누적 스트레스를 전투 밖에서도 들고 있는 곳.
//
// 입구일 뿐이다. 값과 시간 회복 규칙은 StressLedger가, 정산 시각은 IStressClock이 든다.
// 생성자로 의존성을 받을 수 있는 코드는 IStressLedger(GameServices.Stress)를 받는다.
public static class CharacterStress
{
    private static StressLedger State => GameServices.StressState;

    // 시간당 감소량. StressRecovery가 인스펙터 값으로 덮어쓴다.
    public static float RecoveryPerHour
    {
        get => State.RecoveryPerHour;
        set => State.RecoveryPerHour = value;
    }

    // 한 번에 반영할 수 있는 경과 시간의 상한(시간).
    public static float MaxCatchUpHours
    {
        get => State.MaxCatchUpHours;
        set => State.MaxCatchUpHours = value;
    }

    // [테스트] 전투 중 스트레스 누적 스위치(StressLedger.DefaultAccumulation 주석 참조).
    public static bool AccumulationEnabled
    {
        get => State.AccumulationEnabled;
        set => State.AccumulationEnabled = value;
    }

    public static float Get(CharacterSO character) => State.Get(character);

    public static void Set(CharacterSO character, float value) => State.Set(character, value);

    // 세이브에서 읽어올 때만 쓴다(StressLedger.Restore 주석 참조).
    public static void Restore(CharacterSO character, float value) => State.Restore(character, value);

    public static bool Has(CharacterSO character) => State.Has(character);

    public static void Settle() => State.Settle();

    public static void Clear() => State.Clear();
}
