using System.IO;
using UnityEditor;
using UnityEngine;

// 아트 팩에서 온 무기 모델을 "손에 그대로 붙는 무기 프리팹"으로 구워 내는 도구.
//
// 예전에는 무기마다 위치/회전 보정값을 WeaponDefinition에 적어 두고 장착할 때 코드가 밀어 넣었다.
// 그래서 손 위치를 한 번 손보면 스물몇 자루를 전부 다시 맞춰야 했고,
// 어떤 값이 어느 손을 기준으로 맞춘 것인지도 알 수 없었다.
//
// 이제 보정값은 프리팹 안에만 있다. 여기서 굽는 프리팹의 생김새는 언제나 같다.
//
//   Sword_1 (루트 = Grip Point. 소켓 아래에 위치 0 / 회전 0으로 들어간다)
//   ├ GripPoint      (주손이 잡는 지점 표식. 눈으로 확인하고 옮기는 용도)
//   ├ SecondaryGrip  (양손 무기만 — 반대 손이 IK로 따라갈 지점)
//   ├ StringRest     (활만 — 시위가 풀린 자리)
//   ├ StringDraw     (활만 — 끝까지 당겼을 때 자리)
//   └ Model          (아트 팩 프리팹을 그대로 품은 자식. 밀고 돌려 그립을 루트에 맞춰 둔 상태)
//
// 모델마다 피벗이 자루에 있기도, 한가운데 있기도, 칼끝에 있기도 해서 그냥 붙이면 전부 다른 데서 튀어나온다.
// 그래서 메시 정점을 직접 읽어 "가는 쪽이 자루"라는 사실로 자루 위치와 날 방향을 찾아 굽는다.
// 완벽하진 않지만 맨손으로 맞추는 것보다 훨씬 가까운 지점에서 시작할 수 있고,
// 마음에 안 들면 프리팹을 열어 표식을 옮기면 된다(그립은 Align Grip Point, 나머지는 옮기는 즉시 반영).
public static class WeaponGripBaker
{
    public const string OutputFolder = "Assets/Equipment/Weapons";
    public const string ModelChildName = "Model";
    public const string GripPointName = "GripPoint";
    public const string SecondaryGripName = "SecondaryGrip";
    public const string StringRestName = "StringRest";
    public const string StringDrawName = "StringDraw";

    // 활에 물려 둔 화살. 시위가 어디 있는지는 이 화살이 알려 준다.
    private const string NockedArrowName = "NockedArrow";

    // 시위를 끝까지 당겼을 때 물러나는 거리(m). 표식을 처음 놓을 때만 쓰는 값이고,
    // 그 뒤로는 프리팹의 StringDraw가 그 자리를 들고 있다.
    private const float DefaultDrawLength = 0.3f;

    // 이 프리팹이 이미 손에 맞춰 구워 둔 무기인가. 아트 팩에서 온 원본과 구별하는 유일한 표시다.
    public static bool IsGripPrefab(GameObject prefab)
    {
        return prefab != null && prefab.GetComponent<WeaponGrip>() != null;
    }

    // ------------------------------------------------------------------
    // 굽기
    // ------------------------------------------------------------------

    // 정의가 아직 아트 팩 프리팹을 가리키고 있으면 그립 프리팹으로 감싸 물려 준다.
    // 이미 그립 프리팹이면 손대지 않는다 — 손으로 다듬어 둔 자세를 덮어쓰면 안 된다.
    public static bool EnsureGripPrefab(WeaponDefinition definition)
    {
        if (definition == null || definition.model == null) return false;
        if (IsGripPrefab(definition.model)) return false;

        Vector3 position;
        Quaternion rotation;
        MeasureGrip(definition.model, definition.type, out position, out rotation);

        EnsureFolder(OutputFolder);

        GameObject root = BuildRoot(definition.name, definition.model, position, rotation, 1f);
        AuthorHandPoints(root, definition);

        string path = OutputFolder + "/" + definition.name + ".prefab";
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        if (saved == null) return false;

        definition.model = saved;
        EditorUtility.SetDirty(definition);
        return true;
    }

