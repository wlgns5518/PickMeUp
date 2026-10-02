using UnityEngine;

// 무기를 쥔 손 말고, 나머지 한 손이 할 일.
//
// 무기를 든 손은 소켓이 해결한다 — 손이 움직이면 무기가 따라간다. 문제는 반대 손이다.
// 양손검을 들어도 왼손은 애니메이션이 정한 자리에 그대로 있고, 활을 들어도 오른손은
// 시위와 무관하게 흔들린다. 무기 하나에 클립 한 벌을 새로 찍지 않는 한 이 어긋남은 남는다.
//
// 그래서 남은 손만 IK로 무기 쪽에 붙인다. 어디에 붙일지는 전부 무기 프리팹이 들고 있다.
//   · 양손 무기: WeaponGrip.SecondaryGrip — 자루의 두 번째 지점(장병기는 자루의 한 구간)
//   · 활: WeaponGrip.StringRest ~ StringDraw — 시위가 오가는 구간
// 여기 있는 값은 "얼마나 세게 끌어당길지"뿐이고, 위치는 하나도 들고 있지 않다.
//
// 양손 무기라고 늘 두 손으로 드는 것은 아니다. 무기 전용 클립은 공격뿐이고 달리기·피격·회피·시전은
// 모든 무기가 같이 쓰는 클립이라, 거기서는 빈 손이 자루에서 0.3~1.3m 떨어져 제 할 일을 한다.
// 그 손을 자루에 묶어 두면 팔이 몸을 가로질러 끌려간다. 그래서 언제 잡을지는 동작에게 묻는다 —
// 애니메이션이 빈 손을 자루 곁에 두면 잡고(gripDistance), 멀리 가져가면 놓는다(releaseDistance).
// 창을 돌리느라 한 손을 떼는 공격도 같은 규칙으로 그 구간만 한 손이 된다.
//
// 팔은 Animator의 IK가 아니라 LateUpdate에서 직접 푼다. Animator IK 콜백은 뼈가 쓰이기 전에 불려서
// 그 안에서 보이는 무기는 지난 프레임의 자리이고(빠른 찌르기에서 손이 자루를 10cm 넘게 늦게 따라간다),
// 애니메이션이 손을 둔 자리도 뼈가 아니라 IK 목표로만 읽을 수 있는데 그 값은 몸의 팔 길이에 따라
// 실제 손목과 15cm까지 어긋난다. LateUpdate에서는 손도 무기도 이번 프레임의 실제 자리에 있다.
//
// 회전 가중치가 기본 0인 이유: 손목 방향까지 IK가 정하면 클립이 잡아 둔 자연스러운 손 모양이
// 통째로 덮인다. 위치만 맞추고 손목은 애니메이션에 맡기는 편이 거의 언제나 낫다.
// 무기 프리팹의 표식 회전을 제대로 맞춰 둔 뒤에 필요한 만큼만 올리면 된다.
[RequireComponent(typeof(Animator))]
[DisallowMultipleComponent]
public class WeaponHandIK : MonoBehaviour
{
    [SerializeField] private WeaponEquipper equipment;

    [Header("양손 무기 — 빈 손이 보조 그립을 잡는다")]
    [SerializeField, Range(0f, 1f)] private float secondaryGripWeight = 1f;
    [SerializeField, Range(0f, 1f)] private float secondaryGripRotationWeight;

