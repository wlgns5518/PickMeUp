using System.Collections.Generic;
using UnityEngine;

// WeaponType별로 어떤 Animator Override Controller를 쓸지 담아 두는 표.
//
// WeaponCatalog(모델)와 같은 이유로 Resources 싱글턴을 쓴다 — 유닛 프리팹마다 이 표를
// 인스펙터로 물고 있으면 무기 종류를 늘릴 때마다 프리팹을 전부 찾아 다시 연결해야 한다.
[CreateAssetMenu(fileName = "WeaponAnimationLibrary", menuName = "PickMeUp/Weapon Animation Library")]
public class WeaponAnimationLibrary : ScriptableObject
{
    // Assets/Resources/WeaponAnimationLibrary.asset
    public const string ResourceName = "WeaponAnimationLibrary";

    [System.Serializable]
    public class Entry
    {
        public WeaponType type;
        public AnimatorOverrideController controller;

        [Tooltip("이 무기가 실제로 가진 공격 클립 수. 원본 팩의 클립 수가 무기마다 달라(단검 3 ~ 양손검 11) " +
                 "베이스 컨트롤러의 Attack1~11 중 앞에서 이 개수만큼만 이 무기 모션으로 덮여 있다.")]
        [Min(1)] public int attackCount = 1;
    }

    public List<Entry> entries = new List<Entry>();

    private static WeaponAnimationLibrary cached;
    private static bool searched;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        cached = null;
        searched = false;
    }

    public static WeaponAnimationLibrary Instance
    {
        get
        {
            if (cached != null) return cached;
            if (searched) return null;

            searched = true;
            cached = Resources.Load<WeaponAnimationLibrary>(ResourceName);
            return cached;
        }
    }

    // 등록되지 않은 종류(None, Shield 등)는 null — 호출 쪽이 기본 컨트롤러(맨손)로 대체한다.
    public static Entry FindEntry(WeaponType type)
    {
        WeaponAnimationLibrary library = Instance;
        if (library == null) return null;

        for (int i = 0; i < library.entries.Count; i++)
        {
            Entry entry = library.entries[i];
            if (entry != null && entry.type == type) return entry;
        }
        return null;
    }

    public static RuntimeAnimatorController Find(WeaponType type)
    {
        Entry entry = FindEntry(type);
        return entry != null ? entry.controller : null;
    }
}
