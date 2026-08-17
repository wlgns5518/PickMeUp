using UnityEngine;

// 프레임레이트를 무제한으로 두면(에디터 Play 모드 포함) GPU/CPU가 디스플레이 주사율까지
// 풀가동되어 랩탑 발열의 주 원인이 된다. 씬 로드 전에 상한을 걸어 항상 적용되게 한다.
public static class PerformanceSettings
{
    private const int TargetFrameRate = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyFrameRateCap()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = TargetFrameRate;
    }
}
