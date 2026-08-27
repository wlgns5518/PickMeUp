using System.Collections.Generic;
using UnityEngine;

// 캐릭터별 누적 스트레스를 전투 밖에서도 들고 있는 곳.
//
// HP와 마나는 전투에 들어갈 때마다 회복되지만 스트레스만은 그렇지 않다.
// 스트레스는 쌓이기만 하고 줄지 않으면 결국 전원이 붕괴하므로,
// 시간이 지나면 회복되는 축이 함께 있어야 성립한다.
//
// 그 회복을 매 프레임 깎아 나가지 않고 "마지막으로 정산한 시각"에서 유도한다.
// 예전에는 StressRecovery가 Update에서 매 프레임 전원의 값을 깎았는데,
//  - 시간당 10 감소는 프레임당 0.0000463이라 나눠 더하는 것 자체가 의미가 없었고,
//  - 값을 고치려면 키 목록을 복사해야 해서 매 프레임 힙 할당이 생겼으며(코드베이스에서 유일했다),
//  - 게임을 꺼둔 동안의 회복과 켜둔 동안의 회복이 서로 다른 경로로 갈라져 있었다.
// 지금은 읽을 때 계산하는 한 가지 경로뿐이라 셋 다 사라진다.
//
// 저장된 값은 항상 "정산 시각(StressClock) 기준의 값"이다. 읽을 때 그 시각 이후 흐른
// 실제 시간만큼을 빼서 돌려준다.
public static class CharacterStress
{
    // 시간당 감소량. StressRecovery가 인스펙터 값으로 덮어쓴다.
    public static float RecoveryPerHour { get; set; } = 10f;

    // 한 번에 반영할 수 있는 경과 시간의 상한(시간).
    // 오래 비워 뒀다고 스트레스가 한꺼번에 전부 사라지지 않게 한다.
    public static float MaxCatchUpHours { get; set; } = 12f;

    private static readonly Dictionary<CharacterSO, float> stressByCharacter =
        new Dictionary<CharacterSO, float>();

    // 정산 때 값을 고쳐야 하는데 Dictionary는 순회 중 수정이 안 된다.
    // 키 목록을 따로 들고 있으면 정산할 때마다 목록을 새로 만들지 않아도 된다.
    private static readonly List<CharacterSO> tracked = new List<CharacterSO>();

    // [테스트] 전투 중 스트레스 누적 스위치. false면 UnitEmotion.AddStress가 아무 일도 하지 않는다.
    //
    // 붕괴(Broken)는 행동불능이라 한 명이라도 무너지면 그 뒤의 전투 흐름을 볼 수 없다.
    // 다른 시스템을 검증하는 동안 꺼 두기 위한 스위치다.
    // 테스트가 끝나면 DefaultAccumulation을 true로 되돌릴 것.
    private const bool DefaultAccumulation = false;

    public static bool AccumulationEnabled { get; set; } = DefaultAccumulation;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 이전 플레이 값이 남지 않도록 비운다.
        // 실제 값은 세이브에서 다시 읽어 온다.
        Clear();
        AccumulationEnabled = DefaultAccumulation;
    }

    // 정산 시각 이후로 쌓인 회복량. 아직 값에 반영되지 않은 몫이다.
    private static float PendingRecovery()
    {
        if (RecoveryPerHour <= 0f) return 0f;

        double hours = StressClock.SecondsSinceStamp() / 3600d;
        if (hours <= 0d) return 0f;

        double capped = System.Math.Min(hours, Mathf.Max(0f, MaxCatchUpHours));
        return (float)(capped * RecoveryPerHour);
    }

    // 기록이 없으면 캐릭터의 타고난 스트레스(히든 스탯)를 시작값으로 본다.
    // 기록이 없는 캐릭터는 아직 한 번도 싸우지 않았다는 뜻이라 회복시킬 것도 없다.
    public static float Get(CharacterSO character)
    {
        if (character == null) return 0f;

        if (stressByCharacter.TryGetValue(character, out float stored))
        {
            return Mathf.Max(0f, stored - PendingRecovery());
        }

        return character.hiddenStats != null ? Mathf.Max(0f, character.hiddenStats.stress) : 0f;
    }

    // "지금 이 값이다"라고 덮어쓴다(전투 종료 시점 등).
    // 먼저 정산해서 기존 값들의 밀린 회복을 반영해야, 새로 넣는 값에 남의 경과분이 겹치지 않는다.
    public static void Set(CharacterSO character, float value)
    {
        if (character == null) return;

        Settle();
        StoreRaw(character, value);
    }

    // "정산 시각 기준의 값이다"라고 얹는다. 세이브에서 읽어올 때만 쓴다 —
    // 저장된 값은 저장 시점(=그때의 정산 시각)의 값이므로, 그 뒤로 흐른 시간은
    // Get이 알아서 빼 준다. 여기서 정산해 버리면 자리비움 회복이 통째로 사라진다.
    public static void Restore(CharacterSO character, float value)
    {
        if (character == null) return;
        StoreRaw(character, value);
    }

    private static void StoreRaw(CharacterSO character, float value)
    {
        if (!stressByCharacter.ContainsKey(character)) tracked.Add(character);
        stressByCharacter[character] = Mathf.Max(0f, value);
    }

    public static bool Has(CharacterSO character)
    {
        return character != null && stressByCharacter.ContainsKey(character);
    }

    // 밀린 회복을 값에 실제로 반영하고 시각을 지금으로 옮긴다.
    // 저장 직전과 값을 덮어쓰기 직전에 부른다 — 그 밖에는 부를 필요가 없다(읽을 때 계산되므로).
    public static void Settle()
    {
        float recovery = PendingRecovery();
        StressClock.Stamp();

        if (recovery <= 0f) return;

        for (int i = 0; i < tracked.Count; i++)
        {
            CharacterSO key = tracked[i];
            if (key == null) continue;
            stressByCharacter[key] = Mathf.Max(0f, stressByCharacter[key] - recovery);
        }
    }

    public static void Clear()
    {
        stressByCharacter.Clear();
        tracked.Clear();
    }
}
