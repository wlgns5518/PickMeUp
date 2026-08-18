using UnityEngine;

// 메인 씬에 두는 컴포넌트. 시간이 지나면 스트레스가 줄어든다.
//
// 전투에 들어갈 때 HP와 마나는 만회복되지만 스트레스만 남기 때문에,
// 줄어드는 축이 없으면 전투를 반복할수록 파티 전원이 붕괴로 끝난다.
// "쉬면 회복된다"가 스트레스를 관리 가능한 자원으로 만들어 준다.
[DisallowMultipleComponent]
public class StressRecovery : MonoBehaviour
{
    [Tooltip("1시간당 감소량. 10이면 6분마다 1씩 줄어들고, 한계치 100에서 0까지 10시간 걸린다.")]
    [Min(0f)] [SerializeField] private float stressPerHour = 10f;

    [Tooltip("게임을 꺼둔 동안 흐른 시간도 회복에 반영한다.")]
    [SerializeField] private bool recoverWhileAway = true;

    [Tooltip("한 번에 반영할 수 있는 자리비움 시간의 상한(시간). 너무 오래 비웠을 때 한꺼번에 다 회복되는 것을 막는다.")]
    [SerializeField] private float maxAwayHours = 12f;

    private void Start()
    {
        if (recoverWhileAway) ApplyAwayRecovery();
    }

    private void Update()
    {
        // 메인 씬에 머무는 동안에도 실시간으로 줄어든다.
        CharacterStress.DecayAll(StressPerSecond * Time.deltaTime);
    }

    private void OnDisable()
    {
        // 씬을 떠나는 시점을 기록해 둬야 다음에 돌아왔을 때 경과분을 계산할 수 있다.
        StressClock.Stamp();
    }

    private float StressPerSecond => stressPerHour / 3600f;

    private void ApplyAwayRecovery()
    {
        double awaySeconds = StressClock.SecondsSinceStamp();
        if (awaySeconds <= 0d) return;

        double capped = System.Math.Min(awaySeconds, maxAwayHours * 3600d);
        CharacterStress.DecayAll((float)(capped * StressPerSecond));
    }
}
