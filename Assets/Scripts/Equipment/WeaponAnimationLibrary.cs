using System.Collections.Generic;
using UnityEngine;

// WeaponType별로 어떤 Animator Override Controller를 쓸지 담아 두는 표.
//
// WeaponCatalog(모델)와 같은 이유로 Resources 싱글턴을 쓴다 — 유닛 프리팹마다 이 표를
// 인스펙터로 물고 있으면 무기 종류를 늘릴 때마다 프리팹을 전부 찾아 다시 연결해야 한다.
[CreateAssetMenu(fileName = "WeaponAnimationLibrary", menuName = "PickMeUp/Weapon Animation Library")]
public class WeaponAnimationLibrary : ScriptableObject
{
    // Assets/Equipment/Resources/WeaponAnimationLibrary.asset
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

    // 방패를 들었을 때만 갈아 끼우는 클립 한 쌍.
    [System.Serializable]
    public class ShieldClip
    {
        [Tooltip("기본 컨트롤러에 물려 있는 클립(갈아 끼울 대상).")]
        public AnimationClip original;
        [Tooltip("방패를 들었을 때 그 자리에 대신 재생할 클립.")]
        public AnimationClip shield;
    }

    [Tooltip("주무기 종류와 무관하게, 보조 손에 방패가 들렸을 때만 갈아 끼우는 클립들. " +
             "막기 자세가 여기 들어간다 — 방패 없이 무기로 받아내는 직군(패링·무기 방어)은 " +
             "기본 클립을 그대로 쓰고, 방패를 든 유닛만 방패를 앞으로 세우는 자세로 바뀐다.")]
    public List<ShieldClip> shieldClips = new List<ShieldClip>();

    private static WeaponAnimationLibrary cached;
    private static bool searched;

    // 방패용으로 한 겹 덧씌운 컨트롤러. 기본 컨트롤러 하나당 하나만 만들어 모든 유닛이 나눠 쓴다 —
    // 유닛마다 만들면 스폰 수만큼 런타임 에셋이 쌓인다.
    private static readonly Dictionary<RuntimeAnimatorController, AnimatorOverrideController> ShieldVariants =
        new Dictionary<RuntimeAnimatorController, AnimatorOverrideController>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        cached = null;
        searched = false;

        // 도메인 리로드를 끈 에디터에서는 이 표가 플레이를 넘어 살아남는다. 지난 판에 만든
        // 컨트롤러를 그대로 두면 재생할 때마다 하나씩 쌓이므로 여기서 걷어낸다.
        foreach (KeyValuePair<RuntimeAnimatorController, AnimatorOverrideController> pair in ShieldVariants)
            if (pair.Value != null) Destroy(pair.Value);
        ShieldVariants.Clear();
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

    // 방패를 든 유닛이 쓸 컨트롤러. shieldClips에 적힌 클립만 갈아 끼운 한 겹을 덧씌운다.
    // 갈아 끼울 것이 없으면 받은 컨트롤러를 그대로 돌려준다.
    //
    // 무기 컨트롤러 위에 한 겹을 더 얹는 방식이라, 무기별 오버라이드를 고쳐도 방패 쪽이
    // 저절로 따라온다 — 무기마다 "방패 있는 판"을 따로 만들어 두면 둘이 어긋난다.
    public static RuntimeAnimatorController WithShield(RuntimeAnimatorController baseController)
    {
        if (baseController == null) return null;

        WeaponAnimationLibrary library = Instance;
        if (library == null || library.shieldClips == null || library.shieldClips.Count == 0) return baseController;

        AnimatorOverrideController variant;
        if (ShieldVariants.TryGetValue(baseController, out variant) && variant != null) return variant;

        // 오버라이드 컨트롤러 위에 오버라이드 컨트롤러를 그대로 씌우면 안쪽 것이 통째로 버려진다
        // (무기 컨트롤러를 감쌌더니 Attack1~7이 맨손 클립으로 돌아갔다). 그래서 뿌리 컨트롤러를
        // 바탕으로 한 장만 만들고, 무기가 갈아 끼운 것을 먼저 옮겨 담은 뒤 방패 것을 얹는다.
        var weapon = baseController as AnimatorOverrideController;
        variant = new AnimatorOverrideController(weapon != null ? weapon.runtimeAnimatorController : baseController);
        if (weapon != null)
        {
            var carried = new List<KeyValuePair<AnimationClip, AnimationClip>>(weapon.overridesCount);
            weapon.GetOverrides(carried);
            variant.ApplyOverrides(carried);
        }
        variant.name = baseController.name + " (Shield)";
        // 씬에 속하지 않는 런타임 에셋이다. 씬을 넘겨도 살아 있어야 하고(유닛이 계속 쓴다)
        // 저장되어서도 안 된다. 치우는 것은 ResetCache가 맡는다.
        variant.hideFlags = HideFlags.HideAndDontSave;

        for (int i = 0; i < library.shieldClips.Count; i++)
        {
            ShieldClip swap = library.shieldClips[i];
            if (swap == null || swap.original == null || swap.shield == null) continue;
            variant[swap.original] = swap.shield;
        }

        ShieldVariants[baseController] = variant;
        return variant;
    }
}
