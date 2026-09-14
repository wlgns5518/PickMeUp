using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 캐릭터 프리팹의 두 손을 무기를 받을 수 있는 상태로 만들어 주는 도구.
//
// 무기를 손에 맞추는 일이 전부 무기 프리팹 쪽으로 넘어갔으므로, 캐릭터 쪽이 할 일은
// 손마다 자리 하나를 내주는 것뿐이다. 두 손은 서로를 모르고, 각자 자기 소켓만 가진다.
//
//   mixamorig:RightHand              mixamorig:LeftHand
//   └ RightHandWeaponSocket          └ LeftHandWeaponSocket
//
// 자리는 HandSocket이 손뼈에서 재는 그 자리 그대로다. 그래서 프리팹에 놓아 둔 소켓과
// 런타임에 만들어지는 소켓이 언제나 같은 곳에 선다.
//
// 소켓만으로는 반쪽이다. 양손 무기의 반대 손과 활시위를 잡는 손은 IK가 옮기므로,
// WeaponHandIK를 붙이고 Animator 레이어의 IK Pass도 같이 켜 준다 — 꺼져 있으면 OnAnimatorIK가
// 아예 호출되지 않아서, 손은 그냥 애니메이션대로 허공에 남는다.
//
// 쓰는 법: 캐릭터 프리팹(또는 씬의 캐릭터)을 고르고 PickMeUp/Equipment/Add Weapon Sockets.
public static class WeaponSocketBuilder
{
    [MenuItem("PickMeUp/Equipment/Add Weapon Sockets (Selected)")]
    public static void AddSockets()
    {
        int count = 0;
        foreach (Object o in Selection.objects)
        {
            var go = o as GameObject;
            if (go == null) continue;

            string path = AssetDatabase.GetAssetPath(go);
            bool done = !string.IsNullOrEmpty(path) && path.EndsWith(".prefab")
                ? AddToPrefab(path)
                : AddToInstance(go);
            if (done) count++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[WeaponSocketBuilder] " + count + "명의 두 손에 소켓을 놓고 손 IK를 붙였다.");
    }

    [MenuItem("PickMeUp/Equipment/Add Weapon Sockets (Selected)", true)]
    private static bool AddSocketsValidate() => Selection.objects.Length > 0;

    // 씬(또는 프리팹 스테이지)에 있는 캐릭터. 그 자리에서 바로 만든다.
    private static bool AddToInstance(GameObject go)
    {
        var animator = go.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) return false;

        float ratio = PalmGripRatio(go);
        Undo.RegisterFullObjectHierarchyUndo(go, "Add Weapon Sockets");

        Transform right = HandSocket.Resolve(animator, EquipHand.Right, ratio);
        Transform left = HandSocket.Resolve(animator, EquipHand.Left, ratio);
        Rename(right, HandSocket.RightSocketName);
        Rename(left, HandSocket.LeftSocketName);

        var equipper = go.GetComponentInChildren<WeaponEquipper>();
        Link(equipper, right, left);
        AttachHandIK(animator, equipper);
        EnableIKPass(animator);
        return true;
    }

