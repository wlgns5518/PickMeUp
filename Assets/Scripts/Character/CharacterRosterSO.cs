using System.Collections.Generic;
using UnityEngine;

// 보유 중인 캐릭터 전원의 명단.
//
// 세이브를 읽고 쓰려면 "이 게임에 존재하는 캐릭터가 누구인지"를 알아야 하는데,
// 그 목록이 전투 씬 스포너 안에만 있었다. 그래서 메인 씬에서는 세이브를 읽을 수 없었고,
// 결과적으로 스트레스 회복과 층 해금이 전투 씬에 들어가야만 복원되는 상태였다.
//
// 출전 명단(스포너의 allyCharacters)과는 다른 개념이다.
// 이쪽은 "가진 캐릭터 전부", 저쪽은 "이번에 내보낼 캐릭터"다.
[CreateAssetMenu(fileName = "CharacterRoster", menuName = "PickMeUp/Character Roster")]
public class CharacterRosterSO : ScriptableObject
{
    [SerializeField] private List<CharacterSO> members = new List<CharacterSO>();

    public IReadOnlyList<CharacterSO> Members => members;

#if UNITY_EDITOR
    // 에디터에서 소환으로 새 캐릭터가 만들어졌을 때 명단에 얹는다.
    // 여기 없는 캐릭터는 다음 플레이에서 보유 명단에 오르지 못한다 —
    // .asset 파일은 남아 있는데 편성 창에는 나오지 않는 상태가 된다.
    public bool EditorRegister(CharacterSO character)
    {
        if (character == null || members.Contains(character)) return false;

        members.Add(character);
        UnityEditor.EditorUtility.SetDirty(this);
        return true;
    }
#endif
}
