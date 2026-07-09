using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "Character", menuName = "PickMeUp/Character")]
public class CharacterSO : ScriptableObject
{
    // Identity ----------------------------------------------------------
    [Header("Identity")]
    public string characterName;
    [TextArea] public string description;
    public Sprite portrait;
    public string portraitAssetPath; // Assets/CharacterImage/*.png

    // Rarity ------------------------------------------------------------
    [Header("Rarity")]
    [Range(1, 7)] public int starCount = 1;

    public int MaxLevel => CharacterRules.MaxLevelForStars(starCount);

    // Progression -------------------------------------------------------
    [Header("Progression")]
    [Range(1, 99)] public int level = 1;
    public int exp;
    public int expToNext = 10;

    // Job ---------------------------------------------------------------
    [Header("Job")]
    public JobType job;

    // Appearance --------------------------------------------------------
    [Header("Appearance")]
    public Gender gender;
    [Range(0, 1)] public int hairIndex; // 0 or 1
    public OutfitSet outfit;

    public bool HasBeard => gender == Gender.Male;

    // Constitution ------------------------------------------------------
    [Header("Constitution")]
    public Constitution constitution = new Constitution();

    // Visible Stats -----------------------------------------------------
    [Header("Visible Stats")]
    public VisibleStats stats = new VisibleStats();

    // Hidden Stats ------------------------------------------------------
    [Header("Hidden Stats")]
    public HiddenStats hiddenStats = new HiddenStats();

    // Equipment ---------------------------------------------------------
    [Header("Equipment")]
    public WeaponType mainHand = WeaponType.None;
    public OffHandType offHand = OffHandType.None;

    // Relationships -----------------------------------------------------
    [Header("Relationships")]
    public List<FriendshipEntry> friendships = new List<FriendshipEntry>();

    // Utility -----------------------------------------------------------

    public bool CanEquipShield => !CharacterRules.IsTwoHanded(mainHand);

    // Change equipment. Equipping a two-handed weapon automatically removes the shield.
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

    // Gain experience. Level-ups increase visible stats using constitution growth weights.
    public void GainExp(int amount)
    {
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
        int a = total - s - i - v; // Assign the remainder to agility so the total stays exact.

        stats.strength     += s;
        stats.intelligence += i;
        stats.vitality     += v;
        stats.agility      += a;
    }

    // Relationships -----------------------------------------------------

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

    // Editor-only asset deletion. Runtime death/state handling belongs in runtime character data.
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
                Debug.Log($"[CharacterSO] Deleted: {soPath}");
            }
            AssetDatabase.SaveAssets();
        }
        catch (Exception e)
        {
            Debug.LogError($"[CharacterSO] Failed to delete assets: {e.Message}");
        }
#endif
    }
}
