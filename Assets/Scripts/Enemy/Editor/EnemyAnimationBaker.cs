using System.Collections.Generic;
using UnityEditor;
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
    // 어느 클립을 어느 동작으로 쓸지. 애니메이터의 클립 이름과 맞춰 둔다.
    private static readonly (EnemyClip clip, string clipName)[] Wanted =
    {
        (EnemyClip.Idle, "CombatIdle"),
        (EnemyClip.Run, "Run"),
        (EnemyClip.Attack, "Attack1"),
        (EnemyClip.Hit, "Hit"),
        (EnemyClip.Stagger, "Stagger"),
        (EnemyClip.Death, "Death"),
    };

    // 한 클립에서 뽑는 최대 프레임 수. 텍스처 세로 크기를 정하는 값이라 상한을 둔다 —
    // 대기 클립이 9.93초(298프레임)라 그대로 구우면 나머지 다섯 개를 합친 것보다 커진다.
    private const int MaxFramesPerClip = 64;

    private const int SampleFps = 30;

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

            List<AnimationClip> clips = ResolveClips(animator, out List<EnemyClip> kinds, out List<int> frameCounts);
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

                ranges.Add(new EnemyAnimationLibrary.ClipRange
                {
                    clip = kinds[c],
                    startFrame = row,
                    frameCount = frames,
                    length = clip.length,
                });

                for (int f = 0; f < frames; f++)
                {
                    // 마지막 프레임이 클립 끝에 정확히 닿도록 나눈다.
                    float t = frames > 1 ? clip.length * f / (frames - 1) : 0f;
                    clip.SampleAnimation(instance, t);

                    for (int b = 0; b < boneCount; b++)
                    {
                        // 스키닝 행렬 = (루트 기준 뼈 위치) x (바인드 포즈).
                        // 루트 기준으로 잡아야 엔티티의 위치·회전과 곱했을 때 제자리에 선다.
                        Matrix4x4 matrix = root.worldToLocalMatrix * renderer.bones[b].localToWorldMatrix * bindPoses[b];

                        texture.SetPixel(b * 3 + 0, row, new Color(matrix.m00, matrix.m01, matrix.m02, matrix.m03));
                        texture.SetPixel(b * 3 + 1, row, new Color(matrix.m10, matrix.m11, matrix.m12, matrix.m13));
                        texture.SetPixel(b * 3 + 2, row, new Color(matrix.m20, matrix.m21, matrix.m22, matrix.m23));
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

    private static List<AnimationClip> ResolveClips(Animator animator, out List<EnemyClip> kinds, out List<int> frameCounts)
    {
        var clips = new List<AnimationClip>();
        kinds = new List<EnemyClip>();
        frameCounts = new List<int>();

        AnimationClip[] all = animator.runtimeAnimatorController.animationClips;

        foreach ((EnemyClip clip, string clipName) in Wanted)
        {
            AnimationClip found = null;
            foreach (AnimationClip candidate in all)
            {
                if (candidate != null && candidate.name == clipName) { found = candidate; break; }
            }

            if (found == null)
            {
                Debug.LogWarning($"[EnemyAnimationBaker] 클립 '{clipName}'을 찾지 못해 건너뜁니다.");
                continue;
            }

            int frames = Mathf.Clamp(Mathf.RoundToInt(found.length * SampleFps), 2, MaxFramesPerClip);
            clips.Add(found);
            kinds.Add(clip);
            frameCounts.Add(frames);
        }

        return clips;
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

        Debug.Log($"[EnemyAnimationBaker] {prefabName}: 뼈 {boneCount}개, 클립 {ranges.Count}개, " +
                  $"텍스처 {texture.width}x{texture.height} → {path}");

        return library;
    }
}