    // 무기 프리팹 한 벌을 메모리에 세운다. 저장은 부르는 쪽이 한다.
    private static GameObject BuildRoot(string weaponName, GameObject sourceModel,
                                        Vector3 modelPosition, Quaternion modelRotation, float modelScale)
    {
        var root = new GameObject(weaponName);
        var grip = root.AddComponent<WeaponGrip>();

        Transform gripPoint = NewChild(root.transform, GripPointName);

        var model = (GameObject)PrefabUtility.InstantiatePrefab(sourceModel);
        model.name = ModelChildName;
        model.transform.SetParent(root.transform, false);
        model.transform.localPosition = modelPosition;
        model.transform.localRotation = modelRotation;
        model.transform.localScale = Vector3.one * Mathf.Max(0.0001f, modelScale);

        grip.Bind(gripPoint, model.transform);
        return root;
    }

    // ------------------------------------------------------------------
    // 손 · 보조 그립 · 시위
    // ------------------------------------------------------------------

    // 이 무기를 어느 손이 들고, 남은 손은 어디를 잡는지 프리팹에 적어 넣는다.
    //
    // 손은 매번 다시 적는다(값 하나라 다시 계산해도 잃을 게 없다).
    // 표식은 없을 때만 만든다 — 손으로 옮겨 둔 자리를 이 메뉴가 되돌리면 곤란하다.
    // 자동 계산으로 되돌리고 싶으면 그 표식을 지우고 다시 실행하면 된다.
    public static bool AuthorHandPoints(GameObject contents, WeaponDefinition definition)
    {
        var grip = contents.GetComponent<WeaponGrip>();
        if (grip == null) return false;

        grip.BindHand(HandOf(definition));

        // 모델이 뒤집혀 들어와 그립이 무기 밖에 잡힌 경우를 먼저 바로잡는다.
        // 표식은 모델 위의 지점이라, 모델이 움직였으면 근거를 잃으므로 지우고 다시 놓는다.
        if (RepairGripOffWeapon(contents, grip))
        {
            DestroyChild(contents.transform, SecondaryGripName);
            DestroyChild(contents.transform, StringRestName);
            DestroyChild(contents.transform, StringDrawName);
        }

        WeaponType type = definition != null ? definition.type : WeaponType.SwordOneHand;
        AuthorSecondaryGrip(contents, grip, type);
        AuthorBowString(contents, grip, type);

        EditorUtility.SetDirty(grip);
        return true;
    }

    // 활과 방패는 왼손, 나머지는 오른손. 왼손잡이 무기를 만들고 싶으면 프리팹에서 직접 바꾸면 된다.
    private static EquipHand HandOf(WeaponDefinition definition)
    {
        if (definition == null) return EquipHand.Right;
        if (definition.type == WeaponType.Bow) return EquipHand.Left;
        return definition.slot == EquipSlot.OffHand ? EquipHand.Left : EquipHand.Right;
    }

    // 그립(루트 원점)은 무기 몸통 위에 있어야 한다. 손이 잡는 자리니까 당연한 이야기지만,
    // 자동 계산이 무기의 머리 방향을 거꾸로 짚으면 자루가 아니라 허공이 잡힌다 —
    // 실제로 양손검과 플랑베르주가 손보다 1.3m 위에 떠 있었다.
    //
    // 판정은 눈이 아니라 숫자로 한다. 그립이 모델의 축 구간 밖에 있으면 뒤집힌 것이고,
    // 반 바퀴 돌려 구간 안으로 들어오면 그게 맞는 자세다. 돌려도 여전히 밖이면 손대지 않고 알린다.
    private static bool RepairGripOffWeapon(GameObject contents, WeaponGrip grip)
    {
        Transform model = grip.Model;
        if (model == null) return false;

        float min, max;
        MeasureGripAxisExtent(contents, out min, out max);
        if (min <= 0f && max >= 0f) return false;

        Quaternion before = model.localRotation;
        model.localRotation = Quaternion.Euler(0f, 0f, 180f) * before;

        MeasureGripAxisExtent(contents, out min, out max);
        if (min > 0f || max < 0f)
        {
            model.localRotation = before;
            Debug.LogWarning("[WeaponGripBaker] " + contents.name + ": 그립이 무기 밖(Y " +
                             min.ToString("F2") + "~" + max.ToString("F2") + ")에 잡혀 있는데 뒤집어도 맞지 않는다. " +
                             "프리팹을 열어 Model을 직접 옮겨야 한다.");
            return false;
        }

        Debug.Log("[WeaponGripBaker] " + contents.name + ": 그립이 무기 밖에 있어 모델을 반 바퀴 돌렸다.");
        return true;
    }

