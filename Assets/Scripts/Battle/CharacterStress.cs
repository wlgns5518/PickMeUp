using System.Collections.Generic;
using UnityEngine;

// 캐릭터별 누적 스트레스를 전투 밖에서도 들고 있는 곳.
//
// HP와 마나는 전투에 들어갈 때마다 만회복되지만 스트레스만은 그렇지 않다.
// 지금까지는 UnitEmotion.Configure가 매 전투 시작 시 hiddenStats.stress로 되돌려서
// 아무리 험한 전투를 겪어도 다음 전투에 흔적이 남지 않았다.
//
// 스트레스는 쌓이기만 하고 줄지 않으면 결국 전원이 붕괴하므로,
// 시간이 지나면 회복되는 축(StressRecovery)이 함께 있어야 성립한다.
public static class CharacterStress
{
    private static readonly Dictionary<CharacterSO, float> stressByCharacter =
        new Dictionary<CharacterSO, float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 이전 플레이 값이 남지 않도록 비운다.
        // 실제 값은 세이브에서 다시 읽어 온다.
        stressByCharacter.Clear();
    }

    // 기록이 없으면 캐릭터의 타고난 스트레스(히든 스탯)를 시작값으로 본다.
    public static float Get(CharacterSO character)
    {
        if (character == null) return 0f;

        float value;
        if (stressByCharacter.TryGetValue(character, out value)) return value;
        return character.hiddenStats != null ? Mathf.Max(0f, character.hiddenStats.stress) : 0f;
    }

    public static void Set(CharacterSO character, float value)
    {
        if (character == null) return;
        stressByCharacter[character] = Mathf.Max(0f, value);
    }

    public static bool Has(CharacterSO character)
    {
        return character != null && stressByCharacter.ContainsKey(character);
    }

    // 시간 경과분을 한 번에 깎는다. 메인 씬에서 매 프레임, 그리고 세이브를 읽을 때
    // 게임을 꺼둔 동안의 경과분에도 같은 함수를 쓴다.
    public static void DecayAll(float amount)
    {
        if (amount <= 0f || stressByCharacter.Count == 0) return;

        // 순회 중에 값을 바꿀 수 없으므로 키를 먼저 모은다.
        var keys = new List<CharacterSO>(stressByCharacter.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            CharacterSO key = keys[i];
            stressByCharacter[key] = Mathf.Max(0f, stressByCharacter[key] - amount);
        }
    }

    public static void Clear()
    {
        stressByCharacter.Clear();
    }
}
