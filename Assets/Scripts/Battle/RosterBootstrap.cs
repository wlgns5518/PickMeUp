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

        // 성장 값은 CharacterProgress가 런타임에 들고 있으므로 에셋이 더럽혀질 일이 없다.
        // (예전에는 여기서 RosterBaseline이 원본을 붙잡아 두고 플레이를 나갈 때 되돌렸다.)
        SaveSystem.Load(roster.Members);

        // 에셋의 명단은 시작값이다. 소환으로 늘고 합성으로 주는 실제 보유 명단은 런타임 쪽이 든다.
        OwnedRoster.Seed(roster.Members);

        // 조건 해금은 보통 전투 정산에서 걸린다. 여기서 한 번 더 훑는 것은 그 그물에
        // 걸리지 않는 경우를 위해서다 — 스킬 표에 조건이 새로 추가되면, 이미 조건을 채워 둔
        // 캐릭터들은 다음 전투를 뛰기 전부터 그 스킬을 갖고 있어야 한다.
        SkillUnlocks.EvaluateAll(roster.Members);
    }
}
