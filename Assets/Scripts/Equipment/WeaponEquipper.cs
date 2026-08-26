using System;
using UnityEngine;

// 전투 유닛의 두 손에 무기를 붙이는 쪽.
//
// 두 손은 서로 독립이다. 각자 자기 소켓을 하나씩 갖고, 자기가 든 것만 안다.
//
//   mixamorig:RightHand              mixamorig:LeftHand
//   └ RightHandWeaponSocket          └ LeftHandWeaponSocket
//     └ Sword_1                        └ Round_Wood_Shield
//
// 어느 손에 걸릴지는 무기 프리팹이 스스로 말한다(WeaponGrip.Hand). 활이 왼손에 들리는 것도
// 코드의 예외가 아니라 활 프리팹이 왼손 무기라고 적어 둔 결과다. 무기 프리팹은 루트가 곧
// 손이 쥐는 지점(Grip Point)이라, 소켓 아래에 위치 0 / 회전 0 / 배율 1로 넣으면 그것으로 끝난다 —
// 무기마다 다른 보정값은 코드가 아니라 무기 프리팹이 들고 있다.
//
// 남은 한 손이 하는 일도 무기가 정한다.
//   · 양손 무기: 반대 손이 보조 그립(WeaponGrip.SecondaryGrip)을 따라간다.
//   · 활: 활은 왼손에 걸려 있고, 오른손은 활이 아니라 시위를 잡는다. 공격이 시작되면
//     시위를 당긴 자리로 옮겨 가고(DrawAmount), 쏘는 순간 시위를 따라 앞으로 풀리며 손을 놓는다.
// 실제로 손을 옮기는 것은 WeaponHandIK다. 여기서는 "어디를 잡아야 하는가"만 알려 준다.
//
// 소켓은 캐릭터 프리팹에 미리 놓아 두는 것이 기본이다(메뉴 PickMeUp/Equipment/Add Weapon Sockets).
// 놓여 있지 않으면 휴머노이드 손뼈에서 같은 자리를 계산해 만들어 쓴다.
[DisallowMultipleComponent]
public class WeaponEquipper : MonoBehaviour
{
    private const string NockedArrowName = "NockedArrow";
    // 투사체가 떠나는 지점을 무기 모델 안에 직접 표시해 두고 싶을 때 쓰는 자식 이름.
    // 지팡이의 보석 끝처럼 "쏘는 자리"와 "보이는 물건"이 다른 무기를 위한 것이다.
    private const string ProjectileOriginName = "ProjectileOrigin";

    [Header("Components")]
    [SerializeField] private Animator animator;

    [Header("Sockets (비워두면 손뼈 아래에서 찾고, 없으면 만든다)")]
    [Tooltip("오른손 무기가 붙을 지점. 캐릭터 프리팹의 " + HandSocket.RightSocketName + "을 연결해 둔다.")]
    [SerializeField] private Transform rightHandSocket;
    [Tooltip("왼손 무기가 붙을 지점. 활과 방패가 여기 걸린다.")]
    [SerializeField] private Transform leftHandSocket;

    [Tooltip("소켓을 만들어 써야 할 때만 쓰인다. 손목에서 중지 밑동까지를 1로 봤을 때 " +
             "소켓을 손바닥 쪽으로 밀어내는 비율. 0이면 손목 관절에 그대로 붙는다.")]
    [SerializeField, Range(0f, 1f)] private float palmGripRatio = HandSocket.DefaultPalmGripRatio;

    [Header("Default Loadout")]
    [Tooltip("이 유닛이 빈손일 때 쥐는 장비. CharacterSO 없이 스폰되는 경우와, " +
             "장비를 하나도 고르지 않은 캐릭터가 모두 여기로 내려온다. 비워 두면 맨손 그대로다 — " +
             "적 프리팹은 비워 두는 것이 기본이다.")]
    [SerializeField] private WeaponDefinition defaultMainHand;
    [SerializeField] private WeaponDefinition defaultOffHand;

    [Header("Bow")]
    [Tooltip("시위를 끝까지 당기는 데 걸리는 시간(초). 공격이 시작되는 순간부터 잰다.")]
    [SerializeField, Min(0.01f)] private float drawTime = 0.35f;
    [Tooltip("시위를 놓고 손이 애니메이션으로 돌아가기까지의 시간(초). 짧을수록 튕기듯 놓는다.")]
    [SerializeField, Min(0.01f)] private float releaseTime = 0.12f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs;

