using UnityEngine;

// 무기 프리팹이 "어느 손에, 어디를 잡히는가"를 스스로 들고 있게 하는 표식.
//
// 예전에는 무기마다 위치/회전 보정값을 WeaponDefinition에 적어 두고 장착할 때 코드가 밀어 넣었다.
// 무기가 늘어날수록 그 값이 어느 손을 기준으로 맞춘 것인지 알 수 없어지고,
// 손 위치를 한 번 손보면 스물몇 자루를 전부 다시 맞춰야 했다.
//
// 이제 기준점은 전부 프리팹 안에 있다. 프리팹 루트의 원점이 곧 주손이 자루를 쥐는 지점(Grip Point)이고,
// 모델은 그 아래에서 밀려나 있다. 장착은 소켓 아래에 위치 0 / 회전 0으로 넣는 것이 전부다.
//
//   Sword_1 (루트 = Grip Point, hand = Right)
//   ├ GripPoint (같은 자리에 놓인 표식. 손이 잡는 지점을 눈으로 확인하고 옮기는 용도)
//   └ Model     (실제 무기 모델. 그립이 루트에 오도록 밀고 돌려 둔 상태)
//
// 두 손을 쓰는 무기는 표식이 하나 더 붙는다.
//
//   Spear (주손은 소켓에 걸리고)
//   └ SecondaryGrip (반대 손이 IK로 따라가는 지점 — WeaponHandIK)
//
// 활은 손이 하는 일이 아예 다르다. 활은 왼손에 걸려 있고, 오른손은 활이 아니라 시위를 잡는다.
//
//   Bow (hand = Left)
//   ├ StringRest (시위가 풀린 자리. 화살이 물려 있는 지점이자 오른손이 잡는 지점)
//   └ StringDraw (끝까지 당겼을 때 그 손과 화살이 가 있을 자리)
//
// 손으로 다시 맞출 때는 GripPoint를 자루의 원하는 지점으로 옮긴 뒤
// 메뉴 PickMeUp/Equipment/Align Grip Point 를 실행한다. 그 차이만큼 모델이 밀려나고
// GripPoint는 다시 루트로 돌아온다 — 즉 보정값은 언제나 프리팹 안에만 남는다.
[DisallowMultipleComponent]
public class WeaponGrip : MonoBehaviour
{
    [Tooltip("이 무기를 쥐는 손. 활과 방패는 왼손, 나머지는 오른손이 기본이다. " +
             "왼손잡이 무기를 만들고 싶으면 여기만 바꾸면 된다 — 장착 코드는 이 값을 그대로 따른다.")]
    [SerializeField] private EquipHand hand = EquipHand.Right;

    [Tooltip("주손이 자루를 쥐는 지점. 비워 두면 프리팹 루트가 그 지점이다.")]
    [SerializeField] private Transform gripPoint;

    [Tooltip("반대 손이 따라가는 지점. 양손 무기에만 있다. 비어 있으면 반대 손은 애니메이션대로 둔다.")]
    [SerializeField] private Transform secondaryGrip;

    [Header("Bow")]
    [Tooltip("시위가 풀려 있을 때 화살(과 그것을 잡은 손)이 있는 자리.")]
    [SerializeField] private Transform stringRest;

    [Tooltip("끝까지 당겼을 때 화살(과 그것을 잡은 손)이 가 있는 자리. " +
             "StringRest에서 화살이 날아갈 방향의 반대쪽으로 물러난 지점이다.")]
    [SerializeField] private Transform stringDraw;

    [Tooltip("무기 모델이 담긴 자식. 그립을 다시 맞출 때 대신 밀려나는 쪽.")]
    [SerializeField] private Transform model;

    [Tooltip("씬에서 그립 축을 그릴 때 쓰는 길이(m).")]
    [SerializeField] private float gizmoLength = 0.1f;

