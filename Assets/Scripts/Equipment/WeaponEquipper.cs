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
    [Header("Components")]
    [SerializeField] private Animator animator;

    [Header("Sockets (비워두면 휴머노이드 손뼈에서 자동 생성)")]
    [Tooltip("주무기가 붙을 지점. 직접 지정하면 자동 계산 대신 이 Transform을 그대로 쓴다.")]
    [SerializeField] private Transform mainHandSocket;
    [SerializeField] private Transform offHandSocket;

    [Header("Default Loadout")]
    [Tooltip("CharacterSO 없이 스폰되는 유닛(적 등)이 기본으로 드는 장비.")]
    [SerializeField] private WeaponDefinition defaultMainHand;
    [SerializeField] private WeaponDefinition defaultOffHand;

    [Header("Debug")]
    [SerializeField] private bool debugLogs;

    public WeaponDefinition MainHand { get; private set; }
    public WeaponDefinition OffHand { get; private set; }

    private GameObject mainHandInstance;
    private GameObject offHandInstance;
    private bool socketsResolved;
    private bool appliedOnce;

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
        mainHandInstance = Respawn(mainHandInstance, main, mainHandSocket);
        offHandInstance = Respawn(offHandInstance, off, offHandSocket);
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
        socket.localPosition = Vector3.zero;
        socket.localRotation = GripRotation(rightHand);
        socket.localScale = Vector3.one;
        return socket;
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