    // 전투가 읽는 자리. 어느 손에 들렸는지와 무관하게 "무엇으로 싸우는가"를 말한다.
    public WeaponDefinition MainHand { get; private set; }
    public WeaponDefinition OffHand { get; private set; }

    // 캐릭터 프리팹에 소켓을 놓아 주는 에디터 도구가 같은 자리를 계산하는 데 쓴다.
    public float PalmGripRatio { get { return palmGripRatio; } }

    // 빈손 캐릭터가 이 유닛으로 나갈 때 쥐게 되는 무기.
    // 스포너가 전투 수치를 같은 무기로 잡는 데 쓴다 — 손에 든 것과 숫자가 갈라지지 않도록.
    public WeaponDefinition DefaultMainHand { get { return defaultMainHand; } }

    // 주무기가 바뀔 때마다 Attack1~N 클립이 바뀐 컨트롤러로 갈아 끼운다.
    // UnitController가 캐시해 둔 공격 애니메이션 길이는 이 이벤트를 구독해 다시 계산한다.
    public event Action WeaponAnimatorChanged;

    // 현재 물려 있는 무기 컨트롤러가 가진 공격 단계 수. 0이면 무기 컨트롤러가 적용되지 않은 상태라
    // (맨손이거나, 애초에 이 리그가 무기 컨트롤러를 쓰지 않는 유닛) 호출 쪽이 자기 기본값을 쓴다.
    public int WeaponAttackCount { get; private set; }

    // 화살이 떠나는 지점. 활 모델 안의 NockedArrow(시위에 물려 둔 화살)를 그대로 쓴다 —
    // 그 자리가 곧 시위이고, 그 화살이 향한 쪽(+Z)이 곧 날아갈 방향이다.
    // 그런 자식이 없는 무기는 모델 원점이, 손에 드는 것이 없는 무기는 손 자체가 대신 쓰인다.
    public Transform ProjectileOrigin { get; private set; }

    // 시위를 당긴 정도(0~1)와, 시위를 잡은 손에 IK를 얼마나 실을지(놓는 순간 0으로 풀린다).
    public float DrawAmount { get; private set; }
    public float StringHandWeight { get; private set; }

    // 시위가 있는 무기를 들고 있으면 그 그립과, 그 시위를 잡는 손(= 활을 든 손의 반대).
    public WeaponGrip BowGrip { get; private set; }
    public EquipHand StringHand { get; private set; }

    // 한 손 = 소켓 하나, 무기 하나. 두 손은 서로를 모른다.
    private class Hand
    {
        public readonly EquipHand Side;
        public Transform Socket;
        public WeaponDefinition Definition;
        public GameObject Instance;
        public WeaponGrip Grip;

        public Hand(EquipHand side) { Side = side; }
    }

    private readonly Hand right = new Hand(EquipHand.Right);
    private readonly Hand left = new Hand(EquipHand.Left);

    private bool socketsResolved;
    private bool appliedOnce;
    private RuntimeAnimatorController defaultController;
    private bool defaultControllerCaptured;
    private WeaponType appliedAnimatorWeapon = (WeaponType)(-1);
    private GameObject nockedArrow;
    private Vector3 arrowLocalPosition;
    private Quaternion arrowLocalRotation = Quaternion.identity;
    private bool drawing;

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

    // ------------------------------------------------------------------
    // 장착
    // ------------------------------------------------------------------

    // 로스터 캐릭터가 장착 중인 장비를 그대로 든다.
    //
    // 무기는 무기고에서 실물을 골라 두는 것이 전제라, 여기서 종류를 보고 무엇을 들지 고르는 일은 없다.
    // 고른 것이 없는 빈손 캐릭터만 이 유닛 프리팹의 기본 장비를 쥔다 —
    // 기본 장비를 비워 둔 유닛(적)은 맨손 그대로다.
    public void Equip(CharacterSO character)
    {
        if (character == null)
        {
            Equip(defaultMainHand, defaultOffHand);
            return;
        }

        WeaponDefinition main = character.mainHandWeapon;
        if (main == null) main = defaultMainHand;

        WeaponDefinition off = character.HasShield ? character.offHandWeapon : null;
        if (off == null && character.HasShield) off = defaultOffHand;

        Equip(main, off);
    }