    public EquipHand Hand { get { return hand; } }
    public Transform GripPoint { get { return gripPoint != null ? gripPoint : transform; } }
    public Transform SecondaryGrip { get { return secondaryGrip; } }
    public Transform StringRest { get { return stringRest; } }
    public Transform StringDraw { get { return stringDraw; } }
    public Transform Model { get { return model; } }

    // 반대 손이 잡을 곳이 있는가(양손 무기).
    public bool HasSecondaryGrip { get { return secondaryGrip != null; } }

    // 시위를 당기는 무기인가. 활이면 두 표식이 모두 있어야 한다 — 하나만 있으면 당길 구간을 알 수 없다.
    public bool HasBowString { get { return stringRest != null && stringDraw != null; } }

    // 그립이 루트에 맞춰져 있는가. 어긋나 있으면 그만큼 손에서 밀려 보인다.
    public bool IsAligned
    {
        get
        {
            if (gripPoint == null || gripPoint == transform) return true;
            return gripPoint.localPosition.sqrMagnitude < 1e-8f &&
                   Quaternion.Angle(gripPoint.localRotation, Quaternion.identity) < 0.01f;
        }
    }

    // 시위를 t만큼 당겼을 때 화살과 그 손이 있어야 할 자세.
    public bool TryGetStringPose(float draw, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (!HasBowString) return false;

        float t = Mathf.Clamp01(draw);
        position = Vector3.Lerp(stringRest.position, stringDraw.position, t);
        rotation = Quaternion.Slerp(stringRest.rotation, stringDraw.rotation, t);
        return true;
    }

    // 자손까지 내려가며 이름으로 자식을 찾는다.
    //
    // Transform.Find는 바로 아래 자식만 본다. 무기 프리팹은 그립을 맞추느라 모델을 한 겹 감싸고 있어
    // 표식(NockedArrow 등)이 늘 손자 이상에 있으므로, 붙이는 쪽도 굽는 쪽도 이 탐색이 필요하다.
    public static Transform FindDeep(Transform root, string childName)
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

    public void Bind(Transform newGripPoint, Transform newModel)
    {
        gripPoint = newGripPoint;
        model = newModel;
    }

    public void BindHand(EquipHand newHand)
    {
        hand = newHand;
    }

    public void BindSecondaryGrip(Transform newSecondaryGrip)
    {
        secondaryGrip = newSecondaryGrip;
    }

    public void BindBowString(Transform rest, Transform draw)
    {
        stringRest = rest;
        stringDraw = draw;
    }

    // 손이 쥐는 자리와 방향을 씬에서 눈으로 확인할 수 있게 그린다.
    // 초록 = 날이 뻗는 방향(+Y), 파랑 = 칼날 면의 법선(+Z). 소켓 축과 같은 약속이다.
    private void OnDrawGizmosSelected()
    {
        Transform grip = GripPoint;
        float length = Mathf.Max(0.01f, gizmoLength);

        DrawAxes(grip, length);
        Gizmos.color = IsAligned ? Color.yellow : Color.red;
        Gizmos.DrawWireSphere(grip.position, length * 0.15f);

        if (secondaryGrip != null)
        {
            DrawAxes(secondaryGrip, length * 0.7f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(secondaryGrip.position, length * 0.12f);
            Gizmos.DrawLine(grip.position, secondaryGrip.position);
        }

        if (!HasBowString) return;

        // 시위가 오가는 구간. 이 선이 곧 화살이 날아갈 축이다.
        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(stringRest.position, stringDraw.position);
        Gizmos.DrawWireSphere(stringRest.position, length * 0.12f);
        Gizmos.DrawWireSphere(stringDraw.position, length * 0.12f);
        Gizmos.color = Color.white;
        Gizmos.DrawLine(stringRest.position, stringRest.position + stringRest.forward * length * 3f);
    }

    private static void DrawAxes(Transform t, float length)
    {
        Gizmos.color = Color.green;
        Gizmos.DrawLine(t.position, t.position + t.up * length);
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(t.position, t.position + t.forward * length * 0.5f);
    }
}
