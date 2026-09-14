using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// 받아 온 FBX를 "싸울 수 있는 몸"으로 만든다.
//
// 싸울 수 있다는 것의 조건은 딱 셋이다.
//  1. Unity가 그 스켈레톤을 사람으로 읽는다 — 전투 애니메이션이 전부 휴머노이드로 굽혀 있어서,
//     아바타만 서면 나머지는 리타깃이 알아서 한다.
//  2. 두 손에 무기를 걸 자리가 있다 — WeaponSocketBuilder가 손뼈에서 재어 놓는다.
//  3. 전투 부품이 붙어 있다 — UnitController/TargetScanner/UnitEmotion/WeaponEquipper/WeaponHandIK/NavMeshAgent.
//
// 3번은 새로 쓰지 않고 기준 프리팹(Y Bot)에서 통째로 베껴 온다. 값이 두 군데로 갈라지면
// 한쪽만 고치는 날이 반드시 오기 때문이다. 베낄 때 남는 문제는 참조뿐이다 —
// 기준 프리팹 안을 가리키던 필드(Animator, 소켓, WeaponEquipper)는 새 몸의 짝으로 다시 묶는다(Rebind).
public static class CharacterModelBuilder
{
    // 전투 부품을 어디서 베껴 오는가. 이 프리팹의 루트 컴포넌트 구성이 곧 "아군 한 명"의 정의다.
    public const string TemplatePrefabPath = "Assets/Prefabs/Y Bot.prefab";

    [MenuItem("PickMeUp/Character/3D 모델 다시 조립 (받아 둔 파일로)", priority = 22)]
    private static void RebuildSelected()
    {
        int done = 0;
        foreach (Object o in Selection.objects)
        {
            if (!(o is CharacterSO character)) continue;

            string directory = Path.Combine(MeshyModelPipeline.ModelRoot,
                                            MeshyModelPipeline.Sanitize(Name(character))).Replace('\\', '/');
            string stem = MeshyModelPipeline.Sanitize(Name(character));
            string fbx = $"{directory}/{stem}.fbx";

            if (!File.Exists(fbx))
            {
                Debug.LogWarning($"[CharacterModelBuilder] {stem}: 받아 둔 FBX가 없다 — {fbx}", character);
                continue;
            }

            string texture = File.Exists($"{directory}/{stem}_BaseColor.png") ? $"{directory}/{stem}_BaseColor.png" : null;
            string sheet = File.Exists($"{directory}/{stem}_Sheet0.png") ? $"{directory}/{stem}_Sheet0.png" : null;

            if (Build(character, fbx, texture, sheet) != null) done++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[CharacterModelBuilder] {done}명의 몸을 다시 조립했다.");
    }

    [MenuItem("PickMeUp/Character/3D 모델 다시 조립 (받아 둔 파일로)", true)]
    private static bool RebuildSelectedValidate()
    {
        foreach (Object o in Selection.objects) if (o is CharacterSO) return true;
        return false;
    }

    /// FBX → 휴머노이드 아바타 → 재질 → 전투 프리팹 → CharacterSO에 꽂기.
    public static GameObject Build(CharacterSO character, string fbxPath, string texturePath, string sheetPath)
    {
        if (character == null || string.IsNullOrEmpty(fbxPath)) return null;

        Avatar avatar = ImportAsHumanoid(fbxPath);
        if (avatar == null) return null;

        Material material = BuildMaterial(fbxPath, texturePath);
        string prefabPath = AssemblePrefab(character, fbxPath, material);
        if (string.IsNullOrEmpty(prefabPath)) return null;

        // 손뼈에서 재어 두 손에 소켓을 놓고, 손 IK와 Animator의 IK Pass까지 켠다.
        // 여기까지 와야 무기가 손에 잡힌다 — 소켓이 없으면 WeaponEquipper가 런타임에 만들긴 하지만,
        // 프리팹에 박아 두면 에디터에서 눈으로 확인할 수 있고 매번 다시 재지 않는다.
        if (!WeaponSocketBuilder.AddToPrefab(prefabPath))
            Debug.LogWarning($"[CharacterModelBuilder] {Name(character)}: 손 소켓을 놓지 못했다. " +
                             "무기가 손목에 매달릴 수 있다.", character);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        Undo.RecordObject(character, "3D 모델 연결");
        character.battlePrefab = prefab;
        if (!string.IsNullOrEmpty(sheetPath)) character.modelSheetAssetPath = sheetPath;
        EditorUtility.SetDirty(character);

        Debug.Log($"[CharacterModelBuilder] {Name(character)}의 몸을 만들었다: {prefabPath}", prefab);
        return prefab;
    }

    // ── 1. 휴머노이드로 읽히게 하기 ───────────────────────────────────────

    private static Avatar ImportAsHumanoid(string fbxPath)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[CharacterModelBuilder] 모델 임포터를 찾지 못했다: {fbxPath}");
            return null;
        }

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

