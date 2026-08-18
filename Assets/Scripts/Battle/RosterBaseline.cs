using System.Collections.Generic;
using UnityEngine;

// CharacterSO의 "원본 값"을 플레이 시작 시점에 붙잡아 두고, 플레이가 끝나면 되돌린다.
//
// CharacterSO는 에셋이라 GainExp로 레벨이 오르면 그 변경이 에디터 에셋에 그대로 남는다.
// 즉 테스트로 한 판 돌릴 때마다 원본 캐릭터가 영구히 레벨업해 버린다.
// 진짜 진행도는 세이브 파일이 들고 있고, 에셋은 어디까지나 시작값 템플릿이어야 한다.
//
// 그래서 런타임에는 에셋을 작업 사본처럼 자유롭게 고치되,
// 플레이 모드를 나갈 때 원본으로 되돌려 에디터 데이터가 더럽혀지지 않게 한다.
public static class RosterBaseline
{
    private struct Baseline
    {
        public int Level;
        public int Exp;
        public int ExpToNext;
        public int Strength;
        public int Intelligence;
        public int Vitality;
        public int Agility;
    }

    private static readonly Dictionary<CharacterSO, Baseline> baselines = new Dictionary<CharacterSO, Baseline>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        baselines.Clear();
#if UNITY_EDITOR
        // 중복 구독을 피하려고 먼저 떼고 붙인다(도메인 리로드를 끈 설정 대비).
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
#endif
    }

#if UNITY_EDITOR
    private static void OnPlayModeChanged(UnityEditor.PlayModeStateChange change)
    {
        if (change != UnityEditor.PlayModeStateChange.ExitingPlayMode) return;
        RestoreAll();
    }
#endif

    // 아직 아무것도 고치지 않은 시점에 불러야 한다. 이미 잡아둔 캐릭터는 다시 덮어쓰지 않는다.
    public static void Capture(CharacterSO character)
    {
        if (character == null || baselines.ContainsKey(character)) return;

        baselines[character] = new Baseline
        {
            Level = character.level,
            Exp = character.exp,
            ExpToNext = character.expToNext,
            Strength = character.stats.strength,
            Intelligence = character.stats.intelligence,
            Vitality = character.stats.vitality,
            Agility = character.stats.agility,
        };
    }

    public static void CaptureAll(IReadOnlyList<CharacterSO> roster)
    {
        if (roster == null) return;
        for (int i = 0; i < roster.Count; i++) Capture(roster[i]);
    }

    public static void RestoreAll()
    {
        foreach (KeyValuePair<CharacterSO, Baseline> pair in baselines)
        {
            CharacterSO so = pair.Key;
            if (so == null) continue;

            Baseline b = pair.Value;
            so.level = b.Level;
            so.exp = b.Exp;
            so.expToNext = b.ExpToNext;
            so.stats.strength = b.Strength;
            so.stats.intelligence = b.Intelligence;
            so.stats.vitality = b.Vitality;
            so.stats.agility = b.Agility;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(so);
#endif
        }

        baselines.Clear();
    }
}
