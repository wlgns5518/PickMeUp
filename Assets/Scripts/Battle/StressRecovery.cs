using UnityEngine;

// 메인 씬에 두는 컴포넌트. 스트레스 회복 속도를 정하는 곳이다.
//
// 전투에 들어갈 때 HP와 마나는 만회복되지만 스트레스만 남기 때문에,
// 줄어드는 축이 없으면 전투를 반복할수록 파티 전원이 붕괴로 끝난다.
// "쉬면 회복된다"가 스트레스를 관리 가능한 자원으로 만들어 준다.
//
// 예전에는 이 컴포넌트가 Update에서 매 프레임 전원의 값을 깎았다. 지금은 회복을 읽는 시점에
// 계산하므로(CharacterStress 주석 참조) 여기 남은 일은 두 가지뿐이다 — 수치를 넘겨주는 것과,
// 씬을 떠날 때 밀린 회복을 값에 확정해 두는 것.
[DisallowMultipleComponent]
public class StressRecovery : MonoBehaviour
{
    [Tooltip("1시간당 감소량. 10이면 6분마다 1씩 줄어들고, 한계치 100에서 0까지 10시간 걸린다.")]
    [Min(0f)] [SerializeField] private float stressPerHour = 10f;

    [Tooltip("한 번에 반영할 수 있는 경과 시간의 상한(시간). 너무 오래 비웠을 때 한꺼번에 다 회복되는 것을 막는다. " +
             "게임을 꺼둔 시간과 켜둔 채 방치한 시간 모두에 같은 상한이 걸린다.")]
    [Min(0f)] [SerializeField] private float maxCatchUpHours = 12f;

    private void Awake()
    {
        Apply();
    }

    private void OnValidate()
    {
        // 인스펙터에서 값을 바꾸면 플레이 중에도 바로 반영된다.
        if (Application.isPlaying) Apply();
    }

    private void OnDisable()
    {
        // 씬을 떠나기 전에 여기까지의 회복을 값에 확정해 둔다.
        // 이걸 하지 않으면 전투 씬에서 저장할 때 정산 시각이 예전 그대로라 같은 몫을 두 번 깎는다.
        CharacterStress.Settle();
    }

    private void Apply()
    {
        CharacterStress.RecoveryPerHour = stressPerHour;
        CharacterStress.MaxCatchUpHours = maxCatchUpHours;
    }
}