    public void Equip(WeaponDefinition main, WeaponDefinition off)
    {
        appliedOnce = true;

        // 두손 무기는 보조 손을 비운다. 여기서 한 번 더 막지 않으면 씬에 직접 놓은 유닛이
        // 창과 방패를 동시에 든 채로 돌아다닌다.
        if (main != null && main.IsTwoHanded) off = null;

        ResolveSockets();

        // 주무기가 어느 손에 걸릴지는 그 무기가 정한다. 보조 장비는 남은 손으로 간다 —
        // 활이 왼손으로 가면 방패 자리도 자연히 오른손이 된다(활은 두손이라 실제로는 비어 있지만).
        EquipHand mainSide = HandOf(main, EquipHand.Right);
        EquipHand offSide = main != null ? HandSocket.Opposite(mainSide) : HandOf(off, EquipHand.Left);

        // 두 손을 한 번에 정한다. 한쪽만 갈아 끼우면 이전 무기가 반대 손에 남는다.
        SetHand(mainSide, main);
        SetHand(offSide, off);

        MainHand = main;
        OffHand = off;
        AfterHandsChanged();
    }

    // 한 손만 따로 갈아 끼운다. 반대 손이 든 것은 건드리지 않는다.
    public void EquipToHand(EquipHand hand, WeaponDefinition definition)
    {
        appliedOnce = true;
        ResolveSockets();

        SetHand(hand, definition);
        RefreshLogicalSlots();
        AfterHandsChanged();
    }

    public void EquipRightHand(WeaponDefinition definition) => EquipToHand(EquipHand.Right, definition);
    public void EquipLeftHand(WeaponDefinition definition) => EquipToHand(EquipHand.Left, definition);

    public void Unequip() => Equip(null, null);

    public WeaponDefinition WeaponIn(EquipHand hand) => Of(hand).Definition;
    public Transform SocketOf(EquipHand hand) => Of(hand).Socket;
    public WeaponGrip GripIn(EquipHand hand) => Of(hand).Grip;
    public bool IsHandEmpty(EquipHand hand) => Of(hand).Definition == null;

    // 반대 손이 따라가야 할 지점이 있는가. 양손 무기를 들었고 그 반대 손이 비어 있을 때만이다 —
    // 방패를 들고 있는 손을 자루로 끌어오면 방패가 몸을 가로질러 날아간다.
    public bool TryGetSecondaryGrip(out EquipHand freeHand, out Transform target)
    {
        if (TrySecondary(right, left, out freeHand, out target)) return true;
        return TrySecondary(left, right, out freeHand, out target);
    }

    private static bool TrySecondary(Hand holder, Hand other, out EquipHand freeHand, out Transform target)
    {
        freeHand = other.Side;
        target = null;
        if (holder.Grip == null || !holder.Grip.HasSecondaryGrip) return false;
        if (other.Definition != null) return false;

        target = holder.Grip.SecondaryGrip;
        return true;
    }