    // 프리팹 에셋. 뼈 자세는 살아 있는 Animator에서만 읽히므로,
    // 임시 씬에 한 번 세워 재어 두고 그 값을 프리팹에 옮겨 적는다.
    private static bool AddToPrefab(string path)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) return false;

        string rightBone, leftBone;
        Vector3 rightPosition, leftPosition;
        Quaternion rightRotation, leftRotation;
        if (!Measure(asset, out rightBone, out rightPosition, out rightRotation,
                            out leftBone, out leftPosition, out leftRotation)) return false;

        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Transform right = Place(contents.transform, rightBone, EquipHand.Right, rightPosition, rightRotation);
            Transform left = Place(contents.transform, leftBone, EquipHand.Left, leftPosition, leftRotation);
            if (right == null && left == null) return false;

            var equipper = contents.GetComponentInChildren<WeaponEquipper>();
            Link(equipper, right, left);

            var animator = contents.GetComponentInChildren<Animator>();
            AttachHandIK(animator, equipper);
            EnableIKPass(animator);

            PrefabUtility.SaveAsPrefabAsset(contents, path);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // 임시 씬에 세워 두 손의 소켓 자리를 잰다. 재고 나면 씬은 저장 없이 닫는다.
    private static bool Measure(GameObject asset,
                                out string rightBone, out Vector3 rightPosition, out Quaternion rightRotation,
                                out string leftBone, out Vector3 leftPosition, out Quaternion leftRotation)
    {
        rightBone = leftBone = null;
        rightPosition = leftPosition = Vector3.zero;
        rightRotation = leftRotation = Quaternion.identity;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            var animator = instance.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                Debug.LogWarning("[WeaponSocketBuilder] " + asset.name + ": 휴머노이드 리그가 아니라 손을 찾을 수 없다.", asset);
                return false;
            }

            float ratio = PalmGripRatio(instance);
            bool right = HandSocket.TryCompute(animator, EquipHand.Right, ratio, out rightPosition, out rightRotation);
            bool left = HandSocket.TryCompute(animator, EquipHand.Left, ratio, out leftPosition, out leftRotation);

            if (right) rightBone = RelativePath(instance.transform, HandSocket.GetHandBone(animator, EquipHand.Right));
            if (left) leftBone = RelativePath(instance.transform, HandSocket.GetHandBone(animator, EquipHand.Left));
            return right || left;
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static Transform Place(Transform root, string bonePath, EquipHand hand,
                                   Vector3 localPosition, Quaternion localRotation)
    {
        if (string.IsNullOrEmpty(bonePath)) return null;

        Transform bone = root.Find(bonePath);
        if (bone == null) return null;

        string name = HandSocket.NameFor(hand);

        // 양손을 한 이름으로 쓰던 시절의 소켓이 남아 있으면 이름만 갈아 준다.
        // 지우고 새로 만들면 그 소켓을 가리키던 참조가 전부 끊긴다.
        Transform socket = bone.Find(name);
        if (socket == null) socket = bone.Find("WeaponSocket");
        if (socket == null)
        {
            socket = new GameObject(name).transform;
            socket.SetParent(bone, false);
        }
        socket.name = name;

        socket.localPosition = localPosition;
        socket.localRotation = localRotation;
        socket.localScale = Vector3.one;
        return socket;
    }

    private static void Rename(Transform socket, string name)
    {
        if (socket != null) socket.name = name;
    }

    private static void Link(WeaponEquipper equipper, Transform right, Transform left)
    {
        if (equipper == null) return;

        var serialized = new SerializedObject(equipper);
        if (right != null) serialized.FindProperty("rightHandSocket").objectReferenceValue = right;
        if (left != null) serialized.FindProperty("leftHandSocket").objectReferenceValue = left;
        serialized.ApplyModifiedProperties();
    }

    // 손 IK는 Animator가 붙은 GameObject에 있어야 한다 — OnAnimatorIK는 거기로만 온다.
    private static void AttachHandIK(Animator animator, WeaponEquipper equipper)
    {
        if (animator == null) return;

        var ik = animator.GetComponent<WeaponHandIK>();
        if (ik == null) ik = animator.gameObject.AddComponent<WeaponHandIK>();
        if (equipper == null) return;

        var serialized = new SerializedObject(ik);
        serialized.FindProperty("equipment").objectReferenceValue = equipper;
        serialized.ApplyModifiedProperties();
    }

    // IK Pass가 꺼져 있으면 OnAnimatorIK 자체가 호출되지 않는다. 무기 컨트롤러는 전부
    // 같은 베이스에서 갈라져 나오므로, 베이스 레이어 하나만 켜면 무기를 바꿔도 유지된다.
    private static void EnableIKPass(Animator animator)
    {
        if (animator == null) return;
        EnableIKPass(animator.runtimeAnimatorController);

        WeaponAnimationLibrary library = Resources.Load<WeaponAnimationLibrary>(WeaponAnimationLibrary.ResourceName);
        if (library == null) return;

        for (int i = 0; i < library.entries.Count; i++)
        {
            WeaponAnimationLibrary.Entry entry = library.entries[i];
            if (entry != null) EnableIKPass(entry.controller);
        }
    }

    private static void EnableIKPass(RuntimeAnimatorController runtime)
    {
        var overrides = runtime as AnimatorOverrideController;
        if (overrides != null) runtime = overrides.runtimeAnimatorController;

        var controller = runtime as AnimatorController;
        if (controller == null || controller.layers.Length == 0 || controller.layers[0].iKPass) return;

        // layers는 복사본을 돌려주므로 고친 배열을 다시 넣어야 저장된다.
        AnimatorControllerLayer[] layers = controller.layers;
        layers[0].iKPass = true;
        controller.layers = layers;
        EditorUtility.SetDirty(controller);

        Debug.Log("[WeaponSocketBuilder] " + controller.name + ": 베이스 레이어의 IK Pass를 켰다.", controller);
    }

    // 소켓을 손바닥 쪽으로 얼마나 밀지는 캐릭터가 들고 있다. 없으면 기본값.
    private static float PalmGripRatio(GameObject go)
    {
        var equipper = go.GetComponentInChildren<WeaponEquipper>();
        return equipper != null ? equipper.PalmGripRatio : HandSocket.DefaultPalmGripRatio;
    }

    private static string RelativePath(Transform root, Transform target)
    {
        if (target == null || target == root) return null;

        string path = target.name;
        for (Transform t = target.parent; t != null && t != root; t = t.parent) path = t.name + "/" + path;
        return path;
    }
}