    private static void AuthorSecondaryGrip(GameObject contents, WeaponGrip grip, WeaponType type)
    {
        float offset = SecondaryGripOffset(type);
        Transform existing = contents.transform.Find(SecondaryGripName);

        if (offset == 0f)
        {
            // 한손 무기인데 표식이 남아 있으면 반대 손이 공연히 끌려간다.
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            grip.BindSecondaryGrip(null);
            return;
        }

        bool created = existing == null;
        if (created)
        {
            existing = NewChild(contents.transform, SecondaryGripName);

            // 자루를 벗어난 자리를 잡으면 손이 허공을 쥔다. 모델이 실제로 뻗어 있는 구간 안으로 물린다.
            float min, max;
            MeasureGripAxisExtent(contents, out min, out max);
            float y = Mathf.Clamp(offset, min + 0.03f, Mathf.Max(min + 0.03f, max - 0.10f));

            existing.localPosition = new Vector3(0f, y, 0f);
            existing.localRotation = Quaternion.identity;
        }

        grip.BindSecondaryGrip(existing);
    }

    // 두 손으로 드는 무기에서 반대 손이 잡는 지점. 주손 그립에서 자루를 따라 얼마나 떨어져 있는가(m).
    // 양손검은 폼멜 쪽(아래), 장병기는 날 쪽(위) — 창을 찌를 때 앞손이 위로 가는 것과 같은 이치다.
    // 활은 반대 손이 자루가 아니라 시위를 잡으므로 여기 해당하지 않는다.
    private static float SecondaryGripOffset(WeaponType type)
    {
        switch (type)
        {
            case WeaponType.SwordTwoHand: return -0.14f;
            case WeaponType.Spear:
            case WeaponType.Polearm: return 0.32f;
            default: return 0f;
        }
    }

    // 시위가 오가는 구간을 활에 적어 넣는다. 기준은 활에 물려 둔 화살이다 —
    // 화살의 오늬(뒤끝)가 곧 시위이고, 화살이 향한 쪽이 곧 날아갈 방향이다.
    private static void AuthorBowString(GameObject contents, WeaponGrip grip, WeaponType type)
    {
        if (type != WeaponType.Bow)
        {
            grip.BindBowString(null, null);
            return;
        }

        Transform arrow = FindDeep(contents.transform, NockedArrowName);
        if (arrow == null)
        {
            Debug.LogWarning("[WeaponGripBaker] " + contents.name + ": 활인데 " + NockedArrowName +
                             " 이 없어 시위를 잡을 수 없다. 화살을 활 모델 안에 넣어야 한다.");
            return;
        }

        Transform rest = contents.transform.Find(StringRestName);
        Transform draw = contents.transform.Find(StringDrawName);
        bool created = rest == null || draw == null;

        if (rest == null) rest = NewChild(contents.transform, StringRestName);
        if (draw == null) draw = NewChild(contents.transform, StringDrawName);

        if (created)
        {
            rest.SetPositionAndRotation(ArrowTail(arrow), arrow.rotation);
            draw.SetPositionAndRotation(rest.position - arrow.forward * DefaultDrawLength, arrow.rotation);
        }

        grip.BindBowString(rest, draw);
    }

