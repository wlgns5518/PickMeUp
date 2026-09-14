using UnityEngine;

// 무기가 들리는 손.
//
// EquipSlot(MainHand/OffHand)은 "전투에서 어느 자리를 맡는가"를 말하고,
// 이쪽은 "몸의 어느 손에 걸리는가"를 말한다. 둘은 늘 같지 않다 —
// 활은 주무기지만 왼손에 들리고, 방패는 보조 장비지만 오른손잡이 기준 왼손을 통째로 쓴다.
public enum EquipHand { Right, Left }

// 손에 무기를 매다는 자리.
//
// 무기별 보정값을 코드에서 들고 있지 않으려면, 붙이는 자리가 손마다 딱 하나로 정해져 있어야 한다.
// 그 자리가 손뼈 아래에 놓인 소켓이다. 두 손은 서로를 모른다 — 각자 자기 소켓에 자기 무기를 걸 뿐이고,
// 무기 프리팹은 소켓 아래에 위치 0 / 회전 0 / 배율 1로 들어간다.
//
//   mixamorig:RightHand              mixamorig:LeftHand
//   └ RightHandWeaponSocket          └ LeftHandWeaponSocket
//     └ Sword_1 (Grip Point = 루트)    └ Round_Wood_Shield
//
// 소켓은 캐릭터 프리팹에 미리 놓아 둔다(메뉴 PickMeUp/Equipment/Add Weapon Sockets).
// 그게 없는 캐릭터를 위해 같은 자리를 뼈에서 계산해 만들어 주는 길도 남겨 둔다 —
// 놓아 둔 소켓과 계산한 소켓이 같은 식에서 나오므로 어느 쪽이든 무기는 같은 자세로 잡힌다.
public static class HandSocket
{
    public const string RightSocketName = "RightHandWeaponSocket";
    public const string LeftSocketName = "LeftHandWeaponSocket";

    // 양손을 한 이름으로 쓰던 시절의 소켓. 아직 그 이름으로 놓여 있는 캐릭터도 그대로 잡히게 둔다.
    private const string LegacySocketName = "WeaponSocket";

    // 손목에서 중지 밑동까지를 1로 봤을 때 소켓을 손바닥 쪽으로 밀어내는 비율.
    // 0이면 손목 관절(Hand 본 원점)이라 무기가 손목에 매달린 것처럼 보인다.
    public const float DefaultPalmGripRatio = 0.6f;

    // 손가락 뼈가 없는 리그를 위한 값들.
    //
    // AI가 구워 온 리그에는 손가락이 없는 경우가 흔하다(Meshy의 자동 리깅이 그렇다).
    // 손가락이 없으면 손 모양을 읽을 수 없으니, 손뼈 자체의 국소 축에서 같은 자리를 잡는다.
    // 믹사모 규약을 따르는 리그는 손뼈 국소 +Y가 손가락 방향이고 ±X가 손바닥을 가로지른다 —
    // Y Bot(믹사모)에서 재면 팔 방향이 손뼈 국소 (0, 1, 0), 날 축이 (∓0.984, 0.177, 0)이고,
    // Meshy 리그도 팔 방향이 (0.31, 0.95, -0.08)로 같은 +Y 규약을 쓴다.
    //
    // 아래 두 상수로 계산하면 Y Bot에 손으로 맞춰 둔 소켓 회전(-0.64143, 0.76718)이 그대로 나온다.
    // 즉 이 대체 경로는 손가락이 있는 리그에서도 거의 같은 답을 낸다.
    private static readonly Vector3 FingerAxis = Vector3.up;
    private const float BladeTilt = 0.18f;

    // 아래팔 길이를 1로 봤을 때 손목에서 중지 밑동까지의 길이. Y Bot에서 잰 값(0.1278 / 0.2761).
    // 손가락이 없으면 손 길이를 잴 수 없으므로 팔 길이에서 비례로 짚는다.
    private const float HandToForearmRatio = 0.463f;

    public static string NameFor(EquipHand hand)
    {
        return hand == EquipHand.Right ? RightSocketName : LeftSocketName;
    }

    public static EquipHand Opposite(EquipHand hand)
    {
        return hand == EquipHand.Right ? EquipHand.Left : EquipHand.Right;
    }

    public static AvatarIKGoal GoalFor(EquipHand hand)
    {
        return hand == EquipHand.Right ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;
    }

