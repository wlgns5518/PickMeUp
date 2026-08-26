using UnityEngine;

// 손에 들리는 장비 한 점.
//
// 지금까지 장비는 WeaponType enum 하나뿐이었다. 전투 수치를 뽑기에는 충분했지만
// "검"이 몇 자루든 전부 같은 값이라 화면에 무엇을 띄워야 할지는 알 수 없었다.
// 반대로 모델 프리팹만 들고 있으면 이번엔 전투 수치가 붙을 곳이 없다.
//
// 이 에셋이 그 둘을 잇는다. 전투는 여전히 type만 읽고(JobProfile.For), 화면에는 model이 나간다.
// 덕분에 같은 SwordOneHand라도 캐릭터마다 다른 검을 들 수 있고,
// 새 무기 팩을 사와도 에셋만 늘어날 뿐 전투 코드는 그대로다.
[CreateAssetMenu(fileName = "Weapon", menuName = "PickMeUp/Weapon")]
public class WeaponDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName;
    [TextArea] public string description;

    [Header("Combat")]
    [Tooltip("전투 수치를 결정하는 분류. 모델이 무엇이든 이 값이 JobProfile 표를 탄다.")]
    public WeaponType type = WeaponType.SwordOneHand;
    public EquipSlot slot = EquipSlot.MainHand;

    [Header("Model")]
    [Tooltip("WeaponSocket 아래에 그대로 생성될 무기 프리팹. 루트가 곧 손이 쥐는 지점(Grip Point)이다. " +
             "비어 있으면 수치만 적용되고 아무것도 보이지 않는다 — 맨손 시전이 그 경우다.")]
    public GameObject model;

    [Header("Projectile")]
    [Tooltip("손을 떠나 날아가는 것. 활의 화살이 여기 들어간다. " +
             "비워두면 타격이 그 자리에서 들어간다 — 근접 무기는 전부 비어 있다.")]
    public GameObject projectile;

    public bool IsTwoHanded => CharacterRules.IsTwoHanded(type);

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

    // 인스펙터에서 슬롯만 바꾸고 종류를 안 고치는 실수를 잡아준다.
    // (방패 슬롯에 창이 꽂혀 있으면 왼손에 창이 들리고 전투 수치는 방패로 계산된다)
    private void OnValidate()
    {
        if (slot == EquipSlot.OffHand && type != WeaponType.None && !CharacterRules.IsShield(type))
            Debug.LogWarning($"[WeaponDefinition] {name}: 보조 손 슬롯인데 종류가 {type}다. Shield로 두거나 슬롯을 MainHand로 바꿔야 한다.", this);
    }
}
