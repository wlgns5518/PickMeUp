using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "Character", menuName = "PickMeUp/Character")]
public class CharacterSO : ScriptableObject
{
    // 신원 --------------------------------------------------------------
    [Header("Identity")]
    public string characterName;
    [TextArea] public string description;
    public Sprite portrait;
    public string portraitAssetPath; // Assets/CharacterImage/*.png

    // 희귀도 ------------------------------------------------------------
    [Header("Rarity")]
    [Range(1, 7)] public int starCount = 1;

    public int MaxLevel => CharacterRules.MaxLevelForStars(starCount);

    // 진행도 ------------------------------------------------------------
    [Header("Progression")]
    [Range(1, 99)] public int level = 1;
    public int exp;
    public int expToNext = 10;

    // 직업 --------------------------------------------------------------
    [Header("Job")]
    public JobType job;

    // 체질 (스탯 성장 가중치) -------------------------------------------
    [Header("Constitution")]
    public Constitution constitution = new Constitution();

    // 보이는 스탯 -------------------------------------------------------
    [Header("Visible Stats")]
    public VisibleStats stats = new VisibleStats();

    // 보이지 않는 스탯 ---------------------------------------------------
    [Header("Hidden Stats")]
    public HiddenStats hiddenStats = new HiddenStats();

    // 장비 --------------------------------------------------------------
    [Header("Equipment")]
    public WeaponType mainHand = WeaponType.None;
    public OffHandType offHand = OffHandType.None;

    // 관계 --------------------------------------------------------------
    [Header("Relationships")]
    public List<FriendshipEntry> friendships = new List<FriendshipEntry>();

    // 런타임 상태 -------------------------------------------------------
    [Header("Runtime State")]
    public bool isDead;
    public bool firstBattleDone;
    public int currentHP;
    public EmotionState emotionState = EmotionState.None;

    // 유틸 --------------------------------------------------------------

    public bool CanEquipShield => !CharacterRules.IsTwoHanded(mainHand);

    public bool HasEmotion(EmotionState s) => (emotionState & s) != 0;
    public void AddEmotion(EmotionState s) => emotionState |= s;
    public void RemoveEmotion(EmotionState s) => emotionState &= ~s;

    // 장비 변경 — 두손무기 장착 시 방패 자동 해제
    public void EquipMainHand(WeaponType w)
    {
        mainHand = w;
        if (CharacterRules.IsTwoHanded(w)) offHand = OffHandType.None;
    }

    public bool EquipShield()
    {
        if (!CanEquipShield) return false;
        offHand = OffHandType.Shield;
        return true;
    }

    // 경험치 획득. 한계 레벨에 도달하면 더 이상 안 오름.
    // 별이 높으면 더 많이 오름 + 체질 가중치로 4스탯 분배.
    public void GainExp(int amount)
    {
        if (isDead) return;
        exp += amount;
        while (exp >= expToNext && level < MaxLevel)
        {
            exp -= expToNext;
            level++;
            ApplyLevelUpStats();
            expToNext = ExpForLevel(level);
        }
        if (level >= MaxLevel) exp = 0;
    }

    private static int ExpForLevel(int level) => 10 + (level - 1) * 5;

    private void ApplyLevelUpStats()
    {
        int total = CharacterRules.StatPointsPerLevel(starCount);
        float wS = Mathf.Max(0f, constitution.strengthGrowth);
        float wI = Mathf.Max(0f, constitution.intelligenceGrowth);
        float wV = Mathf.Max(0f, constitution.vitalityGrowth);
        float wA = Mathf.Max(0f, constitution.agilityGrowth);
        float sum = wS + wI + wV + wA;
        if (sum <= 0f) { wS = wI = wV = wA = 1f; sum = 4f; }

        int s = Mathf.RoundToInt(total * (wS / sum));
        int i = Mathf.RoundToInt(total * (wI / sum));
        int v = Mathf.RoundToInt(total * (wV / sum));
        int a = total - s - i - v; // 잔여를 민첩에 몰아주어 합계 보장

        stats.strength     += s;
        stats.intelligence += i;
        stats.vitality     += v;
        stats.agility      += a;
    }

    // 친밀도 ------------------------------------------------------------

    public int GetFriendship(CharacterSO other)
    {
        if (other == null) return 0;
        for (int i = 0; i < friendships.Count; i++)
            if (friendships[i].target == other) return friendships[i].value;
        return 0;
    }

    public void ModifyFriendship(CharacterSO other, int delta)
    {
        if (other == null) return;
        for (int i = 0; i < friendships.Count; i++)
        {
            if (friendships[i].target == other)
            {
                friendships[i].value += delta;
                return;
            }
        }
        friendships.Add(new FriendshipEntry { target = other, value = delta });
    }

    // 사망 처리 — PickMeUp 설정: 죽으면 사라진다.
    // 이미지 PNG와 SO 에셋도 함께 삭제한다. (에디터 한정)
    public void Kill()
    {
        isDead = true;
        emotionState = EmotionState.None;
        currentHP = 0;
        DeleteAssets();
    }

    public void DeleteAssets()
    {
#if UNITY_EDITOR
        try
        {
            if (!string.IsNullOrEmpty(portraitAssetPath))
            {
                AssetDatabase.DeleteAsset(portraitAssetPath);
                portraitAssetPath = null;
                portrait = null;
            }

            string soPath = AssetDatabase.GetAssetPath(this);
            if (!string.IsNullOrEmpty(soPath))
            {
                AssetDatabase.DeleteAsset(soPath);
                Debug.Log($"[CharacterSO] 삭제: {soPath}");
            }
            AssetDatabase.SaveAssets();
        }
        catch (Exception e)
        {
            Debug.LogError($"[CharacterSO] 에셋 삭제 실패: {e.Message}");
        }
#endif
    }
}
