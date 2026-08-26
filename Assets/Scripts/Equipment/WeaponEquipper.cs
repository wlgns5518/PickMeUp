using System;
using UnityEngine;

// 전투 유닛의 손에 무기 모델을 실제로 붙이는 쪽.
//
// 손 소켓을 프리팹마다 손으로 만들어 두는 방법도 있었지만, 캐릭터 프리팹이 늘어날 때마다
// 같은 작업을 반복해야 하고 한 번 어긋나면 무기가 허공에 뜬 채로 릴리스된다.
// 그래서 소켓을 휴머노이드 뼈에서 런타임에 만들어 쓴다. Mixamo든 다른 리그든
// 손가락 뼈만 매핑돼 있으면 같은 자세가 나온다.
[DisallowMultipleComponent]
public class WeaponEquipper : MonoBehaviour
{
    private const string NockedArrowName = "NockedArrow";
    // 투사체가 떠나는 지점을 무기 모델 안에 직접 표시해 두고 싶을 때 쓰는 자식 이름.
    // 지팡이의 보석 끝처럼 "쏘는 자리"와 "보이는 물건"이 다른 무기를 위한 것이다.
    private const string ProjectileOriginName = "ProjectileOrigin";

    [Header("Components")]
    [SerializeField] private Animator animator;

    [Header("Sockets (비워두면 휴머노이드 손뼈에서 자동 생성)")]
    [Tooltip("주무기가 붙을 지점. 직접 지정하면 자동 계산 대신 이 Transform을 그대로 쓴다.")]
    [SerializeField] private Transform mainHandSocket;
    [SerializeField] private Transform offHandSocket;

    [Tooltip("손목에서 중지 밑동까지를 1로 봤을 때 소켓을 손바닥 쪽으로 밀어내는 비율. " +
             "0이면 손목 관절(Hand 본 원점)에 그대로 붙어 무기가 손목에 매달린 것처럼 보인다.")]
    [SerializeField, Range(0f, 1f)] private float palmGripRatio = 0.6f;

    [Header("Default Loadout")]
    [Tooltip("CharacterSO 없이 스폰되는 유닛(적 등)이 기본으로 드는 장비.")]
    [SerializeField] private WeaponDefinition defaultMainHand;
    [SerializeField] private WeaponDefinition defaultOffHand;

    [Header("Debug")]
    [SerializeField] private bool debugLogs;

    public WeaponDefinition MainHand { get; private set; }
    public WeaponDefinition OffHand { get; private set; }

    // 주무기가 바뀔 때마다 Attack1~N 클립이 바뀐 컨트롤러로 갈아 끼운다.
    // UnitController가 캐시해 둔 공격 애니메이션 길이는 이 이벤트를 구독해 다시 계산한다.
    public event Action WeaponAnimatorChanged;

    // 현재 물려 있는 무기 컨트롤러가 가진 공격 단계 수. 0이면 무기 컨트롤러가 적용되지 않은 상태라
    // (맨손이거나, 애초에 이 리그가 무기 컨트롤러를 쓰지 않는 유닛) 호출 쪽이 자기 기본값을 쓴다.
    public int WeaponAttackCount { get; private set; }

    // 화살이 떠나는 지점. 활 모델 안의 NockedArrow(시위에 물려 둔 화살)를 그대로 쓴다 —
    // 그 자리가 곧 시위이고, 그 화살이 향한 쪽(+Z)이 곧 날아갈 방향이다.
    // 그런 자식이 없는 무기는 모델 원점이 대신 쓰인다.
    public Transform ProjectileOrigin { get; private set; }

    private GameObject mainHandInstance;
    private GameObject offHandInstance;
    private bool socketsResolved;
    private bool appliedOnce;
    private RuntimeAnimatorController defaultController;
    private bool defaultControllerCaptured;
    private WeaponType appliedAnimatorWeapon = (WeaponType)(-1);
    private GameObject nockedArrow;

    private void Awake()
    {
        ResolveAnimator();
    }

