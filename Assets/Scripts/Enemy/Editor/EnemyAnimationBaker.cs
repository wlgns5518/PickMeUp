using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 적 애니메이션을 텍스처로 굽는 에디터 도구.
//
// 엔티티에는 Animator가 없으니 뼈를 실시간으로 돌릴 수 없다. 대신 여기서 미리 돌려 놓는다:
// 클립을 프레임 단위로 샘플링하면서 각 뼈의 스키닝 행렬을 텍스처 한 줄에 적고, 셰이더가
// 그 줄을 읽어 정점을 옮긴다(EnemyGpuSkin.shader).
//
// 함께 만드는 것이 하나 더 있다. 일반 MeshRenderer로 그리면 정점 셰이더에 뼈 번호와
// 가중치가 오지 않으므로, 메시를 복제해 그 둘을 UV2/UV3에 실어 둔다.
public static class EnemyAnimationBaker
{
    // 어느 동작을 어느 이름으로 찾을지. 애니메이터의 상태 이름과 맞춰 둔다.
    //
    // loops가 붙은 것은 끝에서 처음으로 이어 붙여 도는 클립이다. 그 셋만 제자리로 만든다
    // (StripRootMotion) — 나머지는 한 번만 재생하므로 앞으로 나아가는 것이 동작의 일부다
    // (쓰러지며 앞으로 무너지는 Death, 내지르며 파고드는 Attack7).
    private static readonly (EnemyClip clip, string stateName, bool loops)[] Wanted =
    {
        (EnemyClip.Idle, "CombatIdle", true),
        (EnemyClip.Walk, "Walk", true),
        (EnemyClip.Run, "Run", true),
        (EnemyClip.Attack, "Attack1", false),
        (EnemyClip.Hit, "Hit", false),
        (EnemyClip.Stagger, "Stagger", false),
        (EnemyClip.Death, "Death", false),

        // 콤보 2~7단. 없는 클립은 ResolveClips가 조용히 건너뛰므로, 리그마다 단수가 달라도 된다.
        (EnemyClip.Attack2, "Attack2", false),
        (EnemyClip.Attack3, "Attack3", false),
        (EnemyClip.Attack4, "Attack4", false),
        (EnemyClip.Attack5, "Attack5", false),
        (EnemyClip.Attack6, "Attack6", false),
        (EnemyClip.Attack7, "Attack7", false),

        (EnemyClip.Kick, "Kick", false),
        (EnemyClip.LeapAttack, "LeapAttack", false),
        (EnemyClip.Bite, "Bite", false),

        (EnemyClip.HitFront, "HitFront", false),
        (EnemyClip.HitBack, "HitBack", false),
        (EnemyClip.HitLeft, "HitLeft", false),
        (EnemyClip.HitRight, "HitRight", false),
    };

    // 한 클립에서 뽑는 최대 프레임 수. 텍스처 세로 크기를 정하는 값이라 상한을 둔다 —
    // 대기 클립이 9.93초(298프레임)라 그대로 구우면 나머지 다섯 개를 합친 것보다 커진다.
    private const int MaxFramesPerClip = 128;

    // 클립을 몇 fps로 뽑을 것인가.
    //
    // 30이면 화면이 60Hz일 때 한 줄이 두 프레임씩 버텨서 적만 뚝뚝 끊겨 보인다. 아군은 Animator가
    // 사이를 메워 주므로 나란히 놓으면 차이가 그대로 드러난다. 특히 제자리걸음과 달리기가 그렇다 —
    // 그 둘만 구운 속도 그대로(1.0배) 돌고, 나머지는 게임플레이 시간에 맞춰 눌려서 오히려 줄을
    // 건너뛰기 때문이다.
    //
    // 셰이더에서 줄 사이를 섞는 방법도 있지만 뼈마다 텍스처 조회가 3 → 6으로 늘어난다. 60으로 굽는
    // 쪽은 런타임 비용이 0이고 텍스처만 1.30 → 2.60MB가 된다(138 x 1235, RGBAFloat).
    private const int SampleFps = 60;

