using UnityEngine;

// 어느 씬에서 시작하든 로스터 상태를 같은 방식으로 불러오는 진입점.
//
// 예전에는 전투 씬 스포너의 Awake에서만 세이브를 읽었다.
// 그래서 메인 씬에서 시작하면 층 해금도 스트레스도 복원되지 않았고,
// 자리비움 회복은 계산할 기준값 자체가 없어 영영 적용되지 않았다.
//
// 메인 씬과 전투 씬 양쪽에 하나씩 두면 어느 쪽으로 진입해도 상태가 같아진다.
// 두 번 불려도 결과가 같도록 되어 있어 중복 배치는 문제가 되지 않는다.
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public class RosterBootstrap : MonoBehaviour
{
    [Tooltip("보유 캐릭터 전원의 명단. 세이브를 읽고 쓰는 범위가 된다.")]
    [SerializeField] private CharacterRosterSO roster;

    private void Awake()
    {
        if (roster == null)
        {
            Debug.LogWarning("[RosterBootstrap] 로스터 에셋이 지정되지 않아 세이브를 읽지 못합니다.", this);
            return;
        }

        // 무엇이든 고치기 전에 원본 값을 붙잡아 둔다. 그 뒤에 세이브를 얹는다.
        RosterBaseline.CaptureAll(roster.Members);
        SaveSystem.Load(roster.Members);
    }
}
