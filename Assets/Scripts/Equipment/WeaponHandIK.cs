using UnityEngine;

// 무기를 쥔 손 말고, 나머지 한 손이 할 일.
//
// 무기를 든 손은 소켓이 해결한다 — 손이 움직이면 무기가 따라간다. 문제는 반대 손이다.
// 양손검을 들어도 왼손은 애니메이션이 정한 자리에 그대로 있고, 활을 들어도 오른손은
// 시위와 무관하게 흔들린다. 무기 하나에 클립 한 벌을 새로 찍지 않는 한 이 어긋남은 남는다.
//
// 그래서 남은 손만 IK로 무기 쪽에 붙인다. 어디에 붙일지는 전부 무기 프리팹이 들고 있다.
//   · 양손 무기: WeaponGrip.SecondaryGrip — 자루의 두 번째 지점
//   · 활: WeaponGrip.StringRest ~ StringDraw — 시위가 오가는 구간
// 여기 있는 값은 "얼마나 세게 끌어당길지"뿐이고, 위치는 하나도 들고 있지 않다.
//
// 회전 가중치가 기본 0인 이유: 손목 방향까지 IK가 정하면 클립이 잡아 둔 자연스러운 손 모양이
// 통째로 덮인다. 위치만 맞추고 손목은 애니메이션에 맡기는 편이 거의 언제나 낫다.
// 무기 프리팹의 표식 회전을 제대로 맞춰 둔 뒤에 필요한 만큼만 올리면 된다.
//
// Animator의 해당 레이어에 IK Pass가 켜져 있어야 이 콜백이 돌아온다.
[RequireComponent(typeof(Animator))]
[DisallowMultipleComponent]
public class WeaponHandIK : MonoBehaviour
{
    [SerializeField] private WeaponEquipper equipment;

    [Header("양손 무기 — 빈 손이 보조 그립을 잡는다")]
    [SerializeField, Range(0f, 1f)] private float secondaryGripWeight = 1f;
    [SerializeField, Range(0f, 1f)] private float secondaryGripRotationWeight;

    [Header("활 — 반대 손이 시위를 잡는다")]
    [SerializeField, Range(0f, 1f)] private float stringHandWeight = 1f;
    [SerializeField, Range(0f, 1f)] private float stringHandRotationWeight;

    [Tooltip("손이 무기에 붙고 떨어지는 속도(초당 가중치). 무기를 바꾸는 순간 손이 순간이동하지 않도록 한다.")]
    [SerializeField, Min(0.01f)] private float blendSpeed = 8f;

    private Animator rig;
    private EquipHand appliedHand;
    private float appliedWeight;
    private Vector3 correction;
    private Vector3 target;
    private bool targeting;

    private void Awake()
    {
        rig = GetComponent<Animator>();
        if (equipment == null) equipment = GetComponentInParent<WeaponEquipper>();

        // 잡을 것이 없으면 스스로 잠든다(LateUpdate). 다시 깨우는 건 장비가 바뀌는 순간뿐이다.
        if (equipment != null) equipment.LoadoutChanged += Wake;
    }

    private void OnDestroy()
    {
        if (equipment != null) equipment.LoadoutChanged -= Wake;
    }

    private void Wake()
    {
        enabled = true;
    }

    // 지금 이 손이 할 일이 있는가.
    // 활은 언제 당길지 모르니 들고 있는 동안 계속 깨어 있어야 하고,
    // 양손 무기는 빈 손이 자루를 잡고 있어야 한다. 그 밖에는 애니메이션이 알아서 한다.
    private bool HasWork()
    {
        if (equipment == null) return false;
        if (equipment.BowGrip != null) return true;

        EquipHand hand;
        Transform target;
        return equipment.TryGetSecondaryGrip(out hand, out target);
    }

