using System;
using UnityEngine;

// 스트레스 회복량을 계산하기 위한 "마지막으로 본 시각" 기록.
//
// 게임을 껐다 켜도 시간이 흐른 만큼은 회복돼야 하므로,
// 플레이 시간이 아니라 실제 시계(UTC)를 기준으로 삼는다.
// 시스템 시계를 뒤로 돌리면 음수가 나올 수 있어 그 경우는 0으로 취급한다.
public static class StressClock
{
    private const string Key = "pickmeup_stress_stamp_utc";

    private static long stampTicks;
    private static bool hasStamp;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        hasStamp = false;
        stampTicks = 0L;

        // 세이브 파일과 별개로 관리한다. 시각은 로스터 진행도가 아니라 세션 정보라서,
        // 세이브가 없는 첫 실행에서도 기준점이 필요하다.
        string raw = PlayerPrefs.GetString(Key, string.Empty);
        if (string.IsNullOrEmpty(raw)) return;

        long parsed;
        if (!long.TryParse(raw, out parsed)) return;

        stampTicks = parsed;
        hasStamp = true;
    }

    public static void Stamp()
    {
        stampTicks = DateTime.UtcNow.Ticks;
        hasStamp = true;
        PlayerPrefs.SetString(Key, stampTicks.ToString());
        PlayerPrefs.Save();
    }

    // 마지막 기록 이후 흐른 실제 초. 기록이 없으면 0.
    public static double SecondsSinceStamp()
    {
        if (!hasStamp) return 0d;

        double seconds = (DateTime.UtcNow.Ticks - stampTicks) / (double)TimeSpan.TicksPerSecond;
        return seconds > 0d ? seconds : 0d;
    }
}