    [Tooltip("애니메이션의 빈 손(손목)이 자루에서 이 거리(m) 안으로 들어오면 자루를 잡는다. " +
             "양손 공격 클립은 손목이 자루에서 0~0.18m에 있다.")]
    [SerializeField, Min(0f)] private float gripDistance = 0.16f;
    [Tooltip("자루를 잡은 손이 애니메이션에서 이 거리(m)보다 멀어지면 놓는다. 잡는 거리보다 넉넉해야 " +
             "경계에서 잡았다 놓았다 하지 않는다. 한 손 동작은 가장 가까울 때도 0.19~0.31m라 그 사이에 둔다.")]
    [SerializeField, Min(0f)] private float releaseDistance = 0.34f;
    [Tooltip("주손에서 먼 곳을 잡을수록 두 거리를 넓혀 주는 비율(주손까지의 거리 × 이 값, 0.3 ≈ 17°). " +
             "자루를 가로로 들어 막는 자세는 두 손이 1m쯤 벌어지는데, 그 거리에서는 클립의 창과 우리 창의 " +
             "방향이 12°만 달라도 손목이 자루에서 0.21~0.23m 떨어진다. 0.25로는 팔이 짧은 몸에서 못 잡았다.")]
    [SerializeField, Min(0f)] private float gripLeverage = 0.3f;

    [Header("활 — 반대 손이 시위를 잡는다")]
    [SerializeField, Range(0f, 1f)] private float stringHandWeight = 1f;
    [SerializeField, Range(0f, 1f)] private float stringHandRotationWeight;

    [Tooltip("손이 무기에 붙고 떨어지는 속도(초당 가중치). 무기를 바꾸는 순간 손이 순간이동하지 않도록 한다.")]
    [SerializeField, Min(0.01f)] private float blendSpeed = 8f;

    private Animator rig;
    private EquipHand appliedHand;
    private float appliedWeight;

    // 지금 옮기고 있는 팔. 어깨 → 팔꿈치 → 손목, 그리고 그 손의 손바닥(소켓).
    private Transform upperArm;
    private Transform lowerArm;
    private Transform wrist;
    private Transform palm;
    private bool armBound;

    // 지난번에 우리가 써 넣은 팔 자세. 뼈가 아직 이 값 그대로면 Animator가 이번 프레임에 자세를
    // 새로 쓰지 않은 것이다(화면 밖이라 건너뛰었거나, 죽은 뒤 꺼졌다).
    private Quaternion writtenUpper;
    private Quaternion writtenLower;
    private bool written;

    // 양손 무기의 빈 손이 지금 자루를 잡고 있는가.
    private bool gripping;

    // 자루를 놓는 중인가, 그리고 놓기 직전에 손바닥을 클립의 자리에서 얼마나 옮겨 두고 있었는가(몸 기준).
    //
    // 놓은 손을 가중치가 다 빠질 때까지 계속 자루 쪽으로 당기면 안 된다. 창을 돌리는 공격은 손을 떼자마자
    // 한 프레임에 0.6m씩 달아나고 자루도 반대로 휘돌아서, 그 사이 팔이 다 펴졌다 접혔다 하며 허우적댄다.
    // 놓는 순간의 어긋남만 들고 있다가 그것을 0으로 줄여 가면, 손은 클립을 그대로 따라가면서 풀린다.
    private bool releasing;
    private Vector3 heldPull;
    private float heldWeight;

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