    // 화살의 오늬 자리. 화살은 제 몸 한가운데를 원점으로 놓여 있어서 뒤끝을 따로 재야 한다.
    private static Vector3 ArrowTail(Transform arrow)
    {
        var renderer = arrow.GetComponentInChildren<Renderer>();
        Bounds bounds;
        if (renderer == null || !TryGetMeshBounds(renderer, out bounds)) return arrow.position;

        var tail = new Vector3(0f, 0f, bounds.center.z - bounds.extents.z);
        return renderer.transform.TransformPoint(tail);
    }

    // 렌더러가 실제로 그리는 메시의 바운즈.
    //
    // Renderer.localBounds를 쓰면 안 된다. 그것은 월드 AABB를 로컬 좌표로 되돌린 상자라,
    // 돌려 놓은 모델에서는 메시와 전혀 다른 크기가 나온다(양손검은 자루 끝이 1.3m 위로 잡혔다).
    private static bool TryGetMeshBounds(Renderer renderer, out Bounds bounds)
    {
        bounds = new Bounds();

        var filter = renderer.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            bounds = filter.sharedMesh.bounds;
            return true;
        }

        var skinned = renderer as SkinnedMeshRenderer;
        if (skinned != null && skinned.sharedMesh != null)
        {
            bounds = skinned.sharedMesh.bounds;
            return true;
        }
        return false;
    }

    // 무기가 그립 축(+Y)을 따라 어디부터 어디까지 뻗어 있는지. 보조 그립을 자루 안에 물리는 데 쓴다.
    private static void MeasureGripAxisExtent(GameObject contents, out float min, out float max)
    {
        min = 0f;
        max = 0f;
        bool any = false;

        foreach (Renderer renderer in contents.GetComponentsInChildren<Renderer>(true))
        {
            Bounds local;
            if (!TryGetMeshBounds(renderer, out local)) continue;

            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y,
                    (corner & 4) == 0 ? local.min.z : local.max.z);

                float y = contents.transform.InverseTransformPoint(renderer.transform.TransformPoint(point)).y;
                if (!any) { min = max = y; any = true; continue; }
                min = Mathf.Min(min, y);
                max = Mathf.Max(max, y);
            }
        }
    }

    private static Transform NewChild(Transform parent, string name)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
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
    // 메뉴
    // ------------------------------------------------------------------

    // 모든 무기 프리팹에 손 · 보조 그립 · 시위 표식을 채운다.
    // 이미 있는 표식은 그대로 두므로 여러 번 실행해도 손으로 맞춰 둔 자리는 남는다.
    [MenuItem("PickMeUp/Equipment/Author Hand Points (All Weapons)")]
    public static void AuthorAllHandPoints()
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:WeaponDefinition"))
        {
            var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (definition == null || !IsGripPrefab(definition.model)) continue;

            string path = AssetDatabase.GetAssetPath(definition.model);
            WeaponDefinition captured = definition;
            if (EditPrefab(path, contents => AuthorHandPoints(contents, captured))) count++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[WeaponGripBaker] " + count + "자루에 손 · 보조 그립 · 시위 표식을 채웠다.");
    }

    // 손으로 그립을 다시 잡는 길. 프리팹을 열어 GripPoint를 자루의 원하는 지점으로 옮긴 뒤 실행하면,
    // 그 차이만큼 모델이 반대로 밀려나고 GripPoint는 루트로 돌아온다.
    // 즉 어긋난 값은 언제나 프리팹 안에서 정리되고, 장착 코드는 아무것도 몰라도 된다.
    [MenuItem("PickMeUp/Equipment/Align Grip Point (Selected)")]
    public static void AlignSelected()
    {
        int count = 0;
        foreach (Object o in Selection.objects)
        {
            var go = o as GameObject;
            if (go == null) continue;

            string path = AssetDatabase.GetAssetPath(go);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
            {
                if (EditPrefab(path, Align)) count++;
                continue;
            }

            // 프리팹 스테이지나 씬에서 고른 경우. 그 자리에서 바로 맞춘다.
            var grip = go.GetComponentInParent<WeaponGrip>();
            if (grip == null) continue;

            Undo.RegisterFullObjectHierarchyUndo(grip.gameObject, "Align Grip Point");
            if (Align(grip.gameObject)) count++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[WeaponGripBaker] " + count + "자루의 그립을 루트에 맞췄다.");
    }

    [MenuItem("PickMeUp/Equipment/Align Grip Point (Selected)", true)]
    private static bool AlignSelectedValidate() => Selection.objects.Length > 0;

    // 손으로 옮겨 놓은 무기의 자세를 다시 자동 계산으로 되돌리고 싶을 때.
    // 무기 정의든 무기 프리팹이든 골라 두면 된다. 표식도 모델을 따라 다시 놓인다.
    [MenuItem("PickMeUp/Equipment/Recompute Grip (Selected)")]
    public static void RecomputeSelected()
    {
        int count = 0;
        foreach (Object o in Selection.objects)
        {
            GameObject prefab = o as GameObject;
            var definition = o as WeaponDefinition;
            if (definition != null) prefab = definition.model;
            else if (prefab != null) definition = FindDefinition(prefab);

            if (prefab == null || definition == null) continue;
            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab")) continue;

            WeaponDefinition captured = definition;
            if (EditPrefab(path, contents => Remeasure(contents, captured))) count++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[WeaponGripBaker] " + count + "자루의 쥐는 자세를 다시 계산했다.");
    }

    [MenuItem("PickMeUp/Equipment/Recompute Grip (Selected)", true)]
    private static bool RecomputeSelectedValidate() => Selection.objects.Length > 0;

    // ------------------------------------------------------------------
    // 프리팹 손질
    // ------------------------------------------------------------------

    private static bool EditPrefab(string path, System.Func<GameObject, bool> edit)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (!edit(contents)) return false;

            PrefabUtility.SaveAsPrefabAsset(contents, path);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // GripPoint가 루트에서 벗어난 만큼 나머지 자식을 반대로 민다.
    // 모델만이 아니라 표식도 같이 밀린다 — 표식은 모델 위의 한 지점이므로 함께 움직여야 제자리다.
    private static bool Align(GameObject contents)
    {
        var grip = contents.GetComponent<WeaponGrip>();
        if (grip == null) return false;

        Transform gripPoint = grip.GripPoint;
        if (gripPoint == contents.transform || grip.IsAligned) return false;

        Vector3 offsetPosition = gripPoint.localPosition;
        Quaternion inverse = Quaternion.Inverse(gripPoint.localRotation);

        for (int i = 0; i < contents.transform.childCount; i++)
        {
            Transform child = contents.transform.GetChild(i);
            if (child == gripPoint) continue;

            child.localPosition = inverse * (child.localPosition - offsetPosition);
            child.localRotation = inverse * child.localRotation;
        }

        gripPoint.localPosition = Vector3.zero;
        gripPoint.localRotation = Quaternion.identity;
        return true;
    }

    // 메시를 다시 재어 Model 자식의 자세를 새로 쓰고, 표식도 새 자세에 맞춰 다시 놓는다.
    private static bool Remeasure(GameObject contents, WeaponDefinition definition)
    {
        var grip = contents.GetComponent<WeaponGrip>();
        Transform model = grip != null ? grip.Model : contents.transform.Find(ModelChildName);
        if (model == null) return false;

        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject);
        if (source == null) source = model.gameObject;

        Vector3 position;
        Quaternion rotation;
        if (!MeasureGrip(source, definition.type, out position, out rotation)) return false;

        model.localPosition = position;
        model.localRotation = rotation;
        model.localScale = Vector3.one;

        Transform gripPoint = grip != null ? grip.GripPoint : null;
        if (gripPoint != null && gripPoint != contents.transform)
        {
            gripPoint.localPosition = Vector3.zero;
            gripPoint.localRotation = Quaternion.identity;
        }

        // 표식은 모델 위의 지점이라 모델이 움직였으면 근거를 잃는다. 지우고 다시 놓는다.
        DestroyChild(contents.transform, SecondaryGripName);
        DestroyChild(contents.transform, StringRestName);
        DestroyChild(contents.transform, StringDrawName);
        AuthorHandPoints(contents, definition);
        return true;
    }

    private static void DestroyChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) Object.DestroyImmediate(child.gameObject);
    }

    // 무기 프리팹만 골랐을 때 전투 분류를 알아내는 길. 그 프리팹을 쓰는 정의에서 빌려 온다.
    private static WeaponDefinition FindDefinition(GameObject prefab)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:WeaponDefinition"))
        {
            var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (definition != null && definition.model == prefab) return definition;
        }
        return null;
    }

    // ------------------------------------------------------------------
    // 메시 → 쥐는 자세
    //
    // 소켓은 +Y가 날이 뻗는 방향, +Z가 칼날 면의 법선이다(HandSocket 참고).
    // 여기서 할 일은 모델을 그 방향으로 세우고, 자루가 루트 원점에 오도록 밀어 넣는 것.
    // ------------------------------------------------------------------

    public static bool MeasureGrip(GameObject model, WeaponType type, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (model == null) return false;

        Mesh mesh = FindMesh(model);
        if (mesh == null) return false;

        Bounds bounds = mesh.bounds;

        if (type == WeaponType.Shield)
        {
            // 방패는 자루가 없다. 가장 얇은 축이 방패 면의 법선이니 그쪽을 손등 바깥(소켓 +Z)으로 돌린다.
            Vector3 thin = ThinnestAxis(bounds.size);
            Quaternion flat = Quaternion.FromToRotation(thin, Vector3.forward);
            rotation = flat;
            position = -(flat * bounds.center);
            return true;
        }

        // 긴 축이 무기의 축이다. 이 팩은 전부 Y지만 다른 팩을 넣어도 되도록 재어 본다.
        int axis = LongestAxis(bounds.size);
        bool headIsPositive = FindHeadDirection(mesh, axis);

        // 날이 축의 반대쪽을 향해 있으면 뒤집어야 소켓 +Y와 맞는다.
        Quaternion align = AlignToUp(axis, headIsPositive);
        rotation = align;

        float gripAlong = FindGripPoint(mesh, axis, headIsPositive, type);
        Vector3 gripPoint = CrossSectionCenter(mesh, axis, gripAlong);
        gripPoint[axis] = gripAlong;

        position = -(align * gripPoint);
        return true;
    }

    private static Mesh FindMesh(GameObject model)
    {
        var filter = model.GetComponentInChildren<MeshFilter>();
        if (filter != null && filter.sharedMesh != null) return filter.sharedMesh;

        var skinned = model.GetComponentInChildren<SkinnedMeshRenderer>();
        return skinned != null ? skinned.sharedMesh : null;
    }

    private static int LongestAxis(Vector3 size)
    {
        if (size.y >= size.x && size.y >= size.z) return 1;
        return size.x >= size.z ? 0 : 2;
    }

    private static Vector3 ThinnestAxis(Vector3 size)
    {
        if (size.x <= size.y && size.x <= size.z) return Vector3.right;
        return size.y <= size.z ? Vector3.up : Vector3.forward;
    }

    // 모델 축을 소켓 +Y로 세운다.
    private static Quaternion AlignToUp(int axis, bool headIsPositive)
    {
        if (axis == 1)
        {
            // 이미 세로로 서 있다. 방향이 맞으면 그대로, 거꾸로면 제 축을 중심으로 반 바퀴 돌린다.
            // 옆으로 눕히지 않아야 칼날의 넓은 면이 손바닥과 나란한 채로 남는다.
            return headIsPositive ? Quaternion.identity : Quaternion.Euler(0f, 0f, 180f);
        }

        Vector3 head = axis == 0 ? Vector3.right : Vector3.forward;
        if (!headIsPositive) head = -head;
        return Quaternion.FromToRotation(head, Vector3.up);
    }

    // 자루 쪽은 가늘고 머리(날) 쪽은 두껍다. 축을 따라 잘라 보고 굵은 쪽을 머리로 본다.
    private static bool FindHeadDirection(Mesh mesh, int axis)
    {
        const int slices = 20;
        float[] radius = SliceRadii(mesh, axis, slices);

        // 양 끝 25%씩만 비교한다. 가운데는 어느 무기든 비슷해서 판단에 도움이 안 된다.
        int edge = Mathf.Max(1, slices / 4);
        float low = 0f;
        float high = 0f;
        for (int i = 0; i < edge; i++)
        {
            low += radius[i];
            high += radius[slices - 1 - i];
        }
        return high >= low;
    }

    // 축을 따라 자른 단면의 평균 반지름.
    private static float[] SliceRadii(Mesh mesh, int axis, int slices)
    {
        Vector3[] vertices = mesh.vertices;
        Bounds bounds = mesh.bounds;
        float min = bounds.min[axis];
        float length = Mathf.Max(0.0001f, bounds.size[axis]);

        var sum = new float[slices];
        var count = new int[slices];
        int a = (axis + 1) % 3;
        int b = (axis + 2) % 3;

        for (int i = 0; i < vertices.Length; i++)
        {
            int s = Mathf.Clamp((int)((vertices[i][axis] - min) / length * slices), 0, slices - 1);
            sum[s] += Mathf.Sqrt(vertices[i][a] * vertices[i][a] + vertices[i][b] * vertices[i][b]);
            count[s]++;
        }

        var radius = new float[slices];
        for (int i = 0; i < slices; i++) radius[i] = count[i] > 0 ? sum[i] / count[i] : 0f;
        return radius;
    }

    // 손이 놓일 지점(모델 로컬 좌표의 축 값).
    private static float FindGripPoint(Mesh mesh, int axis, bool headIsPositive, WeaponType type)
    {
        const int slices = 20;
        float[] radius = SliceRadii(mesh, axis, slices);
        Bounds bounds = mesh.bounds;
        float length = bounds.size[axis];
        float buttEnd = headIsPositive ? bounds.min[axis] : bounds.max[axis];
        float toHead = headIsPositive ? 1f : -1f;

        float max = 0f;
        for (int i = 0; i < slices; i++) max = Mathf.Max(max, radius[i]);

        // 손잡이 끝에서 시작해 굵어지기 직전까지가 자루다.
        float haft = 0f;
        for (int i = 0; i < slices; i++)
        {
            int s = headIsPositive ? i : slices - 1 - i;
            if (radius[s] > max * 0.5f) break;
            haft += length / slices;
        }

        switch (type)
        {
            // 장병기는 자루 전체가 가늘어서 위 계산이 무기 길이 전부를 자루로 본다.
            // 한 손으로 들 때 실제로 잡는 지점은 밑동에서 1/3쯤이다.
            case WeaponType.Spear:
            case WeaponType.Polearm:
                return buttEnd + toHead * length * 0.33f;
            case WeaponType.SwordTwoHand:
                return buttEnd + toHead * Mathf.Min(haft * 0.5f, 0.12f);
            default:
                return buttEnd + toHead * Mathf.Min(haft * 0.5f, 0.07f);
        }
    }

    // 자루가 손 한가운데 오도록, 그 높이의 단면 중심을 쓴다.
    // 도끼처럼 날이 한쪽으로 쏠린 무기는 전체 바운즈 중심을 쓰면 자루가 손 밖으로 밀린다.
    private static Vector3 CrossSectionCenter(Mesh mesh, int axis, float along)
    {
        Vector3[] vertices = mesh.vertices;
        float window = Mathf.Max(0.01f, mesh.bounds.size[axis] * 0.05f);

        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (Mathf.Abs(vertices[i][axis] - along) > window) continue;
            sum += vertices[i];
            count++;
        }

        return count > 0 ? sum / count : mesh.bounds.center;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
