using System.Collections.Generic;
using UnityEngine;

// 애니메이터 컨트롤러에 든 클립의 길이를 이름으로 찾는다.
//
// runtimeAnimatorController.animationClips는 접근할 때마다 배열을 새로 만들어 반환한다.
// 유닛 하나당 여러 번(공격/스킬/피격 …) 호출되므로 스폰이 많아지면 그대로 GC 부담이 된다.
// 컨트롤러 에셋 단위로 클립 길이를 한 번만 만들어 모든 유닛이 공유한다.
public static class AnimatorClipLengths
{
    private static readonly Dictionary<RuntimeAnimatorController, Dictionary<string, float>> Cache =
        new Dictionary<RuntimeAnimatorController, Dictionary<string, float>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 파괴된 컨트롤러 참조가 남지 않도록 플레이 시작마다 비운다.
        Cache.Clear();
    }

    /// 이 애니메이터에 clipName이라는 클립이 있으면 그 길이, 없으면 fallback.
    public static float Of(Animator animator, string clipName, float fallback)
    {
        if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(clipName))
        {
            return fallback;
        }

        return LengthsOf(animator.runtimeAnimatorController).TryGetValue(clipName, out float length) ? length : fallback;
    }

    private static Dictionary<string, float> LengthsOf(RuntimeAnimatorController controller)
    {
        if (Cache.TryGetValue(controller, out Dictionary<string, float> cached)) return cached;

        AnimationClip[] clips = controller.animationClips;
        var lengths = new Dictionary<string, float>(clips.Length);
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip != null) lengths[clip.name] = clip.length;
        }

        Cache[controller] = lengths;
        return lengths;
    }
}