    // 시위를 t만큼 당겼을 때 화살과 그것을 잡은 손이 있어야 할 자세.
    public bool TryGetStringPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        return BowGrip != null && BowGrip.TryGetStringPose(DrawAmount, out position, out rotation);
    }

    private Hand Of(EquipHand hand) => hand == EquipHand.Right ? right : left;

    // 이 무기가 어느 손에 걸리는지는 무기 프리팹이 들고 있다.
    private static EquipHand HandOf(WeaponDefinition definition, EquipHand fallback)
    {
        if (definition == null || definition.model == null) return fallback;

        var grip = definition.model.GetComponent<WeaponGrip>();
        return grip != null ? grip.Hand : fallback;
    }

    private void SetHand(EquipHand side, WeaponDefinition definition)
    {
        Hand hand = Of(side);
        hand.Definition = definition;
        hand.Instance = Respawn(hand.Instance, definition, hand.Socket);
        hand.Grip = hand.Instance != null ? hand.Instance.GetComponent<WeaponGrip>() : null;
    }

    // 어느 손에 들렸든, 그 장비가 스스로 말하는 자리(EquipSlot)가 곧 전투에서의 역할이다.
    private void RefreshLogicalSlots()
    {
        MainHand = WeaponForSlot(EquipSlot.MainHand);
        OffHand = WeaponForSlot(EquipSlot.OffHand);
    }

    private WeaponDefinition WeaponForSlot(EquipSlot slot)
    {
        if (right.Definition != null && right.Definition.slot == slot) return right.Definition;
        if (left.Definition != null && left.Definition.slot == slot) return left.Definition;
        return null;
    }

    private void AfterHandsChanged()
    {
        ResolveBowRig();
        ResolveProjectileOrigin();
        ApplyWeaponAnimator(MainHand != null ? MainHand.type : WeaponType.None);
    }

    // ------------------------------------------------------------------
    // 활 — 시위와 화살
    // ------------------------------------------------------------------

    // 공격이 시작됐다. 시위를 당기기 시작하고, 앞선 화살이 떠나면서 감춰 둔 화살을 다시 물린다.
    public void BeginDraw()
    {
        drawing = true;
        ShowNockedArrow(true);
    }

    // 화살이 시위를 떠났다. 화살은 감추고(날아간 화살이 활에도 붙어 있으면 두 대로 보인다),
    // 시위와 그것을 잡은 손은 앞으로 풀리면서 애니메이션으로 돌아간다.
    public void ReleaseArrow()
    {
        drawing = false;
        ShowNockedArrow(false);
    }

    private void Update()
    {
        if (BowGrip == null)
        {
            DrawAmount = 0f;
            StringHandWeight = 0f;
            return;
        }

        float dt = Time.deltaTime;

        // 당길 때는 drawTime에 걸쳐 끝까지, 놓을 때는 releaseTime에 걸쳐 제자리로.
        // 손에 실리는 무게도 같은 창을 쓴다 — 그래서 놓는 순간 손이 시위를 따라 앞으로 나갔다가 풀린다.
        float span = Mathf.Max(0.01f, drawing ? drawTime : releaseTime);
        DrawAmount = Mathf.MoveTowards(DrawAmount, drawing ? 1f : 0f, dt / span);
        StringHandWeight = Mathf.MoveTowards(StringHandWeight, drawing ? 1f : 0f,
                                             dt / Mathf.Max(0.01f, drawing ? drawTime * 0.5f : releaseTime));

        ApplyStringToArrow();
    }

    // 화살을 시위에 붙여 둔다. 시위가 물러나면 화살도 같이 물러나므로,
    // 화살이 향한 쪽은 언제나 활이 겨눈 쪽 그대로다 — 발사 지점(ProjectileOrigin)도 그 화살이다.
    private void ApplyStringToArrow()
    {
        if (nockedArrow == null) return;

        Vector3 position;
        Quaternion rotation;
        if (!BowGrip.TryGetStringPose(DrawAmount, out position, out rotation)) return;

        nockedArrow.transform.SetPositionAndRotation(position + rotation * arrowLocalPosition,
                                                     rotation * arrowLocalRotation);
    }

    private void ShowNockedArrow(bool visible)
    {
        if (nockedArrow != null) nockedArrow.SetActive(visible);
    }

    // 시위를 가진 무기를 들었는지 확인하고, 그 시위를 잡을 손과 화살의 놓인 자세를 기억해 둔다.
    // 무기를 바꿀 때마다 다시 잡는다 — Respawn이 이전 모델을 통째로 파괴하므로 들고 있던 참조는 그때 죽는다.
    private void ResolveBowRig()
    {
        BowGrip = null;
        drawing = false;
        DrawAmount = 0f;
        StringHandWeight = 0f;
        nockedArrow = null;
        arrowLocalPosition = Vector3.zero;
        arrowLocalRotation = Quaternion.identity;

        Hand bowHand = right.Grip != null && right.Grip.HasBowString ? right
                     : left.Grip != null && left.Grip.HasBowString ? left
                     : null;
        if (bowHand == null) return;

        BowGrip = bowHand.Grip;
        StringHand = HandSocket.Opposite(bowHand.Side);

        Transform arrow = FindDeep(bowHand.Instance.transform, NockedArrowName);
        if (arrow == null) return;

        // 화살이 시위(StringRest)에 대해 어떤 자세로 놓여 있었는지 그대로 기억한다.
        // 이 값이 곧 "시위에 물린 화살"의 정의라, 활마다 다른 보정값을 코드가 들고 있을 필요가 없다.
        nockedArrow = arrow.gameObject;
        Transform rest = BowGrip.StringRest;
        arrowLocalPosition = rest.InverseTransformPoint(arrow.position);
        arrowLocalRotation = Quaternion.Inverse(rest.rotation) * arrow.rotation;
    }

    // ------------------------------------------------------------------
    // 투사체
    // ------------------------------------------------------------------

    private void ResolveProjectileOrigin()
    {
        ProjectileOrigin = null;

        Hand shooter = HandHolding(MainHand);
        if (shooter == null) return;

        // 손에 드는 것이 없는 무기(맨손 시전)도 투사체는 나간다. 그때는 손 자체가 발사 지점이다 —
        // 소켓이 곧 주먹이 자루를 쥐는 자리라 손 한가운데다.
        if (shooter.Instance == null)
        {
            ProjectileOrigin = shooter.Socket;
            return;
        }

        // 무기 프리팹은 그립을 맞추느라 모델을 한 겹 감싸고 있다.
        // 표시는 그 안쪽 모델에 붙어 있으므로 자손까지 내려가며 찾는다.
        // 전용 표시가 있으면 그쪽, 없으면 시위에 물려 둔 화살(활), 그것도 없으면 모델 원점.
        Transform muzzle = FindDeep(shooter.Instance.transform, ProjectileOriginName);
        if (muzzle == null && nockedArrow != null) muzzle = nockedArrow.transform;
        ProjectileOrigin = muzzle != null ? muzzle : shooter.Instance.transform;
    }

    private Hand HandHolding(WeaponDefinition definition)
    {
        if (definition == null) return null;
        if (right.Definition == definition) return right;
        if (left.Definition == definition) return left;
        return null;
    }

    private static Transform FindDeep(Transform root, string childName)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName) return child;

            Transform found = FindDeep(child, childName);
            if (found != null) return found;
        }
        return null;
    }

    // ------------------------------------------------------------------
    // 애니메이터
    // ------------------------------------------------------------------

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

    // ------------------------------------------------------------------
    // 모델 생성
    // ------------------------------------------------------------------

    private GameObject Respawn(GameObject current, WeaponDefinition definition, Transform socket)
    {
        if (current != null) Destroy(current);
        if (definition == null || definition.model == null || socket == null) return null;

        GameObject instance = Instantiate(definition.model, socket);
        instance.name = definition.DisplayName;

        // 여기가 장착의 전부다. 무기가 무엇이든 소켓 원점에 그대로 앉힌다 —
        // 손에 맞추는 일은 무기 프리팹이 이미 끝내 놓았다.
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        // 무기도 유닛과 같은 레이어에 둔다. 감싸는 루트만 바꾸면 정작 보이는 모델이 남으므로 전부 내려가며 바꾼다.
        SetLayerRecursively(instance.transform, gameObject.layer);

        // 무기 모델에 콜라이더가 딸려 오면 손에 든 채로 본체를 밀거나 스캐너에 잡힌다.
        // 타격 판정은 UnitController가 거리로 하고 있으니 물리는 전부 꺼둔다.
        foreach (Collider c in instance.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (Rigidbody rb in instance.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;

        WarnIfGripNotBaked(instance, definition);

        if (debugLogs) Debug.Log($"[WeaponEquipper] {name}: {definition.DisplayName} → {socket.name}", this);
        return instance;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), layer);
    }

    // 그립 표식이 프리팹 루트에서 벗어나 있으면 딱 그만큼 손에서 밀려 보인다.
    // 손에 붙여 놓고 왜 어긋났는지 되짚는 것보다, 붙이는 순간 짚어 주는 편이 빠르다.
    private void WarnIfGripNotBaked(GameObject instance, WeaponDefinition definition)
    {
        var grip = instance.GetComponent<WeaponGrip>();
        if (grip == null || grip.IsAligned) return;

        Debug.LogWarning($"[WeaponEquipper] {definition.DisplayName}: GripPoint가 프리팹 루트에서 벗어나 있다. " +
                         "메뉴 PickMeUp/Equipment/Align Grip Point 로 모델 쪽에 구워 넣어야 손에 맞는다.", this);
    }

    private void ResolveSockets()
    {
        if (socketsResolved) return;
        socketsResolved = true;

        Animator rig = ResolveAnimator();
        if (rightHandSocket == null) rightHandSocket = HandSocket.Resolve(rig, EquipHand.Right, palmGripRatio);
        if (leftHandSocket == null) leftHandSocket = HandSocket.Resolve(rig, EquipHand.Left, palmGripRatio);

        right.Socket = rightHandSocket;
        left.Socket = leftHandSocket;
    }
}
