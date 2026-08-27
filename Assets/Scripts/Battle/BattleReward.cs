using System;
using System.Collections.Generic;
using UnityEngine;

// 전투 한 판에서 아군 한 명이 남긴 기록 + 정산 결과.
// 유닛 인스턴스는 전투 후 파괴될 수 있으므로 결과창이 읽을 값은 여기로 복사해 둔다.
//
// 기여도는 참전한 전원이 남긴다(쓰러진 동료 포함). 정산은 살아서 판을 끝낸 쪽만 받는다 —
// 어느 쪽이 어디까지인지는 BattleManager.Settle 참조.
public class BattleReward
{
    public CharacterSO Character;
    public string DisplayName;

    // 기여도
    public int Kills;
    public int DamageDealt;
    public int DamageTaken;
    public bool Survived;

    // 정산. Survived가 false면 전부 손대지 않은 채로 남는다(경험치 0, 레벨 그대로).
    public int ExpGained;
    public int LevelBefore;
    public int LevelAfter;
    public bool IsMvp;

    // 이번 정산에서 조건이 채워져 새로 열린 스킬의 id. 합성과 무관하게 붙는다(SkillUnlocks 참조).
    public readonly List<string> UnlockedSkills = new List<string>();

    public bool LeveledUp => LevelAfter > LevelBefore;
}

// 정산 공식. 밸런싱 대상이라 전부 인스펙터에 노출한다.
//
// 경험치도 MVP도 살아남은 참가자만 대상으로 한다. 쓰러진 채 판이 끝난 동료는 이번 판에서
// 아무것도 얻지 못한다 — 끝까지 서 있었는가가 이 게임에서 유일하게 확실한 기여도다.
[Serializable]
public class BattleRewardSettings
{
    [Header("경험치")]
    [Tooltip("승리 시 살아남은 참가자가 받는 기본 경험치.")]
    [Min(0)] public int expOnVictory = 20;
    [Tooltip("패배/무승부로 끝났을 때 살아남은 참가자가 받는 기본 경험치.")]
    [Min(0)] public int expOnDefeat = 5;
    [Tooltip("처치 1회당 경험치. 가한 피해는 경험치로 쳐주지 않는다 — 이유는 BattleManager.Settle 참조.")]
    [Min(0)] public int expPerKill = 8;
    [Tooltip("MVP 추가 경험치.")]
    [Min(0)] public int mvpExpBonus = 10;

    [Header("MVP 점수")]
    [Tooltip("처치 1회의 환산 점수. 가한 피해는 1점으로 계산된다.")]
    public float mvpKillWeight = 25f;
    [Tooltip("받은 피해의 환산 점수. 앞에서 얻어맞고도 버틴 탱커를 후보에 올리는 가중치.")]
    public float mvpTankWeight = 0.3f;
}
