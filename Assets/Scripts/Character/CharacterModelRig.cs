using System.Collections.Generic;
using UnityEngine;

// 어디서 굴러온 스켈레톤이든 Unity가 사람으로 읽게 만드는 곳.
//
// 전투 동작 한 벌이 전부 휴머노이드로 굽혀 있어서, 새 몸이 싸울 수 있느냐는 결국 이 한 줄로 갈린다 —
// 아바타가 서느냐 마느냐. 에디터에서는 FBX 임포터가 알아서 세워 주지만(CharacterModelBuilder),
// 빌드에서 내려받은 GLB에는 임포터가 없다. 그래서 뼈 이름과 계층만 보고 직접 세운다.
//
// 이름표는 Meshy의 자동 리깅이 내놓는 규약을 기준으로 삼는다 — 믹사모 이름에서 접두사만 뺀 꼴이다.
//
//   Hips
//     Left/RightUpLeg → Left/RightLeg → Left/RightFoot → Left/RightToeBase
//     Spine02 → Spine01 → Spine → { Left/RightShoulder → Arm → ForeArm → Hand, neck → Head }
//
// 척추만은 이름으로 정하지 않는다. Meshy는 위에서부터 세어서 Spine이 목 바로 아래고 Spine02가
// 골반 바로 위다 — 이름을 곧이곧대로 믿으면 척추가 뒤집힌 채로 매핑된다. 계층을 걸어서
// 골반에서 목으로 올라가는 순서대로 Spine / Chest / UpperChest를 붙인다.
//
// 손가락은 매핑하지 않는다. Meshy 리그에 아예 없고, 있는 리그라도 손 소켓은 손뼈 축에서
// 같은 답을 내도록 되어 있다(HandSocket 참조).
public static class CharacterModelRig
{
    // Unity가 사람이라고 인정하는 데 반드시 필요한 뼈.
    private static readonly HumanBodyBones[] Required =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Head,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
        HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
    };

    // 정규화한 뼈 이름 → 사람 뼈. 정규화는 Normalize()가 한다(소문자, 접두사와 구분자 제거).
    private static readonly Dictionary<string, HumanBodyBones> ByName = new Dictionary<string, HumanBodyBones>
    {
        { "hips", HumanBodyBones.Hips }, { "pelvis", HumanBodyBones.Hips },
        { "neck", HumanBodyBones.Neck },
        { "head", HumanBodyBones.Head },

        { "leftshoulder", HumanBodyBones.LeftShoulder }, { "shoulderl", HumanBodyBones.LeftShoulder },
        { "claviclel", HumanBodyBones.LeftShoulder },
        { "leftarm", HumanBodyBones.LeftUpperArm }, { "leftupperarm", HumanBodyBones.LeftUpperArm },
        { "upperarml", HumanBodyBones.LeftUpperArm },
        { "leftforearm", HumanBodyBones.LeftLowerArm }, { "leftlowerarm", HumanBodyBones.LeftLowerArm },
        { "lowerarml", HumanBodyBones.LeftLowerArm }, { "forearml", HumanBodyBones.LeftLowerArm },
        { "lefthand", HumanBodyBones.LeftHand }, { "handl", HumanBodyBones.LeftHand },

        { "rightshoulder", HumanBodyBones.RightShoulder }, { "shoulderr", HumanBodyBones.RightShoulder },
        { "clavicler", HumanBodyBones.RightShoulder },
        { "rightarm", HumanBodyBones.RightUpperArm }, { "rightupperarm", HumanBodyBones.RightUpperArm },
        { "upperarmr", HumanBodyBones.RightUpperArm },
        { "rightforearm", HumanBodyBones.RightLowerArm }, { "rightlowerarm", HumanBodyBones.RightLowerArm },
        { "lowerarmr", HumanBodyBones.RightLowerArm }, { "forearmr", HumanBodyBones.RightLowerArm },
        { "righthand", HumanBodyBones.RightHand }, { "handr", HumanBodyBones.RightHand },

        { "leftupleg", HumanBodyBones.LeftUpperLeg }, { "leftupperleg", HumanBodyBones.LeftUpperLeg },
        { "thighl", HumanBodyBones.LeftUpperLeg }, { "upperlegl", HumanBodyBones.LeftUpperLeg },
        { "leftleg", HumanBodyBones.LeftLowerLeg }, { "leftlowerleg", HumanBodyBones.LeftLowerLeg },
        { "calfl", HumanBodyBones.LeftLowerLeg }, { "shinl", HumanBodyBones.LeftLowerLeg },
        { "leftfoot", HumanBodyBones.LeftFoot }, { "footl", HumanBodyBones.LeftFoot },
        { "lefttoebase", HumanBodyBones.LeftToes }, { "balll", HumanBodyBones.LeftToes },
        { "lefttoe", HumanBodyBones.LeftToes },

        { "rightupleg", HumanBodyBones.RightUpperLeg }, { "rightupperleg", HumanBodyBones.RightUpperLeg },
        { "thighr", HumanBodyBones.RightUpperLeg }, { "upperlegr", HumanBodyBones.RightUpperLeg },
        { "rightleg", HumanBodyBones.RightLowerLeg }, { "rightlowerleg", HumanBodyBones.RightLowerLeg },
        { "calfr", HumanBodyBones.RightLowerLeg }, { "shinr", HumanBodyBones.RightLowerLeg },
        { "rightfoot", HumanBodyBones.RightFoot }, { "footr", HumanBodyBones.RightFoot },
        { "righttoebase", HumanBodyBones.RightToes }, { "ballr", HumanBodyBones.RightToes },
        { "righttoe", HumanBodyBones.RightToes },
    };

    /// 계층을 읽어 아바타를 세운다. 세우지 못하면 왜 못 세웠는지를 problem에 담는다.
    ///
    /// root 아래에 스켈레톤 전부가 들어 있어야 한다. 뼈가 한 겹 더 들어간 것(root/Armature/Hips)은
    /// 상관없다 — FBX로 임포트할 때와 같은 모양이고, 그쪽도 그렇게 서 있다.
    public static bool TryBuildAvatar(GameObject root, out Avatar avatar, out string problem)
    {
        avatar = null;

        Dictionary<HumanBodyBones, Transform> bones = Map(root.transform);

        for (int i = 0; i < Required.Length; i++)
        {
            if (bones.ContainsKey(Required[i])) continue;
            problem = $"필수 뼈 {Required[i]}를 찾지 못했다. 리깅이 팔다리를 제대로 잡지 못한 몸이다.";
            return false;
        }

        HumanDescription description = Describe(root.transform, bones, out problem);
        if (problem != null) return false;

        // 아바타 세우기는 네이티브로 내려가고, 값이 이상하면 예외를 내는 대신 에디터째 멈추는 일이 있다.
        // 무엇을 넘겼는지 미리 남겨 두면 멈췄을 때 로그만 보고 원인을 좁힐 수 있다.
        Debug.Log($"[CharacterModelRig] {root.name}: 뼈 {description.human.Length}개 매핑, " +
                  $"스켈레톤 {description.skeleton.Length}개 — 아바타를 세운다.");

        avatar = AvatarBuilder.BuildHumanAvatar(root, description);
        if (avatar == null || !avatar.isValid)
        {
            problem = "AvatarBuilder가 아바타를 세우지 못했다(자세가 사람 범위를 벗어났을 수 있다).";
            avatar = null;
            return false;
        }

        avatar.name = root.name + "Avatar";
        return true;
    }

    // ── 뼈 짚기 ──────────────────────────────────────────────────────────

    public static Dictionary<HumanBodyBones, Transform> Map(Transform root)
    {
        var bones = new Dictionary<HumanBodyBones, Transform>();

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!ByName.TryGetValue(Normalize(t.name), out HumanBodyBones bone)) continue;
            // 같은 이름이 둘이면 먼저 만난 쪽(계층에서 위)을 쓴다.
            if (!bones.ContainsKey(bone)) bones[bone] = t;
        }

        MapSpine(bones);
        return bones;
    }

    // 골반에서 목(없으면 어깨)까지 올라가는 길이 곧 척추다. 이름이 아니라 이 순서가 정답이다.
    private static void MapSpine(Dictionary<HumanBodyBones, Transform> bones)
    {
        if (!bones.TryGetValue(HumanBodyBones.Hips, out Transform hips)) return;

        Transform top = null;
        if (bones.TryGetValue(HumanBodyBones.Neck, out Transform neck)) top = neck;
        else if (bones.TryGetValue(HumanBodyBones.Head, out Transform head)) top = head;
        else if (bones.TryGetValue(HumanBodyBones.LeftShoulder, out Transform shoulder)) top = shoulder;
        if (top == null) return;

        // 목에서 골반까지 부모를 거슬러 올라가며 모은 뒤 뒤집으면 골반 → 목 순서가 된다.
        var chain = new List<Transform>();
        for (Transform t = top.parent; t != null && t != hips; t = t.parent) chain.Add(t);
        if (chain.Count == 0) return;
        chain.Reverse();

        bones[HumanBodyBones.Spine] = chain[0];
        if (chain.Count >= 2) bones[HumanBodyBones.Chest] = chain[1];
        // 마디가 넷 이상이면 가운데는 그냥 둔다 — Unity가 아는 척추는 셋뿐이고,
        // 매핑되지 않은 뼈는 그 사이에 그대로 남아 같이 움직인다.
        if (chain.Count >= 3) bones[HumanBodyBones.UpperChest] = chain[chain.Count - 1];
    }

    private static bool Finite(Vector3 v) =>
        !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
        !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

    // "mixamorig:LeftUpLeg" → "leftupleg", "thigh_L" → "thighl", "Spine.001" → "spine001"
    private static string Normalize(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        int colon = name.LastIndexOf(':');
        if (colon >= 0) name = name.Substring(colon + 1);

        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    // ── 아바타 설명서 ────────────────────────────────────────────────────

    // 지금 서 있는 자세가 곧 기준 자세다. 모델을 세우자마자(애니메이터가 한 번도 돌기 전에) 부르면
    // 그게 바인드 포즈라, 여기서 읽는 트랜스폼이 그대로 아바타의 기준이 된다.
    //
    // 로컬 값을 그대로 적어야 한다. 고쳐 적으면 안 된다.
    //
    // Meshy의 GLB는 센티미터로 만들어져 있어서 Armature 노드에 0.01 배율이 걸려 있고, 그 아래
    // 뼈들의 로컬 위치는 87.8 같은 센티미터 값이다. 그래서 Unity가 이 값들만 보고 계산하는
    // 사람의 크기(humanScale)가 FBX 임포터의 값과 조금 어긋난다 — 실측으로 0.9312 대 0.9585였고,
    // 그 차이만큼(약 5%) 런타임 몸이 에디터에서 구운 몸보다 낮게 선다.
    //
    // 그렇다고 여기서 배율을 없앤 값으로 고쳐 적으면 안 된다. 아바타의 기준 골격은 살아 있는
    // 계층을 그대로 가리켜야 해서, 미터로 적어 두면 센티미터 계층에 그 값이 실리며 뼈가 100배로
    // 쪼그라든다(실측: 머리 높이가 1.27m에서 0.013m가 됐다). 제대로 고치려면 계층 자체의 배율을
    // 없애고 스킨의 바인드 포즈까지 같이 고쳐야 하는데, 5%는 고블린 옆에 세워 놓고 분간이
    // 되지 않는 차이라 그대로 둔다.
    private static HumanDescription Describe(Transform root,
                                             Dictionary<HumanBodyBones, Transform> bones,
                                             out string problem)
    {
        problem = null;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        var skeleton = new SkeletonBone[all.Length];
        var seen = new HashSet<string>();

        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            // 아바타는 뼈를 이름으로 찾는다. 같은 이름이 둘이면 어느 쪽인지 정할 수가 없다.
            if (!seen.Add(t.name))
            {
                problem = $"뼈 이름이 겹친다: {t.name}. 이름이 겹치면 아바타가 어느 뼈인지 정하지 못한다.";
                return default;
            }

            // 값이 성하지 않은 뼈가 하나만 있어도 아바타 세우기가 조용히 멈춘다.
            // glTF는 좌표계가 달라서 변환 과정에서 배율이 뒤집히거나 0이 되는 경우가 있다.
            if (!Finite(t.localPosition) || !Finite(t.localScale) ||
                t.localScale.x * t.localScale.y * t.localScale.z <= 0f)
            {
                problem = $"뼈 {t.name}의 값이 성하지 않다(위치 {t.localPosition}, 배율 {t.localScale}). " +
                          "이대로 아바타를 세우면 에디터가 멈춘다.";
                return default;
            }

            skeleton[i] = new SkeletonBone
            {
                name = t.name,
                position = t.localPosition,
                rotation = t.localRotation,
                scale = t.localScale,
            };
        }

        var human = new List<HumanBone>(bones.Count);
        foreach (KeyValuePair<HumanBodyBones, Transform> pair in bones)
        {
            human.Add(new HumanBone
            {
                humanName = HumanTrait.BoneName[(int)pair.Key],
                boneName = pair.Value.name,
                limit = new HumanLimit { useDefaultValues = true },
            });
        }

        return new HumanDescription
        {
            human = human.ToArray(),
            skeleton = skeleton,
            // FBX 임포터의 기본값과 같은 값. 여기가 다르면 같은 몸인데도 에디터에서 구운 것과
            // 빌드에서 만든 것이 미묘하게 다르게 움직인다.
            upperArmTwist = 0.5f,
            lowerArmTwist = 0.5f,
            upperLegTwist = 0.5f,
            lowerLegTwist = 0.5f,
            armStretch = 0.05f,
            legStretch = 0.05f,
            feetSpacing = 0f,
            hasTranslationDoF = false,
        };
    }
}