    // 베이스 레이어에서만 손을 옮긴다. 레이어마다 한 번씩 불려 오는 콜백이라
    // 거르지 않으면 한 프레임에 가중치가 여러 번 앞으로 간다.
    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || rig == null || !rig.isHuman) return;

        EquipHand hand;
        Vector3 position;
        Quaternion rotation;
        float targetWeight;
        float rotationWeight;
        bool wanted = ResolveTarget(out hand, out position, out rotation, out targetWeight, out rotationWeight);

        // 잡을 손이 바뀌면 이전 손은 그 자리에서 놓는다. 두 손을 동시에 끌어당기지 않는다.
        if (wanted && hand != appliedHand)
        {
            appliedHand = hand;
            appliedWeight = 0f;
            correction = Vector3.zero;
        }

        appliedWeight = Mathf.MoveTowards(appliedWeight, wanted ? targetWeight : 0f, blendSpeed * Time.deltaTime);

        // 쓰지 않는 손의 목표는 매 프레임 확실히 풀어 준다 — 한 번 실린 가중치는 스스로 사라지지 않는다.
        Clear(HandSocket.GoalFor(HandSocket.Opposite(appliedHand)));

        AvatarIKGoal goal = HandSocket.GoalFor(appliedHand);
        if (appliedWeight <= 0f)
        {
            Clear(goal);
            return;
        }

        rig.SetIKPositionWeight(goal, appliedWeight);
        rig.SetIKPosition(goal, position + correction);
        rig.SetIKRotationWeight(goal, appliedWeight * rotationWeight);
        if (rotationWeight > 0f) rig.SetIKRotation(goal, rotation);

        target = position;
        targeting = true;
    }

    // IK가 옮기는 것은 손목이지만, 무기를 잡는 것은 손바닥이다.
    // 손목을 표식에 얹으면 자루가 손바닥에서 한 뼘 떠 있게 된다.
    //
    // 그 한 뼘을 미리 빼 두는 것만으로는 모자란다. 손목을 옮기면 팔 전체가 다시 풀리면서
    // 손의 방향이 같이 바뀌고, 그만큼 손바닥도 다른 데로 간다.
    // 그래서 지난 프레임에 손바닥이 실제로 어디 닿았는지 보고 그 차이만큼 목표를 밀어 준다.
    // 몇 프레임이면 손바닥이 표식 위에 앉고, 리그가 달라져도 스스로 다시 맞는다.
    private const float CorrectionGain = 0.6f;
    private const float MaxCorrection = 0.25f;

    private void LateUpdate()
    {
        // 애니메이션과 IK가 모두 끝난 뒤라, 지금 손바닥이 있는 자리가 이번 프레임의 결과다.
        if (!targeting || equipment == null)
        {
            correction = Vector3.zero;

            // 잡을 것도 없고 손도 다 풀렸으면 다음 장비 변경까지 쉰다.
            // 손이 아직 붙어 있는 동안 꺼 버리면 그 자리에서 뚝 끊기므로 가중치가 0이 된 뒤에 끈다.
            if (appliedWeight <= 0f && !HasWork()) enabled = false;
            return;
        }

        Transform socket = equipment.SocketOf(appliedHand);
        if (socket == null) return;

        Vector3 error = (target - socket.position) * (appliedWeight * CorrectionGain);
        correction = Vector3.ClampMagnitude(correction + error, MaxCorrection);
        targeting = false;
    }

    // 지금 남은 손이 잡아야 할 곳. 활이 먼저다 — 활을 들었으면 그 손은 시위 담당이다.
    private bool ResolveTarget(out EquipHand hand, out Vector3 position, out Quaternion rotation,
                               out float weight, out float rotationWeight)
    {
        hand = appliedHand;
        position = Vector3.zero;
        rotation = Quaternion.identity;
        weight = 0f;
        rotationWeight = 0f;
        if (equipment == null) return false;

        if (equipment.BowGrip != null)
        {
            if (!equipment.TryGetStringPose(out position, out rotation)) return false;

            hand = equipment.StringHand;
            weight = stringHandWeight * equipment.StringHandWeight;
            rotationWeight = stringHandRotationWeight;
            return weight > 0f;
        }

        Transform secondary;
        if (!equipment.TryGetSecondaryGrip(out hand, out secondary)) return false;

        position = secondary.position;
        rotation = secondary.rotation;
        weight = secondaryGripWeight;
        rotationWeight = secondaryGripRotationWeight;
        return weight > 0f;
    }

    private void Clear(AvatarIKGoal goal)
    {
        rig.SetIKPositionWeight(goal, 0f);
        rig.SetIKRotationWeight(goal, 0f);
    }
}