    [MenuItem("PickMeUp/적 애니메이션 굽기")]
    private static void BakeSelected()
    {
        GameObject prefab = Selection.activeGameObject;
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("적 애니메이션 굽기",
                "굽고 싶은 캐릭터 프리팹을 프로젝트 창에서 고른 뒤 다시 실행하세요.", "확인");
            return;
        }

        Bake(prefab);
    }

    public static EnemyAnimationLibrary Bake(GameObject prefab)
    {
        GameObject instance = Object.Instantiate(prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;

        try
        {
            var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var animator = instance.GetComponentInChildren<Animator>(true);

            if (renderer == null || renderer.sharedMesh == null)
            {
                Debug.LogError($"[EnemyAnimationBaker] {prefab.name}에 SkinnedMeshRenderer가 없습니다.");
                return null;
            }

            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Debug.LogError($"[EnemyAnimationBaker] {prefab.name}에 Animator 컨트롤러가 없습니다.");
                return null;
            }

            List<AnimationClip> clips = ResolveClips(animator,
                out List<EnemyClip> kinds, out List<int> frameCounts, out List<bool> loops);
            if (clips.Count == 0)
            {
                Debug.LogError($"[EnemyAnimationBaker] {prefab.name}에서 구울 클립을 하나도 찾지 못했습니다.");
                return null;
            }

            int boneCount = renderer.bones.Length;
            int totalFrames = 0;
            foreach (int f in frameCounts) totalFrames += f;

            // 가로 = 뼈 하나당 텍셀 셋(3x4 행렬의 세 줄), 세로 = 모든 클립의 프레임을 이어 붙인 것.
            var texture = new Texture2D(boneCount * 3, totalFrames, TextureFormat.RGBAFloat, false, true)
            {
                name = prefab.name + "_BoneMatrices",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            var ranges = new List<EnemyAnimationLibrary.ClipRange>();
            Matrix4x4[] bindPoses = renderer.sharedMesh.bindposes;
            Transform root = instance.transform;

            int row = 0;
            for (int c = 0; c < clips.Count; c++)
            {
                AnimationClip clip = clips[c];
                int frames = frameCounts[c];

                // 도는 클립이 한 바퀴 동안 앞으로 흘러간 거리. 이걸 걷어내야 제자리에서 돈다.
                Vector3 drift = loops[c] ? MeasureDrift(clip, instance, root, renderer.bones) : Vector3.zero;

                ranges.Add(new EnemyAnimationLibrary.ClipRange
                {
                    clip = kinds[c],
                    startFrame = row,
                    frameCount = frames,
                    length = clip.length,
                    // 걷어낸 거리가 곧 이 클립이 표현하는 이동 속도다. 재생 배속을 여기에 맞춘다.
                    groundSpeed = clip.length > 0.01f ? drift.magnitude / clip.length : 0f,
                });

                for (int f = 0; f < frames; f++)
                {
                    // 마지막 프레임이 클립 끝에 정확히 닿도록 나눈다.
                    float t = frames > 1 ? clip.length * f / (frames - 1) : 0f;
                    clip.SampleAnimation(instance, t);

                    // 흘러간 만큼을 고르게 되돌린다. 첫 프레임과 끝 프레임이 같은 자리에 서므로
                    // 진행도가 한 바퀴를 돌아도 몸이 튀지 않는다.
                    Vector3 offset = frames > 1 ? drift * ((float)f / (frames - 1)) : Vector3.zero;

                    for (int b = 0; b < boneCount; b++)
                    {
                        // 스키닝 행렬 = (루트 기준 뼈 위치) x (바인드 포즈).
                        // 루트 기준으로 잡아야 엔티티의 위치·회전과 곱했을 때 제자리에 선다.
                        Matrix4x4 matrix = root.worldToLocalMatrix * renderer.bones[b].localToWorldMatrix * bindPoses[b];

                        texture.SetPixel(b * 3 + 0, row, new Color(matrix.m00, matrix.m01, matrix.m02, matrix.m03 - offset.x));
                        texture.SetPixel(b * 3 + 1, row, new Color(matrix.m10, matrix.m11, matrix.m12, matrix.m13 - offset.y));
                        texture.SetPixel(b * 3 + 2, row, new Color(matrix.m20, matrix.m21, matrix.m22, matrix.m23 - offset.z));
                    }

                    row++;
                }
            }

            texture.Apply(false, false);

            Mesh skinnedMesh = BuildMeshWithSkinUVs(renderer.sharedMesh, prefab.name);
            Material material = BuildMaterial(renderer.sharedMaterial, texture, prefab.name);

            return SaveAssets(prefab.name, texture, skinnedMesh, material, boneCount, ranges);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static List<AnimationClip> ResolveClips(Animator animator,
        out List<EnemyClip> kinds, out List<int> frameCounts, out List<bool> loops)
    {
        var clips = new List<AnimationClip>();
        kinds = new List<EnemyClip>();
        frameCounts = new List<int>();
        loops = new List<bool>();

        var controller = animator.runtimeAnimatorController as AnimatorController;
        AnimationClip[] all = animator.runtimeAnimatorController.animationClips;

        foreach ((EnemyClip clip, string stateName, bool loop) in Wanted)
        {
            // 상태 이름으로 먼저 찾는다. 상태가 어떤 클립 자산을 물고 있는지는 리그마다 다르다 —
            // 고블린의 Walk는 PlayerWalk.anim을 리타깃해 쓴다. 클립 이름만 보던 시절에는
            // 그런 상태를 통째로 놓쳤다(이름이 'Walk'인 클립이 없으므로).
            AnimationClip found = FindByState(controller, stateName);

            // 컨트롤러를 못 읽었거나(런타임 오버라이드) 그런 이름의 상태가 없으면 클립 이름으로 찾는다.
            if (found == null)
            {
                foreach (AnimationClip candidate in all)
                {
                    if (candidate != null && candidate.name == stateName) { found = candidate; break; }
                }
            }

            if (found == null)
            {
                Debug.LogWarning($"[EnemyAnimationBaker] '{stateName}' 상태도 클립도 찾지 못해 건너뜁니다.");
                continue;
            }

            int frames = Mathf.Clamp(Mathf.RoundToInt(found.length * SampleFps), 2, MaxFramesPerClip);
            clips.Add(found);
            kinds.Add(clip);
            frameCounts.Add(frames);
            loops.Add(loop);
        }

        return clips;
    }

    // 이름이 같은 상태가 물고 있는 클립. 블렌드 트리는 클립 하나로 줄일 수 없으니 건너뛴다.
    private static AnimationClip FindByState(AnimatorController controller, string stateName)
    {
        if (controller == null) return null;

        foreach (AnimatorControllerLayer layer in controller.layers)
        {
            AnimationClip found = FindByState(layer.stateMachine, stateName);
            if (found != null) return found;
        }

        return null;
    }

    private static AnimationClip FindByState(AnimatorStateMachine machine, string stateName)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state == null || child.state.name != stateName) continue;
            return child.state.motion as AnimationClip;
        }

        foreach (ChildAnimatorStateMachine sub in machine.stateMachines)
        {
            AnimationClip found = FindByState(sub.stateMachine, stateName);
            if (found != null) return found;
        }

        return null;
    }

    // 도는 클립이 한 바퀴 동안 수평으로 흘러간 거리.
    //
    // 클립에 루트 모션이 들어 있으면 샘플링한 포즈가 그대로 앞으로 흘러간다. 그 상태로 구우면
    // 몸이 클립 길이만큼 앞으로 미끄러졌다가, 진행도가 한 바퀴를 돌아 처음으로 감기는 그 프레임에
    // 그만큼 뒤로 튄다 — 고블린 Run이 0.867초에 1.447m였고, 배속 1.75배로 돌아 초당 두 번씩
    // 1.4m를 되감았다. "달리다 되감긴다"가 이것이다.
    //
    // 엔티티의 자리는 시뮬레이션이 정하므로(EnemyMovementSystem) 클립은 제자리에서 돌기만 하면 된다.
    // 도는 클립은 첫 프레임과 끝 프레임의 포즈가 같으니, 그 둘의 몸 중심 차이가 곧 흘러간 거리다.
    // 뼈 하나가 아니라 전체 평균을 쓰는 이유는 팔다리가 흔들리는 것에 휘둘리지 않기 위해서다.
    private static Vector3 MeasureDrift(AnimationClip clip, GameObject instance, Transform root, Transform[] bones)
    {
        Vector3 drift = Centroid(clip, instance, root, bones, clip.length) -
                        Centroid(clip, instance, root, bones, 0f);

        // 위아래로 뜨고 가라앉는 것은 걸음의 일부다. 수평으로 흘러가는 것만 걷어낸다.
        drift.y = 0f;
        return drift;
    }

    private static Vector3 Centroid(AnimationClip clip, GameObject instance, Transform root, Transform[] bones, float time)
    {
        clip.SampleAnimation(instance, time);

        Vector3 sum = Vector3.zero;
        for (int b = 0; b < bones.Length; b++) sum += root.InverseTransformPoint(bones[b].position);
        return sum / Mathf.Max(1, bones.Length);
    }

    // 뼈 번호와 가중치를 UV2/UV3에 실어 둔 메시를 만든다.
    //
    // 스킨드 메시가 원래 들고 있는 BoneWeight는 SkinnedMeshRenderer가 쓰는 것이라,
    // 일반 렌더러로 그리면 셰이더까지 오지 않는다. 그래서 UV로 옮겨 싣는다.
    private static Mesh BuildMeshWithSkinUVs(Mesh source, string prefabName)
    {
        var mesh = Object.Instantiate(source);
        mesh.name = prefabName + "_GpuSkin";

        BoneWeight[] weights = source.boneWeights;
        var indices = new List<Vector4>(weights.Length);
        var amounts = new List<Vector4>(weights.Length);

        for (int i = 0; i < weights.Length; i++)
        {
            BoneWeight w = weights[i];
            indices.Add(new Vector4(w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3));
            amounts.Add(new Vector4(w.weight0, w.weight1, w.weight2, w.weight3));
        }

        mesh.SetUVs(2, indices);
        mesh.SetUVs(3, amounts);

        // 스킨 정보를 옮겨 실었으므로 원래 것은 지운다. 남겨 두면 Unity가 이 메시를
        // 여전히 스킨드로 보고 일반 렌더러에서 경고를 낸다.
        mesh.boneWeights = new BoneWeight[0];
        mesh.bindposes = new Matrix4x4[0];

        // 스키닝으로 정점이 바인드 포즈 밖으로 나가므로 경계를 넉넉히 잡는다.
        Bounds bounds = mesh.bounds;
        bounds.Expand(1f);
        mesh.bounds = bounds;

        return mesh;
    }

    private static Material BuildMaterial(Material source, Texture2D boneTexture, string prefabName)
    {
        Shader shader = Shader.Find("PickMeUp/Enemy GPU Skin");
        if (shader == null)
        {
            Debug.LogError("[EnemyAnimationBaker] 'PickMeUp/Enemy GPU Skin' 셰이더를 찾지 못했습니다.");
            return null;
        }

        var material = new Material(shader) { name = prefabName + "_GpuSkin" };

        if (source != null && source.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
        }

        material.SetTexture("_BoneTexture", boneTexture);
        material.SetColor("_BaseColor", Color.white);
        // 엔티티마다 다른 값을 받으려면 인스턴싱이 켜져 있어야 한다.
        material.enableInstancing = true;

        return material;
    }

    private static EnemyAnimationLibrary SaveAssets(string prefabName, Texture2D texture, Mesh mesh,
        Material material, int boneCount, List<EnemyAnimationLibrary.ClipRange> ranges)
    {
        const string folder = "Assets/Enemy/Baked";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder("Assets/Enemy", "Baked");
        }

        string path = $"{folder}/{prefabName}Animation.asset";

        var library = ScriptableObject.CreateInstance<EnemyAnimationLibrary>();
        library.name = prefabName + "Animation";
        library.boneCount = boneCount;
        library.clips = ranges.ToArray();

        AssetDatabase.CreateAsset(library, path);
        AssetDatabase.AddObjectToAsset(texture, library);
        AssetDatabase.AddObjectToAsset(mesh, library);
        if (material != null) AssetDatabase.AddObjectToAsset(material, library);

        library.boneTexture = texture;
        library.skinnedMesh = mesh;
        library.material = material;

        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return library;
    }
}
