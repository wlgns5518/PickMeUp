// 스트레스 회복량을 계산하기 위한 "마지막으로 본 시각" 기록.
//
// 입구일 뿐이다. 시각은 PlayerPrefsStressClock이 든다. 생성자로 의존성을 받을 수 있는 코드는
// IStressClock(GameServices.Clock)을 받는다.
public static class StressClock
{
    private static IStressClock State => GameServices.ClockState;

    // 기록된 시각. 0이면 아직 기록이 없다는 뜻이다.
    public static long StampTicks => State.StampTicks;

    public static void Stamp() => State.Stamp();

    // 세이브 파일에 담긴 정산 시각을 복원할 때 쓴다.
    public static void RestoreStamp(long utcTicks) => State.RestoreStamp(utcTicks);

    // 마지막 기록 이후 흐른 실제 초. 기록이 없으면 0.
    public static double SecondsSinceStamp() => State.SecondsSinceStamp();
}