    // Awake을 기다리지 않는다. 스포너가 Instantiate 직후 Configure를 부르는 경로도 있고,
    // 에디터 미리보기처럼 Awake 자체가 돌지 않는 경우도 있다.
    private Animator ResolveAnimator()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        return animator;
    }

    private void Start()
    {
        // CharacterSO를 들고 오는 스폰 경로는 Configure에서 이미 장착을 마쳤다.
        // 그 경로를 타지 않은 유닛(더미 적, 씬에 직접 놓인 유닛)만 기본 장비를 든다.
        if (!appliedOnce) Equip(defaultMainHand, defaultOffHand);
    }

    // 로스터 캐릭터가 장착 중인 장비를 그대로 든다.
    // 캐릭터에 모델이 지정돼 있지 않으면 WeaponType만 보고 카탈로그에서 대표 모델을 찾는다.
    public void Equip(CharacterSO character)
    {
        if (character == null)
        {
            Equip(defaultMainHand, defaultOffHand);
            return;
        }

        WeaponDefinition main = character.mainHandWeapon;
        if (main == null) main = WeaponCatalog.FindByType(character.mainHand, EquipSlot.MainHand);

        WeaponDefinition off = null;
        if (character.HasShield)
        {
            off = character.offHandWeapon;
            if (off == null) off = WeaponCatalog.FindByType(WeaponType.Shield, EquipSlot.OffHand);
        }

        Equip(main, off);
    }

    public void Equip(WeaponDefinition main, WeaponDefinition off)
    {
        appliedOnce = true;

        // 두손 무기는 보조 손을 비운다. 여기서 한 번 더 막지 않으면 씬에 직접 놓은 유닛이
        // 창과 방패를 동시에 든 채로 돌아다닌다.
        if (main != null && main.IsTwoHanded) off = null;

        MainHand = main;
        OffHand = off;

        ResolveSockets();
        // 활은 주무기인데도 왼손에 들린다. 활대를 미는 손과 시위를 당기는 손이 나뉘어 있어서,
        // 오른손 소켓에 붙이면 활대가 시위를 당기는 손을 따라 뒤로 끌려간다
        // (Bow 오버라이드 컨트롤러의 클립이 전부 왼손으로 활을 미는 자세다).
        // 활은 두손 무기라 보조 손이 어차피 비어 있으므로 소켓이 겹칠 일은 없다.
        Transform mainSocket = IsLeftHanded(main) ? offHandSocket : mainHandSocket;
        mainHandInstance = Respawn(mainHandInstance, main, mainSocket);
        ResolveProjectileOrigin(mainSocket);
        offHandInstance = Respawn(offHandInstance, off, offHandSocket);

        ApplyWeaponAnimator(main != null ? main.type : WeaponType.None);
    }

    private static bool IsLeftHanded(WeaponDefinition weapon)
    {
        return weapon != null && weapon.type == WeaponType.Bow;
    }

    // 활 모델 안의 NockedArrow를 찾아 발사 지점으로 잡아 둔다. 무기를 바꿀 때마다 다시 찾는다 —
    // Respawn이 이전 모델을 통째로 파괴하므로 들고 있던 Transform은 그때 죽는다.
    private void ResolveProjectileOrigin(Transform fallbackSocket)
    {
        nockedArrow = null;
        ProjectileOrigin = null;

        // 손에 드는 것이 없는 무기(맨손 시전)도 투사체는 나간다. 그때는 손 자체가 발사 지점이다 —
        // 소켓이 곧 주먹이 자루를 쥐는 자리라 손 한가운데다.
        if (mainHandInstance == null)
        {
            ProjectileOrigin = fallbackSocket;
            return;
        }

        Transform nock = mainHandInstance.transform.Find(NockedArrowName);
        if (nock != null) nockedArrow = nock.gameObject;

        // 전용 표시가 있으면 그쪽, 없으면 시위에 물려 둔 화살(활), 그것도 없으면 모델 원점.
        Transform muzzle = mainHandInstance.transform.Find(ProjectileOriginName);
        if (muzzle == null) muzzle = nock;
        ProjectileOrigin = muzzle != null ? muzzle : mainHandInstance.transform;
    }

    // 시위를 떠나는 순간 물려 둔 화살을 감춘다. 화살은 날아갔는데 활에도 그대로 붙어 있으면
    // 화살이 두 대로 보인다. 다음 공격을 시작할 때(시위를 당길 때) 다시 물린다.
    public void ShowNockedArrow(bool visible)
    {
        if (nockedArrow != null) nockedArrow.SetActive(visible);
    }

    // 주무기 종류에 맞는 Attack1~3 Override Controller로 갈아 끼운다.
    // 등록된 게 없으면(None, Shield 등) 프리팹에 원래 물려 있던 컨트롤러(맨손)로 되돌린다.
    private void ApplyWeaponAnimator(WeaponType type)
    {
        if (ResolveAnimator() == null) return;

        if (!defaultControllerCaptured)
        {
            defaultController = animator.runtimeAnimatorController;
            defaultControllerCaptured = true;
        }

        if (type == appliedAnimatorWeapon) return;
        appliedAnimatorWeapon = type;

        WeaponAnimationLibrary.Entry entry = WeaponAnimationLibrary.FindEntry(type);
        RuntimeAnimatorController weaponController = entry != null ? entry.controller : null;

        animator.runtimeAnimatorController = weaponController != null ? weaponController : defaultController;

        // 컨트롤러를 갈아 끼우면 Animator는 다음 갱신에서야 새 상태 기계로 다시 묶인다.
        // 그 사이에 들어온 CrossFade와 HasState는 바뀌기 전 컨트롤러를 기준으로 해석되므로,
        // 무기를 들었는데도 맨손 공격(Unarmed-Attack)이 재생될 수 있다.
        //
        // 원거리 유닛이 정확히 이 창에 걸린다. 스폰된 프레임에 이미 사거리 안이라 그 자리에서
        // 첫 공격을 내기 때문이다(근접은 적에게 걸어가는 동안 프레임이 지나 저절로 비껴간다).
        // 0초 갱신으로 묶기를 여기서 끝내 둔다.
        if (animator.isInitialized) animator.Update(0f);
        WeaponAttackCount = weaponController != null ? Mathf.Max(1, entry.attackCount) : 0;

        WeaponAnimatorChanged?.Invoke();
    }

    public void Unequip() => Equip(null, null);

    private GameObject Respawn(GameObject current, WeaponDefinition definition, Transform socket)
    {
        if (current != null) Destroy(current);
        if (definition == null || definition.model == null || socket == null) return null;

        GameObject instance = Instantiate(definition.model, socket);
        instance.name = definition.DisplayName;
        instance.transform.localPosition = definition.gripPosition;
        instance.transform.localRotation = Quaternion.Euler(definition.gripRotation);
        instance.transform.localScale = Vector3.one * Mathf.Max(0.0001f, definition.gripScale);
        instance.layer = gameObject.layer;

        // 무기 모델에 콜라이더가 딸려 오면 손에 든 채로 본체를 밀거나 스캐너에 잡힌다.
        // 타격 판정은 UnitController가 거리로 하고 있으니 물리는 전부 꺼둔다.
        foreach (Collider c in instance.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (Rigidbody rb in instance.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;

        if (debugLogs) Debug.Log($"[WeaponEquipper] {name}: {definition.DisplayName} → {socket.name}", this);
        return instance;
    }

    private void ResolveSockets()
    {
        if (socketsResolved) return;
        socketsResolved = true;

        if (mainHandSocket == null) mainHandSocket = CreateSocket(true, "WeaponSocket_MainHand");
        if (offHandSocket == null) offHandSocket = CreateSocket(false, "WeaponSocket_OffHand");
    }

    private Transform CreateSocket(bool rightHand, string socketName)
    {
        if (ResolveAnimator() == null || !animator.isHuman) return null;

        Transform hand = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        if (hand == null) return null;

        // 프리팹에 이미 같은 이름의 소켓이 있으면 그쪽을 쓴다(수동으로 다듬어 둔 경우).
        Transform existing = hand.Find(socketName);
        if (existing != null) return existing;

        var socket = new GameObject(socketName).transform;
        socket.SetParent(hand, false);
        socket.localPosition = PalmOffset(rightHand);
        socket.localRotation = GripRotation(rightHand);
        socket.localScale = Vector3.one;
        return socket;
    }

    // 휴머노이드 리그에서 Hand 본의 원점은 손바닥이 아니라 손목 관절이다(이 리그는 손목~중지 밑동이 12.8cm).
    // 소켓을 원점에 그대로 두면 무기가 손목에 매달린 것처럼 보이므로, 주먹이 자루를 쥐는 지점까지 밀어준다.
    // 손가락 뼈가 매핑되지 않은 리그에서는 기준을 잡을 수 없으니 손목 그대로 둔다(예전 동작).
    private Vector3 PalmOffset(bool rightHand)
    {
        Transform hand = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        Transform middle = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
        if (hand == null || middle == null) return Vector3.zero;

        return hand.InverseTransformPoint(middle.position) * palmGripRatio;
    }

    // 소켓의 자세를 손 모양에서 직접 뽑는다.
    //
    // 주먹으로 자루를 쥐면 자루는 새끼손가락 쪽에서 검지 쪽으로 손바닥을 가로지르고,
    // 날은 그대로 검지 너머로 뻗어 나간다. 그래서 소켓 +Y = (검지 - 새끼)로 잡는다.
    // 칼날의 넓은 면은 손바닥과 나란해야 하므로 소켓 +Z(정면)는 손바닥 법선에 맞춘다.
    // 손가락 뼈가 매핑돼 있지 않은 리그에서는 계산할 근거가 없으니 손뼈 자세를 그대로 쓴다.
    private Quaternion GripRotation(bool rightHand)
    {
        Transform hand = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        Transform index = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
        Transform little = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
        Transform middle = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
        if (hand == null || index == null || little == null || middle == null) return Quaternion.identity;

        Vector3 blade = hand.InverseTransformDirection(index.position - little.position).normalized;
        Vector3 fingers = hand.InverseTransformDirection(middle.position - hand.position).normalized;
        Vector3 palm = Vector3.Cross(blade, fingers);
        if (blade.sqrMagnitude < 0.0001f || palm.sqrMagnitude < 0.0001f) return Quaternion.identity;

        return Quaternion.LookRotation(palm.normalized, blade);
    }
}
