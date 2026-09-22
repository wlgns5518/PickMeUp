using UnityEngine;

// 시설 화면이 쓰는 그림 전부 — 재화, 무기 종류, 재료, 소환 배너.
//
// 그림은 Meshy로 그려 에디터 메뉴(PickMeUp/UI/아이콘 굽기)가 이 에셋에 걸어 둔다(UiArtBaker).
// NeonUISkin과 같은 이유로 Resources에서 이름으로 부른다 — 화면은 전부 코드로 지어서 인스펙터로 꽂아 줄 자리가 없다.
// 칸이 비어 있으면 부품이 글자(무기 이름 첫 글자 등)로 대신 그린다 — 모양만 밋밋해질 뿐 동작은 같다.
//
// 무기 아이콘은 종류마다 하나다(모든 롱소드가 같은 그림). 무기 에셋마다 그림을 두면 새 무기를 넣을 때마다
// 그림을 새로 구워야 한다.
[CreateAssetMenu(fileName = ResourceName, menuName = "PickMeUp/UI/Icon Library")]
public class UiIconLibrary : ScriptableObject
{
    public const string ResourceName = "UiIconLibrary";

    [Header("Currency · Top Bar")]
    public Sprite gold;
    public Sprite gem;
    public Sprite mail;
    public Sprite settings;

    [Header("Weapons")]
    public Sprite swordOneHand;
    public Sprite swordTwoHand;
    public Sprite bow;
    public Sprite spear;
    public Sprite dagger;
    public Sprite axe;
    public Sprite blunt;
    public Sprite polearm;
    public Sprite shield;

    [Header("Equipment Slots")]
    [Tooltip("기타 장비 칸(아직 잠김)의 자리 그림")]
    public Sprite accessory;

    [Header("Materials")]
    public Sprite metal;
    public Sprite wood;
    public Sprite leather;

    [Header("Summon Banners (1024x512)")]
    public Sprite bannerNormal;
    public Sprite bannerPremium;

    private static UiIconLibrary cached;
    private static bool searched;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        cached = null;
        searched = false;
    }

    // 없으면 null. 부르는 쪽이 글자로 대신 그린다.
    public static UiIconLibrary Current
    {
        get
        {
            if (cached != null) return cached;
            if (searched) return null;

            searched = true;
            cached = Resources.Load<UiIconLibrary>(ResourceName);
            if (cached == null)
                Debug.LogWarning($"[UiIconLibrary] Resources/{ResourceName}.asset 이 없어 아이콘을 글자로 그립니다.");
            return cached;
        }
    }

    public static Sprite Currency(Currency currency)
    {
        UiIconLibrary lib = Current;
        if (lib == null) return null;
        return currency == global::Currency.Gem ? lib.gem : lib.gold;
    }

    public static Sprite Weapon(WeaponType type)
    {
        UiIconLibrary lib = Current;
        if (lib == null) return null;

        switch (type)
        {
            case WeaponType.SwordOneHand: return lib.swordOneHand;
            case WeaponType.SwordTwoHand: return lib.swordTwoHand;
            case WeaponType.Bow:          return lib.bow;
            case WeaponType.Spear:        return lib.spear;
            case WeaponType.Dagger:       return lib.dagger;
            case WeaponType.Axe:          return lib.axe;
            case WeaponType.Blunt:        return lib.blunt;
            case WeaponType.Polearm:      return lib.polearm;
            case WeaponType.Shield:       return lib.shield;
            default:                      return null;
        }
    }

    // 계열을 대표하는 그림. 셋이 다 달라 계열이 서지 않으면(어떤 무기든) 그림이 없다 — "?"로 그린다.
    public static Sprite Family(WeaponFamily family)
    {
        switch (family)
        {
            case WeaponFamily.Metal:  return Weapon(WeaponType.SwordOneHand);
            case WeaponFamily.Wood:   return Weapon(WeaponType.Bow);
            case WeaponFamily.Shield: return Weapon(WeaponType.Shield);
            default:                  return null;
        }
    }

    public static Sprite Material(MaterialKind kind)
    {
        UiIconLibrary lib = Current;
        if (lib == null) return null;

        switch (kind)
        {
            case MaterialKind.Wood:    return lib.wood;
            case MaterialKind.Leather: return lib.leather;
            default:                   return lib.metal;
        }
    }

    public static Sprite Accessory => Current != null ? Current.accessory : null;

    public static Sprite Banner(SummonKind kind)
    {
        UiIconLibrary lib = Current;
        if (lib == null) return null;
        return kind == SummonKind.Paid ? lib.bannerPremium : lib.bannerNormal;
    }
}
