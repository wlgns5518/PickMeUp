using System;
using UnityEngine;

// 전투 한 판에서 아군 한 명이 남긴 기록 + 정산 결과.
// 유닛 인스턴스는 전투 후 파괴될 수 있으므로 결과창이 읽을 값은 여기로 복사해 둔다.
public class BattleReward
{
    public CharacterSO Character;
    public string DisplayName;

    // 기여도
    public int Kills;
    public int DamageDealt;
    public int DamageTaken;
    public bool Survived;

    // 정산
    public int ExpGained;
    public int LevelBefore;
    public int LevelAfter;
    public bool IsMvp;

    public bool LeveledUp => LevelAfter > LevelBefore;
}

// 정산 공식. 밸런싱 대상이라 전부 인스펙터에 노출한다.
[Serializable]
public class BattleRewardSettings
{
    [Header("경험치")]
    [Tooltip("승리 시 생존자 전원이 받는 기본 경험치.")]
    [Min(0)] public int expOnVictory = 20;
    [Tooltip("패배/무승부로 끝났을 때 생존자가 받는 기본 경험치.")]
    [Min(0)] public int expOnDefeat = 5;
    [Tooltip("처치 1회당 경험치.")]
    [Min(0)] public int expPerKill = 8;
    [Tooltip("가한 피해 이만큼마다 경험치 1.")]
    [Min(1)] public int damagePerExp = 10;
    [Tooltip("MVP 추가 경험치.")]
    [Min(0)] public int mvpExpBonus = 15;
    [Tooltip("전투 중 쓰러진 참가자가 받는 경험치 비율. 영구 죽음이 아니면 이들도 다음 전투에 다시 나오므로, " +
             "0으로 두면 한 번 쓰러진 캐릭터만 성장이 영영 멈춰 파티 레벨이 벌어진다.")]
    [Range(0f, 1f)] public float expRatioWhenDown = 0.5f;

    [Header("MVP 점수")]
    [Tooltip("처치 1회의 환산 점수. 가한 피해는 1점으로 계산된다.")]
    public float mvpKillWeight = 25f;
    [Tooltip("받은 피해의 환산 점수. 앞에서 얻어맞은 탱커도 후보에 오르게 하는 가중치.")]
    public float mvpTankWeight = 0.3f;
    [Tooltip("살아남은 유닛에게 주는 가산점. 쓰러진 동료도 압도적이었다면 MVP가 될 수 있다.")]
    public float mvpSurvivalBonus = 30f;
}