    public static Transform GetHandBone(Animator animator, EquipHand hand)
    {
        if (animator == null || !animator.isHuman) return null;
        return animator.GetBoneTransform(hand == EquipHand.Right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
    }

    // 캐릭터 프리팹에 놓아 둔 소켓. 없으면 null.
    public static Transform Find(Animator animator, EquipHand hand)
    {
        Transform bone = GetHandBone(animator, hand);
        if (bone == null) return null;

        Transform socket = bone.Find(NameFor(hand));
        return socket != null ? socket : bone.Find(LegacySocketName);
    }

    // 놓아 둔 소켓이 있으면 그것을, 없으면 같은 자리에 하나 만들어 돌려준다.
    public static Transform Resolve(Animator animator, EquipHand hand, float palmGripRatio)
    {
        Transform bone = GetHandBone(animator, hand);
        if (bone == null) return null;

        Transform existing = Find(animator, hand);
        if (existing != null) return existing;

        Vector3 localPosition;
        Quaternion localRotation;
        if (!TryCompute(animator, hand, palmGripRatio, out localPosition, out localRotation)) return null;

        var socket = new GameObject(NameFor(hand)).transform;
        socket.SetParent(bone, false);
        socket.localPosition = localPosition;
        socket.localRotation = localRotation;
        socket.localScale = Vector3.one;
        return socket;
    }

    // 손뼈 기준으로 소켓이 설 자리. 캐릭터 프리팹에 소켓을 놓는 에디터 도구도 이 식을 쓴다.
    public static bool TryCompute(Animator animator, EquipHand hand, float palmGripRatio,
                                  out Vector3 localPosition, out Quaternion localRotation)
    {
        localPosition = Vector3.zero;
        localRotation = Quaternion.identity;

        Transform bone = GetHandBone(animator, hand);
        if (bone == null) return false;

        bool right = hand == EquipHand.Right;
        Transform index = animator.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
        Transform little = animator.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
        Transform middle = animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);

        // 손가락이 하나라도 비면 손 모양을 읽을 수 없다. 그때는 손뼈 축에서 같은 자리를 짚는다.
        if (index == null || little == null || middle == null)
        {
            Transform forearm = animator.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            return TryComputeFromBoneAxes(bone, forearm, right, palmGripRatio, out localPosition, out localRotation);
        }

        localPosition = PalmOffset(bone, middle, palmGripRatio);
        localRotation = GripRotation(bone, index, little, middle);
        return true;
    }

    // 손가락 없는 리그. 손뼈의 국소 축이 곧 손 모양이라고 보고 같은 식을 태운다.
    // 아래팔이 없으면 길이를 잴 데가 없어 손목에 그대로 붙인다 — 회전만이라도 맞춰 두면
    // 무기가 손목에서 자라난 것처럼 보일지언정 엉뚱한 방향으로 뻗지는 않는다.
    private static bool TryComputeFromBoneAxes(Transform hand, Transform forearm, bool right, float palmGripRatio,
                                               out Vector3 localPosition, out Quaternion localRotation)
    {
        Vector3 blade = new Vector3(right ? -1f : 1f, BladeTilt, 0f).normalized;
        Vector3 palm = Vector3.Cross(blade, FingerAxis);

        localRotation = Quaternion.LookRotation(palm.normalized, blade);

        // 손뼈 국소 단위로 재야 한다. 리그에 배율이 섞여 있으면 월드 거리는 그대로 쓸 수 없다.
        float handLength = forearm != null
            ? hand.InverseTransformPoint(forearm.position).magnitude * HandToForearmRatio
            : 0f;
        localPosition = FingerAxis * (handLength * Mathf.Clamp01(palmGripRatio));
        return true;
    }

    // 휴머노이드 리그에서 Hand 본의 원점은 손바닥이 아니라 손목 관절이다(이 리그는 손목~중지 밑동이 12.8cm).
    // 소켓을 원점에 그대로 두면 무기가 손목에 매달린 것처럼 보이므로, 주먹이 자루를 쥐는 지점까지 밀어준다.
    // 손가락 뼈가 매핑되지 않은 리그에서는 기준을 잡을 수 없으니 손목 그대로 둔다.
    private static Vector3 PalmOffset(Transform hand, Transform middle, float palmGripRatio)
    {
        if (hand == null || middle == null) return Vector3.zero;
        return hand.InverseTransformPoint(middle.position) * Mathf.Clamp01(palmGripRatio);
    }

    // 소켓의 자세를 손 모양에서 직접 뽑는다.
    //
    // 주먹으로 자루를 쥐면 자루는 새끼손가락 쪽에서 검지 쪽으로 손바닥을 가로지르고,
    // 날은 그대로 검지 너머로 뻗어 나간다. 그래서 소켓 +Y = (검지 - 새끼)로 잡는다.
    // 칼날의 넓은 면은 손바닥과 나란해야 하므로 소켓 +Z(정면)는 손바닥 법선에 맞춘다.
    // 무기 프리팹도 이 약속(+Y가 날, +Z가 칼날 면의 법선)에 맞춰 세워 둔다.
    // 손가락 뼈가 매핑돼 있지 않은 리그에서는 계산할 근거가 없으니 손뼈 자세를 그대로 쓴다.
    private static Quaternion GripRotation(Transform hand, Transform index, Transform little, Transform middle)
    {
        if (hand == null || index == null || little == null || middle == null) return Quaternion.identity;

        Vector3 blade = hand.InverseTransformDirection(index.position - little.position).normalized;
        Vector3 fingers = hand.InverseTransformDirection(middle.position - hand.position).normalized;
        Vector3 palm = Vector3.Cross(blade, fingers);
        if (blade.sqrMagnitude < 0.0001f || palm.sqrMagnitude < 0.0001f) return Quaternion.identity;

        return Quaternion.LookRotation(palm.normalized, blade);
    }
}
