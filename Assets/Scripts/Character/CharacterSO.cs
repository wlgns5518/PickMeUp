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
    // 세이브가 캐릭터를 식별하는 값. 예전에는 에셋 이름(name)을 그대로 썼는데,
    // 에셋을 리네임하는 순간 그 캐릭터의 진행도가 통째로 사라졌다.
    // 한 번 정해지면 바뀌지 않아야 하므로 인스펙터에서는 감춰 둔다.
    [SerializeField, HideInInspector] private string id;

    // 아직 식별자가 없는 에셋에는 처음 읽는 순간 붙여 준다. OnValidate에만 맡기면
    // 그 에셋이 한 번도 임포트되거나 편집되지 않은 경우 영영 비어 있고, 그때는 다시
    // 이름으로 식별하게 되어 리네임에 그대로 무너진다.
    // 빌드에는 에디터에서 이미 붙어 나간 값이 실려 있어야 한다 — 만에 하나 비어 있으면
    // 세션마다 달라지는 값을 만들어 내는 대신 예전 방식(에셋 이름)으로 떨어진다.
    public string Id
    {
        get
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(id)) AssignId();
#endif
            return string.IsNullOrEmpty(id) ? name : id;
        }
    }

#if UNITY_EDITOR
    private void AssignId()
    {
        id = System.Guid.NewGuid().ToString("N");
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    public string characterName;
    [TextArea] public string description;
    public Sprite portrait;
    public string portraitAssetPath; // Assets/Character/CharacterImage/*.png

    // Rarity ------------------------------------------------------------
    [Header("Rarity")]
    [Range(1, 7)] public int starCount = 1;

    public int MaxLevel => CharacterRules.MaxLevelForStars(starCount);

    // Progression -------------------------------------------------------
    // 아래 세 필드와 stats/skillIds는 "에셋에 적힌 시작값"이다. 전투로 굴러가는 현재 값은
    // CharacterProgress가 런타임에 들고 있다 — 에셋을 직접 고치면 에디터에서 한 판 돌릴 때마다
    // 원본 캐릭터가 영구히 바뀌기 때문. 읽을 때는 아래 대문자 프로퍼티(Level, Strength...)를 쓴다.
    [Header("Progression (시작값)")]
    [Range(1, 99)] public int level = 1;
    public int exp;
    public int expToNext = 10;

    // 지금 이 캐릭터의 실제 값. 소문자 필드는 시작값, 대문자 프로퍼티는 현재값이다.
    public int Level => CharacterProgress.LevelOf(this);
    public int Exp => CharacterProgress.ExpOf(this);
    public int ExpToNext => CharacterProgress.ExpToNextOf(this);
    public int Strength => CharacterProgress.StrengthOf(this);
    public int Intelligence => CharacterProgress.IntelligenceOf(this);
    public int Vitality => CharacterProgress.VitalityOf(this);
    public int Agility => CharacterProgress.AgilityOf(this);

    // Job ---------------------------------------------------------------
    [Header("Job")]
    public JobType job;

    [Tooltip("마법사가 평생 귀속되는 속성 하나. 원작에서 마법사는 단 하나의 속성만 다루며, " +
             "복수 속성은 극히 예외적인 천재나 특수 아티팩트에 한정된다. " +
             "이 값이 그 마법사가 쓸 수 있는 마법 전부를 결정한다(SpellCatalog). " +
             "마법사가 아닌 직업에서는 쓰이지 않는다.")]
    public MagicAffinity affinity = MagicAffinity.None;

    // 마법사인데 속성이 정해지지 않은 캐릭터는 화염으로 떨어진다.
    // 속성 없는 마법사는 마법을 하나도 쓸 수 없어 그냥 약한 원거리 유닛이 되기 때문이다 —
    // 조용히 무력해지는 것보다 기본값이라도 갖는 편이 낫다.
    public MagicAffinity Affinity =>
        job == JobType.Mage && affinity == MagicAffinity.None ? MagicAffinity.Fire : affinity;

    // Constitution ------------------------------------------------------
    [Header("Constitution")]
    public Constitution constitution = new Constitution();

    // Visible Stats -----------------------------------------------------
    [Header("Visible Stats (시작값)")]
    public VisibleStats stats = new VisibleStats();

    // Hidden Stats ------------------------------------------------------
    [Header("Hidden Stats")]
    public HiddenStats hiddenStats = new HiddenStats();

    // Equipment ---------------------------------------------------------
    [Header("Equipment")]
    public WeaponType mainHand = WeaponType.None;
    public OffHandType offHand = OffHandType.None;

    [Tooltip("실제로 손에 들리는 무기 에셋. 비워두면 mainHand 종류의 대표 모델을 카탈로그에서 찾는다.")]
    public WeaponDefinition mainHandWeapon;
    [Tooltip("보조 손 방패. 비워둔 채 offHand만 Shield면 카탈로그의 기본 방패가 들린다.")]
    public WeaponDefinition offHandWeapon;

    // Skills ------------------------------------------------------------
    // 합성으로만 늘어난다. 이름과 설명이 아니라 id만 들고 있는 이유는 SkillCatalog 주석 참조.
    [Header("Skills")]
    [Tooltip("배운 스킬의 id. 실제 이름과 설명은 SkillCatalog가 들고 있다.")]
    public List<string> skillIds = new List<string>();

    // Relationships -----------------------------------------------------
    [Header("Relationships")]
    public List<FriendshipEntry> friendships = new List<FriendshipEntry>();

    // Utility -----------------------------------------------------------

    // 장착한 무기 에셋이 있으면 그쪽이 정답이다. mainHand enum은 에셋을 아직 안 고른
    // 캐릭터(그리고 무기 없이 싸우는 생산직)를 위한 값으로만 남는다.
    public WeaponType MainHandType => mainHandWeapon != null ? mainHandWeapon.type : mainHand;

    public bool CanEquipShield => !CharacterRules.IsTwoHanded(MainHandType);

    public bool HasShield => CanEquipShield && (offHandWeapon != null || offHand == OffHandType.Shield);

    // Change equipment. Equipping a two-handed weapon automatically removes the shield.
    public void EquipMainHand(WeaponType w)
    {
        mainHand = w;
        // 종류를 직접 바꾸면 들고 있던 모델과 어긋난다. 같은 종류가 아니면 모델을 내려놓는다.
        if (mainHandWeapon != null && mainHandWeapon.type != w) mainHandWeapon = null;
        if (CharacterRules.IsTwoHanded(w)) UnequipOffHand();
    }

    // 무기 에셋을 그대로 장착한다. 전투 수치용 enum도 함께 맞춰 둔다.
    public bool EquipMainHand(WeaponDefinition weapon)
    {
        if (weapon == null) { mainHandWeapon = null; mainHand = WeaponType.None; return true; }
        if (weapon.slot != EquipSlot.MainHand) return false;

        mainHandWeapon = weapon;
        mainHand = weapon.type;
        if (weapon.IsTwoHanded) UnequipOffHand();
        return true;
    }

    public bool EquipShield()
    {
        if (!CanEquipShield) return false;
        offHand = OffHandType.Shield;
        return true;
    }

    public bool EquipOffHand(WeaponDefinition shield)
    {
        if (shield == null) { UnequipOffHand(); return true; }
        if (shield.slot != EquipSlot.OffHand || !CanEquipShield) return false;

        offHandWeapon = shield;
        offHand = OffHandType.Shield;
        return true;
    }

    public void UnequipOffHand()
    {
        offHand = OffHandType.None;
        offHandWeapon = null;
    }

    // Progression / Skills ----------------------------------------------
    // 실제 계산과 저장은 전부 CharacterProgress가 한다. 여기 남은 것은 부르는 쪽이
    // 편하도록 둔 전달용 껍데기다 — 어느 쪽으로 불러도 에셋은 바뀌지 않는다.

    public void GainExp(int amount) => CharacterProgress.GainExp(this, amount);

    public IReadOnlyList<string> Skills => CharacterProgress.SkillsOf(this);

    public int SkillCount => CharacterProgress.SkillCountOf(this);

    public bool IsSkillFull => CharacterProgress.IsSkillFull(this);

    public bool HasSkill(string skillId) => CharacterProgress.HasSkill(this, skillId);

    // 이미 배웠거나 자리가 없으면 false. 부르는 쪽이 그 이유를 미리 확인한다.
    public bool LearnSkill(string skillId) => CharacterProgress.LearnSkill(this, skillId);

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

#if UNITY_EDITOR
    // 임포트/편집 시점에도 붙여 둔다. Id 게터의 지연 부여와 함께 두는 이유는,
    // 빌드에 나갈 때쯤이면 모든 에셋이 이미 값을 갖고 있게 하기 위해서다.
    // 옛 세이브는 SaveSystem이 이름으로도 찾아 주므로 이 마이그레이션으로 진행도가 끊기지 않는다.
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(id)) AssignId();
    }
#endif

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