        // 새 무기는 놓은 손으로 시작한다. 잡을지는 다음 프레임의 동작이 다시 정한다.
        // 소켓도 다시 찾는다 — 장비가 처음 들릴 때에야 만들어지는 몸이 있다.
        gripping = false;
        releasing = false;
        heldWeight = 0f;
        armBound = false;
    }

    // 지금 이 손이 할 일이 있는가.
    // 활은 언제 당길지 모르니 들고 있는 동안 계속 깨어 있어야 하고,
    // 양손 무기는 빈 손이 자루를 잡을 때를 지켜봐야 한다. 그 밖에는 애니메이션이 알아서 한다.
    private bool HasWork()
    {
        if (equipment == null) return false;
        if (equipment.BowGrip != null) return true;

        EquipHand hand;
        WeaponGrip grip;
        return equipment.TryGetSecondaryGrip(out hand, out grip);
    }

    // 애니메이션이 이번 프레임의 자세를 다 쓴 뒤다. 무기는 주손을 따라 제자리에 와 있고,
    // 빈 손은 아직 클립이 둔 그대로다 — 여기서 재고, 여기서 옮긴다.
    private void LateUpdate()
    {
        if (rig == null || equipment == null || !rig.isHuman)
        {
            enabled = false;
            return;
        }

        // Animator가 자세를 새로 쓰지 않았으면 팔은 지난번에 옮겨 둔 그대로다.
        // 그 위에 또 얹으면 이미 자루에 붙은 손을 "클립이 둔 자리"로 잘못 읽는다.
        if (!rig.enabled || PoseIsStale()) return;

        EquipHand hand;
        Vector3 position;
        Quaternion rotation;
        float targetWeight;
        float rotationWeight;
        bool wanted = ResolveTarget(out hand, out position, out rotation, out targetWeight, out rotationWeight);

        // 잡을 자리가 없어졌으면(무기를 내려놓았다) 남은 가중치로 끌어갈 곳도 없다. 그 자리에서 푼다.
        if (!wanted) appliedWeight = 0f;

        // 잡을 손이 바뀌면 이전 손은 그 자리에서 놓는다. 두 손을 동시에 끌어당기지 않는다.
        if (wanted && (hand != appliedHand || !armBound))
        {
            if (hand != appliedHand) appliedWeight = 0f;
            appliedHand = hand;
            BindArm(hand);
        }

        appliedWeight = Mathf.MoveTowards(appliedWeight, wanted ? targetWeight : 0f, blendSpeed * Time.deltaTime);
        written = false;

        if (appliedWeight <= 0f || !armBound)
        {
            // 잡을 것도 없고 손도 다 풀렸으면 다음 장비 변경까지 쉰다.
            // 손이 아직 붙어 있는 동안 꺼 버리면 그 자리에서 뚝 끊기므로 가중치가 0이 된 뒤에 끈다.
            if (!HasWork()) enabled = false;
            return;
        }

        Reach(position, rotation, appliedWeight, rotationWeight);
    }

    private bool PoseIsStale()
    {
        return written && armBound && upperArm != null && lowerArm != null &&
               upperArm.localRotation == writtenUpper && lowerArm.localRotation == writtenLower;
    }

    private void BindArm(EquipHand hand)
    {
        bool right = hand == EquipHand.Right;
        upperArm = rig.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
        lowerArm = rig.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
        wrist = HandSocket.GetHandBone(rig, hand);
        palm = equipment.SocketOf(hand);

        // 소켓이 없는 손은 손목으로 잡는다. 자루가 손바닥에서 한 뼘 뜨지만 팔은 따라간다.
        if (palm == null) palm = wrist;

        armBound = upperArm != null && lowerArm != null && wrist != null;
        written = false;
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
        releasing = false;

        if (equipment.BowGrip != null)
        {
            if (!equipment.TryGetStringPose(out position, out rotation)) return false;

            hand = equipment.StringHand;
            weight = stringHandWeight * equipment.StringHandWeight;
            rotationWeight = stringHandRotationWeight;
            return true;
        }

        WeaponGrip grip;
        if (!equipment.TryGetSecondaryGrip(out hand, out grip)) return false;

        Transform bone = hand == appliedHand && armBound ? wrist : HandSocket.GetHandBone(rig, hand);
        if (bone == null) return false;

        // 클립이 둔 손에서 가장 가까운 자루 위의 자리. 장병기는 손이 자루를 타고 미끄러지는 대로 따라간다.
        position = grip.ClosestSecondaryPoint(bone.position);
        rotation = grip.SecondaryGrip.rotation;
        rotationWeight = secondaryGripRotationWeight;

        // 잡는 거리와 놓는 거리가 다르다. 하나로 두면 달리기처럼 손이 그 언저리를 스치는 동작에서
        // 걸음마다 잡았다 놓는다. 둘 다 주손에서 먼 자리일수록 넉넉해진다(gripLeverage).
        float distance = Vector3.Distance(bone.position, position);
        float span = Vector3.Distance(position, grip.GripPoint.position);
        float grab = Mathf.Max(gripDistance, span * gripLeverage);
        float release = grab + Mathf.Max(0f, releaseDistance - gripDistance);
        gripping = distance < (gripping ? release : grab);

        weight = gripping ? secondaryGripWeight : 0f;
        releasing = !gripping;
        return true;
    }

    // 손바닥이 goal에 닿도록 팔을 편다. 관절 둘(어깨·팔꿈치)짜리 팔이라 풀이는 하나로 정해진다.
    //
    // 닿게 하는 것이 손목이 아니라 손바닥(소켓)이다. 손목은 클립의 각도를 그대로 지키므로
    // 팔꿈치에서 손바닥까지가 굳은 막대 하나이고, 그 끝을 목표에 놓으면 자루가 손바닥 안에 든다.
    private void Reach(Vector3 goal, Quaternion goalRotation, float weight, float rotationWeight)
    {
        if (rotationWeight > 0f)
        {
            Quaternion turn = goalRotation * Quaternion.Inverse(palm.rotation);
            wrist.rotation = Quaternion.Slerp(Quaternion.identity, turn, weight * rotationWeight) * wrist.rotation;
        }

        Vector3 shoulder = upperArm.position;
        Vector3 elbow = lowerArm.position;
        Vector3 tip = palm.position;

        // 잡는 동안은 목표 쪽으로 가중치만큼, 놓는 동안은 놓던 순간의 어긋남을 남은 가중치만큼.
        Vector3 pull;
        if (releasing && heldWeight > 0f)
        {
            pull = rig.transform.TransformDirection(heldPull) * (weight / heldWeight);
        }
        else
        {
            pull = (goal - tip) * weight;
            heldPull = rig.transform.InverseTransformDirection(pull);
            heldWeight = weight;
        }
        Vector3 wanted = tip + pull;

        float upperLength = Vector3.Distance(shoulder, elbow);
        float lowerLength = Vector3.Distance(elbow, tip);
        if (upperLength < 1e-4f || lowerLength < 1e-4f) return;

        // 팔 길이보다 먼 곳은 뻗을 수 있는 데까지만 간다. 다 펴지기 직전에서 멈춰야 팔꿈치가 뒤집히지 않는다.
        float reach = Mathf.Clamp(Vector3.Distance(shoulder, wanted),
                                  Mathf.Abs(upperLength - lowerLength) + 0.001f,
                                  (upperLength + lowerLength) * 0.999f);

        // 1) 팔꿈치를 굽히거나 펴서 어깨에서 손바닥까지의 거리를 맞춘다. 굽는 면은 클립이 잡아 둔 그대로다.
        Vector3 toShoulder = shoulder - elbow;
        Vector3 toTip = tip - elbow;
        Vector3 hinge = Vector3.Cross(toShoulder, toTip);
        if (hinge.sqrMagnitude < 1e-10f) hinge = Vector3.Cross(toShoulder, wanted - shoulder);
        if (hinge.sqrMagnitude < 1e-10f) hinge = lowerArm.right;

        float cos = (upperLength * upperLength + lowerLength * lowerLength - reach * reach) / (2f * upperLength * lowerLength);
        float bend = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg - Vector3.Angle(toShoulder, toTip);
        lowerArm.rotation = Quaternion.AngleAxis(bend, hinge.normalized) * lowerArm.rotation;

        // 2) 어깨를 돌려 손바닥을 목표 쪽으로 보낸다.
        upperArm.rotation = Quaternion.FromToRotation(palm.position - shoulder, wanted - shoulder) * upperArm.rotation;

        writtenUpper = upperArm.localRotation;
        writtenLower = lowerArm.localRotation;
        written = true;
    }
}