        // 리깅이 딸려 보낸 걷기/달리기는 쓰지 않는다. 전투 동작 한 벌이 이미 있고,
        // 그쪽은 무기별로 갈라지는 컨트롤러까지 짜여 있다.
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importVisibility = false;

        // 뼈를 지우면 안 된다. 손뼈 아래에 소켓을 매달아야 무기가 손에 잡힌다.
        importer.optimizeGameObjects = false;

        // 재질은 우리가 만든다(BuildMaterial). FBX가 들고 오는 재질은 URP가 아니라 회색으로 뜬다.
        importer.materialImportMode = ModelImporterMaterialImportMode.None;

        importer.SaveAndReimport();

        Avatar avatar = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (o is Avatar candidate) avatar = candidate;

        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"[CharacterModelBuilder] {fbxPath}: 사람으로 읽히지 않았다. " +
                           "이 몸으로는 전투 애니메이션을 리타깃할 수 없다. " +
                           "리깅이 팔다리를 제대로 못 찾았을 수 있으니 전신 시트를 다시 구워라.");
            return null;
        }

        return avatar;
    }

    // ── 2. 재질 ──────────────────────────────────────────────────────────

    // Meshy는 FBX에 텍스처를 품어 보내지 않는다. 따로 받아 둔 베이스 컬러로 URP 재질을 만든다.
    private static Material BuildMaterial(string fbxPath, string texturePath)
    {
        if (string.IsNullOrEmpty(texturePath)) return null;

        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
        {
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        }
        if (texture == null) return null;

        string materialPath = Path.ChangeExtension(fbxPath, null) + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        else
        {
            material.shader = shader;
        }

        // 이름이 갈리는 두 셰이더를 한 번에 덮는다(URP는 _BaseMap, 빌트인은 _MainTex).
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);

        // 생성된 텍스처에는 이미 빛이 어느 정도 구워져 있다. 금속감까지 얹으면 번들거린다.
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);

        EditorUtility.SetDirty(material);
        return material;
    }

    // ── 3. 전투 프리팹 조립 ──────────────────────────────────────────────

    private static string AssemblePrefab(CharacterSO character, string fbxPath, Material material)
    {
        var template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePrefabPath);
        if (template == null)
        {
            Debug.LogError($"[CharacterModelBuilder] 기준 프리팹이 없다: {TemplatePrefabPath}");
            return null;
        }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (model == null) return null;

        string prefabPath = Path.ChangeExtension(fbxPath, null) + ".prefab";

        // 조립은 임시 씬에서 한다. 열려 있는 씬에 세우면 만들었다 지운 흔적만으로도 씬이 더러워져,
        // 굽기를 한 번 돌린 것뿐인데 저장하겠냐는 물음이 뜬다.
        UnityEngine.SceneManagement.Scene workspace = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Additive);

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, workspace);
        try
        {
            // FBX와의 연결을 끊는다. 기준 프리팹(Y Bot)도 같은 방식으로 풀려 있고,
            // 붙어 있으면 컴포넌트를 얹은 것이 전부 "오버라이드"가 되어 다루기 번거롭다.
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = MeshyModelPipeline.Sanitize(Name(character));

            CopyRootComponents(template, instance);
            ApplyMaterial(instance, material);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool ok);
            if (!ok || saved == null)
            {
                Debug.LogError($"[CharacterModelBuilder] 프리팹 저장 실패: {prefabPath}");
                return null;
            }
            return prefabPath;
        }
        finally
        {
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(workspace, true);
        }
    }

    // 기준 프리팹 루트에 붙은 전투 부품을 그대로 옮겨 심는다.
    //
    // Animator만은 값을 통째로 덮으면 안 된다 — 아바타까지 따라와서 새 몸이 Y Bot의 뼈대를 쓰겠다고
    // 나서고, 그러면 아무 자세도 잡히지 않는다. 컨트롤러와 나머지 설정만 가져오고 아바타는 제 것을 남긴다.
    private static void CopyRootComponents(GameObject template, GameObject instance)
    {
        Component[] sources = template.GetComponents<Component>();
        var copied = new List<Component>(sources.Length);

        foreach (Component source in sources)
        {
            if (source == null || source is Transform) continue;

            System.Type type = source.GetType();
            Component target = instance.GetComponent(type);
            if (target == null) target = instance.AddComponent(type);
            if (target == null)
            {
                Debug.LogWarning($"[CharacterModelBuilder] {type.Name}을 붙이지 못했다.");
                continue;
            }

            if (target is Animator animator)
            {
                Avatar own = animator.avatar;
                EditorUtility.CopySerialized(source, target);
                animator.avatar = own;
            }
            else
            {
                EditorUtility.CopySerialized(source, target);
            }

            copied.Add(target);
        }

        foreach (Component target in copied) Rebind(target, template.transform, instance.transform);
    }

    // 베껴 온 값에 남아 있는 "기준 프리팹 안을 가리키는 참조"를 새 몸의 짝으로 바꾼다.
    //
    // 짝을 찾지 못하면 비운다. 대표적으로 무기 소켓이 그렇다 — 기준 프리팹의 소켓은
    // mixamorig:RightHand 아래에 있고 새 몸의 뼈 이름은 다르다. 비워 두면 그 다음 단계
    // (WeaponSocketBuilder)가 새 손뼈에서 다시 재어 놓는다. 엉뚱한 프리팹의 뼈를 계속
    // 가리키는 것보다 비어 있는 편이 낫다 — 남아 있으면 무기가 다른 캐릭터 손에 붙는다.
    private static void Rebind(Component component, Transform templateRoot, Transform newRoot)
    {
        var serialized = new SerializedObject(component);
        SerializedProperty property = serialized.GetIterator();
        bool changed = false;

        while (property.NextVisible(true))
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference) continue;

            Object value = property.objectReferenceValue;
            Transform owner = OwnerOf(value);
            // 에셋(무기 정의, 컨트롤러, 재질...)을 가리키는 참조는 그대로 둔다.
            if (owner == null || !owner.IsChildOf(templateRoot)) continue;

            property.objectReferenceValue = Counterpart(value, owner, templateRoot, newRoot);
            changed = true;
        }

        if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Transform OwnerOf(Object value)
    {
        if (value is Component component) return component.transform;
        if (value is GameObject go) return go.transform;
        return null;
    }

    private static Object Counterpart(Object value, Transform owner, Transform templateRoot, Transform newRoot)
    {
        Transform mirror = owner == templateRoot ? newRoot : newRoot.Find(RelativePath(templateRoot, owner));
        if (mirror == null) return null;

        if (value is GameObject) return mirror.gameObject;
        return mirror.GetComponent(value.GetType());
    }

    private static string RelativePath(Transform root, Transform target)
    {
        string path = target.name;
        for (Transform t = target.parent; t != null && t != root; t = t.parent) path = t.name + "/" + path;
        return path;
    }

    private static void ApplyMaterial(GameObject instance, Material material)
    {
        if (material == null) return;

        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            var materials = new Material[renderer.sharedMaterials.Length == 0 ? 1 : renderer.sharedMaterials.Length];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
        }
    }

    private static string Name(CharacterSO character) =>
        string.IsNullOrEmpty(character.characterName) ? character.name : character.characterName;
}
